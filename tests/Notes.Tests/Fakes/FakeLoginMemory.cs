using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Notes.Core.Memory;
using Notes.Core.Profiles;

namespace Notes.Tests.Fakes;

/// <summary>Элемент поддельного окна: имя, текст и отметка (у переключателя Rdo_Char).</summary>
public sealed record FakeControl(string Name, string Text = "", bool Checked = false);

/// <summary>Поддельное окно клиента: имя, показано ли, элементы и какой из них в фокусе.</summary>
public sealed record FakeDialog(string Name, bool Shown, IReadOnlyList<FakeControl> Controls, string? Focus = null)
{
    public FakeDialog(string name, params FakeControl[] controls) : this(name, true, controls)
    {
    }
}

/// <summary>
/// Память клиента с окнами (AUI) по смещениям профиля (по умолчанию PW Classic): база → game → менеджеры окон → текущий менеджер → массив
/// окон → список элементов. <see cref="Show"/> перекладывает экран заново — как клиент, у которого сменились окна.
/// </summary>
public sealed class FakeLoginMemory
{
    private const uint Pointer = 0x00A45740, Root = 0x01000000, Game = 0x01100000, Managers = 0x01200000;

    private uint _next = 0x10000000;

    /// <summary>Место окна и любого элемента в нём — если профиль знает, где их читать (как поле логина у Comeback 1.3.6).</summary>
    public static readonly (int X, int Y) DialogAt = (718, 343);
    public static readonly (int X, int Y, int Width, int Height) ControlAt = (144, 65, 143, 23);

    public FakeLoginMemory(string server = "pwclassic136")
    {
        Profile = ProfileCatalog.Default().Load(server).Data;
        Roots = new GameRoots(Memory, Pointer, Profile);
        Memory.WriteUInt32(Pointer, Root);
        Memory.WriteUInt32(Root + Profile.Base.Game, Game);
        Memory.WriteUInt32(Game + Profile.Ui.Managers, Managers);
        Loading();
    }

    public MemoryImage Memory { get; } = new();
    public ProfileData Profile { get; }
    public GameRoots Roots { get; }

    /// <summary>Менеджера окон нет: клиент грузится.</summary>
    public void Loading()
    {
        Memory.WriteUInt32(Managers + Profile.Ui.Active, 0);
        Memory.WriteUInt32(Managers + Profile.Ui.InGame, 0);
    }

    /// <summary>Экран входа (или мир) с этими окнами.</summary>
    public void Show(bool inWorld, params FakeDialog[] dialogs)
    {
        var o = Profile.LoginScreen;
        var manager = Allocate(0x1000);
        Memory.WriteUInt32(Managers + Profile.Ui.Active, inWorld ? 1u : 0u);
        Memory.WriteUInt32(Managers + Profile.Ui.InGame, manager);

        var array = Allocate((uint)Math.Max(dialogs.Length * 4, 4));
        Memory.WriteUInt32(manager + o.Dialogs, array);
        Memory.WriteUInt32(manager + o.Dialogs + 4, array + (uint)dialogs.Length * 4);
        for (var i = 0; i < dialogs.Length; i++)
            Memory.WriteUInt32(array + (uint)i * 4, Dialog(dialogs[i]));
    }

    private uint Dialog(FakeDialog dialog)
    {
        var o = Profile.LoginScreen;
        var address = Allocate(0x400);
        Memory.WriteUInt32(address + o.DialogName, Ansi(dialog.Name));
        Memory.WriteBytes(address + o.DialogShown, [dialog.Shown ? (byte)1 : (byte)0]);
        if (o.DialogPosition != 0)
        {
            Memory.WriteUInt32(address + o.DialogPosition, (uint)DialogAt.X);
            Memory.WriteUInt32(address + o.DialogPosition + 4, (uint)DialogAt.Y);
        }

        // Список с конца: узел — [следующий, элемент]
        var head = 0u;
        var controls = new Dictionary<string, uint>();
        foreach (var control in dialog.Controls.Reverse())
        {
            var item = Allocate(0x200);
            Memory.WriteUInt32(item + o.ControlName, Ansi(control.Name));
            Memory.WriteUInt32(item + o.ControlText, Unicode(control.Text));
            Memory.WriteBytes(item + o.RadioChecked, [control.Checked ? (byte)1 : (byte)0]);
            if (o.ControlPosition != 0)
            {
                Memory.WriteUInt32(item + o.ControlPosition, (uint)ControlAt.X);
                Memory.WriteUInt32(item + o.ControlPosition + 4, (uint)ControlAt.Y);
                Memory.WriteUInt32(item + o.ControlPosition + 8, (uint)ControlAt.Width);
                Memory.WriteUInt32(item + o.ControlPosition + 12, (uint)ControlAt.Height);
            }
            controls[control.Name] = item;

            var node = Allocate(0x10);
            Memory.WriteUInt32(node + o.ControlNext, head);
            Memory.WriteUInt32(node + o.ControlItem, item);
            head = node;
        }

        Memory.WriteUInt32(address + o.Controls, head);
        Memory.WriteUInt32(address + o.DialogFocus, dialog.Focus is { } focus ? controls[focus] : 0);
        return address;
    }

    private uint Ansi(string text) => Put(Encoding.ASCII.GetBytes(text + "\0"));

    private uint Unicode(string text) => Put(Encoding.Unicode.GetBytes(text + "\0"));

    private uint Put(byte[] bytes)
    {
        var address = Allocate((uint)bytes.Length);
        Memory.WriteBytes(address, bytes);
        return address;
    }

    private uint Allocate(uint size)
    {
        var address = _next;
        _next += (size + 0xF) & ~0xFu;
        Memory.Map(address, (int)size);
        return address;
    }

    // Экраны, как их видели на живом клиенте PW Classic

    public static FakeDialog LoginWindow(string ping = "344 ms", string account = "", string? focus = "DEFAULT_Txt_Account") => new(
        "Win_Login", true,
        [
            new FakeControl("Lbl_Ping", ping), new FakeControl("Lbl_ServerName", "[Classic] RU"),
            new FakeControl("DEFAULT_Txt_Account", account), new FakeControl("Txt_PassWord"), new FakeControl("Btn_Game", "Вход"),
        ],
        focus);

    public static FakeDialog Message(string text) => new("MsgBox_LoginFail", new FakeControl("Control_1", "Да (Y)"), new FakeControl("Control_2", text));

    public static FakeDialog Select(int selected, params string[] names) => new(
        "Win_Select", true,
        names.SelectMany((name, i) => new[]
            {
                new FakeControl($"Txt_Name{i + 1}", name), new FakeControl($"Txt_Level{i + 1}", "Друид Ур. 46"),
            })
            .Concat(Enumerable.Range(1, 8).Select(slot => new FakeControl($"Rdo_Char{slot}", Checked: slot == selected)))
            .ToList());
}
