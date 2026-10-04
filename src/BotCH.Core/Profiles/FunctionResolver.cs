using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Memory;

namespace BotCH.Core.Profiles;

public enum FunctionStatus
{
    /// <summary>Сигнатура совпала по адресу из профиля.</summary>
    Found,
    /// <summary>По адресу из профиля чужой код, но функция однозначно найдена по сигнатуре в другом месте (клиент обновился).</summary>
    Relocated,
    /// <summary>Сигнатура подходит к нескольким местам — какое верное, неизвестно. Не вызывать.</summary>
    Ambiguous,
    /// <summary>Не найдена. Не вызывать.</summary>
    NotFound,
    /// <summary>В профиле функции нет (на этом сервере не поддерживается).</summary>
    NotInProfile,
}

public sealed record FunctionLocation(string Name, FunctionStatus Status, uint Address, string Details)
{
    /// <summary>Можно вызывать: функция точно та.</summary>
    public bool IsUsable => Status is FunctionStatus.Found or FunctionStatus.Relocated;
}

/// <summary>
/// Находит функции клиента: сначала по адресу из профиля (быстро, проверка сигнатуры),
/// при несовпадении — поиском сигнатуры по коду модуля. Код читается один раз, лениво.
/// </summary>
public sealed class FunctionResolver
{
    private readonly IMemory _memory;
    private readonly uint _moduleBase;
    private readonly Lazy<IReadOnlyList<(uint Address, byte[] Bytes)>> _code;

    public FunctionResolver(IMemory memory, uint moduleBase)
    {
        _memory = memory;
        _moduleBase = moduleBase;
        _code = new Lazy<IReadOnlyList<(uint, byte[])>>(ReadCode);
    }

    public FunctionLocation Resolve(string name, GameFunction? function)
    {
        if (function is null || function.Rva == 0)
            return new FunctionLocation(name, FunctionStatus.NotInProfile, 0, "нет в профиле");

        var expected = _moduleBase + function.Rva;
        if (function.Signature is not { } signature)
            return new FunctionLocation(name, FunctionStatus.NotFound, expected, "в профиле нет сигнатуры — вызывать нельзя");

        var actual = new byte[signature.Length];
        if (_memory.TryRead(expected, actual, actual.Length) && signature.Matches(actual))
            return new FunctionLocation(name, FunctionStatus.Found, expected, "сигнатура совпала");

        var hits = FindEverywhere(signature);
        return hits.Count switch
        {
            0 => new FunctionLocation(name, FunctionStatus.NotFound, expected, "сигнатура не найдена"),
            1 => new FunctionLocation(name, FunctionStatus.Relocated, hits[0], $"переехала с 0x{expected:X8}"),
            _ => new FunctionLocation(name, FunctionStatus.Ambiguous, expected,
                $"сигнатура подходит к {hits.Count}{(hits.Count >= 16 ? "+" : "")} местам: "
                + string.Join(", ", hits.Take(4).Select(h => $"0x{h:X8}"))),
        };
    }

    public IReadOnlyList<FunctionLocation> ResolveAll(ProfileData data)
        => data.Functions.Select(f => Resolve(f.Key, f.Value)).ToList();

    /// <summary>Все адреса в коде модуля, где совпала сигнатура.</summary>
    public IReadOnlyList<uint> FindEverywhere(Signature signature)
    {
        var hits = new List<uint>();
        foreach (var (address, bytes) in _code.Value)
        {
            hits.AddRange(signature.FindAll(bytes, 16 - hits.Count).Select(offset => address + (uint)offset));
            if (hits.Count >= 16)
                break;
        }

        return hits;
    }

    private IReadOnlyList<(uint, byte[])> ReadCode()
        => ModuleSections.Read(_memory, _moduleBase)
            .Where(s => s.IsCode && s.Size > 0)
            .Select(s => (s.Address, ModuleSections.ReadRegion(_memory, s.Address, s.Size)))
            .ToList();
}
