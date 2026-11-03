using System;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// «Ремонтировать снаряжение» (<see cref="Settings.RepairSettings"/>): самая поношенная надетая вещь ниже порога прочности
/// (<see cref="WornPercent"/>) — починить всё.
/// <list type="bullet">
/// <item>Из сумки (<see cref="Settings.RepairSettings.FromBag"/>, сервер умеет — <see cref="Capability.RemoteRepair"/>): просто
/// «Починить всё», где стоим. Лавку не открываем: сервер чинит и без неё (проверено на 1.4.6), а её окно «отмена» не закрывает.</item>
/// <item>У NPC: долететь до места, где он стоял, когда его выбрали (<see cref="FlightTrip"/> на высоте ремонта; без полётника —
/// пешком), подойти по прямой, заговорить, «Починить всё», закрыть окно и лететь обратно в центр фарма (<c>home</c>).</item>
/// </list>
/// Начинает, когда боя нет и никто не занят многоходовым делом (слив опыта, возвращение после смерти); пора сливать опыт — слив
/// первым. Не вышло (NPC не выбран, далеко, не нашёлся, после ремонта всё ещё поношено) — пишет в лог и до следующего «Старт» не
/// пробует.
/// Стоит раньше перелёта при «Старт»: поношено — сначала к NPC, а оттуда уже на фарм.
/// </summary>
public sealed class RepairBehavior(Func<BrainContext, Spot?> home) : IBehavior, IMultiStepBehavior
{
    // Ближе — к NPC идём пешком по прямой, без перелёта
    public const float WalkTo = 30f;
    public const float TalkDistance = 4f;
    // Куда встать у NPC: не упираться в него
    private const float NpcStop = 2.5f;
    // NPC ищем у места, где он стоял, когда выбрали
    private const float NpcSearch = 30f;
    // После бега сервер ещё с секунду отвечает «Выполняется другая операция»
    public static readonly TimeSpan Calm = TimeSpan.FromSeconds(1);
    // Окно NPC открывает ответ сервера
    public static readonly TimeSpan OpenWait = TimeSpan.FromSeconds(1.5);
    // Прочность в памяти обновляет ответ сервера на ремонт
    public static readonly TimeSpan RepairWait = TimeSpan.FromSeconds(3);

    private enum Stage { Idle, ToNpc, Approach, Hello, Repair, Close, Back }

    private Stage _stage;
    private bool _fromBag;
    private bool _gaveUp;
    private FlightTrip? _trip;
    private DateTime? _calmSince;
    private DateTime _stageAt;

    public string Name => "ремонт";
    public string? Status { get; private set; }
    public string? InMiddle => _stage == Stage.Idle ? null : "ремонт";

    /// <summary>Прочность самой поношенной надетой вещи, % (вещи «не чинится» не в счёт); null — прочность не знаем.</summary>
    public static double? WornPercent(WorldState w)
    {
        var gear = w.Equipment.Where(i => i.MaxDurability > 0 && !i.NoRepair).ToList();
        return gear.Count == 0 ? null : gear.Min(i => 100.0 * i.Durability / i.MaxDurability);
    }

    public bool Tick(BrainContext c)
    {
        Status = null;
        return _stage switch
        {
            Stage.Idle => Start(c),
            Stage.ToNpc => Fly(c),
            Stage.Approach => Approach(c),
            Stage.Hello => Hello(c),
            Stage.Repair => Repair(c),
            Stage.Close => Close(c),
            _ => Back(c),
        };
    }

    private bool Start(BrainContext c)
    {
        var s = c.Settings.Repair;
        var w = c.World;
        if (!s.Enabled || _gaveUp || c.InMiddle is not null || c.Fight.Busy || TargetSelector.Aggressor(w) is not null
            || ExpDrainBehavior.Due(c) || WornPercent(w) is not { } worn || worn >= s.BelowPercent)
        {
            _calmSince = null;
            return false;
        }

        _fromBag = s.FromBag && c.Runner.Capabilities.Has(Capability.RemoteRepair);
        if (!_fromBag && c.Runner.Capabilities.WhyNot(Capability.Repair) is { } why)
            return GiveUp(c, $"чинить не умею ({why})");

        if (_fromBag)
        {
            if (!Calmed(c))
                return true;
            if (c.Send(new RepairAllAction()) is not SubmitStatus.Sent and var status)
                return GiveUp(c, $"«Починить всё» не ушло ({status})");
            c.Log.Info($"Прочность {worn:0} % — чиню из сумки");
            return Next(c, Stage.Repair, "ремонт: чиню");
        }

        if (s.Npc is not { } npc)
            return GiveUp(c, "не выбран NPC для ремонта");
        var distance = w.Host.Position.HorizontalDistanceTo(npc.Position);
        if (distance > DeathReturnBehavior.MaxTrip)
            return GiveUp(c, $"{npc.Name} в {distance:0} м — слишком далеко");

        c.Log.Info($"Прочность {worn:0} % — лечу чинить к {npc.Name} ({distance:0} м)");
        if (distance <= WalkTo)
            return Next(c, Stage.Approach, "ремонт: иду к NPC");
        _trip = new FlightTrip(new Spot(npc.Position, TripEnd.Ground), s.FlightHeight * 10);
        _stage = Stage.ToNpc;
        return Fly(c);
    }

    private bool Fly(BrainContext c)
    {
        var npc = c.Settings.Repair.Npc;
        Status = $"ремонт: лечу к {npc?.Name}, {c.World.Host.Position.HorizontalDistanceTo(_trip!.To):0} м";
        switch (_trip.Step(c))
        {
            case TripStep.Going:
                return true;
            case TripStep.Arrived:
                _trip = null;
                return Next(c, Stage.Approach, "ремонт: иду к NPC");
            default:
                var failed = _trip.Failed;
                _trip = null;
                return GiveUp(c, $"не добрался до {npc?.Name}: {failed}");
        }
    }

    // У места: найти NPC (тот же вид, ближе всех к месту), подойти по прямой, постоять секунду и заговорить
    private bool Approach(BrainContext c)
    {
        var w = c.World;
        if (c.Mine.Count > 0)
        {
            Status = "ремонт: иду к NPC";
            return true;
        }
        if (c.Settings.Repair.Npc is not { } saved)
            return GiveUp(c, "не выбран NPC для ремонта");

        var npc = w.Npcs.Where(n => n.Kind == NpcKind.Npc && saved.Is(n) && n.Position.HorizontalDistanceTo(saved.Position) <= NpcSearch)
            .OrderBy(n => n.Offset.Horizontal).FirstOrDefault();
        if (npc is null)
            return GiveUp(c, $"{saved.Name} на месте нет");

        if (npc.Offset.Horizontal > TalkDistance)
        {
            _calmSince = null;
            c.Submit(new MoveAction(Toward(w.Host.Position, npc.Position, NpcStop), tolerance: 1f));
            Status = $"ремонт: иду к {saved.Name}";
            return true;
        }

        if (!Calmed(c))
            return true;
        if (c.Send(new NpcHelloAction(npc.Wid)) != SubmitStatus.Sent)
            return true;
        return Next(c, Stage.Hello, $"ремонт: говорю с {saved.Name}");
    }

    // Окно NPC открылось — «Починить всё»
    private bool Hello(BrainContext c)
    {
        Status = "ремонт: жду окно";
        if (c.Now - _stageAt < OpenWait)
            return true;
        c.Send(new RepairAllAction());
        return Next(c, Stage.Repair, "ремонт: чиню");
    }

    // Прочность восстановилась (или вышло время) — закрыть окно
    private bool Repair(BrainContext c)
    {
        Status = "ремонт: чиню";
        var repaired = WornPercent(c.World) is not { } worn || worn >= c.Settings.Repair.BelowPercent;
        if (!repaired && c.Now - _stageAt < RepairWait)
            return true;

        if (repaired)
            c.Log.Info("Починил");
        else
            Remember(c, _fromBag ? "после «Починить всё» прочность не выросла — хватает ли денег?"
                : "после «Починить всё» прочность не выросла — хватает ли денег, чинит ли этот NPC?");
        if (_fromBag)
        {
            // Окон нет — закрывать нечего
            _stage = Stage.Idle;
            return false;
        }
        return Next(c, Stage.Close, "ремонт: закрываю окно");
    }

    // Окно NPC — как кнопкой «Назад»
    private bool Close(BrainContext c)
    {
        Status = "ремонт: закрываю окно";
        if (c.Send(new NpcDialogCloseAction()) != SubmitStatus.Sent)
            return true;
        return GoBack(c);
    }

    private bool GoBack(BrainContext c)
    {
        if (home(c) is not { } spot || c.World.Host.Position.HorizontalDistanceTo(spot.Position) <= WalkTo)
        {
            _stage = Stage.Idle;
            return false;
        }
        c.Log.Info("Лечу обратно на фарм");
        _trip = new FlightTrip(spot, c.Settings.Repair.FlightHeight * 10);
        _stage = Stage.Back;
        return Back(c);
    }

    private bool Back(BrainContext c)
    {
        if (_trip is null)
        {
            _stage = Stage.Idle;
            return false;
        }
        Status = $"ремонт: лечу на фарм, {c.World.Host.Position.HorizontalDistanceTo(_trip.To):0} м";
        switch (_trip.Step(c))
        {
            case TripStep.Going:
                return true;
            case TripStep.Arrived:
                c.Log.Info("Вернулся на фарм");
                break;
            default:
                c.Log.Warning($"Не добрался до фарма: {_trip.Failed} — остаюсь здесь");
                break;
        }
        _trip = null;
        _stage = Stage.Idle;
        return false;
    }

    // Персонаж постоял без дела секунду: иначе сервер ответит «Выполняется другая операция»
    private bool Calmed(BrainContext c)
    {
        if (c.Runner.BodyBusy(c.World) is { } busy)
        {
            _calmSince = null;
            Status = $"ремонт: жду, пока освободится ({busy})";
            return false;
        }
        _calmSince ??= c.Now;
        Status = "ремонт: жду";
        return c.Now - _calmSince >= Calm;
    }

    private bool Next(BrainContext c, Stage stage, string status)
    {
        (_stage, _stageAt, Status) = (stage, c.Now, status);
        _calmSince = null;
        return true;
    }

    // Не вышло — до следующего «Старт» не пробуем; от NPC — обратно на фарм
    private bool GiveUp(BrainContext c, string why)
    {
        Remember(c, why);
        var atNpc = _stage is Stage.ToNpc or Stage.Approach;
        _trip = null;
        if (atNpc)
            return GoBack(c);
        _stage = Stage.Idle;
        return false;
    }

    private void Remember(BrainContext c, string why)
    {
        _gaveUp = true;
        c.Log.Warning($"Ремонт: {why} — до следующего «Старт» не чиню");
    }

    // Точка на линии от перса к цели, не доходя до неё stop метров; высота — цели (земля у неё)
    private static Position Toward(Position from, Position to, float stop)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = (float)Math.Sqrt(dx * dx + dy * dy);
        var keep = length <= stop ? 0f : (length - stop) / length;
        return new Position(from.X + dx * keep, to.Height, from.Y + dy * keep);
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome) => _trip?.OnOutcome(c, outcome);

    public void Reset()
    {
        _stage = Stage.Idle;
        _gaveUp = false;
        _trip = null;
        _calmSince = null;
        Status = null;
    }
}
