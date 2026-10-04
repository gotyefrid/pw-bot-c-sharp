using System.Collections.Generic;
using BotCH.Core.Profiles;
using BotCH.Core.World;

namespace BotCH.Core.Actions;

/// <summary>
/// Исполнитель действий — только посмотреть: что ждёт подтверждения и чем занято тело. Отправляют через сам
/// <see cref="ActionRunner"/> с хозяином (поведения — через BrainContext, он хозяина и ставит).
/// </summary>
public interface IPendingActions
{
    /// <summary>Что умеет клиент: какие функции найдены.</summary>
    Capabilities Capabilities { get; }

    /// <summary>Все ждущие действия.</summary>
    IReadOnlyList<GameAction> Pending { get; }

    /// <summary>Ждущие действия этого хозяина.</summary>
    IReadOnlyList<GameAction> PendingOf(IActionOwner owner);

    bool IsPending(ActionKey key);

    /// <summary>Ждёт ли что-нибудь в слоте (любой скилл, любое действие с петом) — чьё угодно.</summary>
    bool IsPending(ActionSlot slot);

    /// <summary>Действие, которое сейчас занимает тело; null — свободно.</summary>
    GameAction? BodyAction { get; }

    /// <summary>Чем занято тело: ждущее действие, каст или копание; null — свободно.</summary>
    string? BodyBusy(WorldState now);
}
