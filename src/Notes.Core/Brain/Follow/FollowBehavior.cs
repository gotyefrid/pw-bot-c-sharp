using System;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.Core.Brain.Follow;

/// <summary>Главный в режиме «Следовать»: его снимок с табло (<see cref="LeaderBoard"/>).</summary>
internal static class Leader
{
    /// <summary>Снимок главного; null — не выбран, это сам персонаж или его не видно (вкладка закрыта, клиент не отвечает).</summary>
    public static WorldState? Of(BrainContext c, LeaderBoard board)
    {
        var nick = c.Settings.Follow.Leader;
        if (nick.Length == 0 || Self(c))
            return null;
        return board.Find(nick, c.Now);
    }

    public static bool Self(BrainContext c) => string.Equals(c.Settings.Follow.Leader, c.World.Host.Name, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// «Следовать»: идти за главным. Отстал дальше <see cref="Settings.FollowSettings.Distance"/> — идёт к нему (по земле — «умно», в
/// воздухе — летит с его высотой, в воде — плывёт) и останавливается примерно на половине этого расстояния; главный ушёл от точки,
/// к которой идём, — к новому месту. Главный взлетел — взлетает (если включено «летать»); главный на земле, а мы рядом в воздухе —
/// садится. Главный дальше <see cref="TooFar"/> (телепорт) — ждёт на месте. Главного не видно — ждёт, бота не останавливает.
/// Ход не занимает, пока рядом: бой, лут и задания помощника — раньше по порядку.
/// </summary>
public sealed class FollowBehavior(LeaderBoard board) : IBehavior
{
    /// <summary>Дальше этого за главным не идём: телепорт в город, другая локация — ждём, пока вернётся.</summary>
    public const float TooFar = 1000f;

    // Главный отошёл от точки, к которой идём, дальше этого — идти к новому месту, не чаще RetargetPeriod
    private const float RetargetDistance = 5f;
    private static readonly TimeSpan RetargetPeriod = TimeSpan.FromSeconds(1);
    // В воздухе над землёй главного садиться — когда по горизонтали ближе этого (сверх дистанции следования)
    private const float LandMargin = 5f;

    private DateTime _movedAt;

    public string Name => "следовать";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        var s = c.Settings.Follow;
        if (s.Leader.Length == 0)
            return c.RequestStop("главный не выбран — выберите его в настройках режима «Следовать»");
        if (Leader.Self(c))
            return c.RequestStop($"«{s.Leader}» — это сам персонаж; главным выберите другого");

        if (Leader.Of(c, board) is not { } leader)
        {
            Status = $"не вижу «{s.Leader}»: его вкладка должна быть подключена в этом окне бота — жду";
            c.Say("follow-lost", $"Не вижу главного «{s.Leader}» — жду (его вкладка должна быть подключена в этом окне бота)", seconds: 60);
            Stop(c);
            return false;
        }

        var name = leader.Host.Name;
        var me = c.World.Host;
        var at = leader.Host.Position;
        var leaderFlies = leader.Host.Flying == true;
        var iFly = me.Flying == true;

        if (s.Fly && leaderFlies && me.Flying == false && c.World.HasFlyer != false)
        {
            Status = $"{name} взлетел — взлетаю";
            return c.Submit(new FlyAction(up: true));
        }

        if (iFly && (!leaderFlies || !s.Fly) && me.Position.HorizontalDistanceTo(at) <= s.Distance + LandMargin)
        {
            Status = $"{name} на земле — сажусь";
            return c.Submit(new FlyAction(up: false));
        }

        var distance = Distance(c, at);
        if (distance <= s.Distance)
        {
            Status = $"рядом с {name}, {distance:0} м";
            return false;
        }

        if (distance > TooFar)
        {
            Status = $"{name} далеко ({distance:0} м) — жду";
            Stop(c);
            return false;
        }

        Status = $"иду за {name}: {distance:0} м";
        return Go(c, at, Math.Max(FollowSettings.MinDistance, s.Distance / 2f));
    }

    // Идти к главному; уже идём к точке рядом с ним — ждём, он ушёл от неё — к новому месту
    private bool Go(BrainContext c, Position at, float stop)
    {
        var w = c.World;
        var move = w.Host.Flying == true ? new MoveAction(at, stop, fly: true)
            : c.Swimming ? new MoveAction(at, stop, swim: true)
            : new MoveAction(at, stop, smart: true);

        if (c.Mine.OfType<MoveAction>().FirstOrDefault() is { } walking)
        {
            if (walking.Point.DistanceTo(at) <= RetargetDistance || c.Now - _movedAt < RetargetPeriod)
                return true;
            var replaced = c.Replace(move);
            if (replaced == SubmitStatus.Sent)
                _movedAt = c.Now;
            return replaced is SubmitStatus.Sent or SubmitStatus.AlreadyPending;
        }

        var sent = c.Send(move);
        if (sent == SubmitStatus.Sent)
            _movedAt = c.Now;
        return sent is SubmitStatus.Sent or SubmitStatus.AlreadyPending;
    }

    private static void Stop(BrainContext c)
    {
        foreach (var walking in c.Mine.OfType<MoveAction>().ToList())
            c.Forget(walking);
    }

    // Как в ходе в точку: по земле — по горизонтали, в воздухе и в воде — с высотой
    private static float Distance(BrainContext c, Position at)
        => c.World.Host.Flying == true || c.Swimming ? c.World.Host.Position.DistanceTo(at) : c.World.Host.Position.HorizontalDistanceTo(at);

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
    }

    public void Reset()
    {
        _movedAt = default;
        Status = null;
    }
}
