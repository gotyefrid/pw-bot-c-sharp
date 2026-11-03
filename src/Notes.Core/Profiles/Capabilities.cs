using System;
using System.Collections.Generic;
using System.Linq;

namespace Notes.Core.Profiles;

/// <summary>Возможность бота на сервере: то, что есть не у всех клиентов.</summary>
public enum Capability
{
    /// <summary>Идти в точку: менеджер работ персонажа и три его функции.</summary>
    Move,

    /// <summary>Бег с автопутём (в обход препятствий), как кликом по карте.</summary>
    SmartMove,

    /// <summary>Лететь в точку вместе с её высотой.</summary>
    FlyTo,

    /// <summary>Прервать каст или копание, как Esc.</summary>
    Cancel,

    /// <summary>Отозвать пета в клетку.</summary>
    RecallPet,

    /// <summary>Запустить макрос персонажа, как кнопкой на панели.</summary>
    Macro,

    /// <summary>
    /// Открыть банк из сумки где угодно (кнопка «Банк» в окне сумки), переложить туда (в пустую ячейку и на стопку) и закрыть
    /// «отменой»: есть только на клиентах Comeback.
    /// </summary>
    RemoteBank,

    /// <summary>Встать после смерти в ближнем городе — первая кнопка окна смерти.</summary>
    ReviveInTown,

    /// <summary>Чинить надетое у NPC: прочность вещей, разговор с NPC и «Починить всё».</summary>
    Repair,

    /// <summary>Чинить из сумки, без NPC: лавка открывается кнопкой в окне сумки (есть только на клиентах Comeback).</summary>
    RemoteRepair,
}

/// <summary>
/// Что бот может делать на этом сервере — одно место для всех вопросов «умеет ли». Недоступное — с причиной для лога и
/// окна. До подключения — по профилю (<see cref="FromProfile"/>), после — по тому, что реально найдено в клиенте
/// (<see cref="Calls.GameCaller.Capabilities"/>).
/// </summary>
public sealed class Capabilities
{
    private readonly IReadOnlyDictionary<Capability, string> _missing;

    private Capabilities(IReadOnlyDictionary<Capability, string> missing) => _missing = missing;

    /// <summary>Всё доступно (тесты, подставные действия).</summary>
    public static Capabilities All { get; } = new(new Dictionary<Capability, string>());

    public bool Has(Capability capability) => !_missing.ContainsKey(capability);

    /// <summary>Почему недоступно; null — доступно.</summary>
    public string? WhyNot(Capability capability) => _missing.TryGetValue(capability, out var why) ? why : null;

    /// <summary>Копия без одной возможности.</summary>
    public Capabilities Without(Capability capability, string why)
        => new(new Dictionary<Capability, string>(_missing.ToDictionary(m => m.Key, m => m.Value)) { [capability] = why });

    /// <summary>Что заявлено в профиле (функция с адресом) — пока клиент не подключён.</summary>
    public static Capabilities FromProfile(ProfileData profile)
        => From(profile, name => profile.Functions.TryGetValue(name, out var f) && f.Rva != 0 ? null : $"в профиле нет функции {name}");

    /// <param name="whyNoFunction">Почему функцию нельзя вызывать; null — можно.</param>
    internal static Capabilities From(ProfileData profile, Func<string, string?> whyNoFunction)
    {
        string? First(params string[] names) => names.Select(whyNoFunction).FirstOrDefault(why => why is not null);

        var move = profile.Host.WorkMan == 0
            ? "в профиле нет менеджера работ персонажа"
            : First(GameFunctions.WorkCreate, GameFunctions.WorkMoveSetDestination, GameFunctions.WorkStart);
        var missing = new Dictionary<Capability, string>();
        void Set(Capability capability, string? why)
        {
            if (why is not null)
                missing[capability] = why;
        }

        Set(Capability.Move, move);
        Set(Capability.SmartMove, move ?? (profile.MoveTypes.Smart == 0 ? "у сервера нет автопути" : null));
        Set(Capability.FlyTo, move ?? (profile.MoveTypes.Fly == 0 ? "полёт в точку с высотой не найден для этого сервера" : null));
        Set(Capability.Cancel, First(GameFunctions.CancelAction));
        Set(Capability.RecallPet, First(GameFunctions.RecallPet));
        Set(Capability.RemoteBank, First(GameFunctions.OpenRemotePanel, GameFunctions.BankExchange, GameFunctions.BankPutCount, GameFunctions.CancelAction)
                                   ?? (profile.Host.Bank == 0 ? "в профиле нет банка" : null));
        Set(Capability.Macro, First(GameFunctions.HostApplyComboSkill)
                              ?? (profile.Macros.List == 0 || profile.Base.Configs == 0 ? "в профиле нет макросов" : null));
        Set(Capability.ReviveInTown, First(GameFunctions.ReviveInTown));
        var durability = profile.Host.Equipment == 0 || profile.Inventory.ItemMaxDurability == 0 ? "в профиле нет прочности надетых вещей" : null;
        Set(Capability.Repair, durability ?? First(GameFunctions.NpcHello, GameFunctions.RepairItem, GameFunctions.NpcDialogClose));
        Set(Capability.RemoteRepair, durability ?? First(GameFunctions.OpenRemotePanel, GameFunctions.RepairItem, GameFunctions.CancelAction));
        return new Capabilities(missing);
    }
}
