using System;
using System.Collections.Generic;
using Notes.Core.Memory;
using Notes.Core.Profiles;

namespace Notes.Core.World;

/// <summary>
/// Дальность скилла — та, что в подсказке («Дальность: 17.4 м»). Готовым числом её в памяти нет: клиент считает её кодом
/// (ElementSkill::GetPrayRange, elementskill.dll), у каждого скилла своя маленькая функция в описании скилла (stub). Повторяем
/// расчёт по памяти, без вызова игры: объект скилла → описание → тип и функция дальности; функцию узнаём по её коду и берём
/// числа прямо из него.
/// <para>
/// GetPrayRange (1.3.6 elementskill.dll RVA 0x106830): тип описания (байт <see cref="SkillRangeOffsets.Kind"/>) 1 или 3 — атака
/// или проклятие: при флаге <see cref="SkillRangeOffsets.Scaled"/> дальность ещё пересчитывается (не знаем — null), иначе функция;
/// 8 — функция; остальные — по виду дальности (байт <see cref="SkillRangeOffsets.RangeKind"/>): 2 и 5 — на себя (дальности нет),
/// иначе функция. Функция — [[описание] + <see cref="SkillRangeOffsets.Function"/>], аргумент — объект скилла.
/// </para>
/// <para>
/// Знакомые функции: «уровень × A + B» (55 8B EC 8B 45 08 66 0F 6E 40 <c>ур</c> F3 0F E6 C0 F2 0F 59 05 <c>&amp;A</c> F2 0F 58 05
/// <c>&amp;B</c> — уровень int в объекте скилла +<c>ур</c>, A и B — double; так у 97 скиллов 1.3.6, у «Жалящего роя» 299 и
/// «Исцеления питомца» 330: ×0,9 + 16,5, 1-й уровень — 17,4 м, сверено с подсказкой 09.11) и «число» (D9 05 <c>&amp;f</c> C2 04 00).
/// Другая функция — дальность неизвестна (null), бот ведёт себя как без неё.
/// </para>
/// </summary>
public sealed class SkillRangeReader(IMemory memory, SkillRangeOffsets offsets)
{
    private const int CodeSize = 32;

    // Функция дальности по адресу → как считать: уровень × A + B (Level — смещение уровня) или число (Level = null, A = 0)
    private readonly Dictionary<uint, (uint? Level, double A, double B)?> _functions = [];

    // Объект скилла и его уровень в обёртке → дальность: считаем заново, только когда уровень сменился
    private readonly Dictionary<uint, (int Level, float? Range)> _cache = [];

    public bool Known => offsets.Element != 0;

    /// <summary>Дальность скилла по объекту из списка скиллов перса; null — на себя, не знаем или не прочиталось.</summary>
    public float? Read(uint skill, int level)
    {
        if (!Known || skill == 0)
            return null;
        if (_cache.TryGetValue(skill, out var cached) && cached.Level == level)
            return cached.Range;

        var range = Compute(skill);
        _cache[skill] = (level, range);
        return range;
    }

    private float? Compute(uint skill)
    {
        if (!memory.TryReadUInt32(skill + offsets.Element, out var element) || element == 0
            || !memory.TryReadUInt32(element + offsets.Stub, out var stub) || stub == 0)
            return null;

        var kind = Byte(stub + offsets.Kind);
        if (kind is 1 or 3)
        {
            if (Byte(stub + offsets.Scaled) != 0)
                return null;
        }
        else if (kind != 8 && Byte(stub + offsets.RangeKind) is 2 or 5)
        {
            return null;
        }

        if (!memory.TryReadUInt32(stub, out var table) || !memory.TryReadUInt32(table + offsets.Function, out var function))
            return null;
        if (!_functions.TryGetValue(function, out var formula))
            _functions[function] = formula = Recognize(function);
        if (formula is not { } f)
            return null;
        if (f.Level is not { } levelAt)
            return (float)f.B;
        return memory.TryReadUInt32(element + levelAt, out var level) ? (float)((int)level * f.A + f.B) : null;
    }

    private (uint? Level, double A, double B)? Recognize(uint function)
    {
        var code = new byte[CodeSize];
        if (!memory.TryRead(function, code, code.Length))
            return null;
        if (Parse(code) is not { } parsed)
            return null;
        if (parsed.Level is null)
            return memory.TryReadUInt32(parsed.AAddress, out var bits) ? (null, 0, BitConverter.ToSingle(BitConverter.GetBytes(bits), 0)) : null;

        var a = new byte[8];
        var b = new byte[8];
        return memory.TryRead(parsed.AAddress, a, 8) && memory.TryRead(parsed.BAddress, b, 8)
            ? (parsed.Level, BitConverter.ToDouble(a, 0), BitConverter.ToDouble(b, 0))
            : null;
    }

    /// <summary>
    /// Разбор кода функции дальности: «уровень × A + B» — смещение уровня и адреса A и B; «число» — Level null, адрес числа
    /// (float) в AAddress. Незнакомый код — null.
    /// </summary>
    internal static (uint? Level, uint AAddress, uint BAddress)? Parse(byte[] code)
    {
        byte[] linear = [0x55, 0x8B, 0xEC, 0x8B, 0x45, 0x08, 0x66, 0x0F, 0x6E, 0x40];
        if (code.Length >= 31 && Starts(code, 0, linear) && Starts(code, 11, [0xF3, 0x0F, 0xE6, 0xC0, 0xF2, 0x0F, 0x59, 0x05])
            && Starts(code, 23, [0xF2, 0x0F, 0x58, 0x05]))
            return (code[10], BitConverter.ToUInt32(code, 19), BitConverter.ToUInt32(code, 27));

        if (code.Length >= 9 && code[0] == 0xD9 && code[1] == 0x05 && Starts(code, 6, [0xC2, 0x04, 0x00]))
            return (null, BitConverter.ToUInt32(code, 2), 0);

        return null;
    }

    private static bool Starts(byte[] code, int at, byte[] expected)
    {
        for (var i = 0; i < expected.Length; i++)
            if (code[at + i] != expected[i])
                return false;
        return true;
    }

    private byte Byte(uint address)
    {
        var b = new byte[1];
        return memory.TryRead(address, b, 1) ? b[0] : (byte)0;
    }
}
