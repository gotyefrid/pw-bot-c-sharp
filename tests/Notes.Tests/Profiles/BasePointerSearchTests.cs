using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Profiles;
using Xunit;

namespace Notes.Tests.Profiles;

/// <summary>Указатель на базу — самая читаемая кодом переменная из данных exe.</summary>
public class BasePointerSearchTests
{
    private const uint Base = 0x00CB_552C, Other = 0x00C6_B4CC, Cookie = 0x00C7_3A00, ReadOnly = 0x00B6_2A24;

    // Данные exe (.data), куда игра пишет; ReadOnly — вне их (таблица импорта)
    private static bool IsData(uint address) => address is >= 0x00C6_B000 and < 0x00CD_4000;

    // mov eax, [адрес]; mov eax, [eax + 0x1C]
    private static byte[] MovEax(uint address) => [0xA1, .. BitConverter.GetBytes(address), 0x8B, 0x40, 0x1C];

    // mov ecx, [адрес]; mov ecx, [ecx + 0x20]
    private static byte[] MovEcx(uint address) => [0x8B, 0x0D, .. BitConverter.GetBytes(address), 0x8B, 0x49, 0x20];

    // Начало функции с проверкой стека: mov eax, [cookie]; xor eax, ebp
    private static byte[] CookieCheck() => [0xA1, .. BitConverter.GetBytes(Cookie), 0x33, 0xC5];

    private static byte[] Code(params (Func<byte[]> Instruction, int Times)[] parts)
        => parts.SelectMany(p => Enumerable.Range(0, p.Times).SelectMany(_ => p.Instruction())).ToArray();

    [Fact]
    public void MostReadDataVariableIsBase()
    {
        var code = Code((CookieCheck, 20), (() => MovEax(Base), 5), (() => MovEcx(Base), 3), (() => MovEax(Other), 3),
            (() => MovEax(ReadOnly), 30));

        var found = BasePointerSearch.Rank([code], IsData);

        Assert.Equal(new BasePointer(Base, 8, Other, 3), found);
    }

    [Fact]
    public void NoClearLeaderIsNotGuessed()
    {
        var code = Code((() => MovEax(Base), 4), (() => MovEcx(Other), 3));

        var error = Assert.Throws<InvalidOperationException>(() => BasePointerSearch.Rank([code], IsData));
        Assert.Contains("почти одинаково", error.Message);
    }

    [Fact]
    public void CodeWithoutDataReadsIsNotAClient()
        => Assert.Throws<InvalidOperationException>(() => BasePointerSearch.Rank([Code((() => MovEax(ReadOnly), 5))], IsData));
}
