using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BotCH.Core.Profiles;

/// <summary>
/// Байтовый шаблон начала функции: «56 6A 06 E8 ?? ?? ?? ??». «??» — любой байт
/// (адреса внутри кода, которые меняются при пересборке клиента).
/// Нужен для двух вещей: перед вызовом убедиться, что по адресу та самая функция,
/// и найти функцию, если в новом клиенте она переехала.
/// </summary>
public sealed class Signature
{
    // null — «любой байт»
    private readonly byte?[] _bytes;

    public int Length => _bytes.Length;

    /// <summary>Сколько байтов заданы точно (не «??») — чем больше, тем надёжнее поиск.</summary>
    public int FixedCount => _bytes.Count(b => b.HasValue);

    private Signature(byte?[] bytes) => _bytes = bytes;

    public static Signature Parse(string text)
    {
        var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            throw new FormatException("Пустая сигнатура");

        var bytes = parts.Select(part => part is "??" or "?"
            ? (byte?)null
            : byte.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
                ? b
                : throw new FormatException($"Неверный байт «{part}» в сигнатуре «{text}»")).ToArray();

        if (!bytes.Any(b => b.HasValue))
            throw new FormatException($"В сигнатуре «{text}» нет ни одного точного байта");

        return new Signature(bytes);
    }

    public bool Matches(byte[] data, int offset = 0)
    {
        if (offset < 0 || offset + _bytes.Length > data.Length)
            return false;

        for (var i = 0; i < _bytes.Length; i++)
        {
            if (_bytes[i] is { } expected && data[offset + i] != expected)
                return false;
        }

        return true;
    }

    /// <summary>Все позиции в <paramref name="data"/>, где шаблон совпал (не больше <paramref name="limit"/>).</summary>
    public IReadOnlyList<int> FindAll(byte[] data, int limit = 16)
    {
        var hits = new List<int>();
        var anchor = Array.FindIndex(_bytes, b => b.HasValue);
        var anchorByte = _bytes[anchor]!.Value;

        var lastStart = data.Length - _bytes.Length;

        // Быстро прыгаем к следующему вхождению первого точного байта, потом сверяем шаблон целиком
        for (var start = 0; start <= lastStart; start++)
        {
            var found = Array.IndexOf(data, anchorByte, start + anchor, lastStart - start + 1);
            if (found < 0)
                break;

            start = found - anchor;
            if (Matches(data, start))
            {
                hits.Add(start);
                if (hits.Count >= limit)
                    break;
            }
        }

        return hits;
    }

    public override string ToString() => string.Join(" ", _bytes.Select(b => b?.ToString("X2") ?? "??"));
}
