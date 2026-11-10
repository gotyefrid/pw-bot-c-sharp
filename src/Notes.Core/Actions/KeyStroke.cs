using System;
using System.Collections.Generic;
using System.Linq;

namespace Notes.Core.Actions;

/// <summary>
/// Клавиша для игры: код клавиши Windows (VK) и Ctrl/Alt/Shift. В настройках — строкой, как на клавиатуре: «F1», «3», «Ctrl+2».
/// Разбирает и пишет только клавиши из <see cref="Names"/> — других окно и не даст выбрать.
/// </summary>
public readonly record struct KeyStroke(int Key, bool Ctrl = false, bool Alt = false, bool Shift = false)
{
    private static readonly Dictionary<int, string> Names = BuildNames();
    private static readonly Dictionary<string, int> Codes = Names.ToDictionary(n => n.Value, n => n.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Почему такое игре не жмём; null — можно. Alt+F4, Alt+Space и Alt+Esc разбирает сама Windows: закроет клиент, откроет
    /// меню окна (игра встанет, пока оно открыто) или переключит окна.
    /// </summary>
    public string? Forbidden => Alt && !Ctrl && Key is 0x73 or 0x20 or 0x1B ? $"{this} — сочетание Windows, а не игры: не жму" : null;

    /// <summary>Есть ли у клавиши имя (её можно сохранить в настройках).</summary>
    public static bool Known(int key) => Names.ContainsKey(key);

    /// <summary>«Ctrl+Alt+Shift+F1» → клавиша. Пусто или незнакомое имя — false.</summary>
    public static bool TryParse(string? text, out KeyStroke stroke)
    {
        stroke = default;
        var rest = text?.Trim() ?? "";
        bool ctrl = false, alt = false, shift = false;
        // «Num+» — сама клавиша, поэтому модификаторы снимаются только с начала, по одному
        while (true)
        {
            if (Strip(ref rest, "Ctrl+"))
                ctrl = true;
            else if (Strip(ref rest, "Alt+"))
                alt = true;
            else if (Strip(ref rest, "Shift+"))
                shift = true;
            else
                break;
        }

        if (!Codes.TryGetValue(rest, out var key))
            return false;

        stroke = new KeyStroke(key, ctrl, alt, shift);
        return true;
    }

    private static bool Strip(ref string text, string prefix)
    {
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || text.Length == prefix.Length)
            return false;
        text = text.Substring(prefix.Length);
        return true;
    }

    public override string ToString()
        => (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "") + (Names.TryGetValue(Key, out var name) ? name : $"0x{Key:X2}");

    // Имена — как на клавишах; ←↑→↓ — стрелки, Num — цифровой блок
    private static Dictionary<int, string> BuildNames()
    {
        var names = new Dictionary<int, string>
        {
            [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x14] = "CapsLock", [0x1B] = "Esc",
            [0x20] = "Space", [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home",
            [0x25] = "←", [0x26] = "↑", [0x27] = "→", [0x28] = "↓", [0x2D] = "Insert", [0x2E] = "Delete",
            [0x6A] = "Num*", [0x6B] = "Num+", [0x6D] = "Num-", [0x6E] = "Num.", [0x6F] = "Num/",
            [0x90] = "NumLock", [0x91] = "ScrollLock",
            [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
            [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
        };
        for (var i = 0; i <= 9; i++)
        {
            names[0x30 + i] = i.ToString();
            names[0x60 + i] = $"Num{i}";
        }
        for (var c = 'A'; c <= 'Z'; c++)
            names[c] = c.ToString();
        for (var i = 1; i <= 24; i++)
            names[0x6F + i] = $"F{i}";
        return names;
    }
}
