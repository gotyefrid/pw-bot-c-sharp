using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.World;

namespace Notes.Core.Brain.Follow;

/// <summary>
/// «Следовать», телепорт у NPC: главный между двумя снимками «прыгнул» дальше <see cref="Jump"/> и оказался у точки (ближе
/// <see cref="AtPoint"/>) из списка мастера телепортации, который рядом с нами (<see cref="FollowQuestBehavior.NpcRange"/>), — идём к
/// этому мастеру, «привет», просим ту же точку (<see cref="NpcDeal"/>). Вышло — мы у точки. Не вышло (денег или уровня не хватает) —
/// пишем в лог и остаёмся. Мастера с этой точкой рядом нет — руной переноса (<see cref="WorldState.TeleportRunes"/>, первая по
/// профилю), когда нет боя (в бою игра руну не даёт): жмём, ждём каст до <see cref="RuneWait"/>, не вышло — ещё раз, всего
/// <see cref="RuneTries"/> раза. На один телепорт главного — одна попытка.
/// </summary>
public sealed class FollowTeleportBehavior(LeaderBoard board) : IBehavior
{
    /// <summary>Главный сместился дальше этого за снимок — телепорт, а не шаг.</summary>
    public const float Jump = 100f;

    /// <summary>После телепорта игра ставит ровно в точку; с запасом на шаг-другой главного.</summary>
    public const float AtPoint = 20f;

    /// <summary>Руна — каст несколько секунд; не перенесла за это время — жмём ещё.</summary>
    public static readonly System.TimeSpan RuneWait = System.TimeSpan.FromSeconds(20);
    public const int RuneTries = 2;

    private Position? _leaderWas;
    private NpcDeal? _deal;
    private TeleportPoint? _rune;
    private System.DateTime _runeAt;
    private int _runeTries;

    public string Name => "телепорт за главным";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        if (!c.Settings.Follow.Teleport || Leader.Of(c, board) is not { } leader)
        {
            _deal = null;
            _rune = null;
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
        if (_deal is null && _rune is null && was is { } before && before.HorizontalDistanceTo(now) > Jump)
        {
            _deal = Start(c, now);
            if (_deal is null)
                StartRune(c, now);
        }
        if (_rune is not null)
            return Rune(c);
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

    // Мастера с этой точкой рядом нет: точка из таблицы игры — руной
    private void StartRune(BrainContext c, Position leader)
    {
        if (c.World.Waypoints.Where(p => p.Position.HorizontalDistanceTo(leader) <= AtPoint)
                .OrderBy(p => p.Position.HorizontalDistanceTo(leader)).FirstOrDefault() is not { } point)
            return;
        if (c.World.Host.Position.HorizontalDistanceTo(point.Position) <= AtPoint)
            return;
        if (c.World.TeleportRunes.Count == 0)
        {
            c.Log.Warning($"Главный телепортировался в {Title(point)}, а мастера с этой точкой рядом нет и рун переноса нет — остаюсь");
            return;
        }
        c.Log.Info($"Главный телепортировался в {Title(point)} — переношусь руной");
        (_rune, _runeTries, _runeAt) = (point, 0, System.DateTime.MinValue);
    }

    private bool Rune(BrainContext c)
    {
        var point = _rune!;
        var w = c.World;
        Status = $"телепорт: руной в {Title(point)}";
        if (w.Host.Position.HorizontalDistanceTo(point.Position) <= AtPoint)
        {
            c.Log.Info($"Перенёсся руной в {Title(point)}");
            _rune = null;
            return false;
        }
        // Руна в бою не работает: бой — раньше по порядку, ждём его конца
        if (TargetSelector.Aggressor(w) is not null || c.Fight.Busy)
        {
            Status = "телепорт: жду конца боя, потом руна";
            return false;
        }
        if (w.Host.IsCasting || c.Now - _runeAt < RuneWait)
            return true;
        if (_runeTries >= RuneTries || w.TeleportRunes.Count == 0)
        {
            c.Log.Warning($"Телепорт за главным: руной в {Title(point)} не вышло ({(w.TeleportRunes.Count == 0 ? "руны кончились" : $"{RuneTries} раза")}) — остаюсь");
            _rune = null;
            return false;
        }
        foreach (var walking in c.Mine.OfType<MoveAction>().ToList())
            c.Forget(walking);
        if (c.Runner.BodyBusy(w) is not null)
            return true;
        if (c.Send(new TeleportRuneAction(w.TeleportRunes[0], point.Id, point.Name)) == SubmitStatus.Sent)
            (_runeAt, _runeTries) = (c.Now, _runeTries + 1);
        return true;
    }

    private static string Title(TeleportPoint point) => point.Name is { Length: > 0 } name ? $"«{name}»" : $"точку {point.Id}";

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
    }

    public void Reset()
    {
        _leaderWas = null;
        _deal = null;
        _rune = null;
        Status = null;
    }
}
