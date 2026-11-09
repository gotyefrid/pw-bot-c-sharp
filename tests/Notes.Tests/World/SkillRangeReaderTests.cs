using System;
using Notes.Core.Memory;
using Notes.Core.Profiles;
using Notes.Core.World;
using Xunit;

namespace Notes.Tests.World;

public class SkillRangeReaderTests
{
    // Как у «Незаметно» (1.3.6, 09.11): объект скилла → ElementSkill (+4) → описание (+4); функция дальности — в таблице +0x1C
    private const uint Skill = 0x1000, Element = 0x2000, Stub = 0x3000, Table = 0x4000, Function = 0x5000, A = 0x6000, B = 0x6008;

    private static readonly SkillRangeOffsets Offsets = new()
    {
        Element = 4, Stub = 4, Kind = 0x1C, RangeKind = 0x81, Scaled = 0x59, Function = 0x1C,
    };

    // Код «уровень × A + B» из elementskill.dll 1.3.6 (функция «Жалящего роя»), уровень — int в ElementSkill +0x28
    private static readonly byte[] LinearCode =
    [
        0x55, 0x8B, 0xEC, 0x8B, 0x45, 0x08, 0x66, 0x0F, 0x6E, 0x40, 0x28, 0xF3, 0x0F, 0xE6, 0xC0, 0xF2, 0x0F, 0x59, 0x05,
        0x00, 0x60, 0x00, 0x00, 0xF2, 0x0F, 0x58, 0x05, 0x08, 0x60, 0x00, 0x00, 0x66,
    ];

    private static MemoryImage Memory(byte kind, byte rangeKind, int level, byte[] code)
    {
        var m = new MemoryImage();
        foreach (var page in new[] { Skill, Element, Stub, Table, Function, A })
            m.Map(page, MemoryImage.PageSize);
        m.WriteUInt32(Skill + 4, Element);
        m.WriteUInt32(Element + 4, Stub);
        m.WriteUInt32(Element + 0x28, (uint)level);
        m.WriteUInt32(Stub, Table);
        m.TryWrite(Stub + 0x1C, [kind]);
        m.TryWrite(Stub + 0x81, [rangeKind]);
        m.WriteUInt32(Table + 0x1C, Function);
        m.TryWrite(Function, code);
        m.TryWrite(A, BitConverter.GetBytes(0.9));
        m.TryWrite(B, BitConverter.GetBytes(16.5));
        return m;
    }

    [Fact]
    public void AttackSkillRangeIsLevelTimesAPlusB()
    {
        // «Жалящий рой» 1-го уровня — 17,4 м, как в подсказке
        var reader = new SkillRangeReader(Memory(kind: 1, rangeKind: 0, level: 1, LinearCode), Offsets);

        Assert.Equal(17.4f, reader.Read(Skill, 1)!.Value, 3);
    }

    [Fact]
    public void OtherKindUsesSameFunctionUnlessSelfRange()
    {
        // «Исцеление питомца» (тип 10) 5-го уровня — 21 м; скилл на себя (вид дальности 5) — дальности нет
        Assert.Equal(21f, new SkillRangeReader(Memory(10, 0, 5, LinearCode), Offsets).Read(Skill, 5)!.Value, 3);
        Assert.Null(new SkillRangeReader(Memory(2, 5, 1, LinearCode), Offsets).Read(Skill, 1));
    }

    [Fact]
    public void UnknownCodeOrNotFoundGivesNoRange()
    {
        Assert.Null(new SkillRangeReader(Memory(1, 0, 1, [0x55, 0x8B, 0xEC, 0x90, 0xC3]), Offsets).Read(Skill, 1));
        Assert.Null(new SkillRangeReader(Memory(1, 0, 1, LinearCode), new SkillRangeOffsets()).Read(Skill, 1));
    }

    [Fact]
    public void ConstantFunctionIsRead()
    {
        // «fld dword [число]; ret 4»
        var m = Memory(1, 0, 1, [0xD9, 0x05, 0x00, 0x60, 0x00, 0x00, 0xC2, 0x04, 0x00]);
        m.TryWrite(A, BitConverter.GetBytes(25f));

        Assert.Equal(25f, new SkillRangeReader(m, Offsets).Read(Skill, 1));
    }
}
