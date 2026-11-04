using System;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// Возврат в центр фарма: перс за радиусом фарма (погнался за мобом), а делать нечего (бой ищет цель и не нашёл, копать нечего)
/// не меньше <see cref="IdleBefore"/> — бежим в центр и ждём мобов там. Внутри радиуса — тоже, если нечего делать дольше
/// <see cref="IdleInside"/> и до центра больше <see cref="NearCenter"/>: при большом радиусе на краю мобы из списка клиенту
/// не видны, и перс стоял бы там вечно. Центр в воздухе (точку запоминали в полёте), а перс ниже его больше чем на
/// <see cref="NearCenter"/> (сел копать) — тоже не на месте: снизу мобов в воздухе клиенту не видно; взлетаем и поднимаемся к
/// центру. Радиус 0 — не возвращаемся. Бег фоновый: любое действие боя его перебивает.
/// </summary>
public sealed class ReturnBehavior : IBehavior
{
    // Дальше — центр, видимо, в другом месте (сохранён в другой локации): не бежим через полкарты
    private const float MaxDistance = 300f;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Сколько подряд должно быть нечего делать. После убийства и лута бой на шаг отдаёт ход, а следующего моба выбирает
    /// шагом позже — без паузы бег в центр успевал начаться (и игра — строить путь) и тут же отменялся подходом к мобу.
    /// </summary>
    public static readonly TimeSpan IdleBefore = TimeSpan.FromSeconds(2);

    /// <summary>Внутри радиуса нечего делать столько — идём в центр (пауза между мобами короче).</summary>
    public static readonly TimeSpan IdleInside = TimeSpan.FromSeconds(10);

    /// <summary>Ближе к центру — уже на месте, внутри радиуса никуда не идём. Для центра в воздухе — и снизу по высоте.</summary>
    public const float NearCenter = 15f;
    // Шаги мозга — раз в ~0.25 с; пропуск дольше — ход забирал кто-то другой, «нечего делать» начинается заново
    private static readonly TimeSpan TickGap = TimeSpan.FromSeconds(0.6);

    private DateTime _nextTry = DateTime.MinValue;
    private DateTime _idleSince = DateTime.MinValue;
    private DateTime _lastIdle = DateTime.MinValue;

    public string Name => "возврат";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        if (!c.Settings.Target.ReturnToCenter || c.FarmCenter is not { } center || !c.Runner.Capabilities.Has(Capability.Move))
            return false;
        if (c.Fight.Busy)
            return false;

        var w = c.World;
        if (c.Mine.Any(a => a is MoveAction or FlyAction))
        {
            // Своё ждём — делать всё ещё нечего: взлетели — сразу к центру, без новых 10 с
            _lastIdle = c.Now;
            Status = "возвращаюсь в центр фарма";
            return true;
        }

        var distance = w.Host.Position.HorizontalDistanceTo(center);
        var inside = c.InFarmArea(w.Host.Position);
        var below = BelowAirCenter(c, center);
        if (c.Settings.Target.FarmRadius <= 0 || (inside && distance <= NearCenter && !below) || c.Now < _nextTry || c.Runner.BodyBusy(w) is not null)
            return false;

        if (c.Now - _lastIdle > TickGap)
            _idleSince = c.Now;
        _lastIdle = c.Now;
        if (c.Now - _idleSince < (inside ? IdleInside : IdleBefore))
            return false;
        if (distance > MaxDistance)
        {
            c.Say("return-too-far", $"Центр фарма в {distance:0} м — слишком далеко, не возвращаюсь", LogLevel.Warning, 300);
            return false;
        }

        // Под центром в воздухе на земле — сначала взлететь
        if (below && w.Host.Flying != true)
        {
            var takeoff = c.Send(new FlyAction(up: true) { Priority = ActionPriority.Background });
            if (takeoff == SubmitStatus.Sent)
                c.Log.Info($"Целей рядом нет {IdleInside.TotalSeconds:0} с, центр фарма в воздухе — взлетаю к нему");
            Status = "взлетаю к центру фарма";
            return takeoff is SubmitStatus.Sent or SubmitStatus.AlreadyPending;
        }

        var smart = c.Settings.Target.ReturnPath == ApproachPath.Smart;
        // В воздухе — летим к центру на своей высоте (обычный ход в полёте высоту не держит, а к земле на перелёте — в склон);
        // в воде — плывём прямо в центр: обычный ход держит глубину и до центра не доводит. Ниже центра в воздухе — к нему, с его высотой
        var move = w.Host.Flying == true
            ? new MoveAction(below ? center : center with { Height = w.Host.Position.Height }, tolerance: 2f, fly: true) { Priority = ActionPriority.Background }
            : c.Swimming ? new MoveAction(center, tolerance: 2f, swim: true) { Priority = ActionPriority.Background }
            : new MoveAction(center, tolerance: 2f, smart) { Priority = ActionPriority.Background };
        var sent = c.Send(move);
        if (sent == SubmitStatus.Sent)
            c.Log.Info(inside
                ? below ? $"Целей рядом нет {IdleInside.TotalSeconds:0} с — поднимаюсь к центру фарма, {center.Height - w.Host.Position.Height:0} м вверх"
                : $"Целей рядом нет {IdleInside.TotalSeconds:0} с — иду в центр фарма, {distance:0} м"
                : $"Вне радиуса фарма, целей нет — возвращаюсь в центр, {distance:0} м");
        Status = "возвращаюсь в центр фарма";
        return sent is SubmitStatus.Sent or SubmitStatus.AlreadyPending;
    }

    /// <summary>
    /// Центр фарма в воздухе (точку запоминали в полёте), а перс ниже его больше чем на <see cref="NearCenter"/> и может туда
    /// подняться: летит или стоит на земле, а полётник не «точно нет». Выше центра — не в счёт: мобы в воздухе видны и так.
    /// </summary>
    private static bool BelowAirCenter(BrainContext c, Position center)
    {
        var w = c.World;
        if (c.FarmSpot is not { End: TripEnd.Air } || w.Host.Position.Height >= center.Height - NearCenter)
            return false;
        return w.Host.Flying == true
            || w.Host.Flying == false && w.HasFlyer != false && c.Runner.Capabilities.Has(Capability.FlyTo);
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        // Не дошли (не взлетели) — не долбим заново каждый шаг
        if (outcome.Action is MoveAction or FlyAction && outcome.Status is ActionStatus.Rejected or ActionStatus.Timeout or ActionStatus.Failed)
            _nextTry = c.Now + RetryAfter;
    }

    public void Reset()
    {
        _nextTry = DateTime.MinValue;
        _idleSince = _lastIdle = DateTime.MinValue;
        Status = null;
    }
}
