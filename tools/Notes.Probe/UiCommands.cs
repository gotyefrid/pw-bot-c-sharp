using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Notes.Core.Clients;
using Notes.Core.Memory;
using Notes.Core.Profiles;

namespace Notes.Probe;

/// <summary>
/// Окна клиента: экран входа, выбор персонажа, сообщения, окна мира. Только чтение.
/// <c>ui [all]</c> — что видит бот (по профилю) и тексты открытых окон; <c>ui scan …</c> — поиск смещений на новом клиенте.
/// </summary>
internal static class UiCommands
{
    public static int Run(ServerProfile profile, GameProcess game, string[] args)
    {
        var roots = new GameRoots(game, BasePointerSearch.Find(game, game.MainModuleBase).Address, profile.Data);
        return args.Contains("scan") ? Scan(profile.Data, roots, game, args) : Show(profile.Data, roots, game, args.Contains("all"));
    }

    // Что видит бот и все открытые окна с текстами элементов
    private static int Show(ProfileData profile, GameRoots roots, GameProcess game, bool all)
    {
        var o = profile.LoginScreen;
        if (!LoginScreen.Supported(profile))
        {
            Console.WriteLine("В профиле нет окон клиента (loginScreen) — сначала найти: ui scan");
            return 1;
        }

        var view = new LoginScreen(game, roots, profile).Read();
        Console.WriteLine($"Этап: {view.Stage}");
        if (view.Ping is not null)
            Console.WriteLine($"Пинг: «{view.Ping}»");
        if (view.Account is not null)
            Console.WriteLine($"Поле логина: «{view.Account}», фокус: {view.Focus ?? "?"}");
        foreach (var role in view.Roles)
            Console.WriteLine($"Персонаж {role.Slot}{(role.Slot == view.Selected ? " ✔" : "")}: {role.Name} — {role.Info}");
        if (view.Message is not null)
            Console.WriteLine($"Сообщение: «{view.Message}»");

        if (!roots.TryUiManager(out var manager, out var inWorld) || manager == 0)
            return 0;

        Console.WriteLine();
        Console.WriteLine($"{(inWorld ? "Окна мира" : "Окна входа")}: {Rtti(game, manager)} 0x{manager:X8}");
        var begin = game.ReadUInt32(manager + o.Dialogs);
        var end = game.ReadUInt32(manager + o.Dialogs + 4);
        for (var at = begin; at < end && at - begin < 0x2000 * 4; at += 4)
        {
            var dialog = game.ReadUInt32(at);
            var flag = new byte[1];
            var shown = game.TryRead(dialog + o.DialogShown, flag, 1) && flag[0] != 0;
            if (!shown && !all)
                continue;

            var name = game.TryReadUInt32(dialog + o.DialogName, out var n) ? Ansi(game, n) : null;
            Console.WriteLine($"{(shown ? "●" : "○")} {name} {Rtti(game, dialog)} 0x{dialog:X8}");
            var node = game.TryReadUInt32(dialog + o.Controls, out var head) ? head : 0;
            for (var i = 0; i < 300 && node >= 0x10000; i++)
            {
                if (!game.TryReadUInt32(node + o.ControlItem, out var control))
                    break;
                var controlName = game.TryReadUInt32(control + o.ControlName, out var cn) ? Ansi(game, cn) : null;
                if (game.TryReadUInt32(control + o.ControlText, out var t) && Wide(game, t) is { } text)
                {
                    // Пароль в консоль не печатаем (у Comeback 1.3.6 поле читается) — только сколько букв
                    var printed = controlName == LoginScreen.PasswordField ? new string('•', text.Length) : text;
                    Console.WriteLine($"    {controlName} «{printed}»");
                }
                if (!game.TryReadUInt32(node + o.ControlNext, out node))
                    break;
            }
        }

        return 0;
    }

    // Поиск смещений на новом клиенте: менеджеры окон, массив окон, «показано», элементы, строки, сырые байты
    private static int Scan(ProfileData profile, GameRoots roots, GameProcess game, string[] args)
    {
        var hex = args.FirstOrDefault(a => a.StartsWith("0x", StringComparison.OrdinalIgnoreCase));
        var address = hex is null ? 0 : Convert.ToUInt32(hex, 16);
        var at = Array.IndexOf(args, "raw");
        if (at >= 0 && address != 0 && at + 2 < args.Length)
        {
            // raw 0xАДРЕС файл — 0x800 байт объекта в файл, для сравнения «до/после»
            var bytes = new byte[0x800];
            game.TryRead(address, bytes, bytes.Length);
            File.WriteAllBytes(args[at + 2], bytes);
            Console.WriteLine($"0x{address:X8}: 0x800 байт → {args[at + 2]}");
            return 0;
        }

        at = Array.IndexOf(args, "find");
        if (at >= 0 && address != 0 && at + 1 < args.Length)
        {
            Find(game, address, args.Skip(at + 1).First(a => a != hex));
            return 0;
        }

        if (args.Contains("strings") && address != 0)
        {
            Strings(game, address);
            return 0;
        }

        if (address != 0)
        {
            Fields(game, address);
            Lists(game, address);
            return 0;
        }

        var managers = game.ReadUInt32(roots.Game() + profile.Ui.Managers);
        Console.WriteLine($"Менеджеры окон 0x{managers:X8} (game + ui.managers)");
        for (var offset = 0u; offset < 0x40; offset += 4)
        {
            game.TryReadUInt32(managers + offset, out var value);
            Console.WriteLine($"  [+0x{offset:X2}] = 0x{value:X8} {Rtti(game, value)}");
        }

        var only = args.FirstOrDefault(a => a.StartsWith("Win_") || a.StartsWith("MsgBox") || a.StartsWith("Tmpl_"));
        for (var offset = 0u; offset < 0x40; offset += 4)
        {
            if (!game.TryReadUInt32(managers + offset, out var manager) || manager < 0x10000)
                continue;

            foreach (var (vector, dialogs) in Vectors(game, manager, 0x1000))
            {
                // Имя окна AUI: строка ANSI где-то в начале объекта — у известных клиентов +0x4C
                var nameOffset = profile.LoginScreen.DialogName != 0 ? profile.LoginScreen.DialogName : 0x4Cu;
                var names = dialogs.Select(d => game.TryReadUInt32(d + nameOffset, out var p) ? Ansi(game, p) : null).ToList();
                if (names.Count(n => n is not null) < dialogs.Count / 2)
                    continue;

                Console.WriteLine($"Менеджер [+0x{offset:X2}]: массив окон в +0x{vector:X} — {dialogs.Count}");
                Console.WriteLine("  " + string.Join(", ", names.Select(n => n ?? "?")));
                if (args.Contains("bytes"))
                    ShowFlagCandidates(game, dialogs, names);
                if (only is not null && names.IndexOf(only) is var index and >= 0)
                {
                    Fields(game, dialogs[index]);
                    Lists(game, dialogs[index]);
                }
            }
        }

        return 0;
    }

    // Элементы окна: список, у узлов которого объект с RTTI и именем-строкой по +0x18 (как у известных клиентов)
    private static void Lists(GameProcess game, uint dialog)
    {
        for (var offset = 0u; offset < 0x400; offset += 4)
        {
            if (!game.TryReadUInt32(dialog + offset, out var head) || head < 0x10000)
                continue;

            foreach (var (next, data) in new[] { (0u, 8u), (4u, 0u), (0u, 4u), (4u, 8u) })
            {
                var node = head;
                var found = new List<string>();
                for (var i = 0; i < 300 && node >= 0x10000; i++)
                {
                    if (!game.TryReadUInt32(node + data, out var item) || !game.TryReadUInt32(item + 0x18, out var n)
                        || Ansi(game, n) is not { } name || Rtti(game, item).Length == 0)
                        break;
                    found.Add($"{name}@0x{item:X8}");
                    if (!game.TryReadUInt32(node + next, out node))
                        break;
                }

                if (found.Count >= 2)
                    Console.WriteLine($"  список +0x{offset:X} (узел: следующий +{next}, элемент +{data}) {found.Count}: {string.Join(", ", found)}");
            }
        }
    }

    // Все строки UTF-16 (от 4 букв), достижимые из объекта по указателям за 3 шага
    private static void Strings(GameProcess game, uint obj)
    {
        var seen = new HashSet<uint>();
        void Look(uint address, string path, int depth)
        {
            if (!seen.Add(address))
                return;
            if (Wide(game, address, 200) is { Length: >= 4 } text && text.Count(char.IsLetter) >= 3)
                Console.WriteLine($"  {path}: «{text}»");
            var buffer = new byte[0x300];
            if (depth == 0 || !game.TryRead(address, buffer, buffer.Length))
                return;
            for (var i = 0; i < buffer.Length; i += 4)
            {
                var pointer = BitConverter.ToUInt32(buffer, i);
                if (pointer >= 0x10000 && pointer < 0x7FFF0000)
                    Look(pointer, $"{path} [+0x{i:X}]", depth - 1);
            }
        }

        Console.WriteLine($"Строки из 0x{obj:X8}");
        Look(obj, "", 3);
    }

    // Где в объекте (прямо или по указателю, два уровня) лежит строка: UTF-16 и ANSI
    private static void Find(GameProcess game, uint obj, string text)
    {
        var wide = Encoding.Unicode.GetBytes(text);
        var ansi = Encoding.GetEncoding(1251).GetBytes(text);
        void Look(uint address, string path, int depth)
        {
            var buffer = new byte[0x400];
            if (!game.TryRead(address, buffer, buffer.Length))
                return;
            foreach (var (needle, kind) in new[] { (wide, "UTF-16"), (ansi, "ANSI") })
            {
                for (var i = 0; i + needle.Length <= buffer.Length; i++)
                {
                    if (buffer.Skip(i).Take(needle.Length).SequenceEqual(needle))
                        Console.WriteLine($"  {kind}: {path} +0x{i:X}");
                }
            }

            if (depth == 0)
                return;
            for (var i = 0; i < buffer.Length; i += 4)
            {
                var pointer = BitConverter.ToUInt32(buffer, i);
                if (pointer >= 0x10000 && pointer < 0x7FFF0000 && pointer != obj)
                    Look(pointer, $"{path} [+0x{i:X}]", depth - 1);
            }
        }

        Console.WriteLine($"Ищу «{text}» в 0x{obj:X8}");
        Look(obj, "", 2);
    }

    // Поля объекта: указатели на объекты с RTTI и на строки
    private static void Fields(GameProcess game, uint obj)
    {
        Console.WriteLine($"Объект 0x{obj:X8} {Rtti(game, obj)}");
        for (var offset = 0u; offset < 0x400; offset += 4)
        {
            if (!game.TryReadUInt32(obj + offset, out var value))
                continue;
            var rtti = Rtti(game, value);
            var text = Ansi(game, value);
            var wide = Wide(game, value);
            var inner = rtti.Length == 0 && value >= 0x10000 && game.TryReadUInt32(value, out var p) ? Rtti(game, p) : "";
            if (rtti.Length > 0 || text is not null || wide is not null || inner.Length > 0)
                Console.WriteLine($"  +0x{offset:X3} 0x{value:X8} {rtti}{(text is null ? "" : $" «{text}»")}{(wide is null ? "" : $" w«{wide}»")}{(inner.Length == 0 ? "" : $" []→{inner}")}");
        }
    }

    // Байты, которые 1 у немногих окон и 0 у остальных, — кандидаты «окно показано»
    private static void ShowFlagCandidates(GameProcess game, List<uint> dialogs, List<string?> names)
    {
        const int size = 0x300;
        var data = dialogs.Select(d =>
        {
            var b = new byte[size];
            game.TryRead(d, b, size);
            return b;
        }).ToList();
        for (var offset = 0; offset < size; offset++)
        {
            var ones = Enumerable.Range(0, dialogs.Count).Where(i => data[i][offset] == 1).ToList();
            var zeros = Enumerable.Range(0, dialogs.Count).Count(i => data[i][offset] == 0);
            if (ones.Count is > 0 and <= 12 && ones.Count + zeros == dialogs.Count)
                Console.WriteLine($"  +0x{offset:X3}: 1 у {string.Join(", ", ones.Select(i => names[i]))}");
        }
    }

    // Массив указателей: начало и конец подряд
    private static IEnumerable<(uint At, List<uint> Items)> Vectors(GameProcess game, uint obj, uint size)
    {
        for (var offset = 0u; offset < size; offset += 4)
        {
            if (!game.TryReadUInt32(obj + offset, out var begin) || !game.TryReadUInt32(obj + offset + 4, out var end)
                || begin < 0x10000 || end <= begin || (end - begin) % 4 != 0 || (end - begin) / 4 > 2000)
                continue;

            var count = (int)((end - begin) / 4);
            var raw = new byte[count * 4];
            if (!game.TryRead(begin, raw, raw.Length))
                continue;

            var items = Enumerable.Range(0, count).Select(i => BitConverter.ToUInt32(raw, i * 4)).ToList();
            if (count >= 3 && items.All(p => p >= 0x10000))
                yield return (offset, items);
        }
    }

    private static string? Ansi(GameProcess game, uint address, int max = 64)
    {
        var bytes = new byte[max];
        if (address < 0x10000 || !game.TryRead(address, bytes, max))
            return null;

        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 2)
            return null;

        var text = Encoding.GetEncoding(1251).GetString(bytes, 0, length);
        return text.All(c => c >= 0x20) ? text : null;
    }

    // UTF-16 до max букв, табуляция (колонки списков) — « | »; мусор — null
    private static string? Wide(GameProcess game, uint address, int max = 256)
    {
        if (address < 0x10000)
            return null;

        // По 64 буквы: длинный кусок может задеть чужую страницу памяти
        var text = new StringBuilder();
        var chunk = new byte[128];
        while (text.Length < max)
        {
            if (!game.TryRead(address + (uint)(text.Length * 2), chunk, chunk.Length))
                return null;
            var part = Encoding.Unicode.GetString(chunk);
            var end = part.IndexOf('\0');
            text.Append(end >= 0 ? part.Substring(0, end) : part);
            if (end >= 0)
                break;
        }

        var result = text.ToString();
        if (result.Length < 2 || !result.All(c => c == '\t' || c >= ' ' && c < 'Ԁ'))
            return null;
        return result.Replace("\t", " | ");
    }

    // Имя класса по RTTI: [vtable-4] → COL, [+0xC] → описание типа, имя с +8
    private static string Rtti(GameProcess game, uint obj)
    {
        if (obj < 0x10000 || !game.TryReadUInt32(obj, out var vtable) || !game.TryReadUInt32(vtable - 4, out var col)
            || !game.TryReadUInt32(col + 0xC, out var type))
            return "";

        return Ansi(game, type + 8) is { } name && name.StartsWith(".?A") ? name : "";
    }
}
