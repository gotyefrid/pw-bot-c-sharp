using System;
using System.Linq;

namespace BotCH.Core.Profiles;

/// <summary>
/// Делает из байтов начала функции шаблон, переживающий пересборку клиента:
/// адреса (call/jmp rel32 и абсолютные адреса внутри модуля) заменяются на «??».
/// </summary>
public static class SignatureBuilder
{
    /// <param name="code">Байты, начиная с первой инструкции функции.</param>
    /// <param name="moduleBase">Начало модуля — значения внутри [moduleBase, moduleBase+moduleSize) считаем адресами.</param>
    public static Signature FromCode(byte[] code, int length, uint moduleBase, int moduleSize)
    {
        length = Math.Min(length, code.Length);
        var wildcard = new bool[length];

        void Mask(int from)
        {
            for (var i = from; i < from + 4 && i < length; i++)
                wildcard[i] = true;
        }

        for (var i = 0; i < length; i++)
        {
            // call rel32 / jmp rel32
            if (code[i] is 0xE8 or 0xE9 && !wildcard[i])
                Mask(i + 1);
            // jcc rel32: 0F 80..8F
            else if (code[i] == 0x0F && i + 1 < length && code[i + 1] is >= 0x80 and <= 0x8F && !wildcard[i])
                Mask(i + 2);
        }

        // Абсолютные адреса (глобальные переменные, vtable): 4 байта, похожие на адрес в модуле, и только там,
        // где инструкция действительно берёт адрес. Иначе за адрес можно принять, например, «C7 06 64 00» —
        // а «64 00» здесь номер команды, который как раз и отличает одну функцию от соседней
        for (var i = 1; i + 4 <= code.Length && i < length; i++)
        {
            var value = BitConverter.ToUInt32(code, i);
            if (value >= moduleBase && value < moduleBase + (uint)moduleSize && TakesAddress(code, i))
                Mask(i);
        }

        var text = string.Join(" ", Enumerable.Range(0, length).Select(i => wildcard[i] ? "??" : code[i].ToString("X2")));
        return Signature.Parse(text);
    }

    // Байты перед 4-байтным значением говорят, что это адрес/константа инструкции:
    //   A1/A3 — mov eax,[адрес] / mov [адрес],eax;  68 — push адрес;  B8..BF — mov reg, адрес;
    //   ModRM с mod=00, rm=101 (05, 0D, 15 … 3D) — операнд [адрес], например «8B 0D EC 3E 9B 00» = mov ecx,[0x9B3EEC];
    //   C7 xx — mov [reg], константа (так записывают vtable в конструкторах)
    private static bool TakesAddress(byte[] code, int i)
    {
        var prev = code[i - 1];
        return prev is 0xA1 or 0xA3 or 0x68 or >= 0xB8 and <= 0xBF
               || (prev & 0xC7) == 0x05
               || i >= 2 && code[i - 2] == 0xC7;
    }
}
