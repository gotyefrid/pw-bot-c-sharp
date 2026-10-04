using BotCH.Core.Memory;

namespace BotCH.Core.Profiles;

/// <summary>
/// Корни игры в памяти клиента — одна цепочка на всех (чтение мира, вызовы функций, unfreeze, Probe):
/// база = [модуль + <see cref="BaseOffsets.BasePointer"/>], game = [база + <see cref="BaseOffsets.Game"/>],
/// персонаж = [game + <see cref="HostOffsets.Struct"/>], связь с сервером = [база + <see cref="BaseOffsets.Session"/>].
/// </summary>
public sealed class GameRoots(IMemory memory, uint moduleBase, ProfileData profile)
{
    private uint BasePointer => moduleBase + profile.Base.BasePointer;

    /// <summary>База; не читается — <see cref="MemoryAccessException"/>.</summary>
    public uint Base() => memory.ReadUInt32(BasePointer);

    /// <summary>game (0 — игра не загружена); не читается — <see cref="MemoryAccessException"/>.</summary>
    public uint Game() => memory.ReadPointerChain(BasePointer, profile.Base.Game);

    public bool TryBase(out uint address) => memory.TryReadUInt32(BasePointer, out address);

    /// <summary>Персонаж в мире; false — не читается или его нет (экран выбора, загрузка).</summary>
    public bool TryHost(out uint host)
    {
        host = 0;
        return TryBase(out var root)
               && memory.TryReadUInt32(root + profile.Base.Game, out var game)
               && memory.TryReadUInt32(game + profile.Host.Struct, out host)
               && host != 0;
    }

    /// <summary>Объект связи с сервером; false — не читается или <see cref="BaseOffsets.Session"/> нет в профиле.</summary>
    public bool TrySession(out uint session)
    {
        session = 0;
        return profile.Base.Session != 0
               && TryBase(out var root)
               && memory.TryReadUInt32(root + profile.Base.Session, out session)
               && session != 0;
    }
}
