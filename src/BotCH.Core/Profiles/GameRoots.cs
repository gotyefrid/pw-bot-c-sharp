using System;
using BotCH.Core.Memory;

namespace BotCH.Core.Profiles;

/// <summary>
/// Корни игры в памяти клиента — одна цепочка на всех (чтение мира, вызовы функций, Probe):
/// база = [<see cref="Pointer"/>], game = [база + <see cref="BaseOffsets.Game"/>],
/// персонаж = [game + <see cref="HostOffsets.Struct"/>], связь с сервером = [база + <see cref="BaseOffsets.Session"/>].
/// </summary>
public sealed class GameRoots
{
    private readonly IMemory _memory;
    private readonly Lazy<uint> _pointer;
    private readonly ProfileData _profile;

    /// <param name="pointer">Где лежит указатель на базу — адрес в данных exe.</param>
    public GameRoots(IMemory memory, uint pointer, ProfileData profile)
        : this(memory, new Lazy<uint>(() => pointer), profile)
    {
    }

    private GameRoots(IMemory memory, Lazy<uint> pointer, ProfileData profile)
    {
        _memory = memory;
        _pointer = pointer;
        _profile = profile;
    }

    /// <summary>
    /// Указатель на базу ищется по коду клиента (<see cref="BasePointerSearch"/>) при первом чтении, один раз. Не нашёлся —
    /// каждое чтение от базы — <see cref="MemoryAccessException"/> с причиной, как любая непрочитанная память.
    /// </summary>
    public static GameRoots Find(IMemory memory, uint moduleBase, ProfileData profile)
        => new(memory, new Lazy<uint>(() =>
        {
            try
            {
                return BasePointerSearch.Find(memory, moduleBase).Address;
            }
            catch (InvalidOperationException e)
            {
                throw new MemoryAccessException(moduleBase, e.Message);
            }
        }), profile);

    /// <summary>Где лежит указатель на базу; не нашёлся — <see cref="MemoryAccessException"/>.</summary>
    public uint Pointer => _pointer.Value;

    /// <summary>База; не читается — <see cref="MemoryAccessException"/>.</summary>
    public uint Base() => _memory.ReadUInt32(Pointer);

    /// <summary>game (0 — игра не загружена); не читается — <see cref="MemoryAccessException"/>.</summary>
    public uint Game() => _memory.ReadPointerChain(Pointer, _profile.Base.Game);

    public bool TryBase(out uint address)
    {
        address = 0;
        try
        {
            return _memory.TryReadUInt32(Pointer, out address);
        }
        catch (MemoryAccessException)
        {
            return false;
        }
    }

    /// <summary>Персонаж в мире; false — не читается или его нет (экран выбора, загрузка).</summary>
    public bool TryHost(out uint host)
    {
        host = 0;
        return TryBase(out var root)
               && _memory.TryReadUInt32(root + _profile.Base.Game, out var game)
               && _memory.TryReadUInt32(game + _profile.Host.Struct, out host)
               && host != 0;
    }

    /// <summary>Объект связи с сервером; false — не читается или <see cref="BaseOffsets.Session"/> нет в профиле.</summary>
    public bool TrySession(out uint session)
    {
        session = 0;
        return _profile.Base.Session != 0
               && TryBase(out var root)
               && _memory.TryReadUInt32(root + _profile.Base.Session, out session)
               && session != 0;
    }
}
