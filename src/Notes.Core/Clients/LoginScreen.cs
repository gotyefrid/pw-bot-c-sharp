using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Notes.Core.Memory;
using Notes.Core.Profiles;

namespace Notes.Core.Clients;

/// <summary>Где сейчас клиент на пути в мир.</summary>
public enum LoginStage
{
    /// <summary>Окна не читаются: клиент ещё запускается (или профиль не знает окон).</summary>
    Unknown,

    /// <summary>Менеджера окон нет или ни одно знакомое окно не открыто: грузится.</summary>
    Loading,

    /// <summary>Окно ввода логина и пароля.</summary>
    Login,

    /// <summary>Выбор персонажа.</summary>
    Select,

    /// <summary>В мире (окна мира).</summary>
    World,
}

/// <summary>Персонаж на экране выбора: место в списке (1…8), ник, «Друид Ур. 46».</summary>
public sealed record LoginRole(int Slot, string Name, string Info);

/// <param name="Message">Текст открытого окна-сообщения (ошибка входа, разрыв связи); null — сообщения нет.</param>
/// <param name="Ping">Пинг на экране логина как написан («344 ms»); клиент меряет его один раз при запуске.</param>
/// <param name="Account">Что сейчас в поле логина.</param>
/// <param name="Focus">Имя элемента в фокусе окна логина (поле логина или пароля).</param>
/// <param name="Selected">Отмеченное место на выборе персонажа; null — не видно.</param>
/// <param name="Dialogs">Имена открытых окон — для лога.</param>
public sealed record LoginView(
    LoginStage Stage,
    string? Message,
    string? Ping,
    string? Account,
    string? Focus,
    IReadOnlyList<LoginRole> Roles,
    int? Selected,
    IReadOnlyList<string> Dialogs)
{
    public static readonly LoginView None = new(LoginStage.Unknown, null, null, null, null, [], null, []);
}

/// <summary>Что видно на экране входа: читает окна клиента (<see cref="LoginScreenOffsets"/>). Память только читается.</summary>
public interface ILoginScreen
{
    LoginView Read();
}

/// <summary>
/// Экран входа по памяти клиента. Имена окон и элементов — из файлов интерфейса клиента (interfaces\Version01\login.xml,
/// select.xml): Win_Login, Win_Select, окна-сообщения «MsgBox_…». Если у другого клиента они другие — видно в Probe ui.
/// </summary>
public sealed class LoginScreen : ILoginScreen
{
    public const string LoginDialog = "Win_Login", SelectDialog = "Win_Select", MessagePrefix = "MsgBox";
    public const string AccountField = "DEFAULT_Txt_Account", PasswordField = "Txt_PassWord", PingLabel = "Lbl_Ping";

    /// <summary>Мест на выборе персонажа: Txt_Name1…8, Txt_Level1…8, Rdo_Char1…8.</summary>
    public const int Slots = 8;

    // Список элементов окна — не бесконечный: битый указатель не должен завесить чтение
    private const int MaxControls = 300, MaxDialogs = 2000;

    private readonly IMemory _memory;
    private readonly GameRoots _roots;
    private readonly LoginScreenOffsets _offsets;

    public LoginScreen(IMemory memory, GameRoots roots, ProfileData profile)
    {
        _memory = memory;
        _roots = roots;
        _offsets = profile.LoginScreen;
    }

    /// <summary>Профиль знает окна клиента — можно входить по этапам, а не аргументами запуска.</summary>
    public static bool Supported(ProfileData profile) => profile.LoginScreen.Dialogs != 0 && profile.Ui.Managers != 0;

    public LoginView Read()
    {
        if (_offsets.Dialogs == 0 || !_roots.TryUiManager(out var manager, out var inWorld))
            return LoginView.None;
        if (manager == 0)
            return LoginView.None with { Stage = LoginStage.Loading };

        var shown = ShownDialogs(manager);
        var names = shown.Select(d => d.Name).ToList();
        var message = shown.Where(d => d.Name.StartsWith(MessagePrefix, StringComparison.Ordinal))
            .Select(d => Controls(d.Address).Select(c => c.Text).OrderByDescending(t => t.Length).FirstOrDefault())
            .FirstOrDefault(t => !string.IsNullOrEmpty(t));
        if (inWorld)
            return LoginView.None with { Stage = LoginStage.World, Message = message, Dialogs = names };

        if (shown.FirstOrDefault(d => d.Name == SelectDialog) is { Address: not 0 } select)
        {
            var (roles, selected) = Roles(select.Address);
            return LoginView.None with { Stage = LoginStage.Select, Message = message, Roles = roles, Selected = selected, Dialogs = names };
        }

        if (shown.FirstOrDefault(d => d.Name == LoginDialog) is { Address: not 0 } login)
        {
            var controls = Controls(login.Address);
            return LoginView.None with
            {
                Stage = LoginStage.Login,
                Message = message,
                Ping = Text(controls, PingLabel),
                Account = controls.FirstOrDefault(c => c.Name == AccountField)?.Text ?? "",
                Focus = Focus(login.Address, controls),
                Dialogs = names,
            };
        }

        return LoginView.None with { Stage = LoginStage.Loading, Message = message, Dialogs = names };
    }

    private sealed record Dialog(uint Address, string Name);

    private sealed record Control(uint Address, string Name, string Text);

    private List<Dialog> ShownDialogs(uint manager)
    {
        var result = new List<Dialog>();
        if (!_memory.TryReadUInt32(manager + _offsets.Dialogs, out var begin)
            || !_memory.TryReadUInt32(manager + _offsets.Dialogs + 4, out var end)
            || end < begin || (end - begin) / 4 > MaxDialogs)
            return result;

        var count = (int)((end - begin) / 4);
        var raw = new byte[count * 4];
        if (count == 0 || !_memory.TryRead(begin, raw, raw.Length))
            return result;

        var flag = new byte[1];
        for (var i = 0; i < count; i++)
        {
            var dialog = BitConverter.ToUInt32(raw, i * 4);
            if (dialog != 0 && _memory.TryRead(dialog + _offsets.DialogShown, flag, 1) && flag[0] != 0
                && _memory.TryReadUInt32(dialog + _offsets.DialogName, out var name) && Ansi(name) is { } text)
                result.Add(new Dialog(dialog, text));
        }

        return result;
    }

    private List<Control> Controls(uint dialog)
    {
        var result = new List<Control>();
        if (!_memory.TryReadUInt32(dialog + _offsets.Controls, out var node))
            return result;

        for (var i = 0; i < MaxControls && node != 0; i++)
        {
            if (!_memory.TryReadUInt32(node + _offsets.ControlItem, out var control) || control == 0)
                break;

            var name = _memory.TryReadUInt32(control + _offsets.ControlName, out var namePointer) ? Ansi(namePointer) ?? "" : "";
            var text = _memory.TryReadUInt32(control + _offsets.ControlText, out var textPointer) ? Unicode(textPointer) : "";
            result.Add(new Control(control, name, text));
            if (!_memory.TryReadUInt32(node + _offsets.ControlNext, out node))
                break;
        }

        return result;
    }

    private (IReadOnlyList<LoginRole> Roles, int? Selected) Roles(uint dialog)
    {
        var controls = Controls(dialog);
        var roles = new List<LoginRole>();
        int? selected = null;
        var flag = new byte[1];
        for (var slot = 1; slot <= Slots; slot++)
        {
            if (Text(controls, $"Txt_Name{slot}") is { Length: > 0 } name)
                roles.Add(new LoginRole(slot, name, Text(controls, $"Txt_Level{slot}") ?? ""));
            if (_offsets.RadioChecked != 0 && controls.FirstOrDefault(c => c.Name == $"Rdo_Char{slot}") is { } radio
                && _memory.TryRead(radio.Address + _offsets.RadioChecked, flag, 1) && flag[0] != 0)
                selected = slot;
        }

        return (roles, selected);
    }

    private string? Focus(uint dialog, List<Control> controls)
    {
        if (_offsets.DialogFocus == 0 || !_memory.TryReadUInt32(dialog + _offsets.DialogFocus, out var focus) || focus == 0)
            return null;
        return controls.FirstOrDefault(c => c.Address == focus)?.Name;
    }

    private static string? Text(List<Control> controls, string name) => controls.FirstOrDefault(c => c.Name == name)?.Text;

    // Имена окон и элементов — ANSI, короткие
    private string? Ansi(uint address)
    {
        var bytes = new byte[64];
        if (address == 0 || !TryReadUpTo(address, bytes))
            return null;

        var length = Array.IndexOf(bytes, (byte)0);
        return length > 0 ? Encoding.ASCII.GetString(bytes, 0, length) : null;
    }

    // Тексты — UTF-16, до 256 букв (сообщения об ошибках длинные); читаем по 64, чтобы не задеть чужую страницу
    private string Unicode(uint address)
    {
        if (address == 0)
            return "";

        var text = new StringBuilder();
        var chunk = new byte[128];
        while (text.Length < 256)
        {
            if (!TryReadUpTo(address + (uint)(text.Length * 2), chunk))
                break;

            var part = Encoding.Unicode.GetString(chunk);
            var end = part.IndexOf('\0');
            text.Append(end >= 0 ? part.Substring(0, end) : part);
            if (end >= 0)
                break;
        }

        return text.ToString();
    }

    // Целиком или до конца страницы (строка у края доступной памяти); недочитанное — нули
    private bool TryReadUpTo(uint address, byte[] buffer)
    {
        if (_memory.TryRead(address, buffer, buffer.Length))
            return true;

        Array.Clear(buffer, 0, buffer.Length);
        var untilPageEnd = (int)(0x1000 - address % 0x1000);
        return untilPageEnd < buffer.Length && _memory.TryRead(address, buffer, untilPageEnd);
    }
}
