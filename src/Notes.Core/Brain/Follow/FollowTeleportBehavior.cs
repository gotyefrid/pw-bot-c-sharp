using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.World;

namespace Notes.Core.Brain.Follow;

/// <summary>
/// «Следовать», телепорт у NPC: главный между двумя снимками «прыгнул» дальше <see cref="Jump"/> и оказался у точки (ближе
/// <see cref="AtPoint"/>) из списка мастера телепортации, который рядом с нами (<see cref="FollowQuestBehavior.NpcRange"/>), — идём к
/// этому мастеру, «привет», просим ту же точку (<see cref="NpcDeal"/>). Вышло — мы у точки. Не вышло (денег или уровня не хватает) —
/// пишем в лог и остаёмся. На один телепорт главного — одна попытка.
/// </summary>
public sealed class FollowTeleportBehavior(LeaderBoard board) : IBehavior
{
    /// <summary>Главный сместился дальше этого за снимок — телепорт, а не шаг.</summary>
    public const float Jump = 100f;

    /// <summary>После телепорта игра ставит ровно в точку; с запасом на шаг-другой главного.</summary>
    public const float AtPoint = 20f;

    private Position? _leaderWas;
    private NpcDeal? _deal;

    public string Name => "телепорт за главным";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        if (!c.Settings.Follow.Teleport || Leader.Of(c, board) is not { } leader)
        {
            _deal = null;
            return false;
        }

        if (c.Runner.IsPending(ActionSlot.Bank))
        {
            Status = "телепорт: жду ответа NPC";
            return true;
        }

        var was = _leaderWas;
        var now = leader.Host.Position;
        _leaderWas = now;
        if (_deal is null && was is { } before && before.HorizontalDistanceTo(now) > Jump)
            _deal = Start(c, now);
        if (_deal is not { } deal)
            return false;

        var step = deal.Tick(c);
        Status = $"телепорт: {deal.What}";
        if (step == DealStep.Going)
            return true;
        if (step == DealStep.Failed)
            c.Log.Warning($"Телепорт за главным: {deal.What} — не вышло ({deal.Failed}); денег или уровня не хватает? Остаюсь");
        _deal = null;
        return true;
    }

    // Главный появился далеко: у какого мастера рядом с нами есть эта точка
    private static NpcDeal? Start(BrainContext c, Position leader)
    {
        var found = c.World.Npcs
            .Where(n => n.Kind == NpcKind.Npc && n.Teleports is not null && n.Offset.Direct <= FollowQuestBehavior.NpcRange)
            .OrderBy(n => n.Offset.Direct)
            .SelectMany(n => n.Teleports!.Select(t => (Npc: n, Point: t)))
            .Where(x => x.Point.Position.HorizontalDistanceTo(leader) <= AtPoint)
            .Select(x => ((NpcInfo, TeleportPoint)?)x)
            .FirstOrDefault();
        if (found is not var (npc, point))
            return null;

        var title = point.Name is { Length: > 0 } name ? $"«{name}»" : $"точку {point.Id}";
        c.Log.Info($"Главный телепортировался в {title} — телепортируюсь у {npc.Name} (строка {point.Index}, цена {point.Fee})");
        return new NpcDeal(npc.Tid, point.Index, $"в {title}",
            () => new NpcTeleportAction(point.Index, point.Name),
            w => w.Host.Position.HorizontalDistanceTo(point.Position) <= AtPoint,
            $"Телепортировался в {title}",
            $"телепортировал в {title}");
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
    }

    public void Reset()
    {
        _leaderWas = null;
        _deal = null;
        Status = null;
    }
}
