using System.Text.RegularExpressions;

namespace BotCH.Probe.Port;

/// <summary>
/// Правка одного значения в профиле (*.jsonc) по пути «host.hp», «world.npcs.count» — текст вокруг (комментарии, отступы)
/// остаётся как был. Меняет только уже записанное в файле поле: новое поле вписывается руками, с комментарием.
/// </summary>
internal static class JsoncPatch
{
    /// <summary>Новый текст; null — поле по такому пути не найдено.</summary>
    public static string? Set(string text, string path, string value)
    {
        var parts = path.Split('.');
        int start = 0, end = text.Length;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var open = FindKey(text, start, end, parts[i], @"\{");
            if (open < 0)
                return null;
            start = open + 1;
            end = MatchingBrace(text, open);
            if (end < 0)
                return null;
        }

        var key = new Regex("\"" + Regex.Escape(parts[parts.Length - 1]) + "\"\\s*:\\s*(\"[^\"]*\"|-?\\d+)");
        for (var m = key.Match(text, start, end - start); m.Success; m = m.NextMatch())
        {
            if (InComment(text, m.Index))
                continue;
            var v = m.Groups[1];
            return text.Substring(0, v.Index) + value + text.Substring(v.Index + v.Length);
        }

        return null;
    }

    /// <summary>Позиция символа после «"key":», подходящего под <paramref name="after"/> (например, «{»); -1 — нет.</summary>
    private static int FindKey(string text, int start, int end, string key, string after)
    {
        var regex = new Regex("\"" + Regex.Escape(key) + "\"\\s*:\\s*" + after);
        for (var m = regex.Match(text, start, end - start); m.Success; m = m.NextMatch())
        {
            if (!InComment(text, m.Index))
                return m.Index + m.Length - 1;
        }

        return -1;
    }

    // Строка до позиции содержит «//» — значит, это комментарий. Блочные /* */ — только в шапке файла, до первой «{»
    private static bool InComment(string text, int index)
    {
        var lineStart = text.LastIndexOf('\n', index) + 1;
        if (text.IndexOf("//", lineStart, index - lineStart, System.StringComparison.Ordinal) >= 0)
            return true;
        var blockOpen = text.LastIndexOf("/*", index, System.StringComparison.Ordinal);
        return blockOpen >= 0 && text.IndexOf("*/", blockOpen, System.StringComparison.Ordinal) > index;
    }

    private static int MatchingBrace(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                i = text.IndexOf('"', i + 1);
                if (i < 0)
                    return -1;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                i = text.IndexOf('\n', i);
                if (i < 0)
                    return -1;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }
}
