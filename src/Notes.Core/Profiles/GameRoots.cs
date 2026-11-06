using System;
using Notes.Core.Memory;

namespace Notes.Core.Profiles;

/// <summary>
/// Корни игры в памяти клиента — одна цепочка на всех (чтение мира, вызовы функций, Probe):
/// база = [<see cref="Pointer"/>], game = [база + <see cref="BaseOffsets.Game"/>],
/// персонаж = [game + <see cref="HostOffsets.Struct"/>], связь с сервером = [база + <see cref="BaseOffsets.Session"/>].
/// </summary>
public sealed class GameRoots
{
    private readonly IMemory _memory;
    private readonly Lazy<uint> _pointer;
    private readonly Lazy<uint?> _chatList;
    private readonly Lazy<uint?> _questLists;
    private readonly Lazy<uint?> _expTable;
    private readonly ProfileData _profile;

    /// <param name="pointer">Где лежит указатель на базу — адрес в данных exe.</param>
    /// <param name="chatList">Где список сообщений чата (null — не искать).</param>
    /// <param name="questLists">Где переменная с объектом списков заданий (null — не искать).</param>
    /// <param name="expTable">Таблица «опыт до следующего уровня», как <see cref="ExpTable"/> (null — не искать).</param>
    public GameRoots(IMemory memory, uint pointer, ProfileData profile, uint? chatList = null, uint? questLists = null, uint? expTable = null)
        : this(memory, new Lazy<uint>(() => pointer), new Lazy<uint?>(() => chatList), new Lazy<uint?>(() => questLists),
            new Lazy<uint?>(() => expTable), profile)
    {
    }

    private GameRoots(IMemory memory, Lazy<uint> pointer, Lazy<uint?> chatList, Lazy<uint?> questLists, Lazy<uint?> expTable,
        ProfileData profile)
    {
        _memory = memory;
        _pointer = pointer;
        _chatList = chatList;
        _questLists = questLists;
        _expTable = expTable;
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
        }), new Lazy<uint?>(() => FindData(memory, moduleBase, profile.Chat.Find, profile.Chat.At)),
            new Lazy<uint?>(() => FindData(memory, moduleBase, profile.Quests.AvailableFind, profile.Quests.AvailableAt)),
            new Lazy<uint?>(() => FindData(memory, moduleBase, profile.Host.ExpTableFind, profile.Host.ExpTableAt)), profile);

    // Глобальная переменная — по коду, один раз. Не нашлась — то, что от неё зависит (чат, доступные задания, опыт до уровня), просто не читается
    private static uint? FindData(IMemory memory, uint moduleBase, Signature? find, uint at)
    {
        if (find is null)
            return null;
        try
        {
            return CodeDataSearch.Find(memory, moduleBase, find, at);
        }
        catch (Exception e) when (e is InvalidOperationException or MemoryAccessException)
        {
            return null;
        }
    }

    /// <summary>Где лежит указатель на базу; не нашёлся — <see cref="MemoryAccessException"/>.</summary>
    public uint Pointer => _pointer.Value;

    /// <summary>Где список сообщений чата; null — на этом сервере не найден.</summary>
    public uint? ChatList() => _chatList.Value;

    /// <summary>Где переменная с объектом списков заданий (доступные); null — на этом сервере не найдена.</summary>
    public uint? QuestLists() => _questLists.Value;

    /// <summary>Таблица «опыт до следующего уровня»: нужно на уровне L — [таблица + L × 4]; null — на этом сервере не найдена.</summary>
    public uint? ExpTable() => _expTable.Value;

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

    /// <summary>Окно разговора с NPC; false — не в мире, не читается или <see cref="UiOffsets"/> нет в профиле.</summary>
    public bool TryNpcDialog(out uint dialog) => TryDialog(_profile.Ui.NpcDialog, out dialog);

    /// <summary>Окно лавки; false — не в мире, не читается или <see cref="UiOffsets.ShopDialog"/> нет в профиле.</summary>
    public bool TryShopDialog(out uint dialog) => TryDialog(_profile.Ui.ShopDialog, out dialog);

    // Окно мира по его полю в CECGameUIMan
    private bool TryDialog(uint field, out uint dialog)
    {
        dialog = 0;
        var ui = _profile.Ui;
        return ui.Managers != 0 && field != 0
               && TryBase(out var root)
               && _memory.TryReadUInt32(root + _profile.Base.Game, out var game)
               && _memory.TryReadUInt32(game + ui.Managers, out var managers) && managers != 0
               // Активный менеджер 1 — окна мира (0 — экран входа)
               && _memory.TryReadUInt32(managers + ui.Active, out var active) && active == 1
               && _memory.TryReadUInt32(managers + ui.InGame, out var inGame) && inGame != 0
               && _memory.TryReadUInt32(inGame + field, out dialog)
               && dialog != 0;
    }

    /// <summary>
    /// Текущий менеджер окон: на экране входа — окна входа, в мире (<paramref name="inWorld"/>) — окна мира, 0 — грузится.
    /// false — не читается или <see cref="UiOffsets.Managers"/> нет в профиле.
    /// </summary>
    public bool TryUiManager(out uint manager, out bool inWorld)
    {
        manager = 0;
        inWorld = false;
        var ui = _profile.Ui;
        if (ui.Managers == 0 || !TryBase(out var root)
            || !_memory.TryReadUInt32(root + _profile.Base.Game, out var game) || game == 0
            || !_memory.TryReadUInt32(game + ui.Managers, out var managers) || managers == 0
            || !_memory.TryReadUInt32(managers + ui.Active, out var active)
            || !_memory.TryReadUInt32(managers + ui.InGame, out manager))
            return false;

        inWorld = active == 1;
        return true;
    }

    /// <summary>Очередь сообщений игры; false — не читается или <see cref="BaseOffsets.GameMessages"/> нет в профиле.</summary>
    public bool TryMessages(out uint messages)
    {
        messages = 0;
        return _profile.Base.GameMessages != 0
               && TryBase(out var root)
               && _memory.TryReadUInt32(root + _profile.Base.Game, out var game) && game != 0
               && _memory.TryReadUInt32(game + _profile.Base.GameMessages, out messages)
               && messages != 0;
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
