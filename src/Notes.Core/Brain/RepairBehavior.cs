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
/// <item>Из сумки (<see cref="Settings.RepairSettings.FromBag"/>, сервер умеет — <see cref="Capability.RemoteRepair"/>): открыть
/// лавку кнопкой сумки, «Починить всё», закрыть лавку, как крестиком. Без открытой лавки сервер не чинит: «Служба недоступна».
/// Не починилось — бьём дальше и пробуем снова: через <see cref="Retry"/>, потом вдвое реже, но не реже <see cref="RetryMax"/>.</item>
/// <item>У NPC: долететь до места, где он стоял, когда его выбрали (<see cref="FlightTrip"/> на высоте ремонта; без полётника —
/// пешком), подойти по прямой (в воздухе — подлететь и сесть: говорить можно только на высоте NPC), заговорить, «Починить всё», закрыть окно и лететь обратно в центр фарма (<c>home</c>).</item>
/// </list>
/// Начинает, когда боя нет и никто не занят многоходовым делом (слив опыта, возвращение после смерти); пора сливать опыт — слив
/// первым. У NPC не вышло (NPC не выбран, далеко, не нашёлся, после ремонта всё ещё поношено) — пишет в лог и до следующего
/// «Старт» не пробует.
/// Стоит раньше перелёта при «Старт»: поношено — сначала к NPC, а оттуда уже на фарм.
/// </summary>
public sealed class RepairBehavior(Func<BrainContext, Spot?> home) : IBehavior, IMultiStepBehavior
{
    // Ближе — к NPC идём пешком по прямой, без перелёта
    public const float WalkTo = 30f;
    public const float TalkDistance = 4f;
    // Говорить — только на высоте NPC: из воздуха над ним сервер отвечает «Служба недоступна»
    public const float TalkHeight = 3f;
    // Сели — столько ждём, пока персонаж упадёт до высоты NPC
    public static readonly TimeSpan FallMax = TimeSpan.FromSeconds(5);
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
    // Из сумки не починилось — снова через столько, потом вдвое дольше, но не дольше RetryMax
    public static readonly TimeSpan Retry = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan RetryMax = TimeSpan.FromMinutes(1);
    // Канал чата, в который сервер пишет отказы («Служба недоступна», «Выполняется другая операция»)
    private const int ServerChannel = 10;

    private enum Stage { Idle, ToNpc, Approach, Hello, Repair, Close, Back }

    private Stage _stage;
    private bool _fromBag;
    private bool _gaveUp;
    private FlightTrip? _trip;
    private DateTime? _calmSince;
    // С какого времени у NPC ждём посадки и спуска до его высоты
    private DateTime? _landedAt;
    private DateTime _stageAt;
    // Из сумки: когда пробовать снова и сколько раз подряд не вышло
    private DateTime? _retryAt;
    private int _failures;
    // Последнее сообщение чата до ремонта: ответ сервера — после него
    private int _chatSeen;

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
        if (!s.Enabled || _gaveUp || _retryAt > c.Now || c.InMiddle is not null || c.Fight.Busy || TargetSelector.Aggressor(w) is not null
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
            _chatSeen = LastChat(w);
            if (c.Send(new OpenShopAction()) is not SubmitStatus.Sent and var status)
                return BagFailed(c, $"лавка не открылась ({status})", close: false);
            c.Log.Info($"Прочность {worn:0} % — открываю лавку и чиню");
            return Next(c, Stage.Hello, "ремонт: открываю лавку");
        }

        if (s.Npc is not { } npc)
            return GiveUp(c, "не выбран NPC для ремонта");
        var distance = w.Host.Position.HorizontalDistanceTo(npc.Position);
        if (distance > DeathReturnBehavior.MaxTrip)
            return GiveUp(c, $"{npc.Name} в {distance:0} м — слишком далеко");

        _chatSeen = LastChat(w);
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

        var here = w.Host.Position;
        if (w.Host.Flying == true)
        {
            // В воздухе: долететь до места над NPC на своей высоте (вниз по пути — врезаться в дом) и сесть
            _calmSince = null;
            if (npc.Offset.Horizontal > TalkDistance)
            {
                c.Submit(new MoveAction(Toward(here, npc.Position, NpcStop) with { Height = here.Height }, tolerance: 1f, fly: true));
                Status = $"ремонт: подлетаю к {saved.Name}";
            }
            else
            {
                _landedAt ??= c.Now;
                if (c.Now - _landedAt >= FallMax)
                    return GiveUp(c, $"не сел у {saved.Name}");
                c.Submit(new FlyAction(up: false));
                Status = "ремонт: сажусь";
            }
            return true;
        }

        if (npc.Offset.Horizontal > TalkDistance)
        {
            _calmSince = null;
            c.Submit(new MoveAction(Toward(here, npc.Position, NpcStop), tolerance: 1f));
            Status = $"ремонт: иду к {saved.Name}";
            return true;
        }

        // Рядом по карте, но не на его высоте: после посадки ещё падаем — ждём; так и не спустились (крыша, обрыв) — не чиним.
        // Из воздуха сервер говорить не даёт: «Служба недоступна»
        if (Math.Abs(npc.Offset.Vertical) > TalkHeight)
        {
            _calmSince = null;
            _landedAt ??= c.Now;
            if (c.Now - _landedAt < FallMax)
            {
                Status = $"ремонт: спускаюсь к {saved.Name}";
                return true;
            }
            return GiveUp(c, $"{saved.Name} {(npc.Offset.Vertical < 0 ? "ниже" : "выше")} на {Math.Abs(npc.Offset.Vertical):0} м — не спуститься к нему");
        }
        _landedAt = null;

        if (!Calmed(c))
            return true;
        if (c.Send(new NpcHelloAction(npc.Wid)) != SubmitStatus.Sent)
            return true;
        return Next(c, Stage.Hello, $"ремонт: говорю с {saved.Name}");
    }

    // Окно NPC или лавки открылось — «Починить всё»
    private bool Hello(BrainContext c)
    {
        Status = "ремонт: жду окно";
        if (c.Now - _stageAt < OpenWait)
            return true;
        if (c.Send(new RepairAllAction()) is not SubmitStatus.Sent and var status && _fromBag)
            return BagFailed(c, $"«Починить всё» не ушло ({status})", close: true);
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
        {
            c.Log.Info("Починил");
            (_failures, _retryAt) = (0, null);
        }
        else if (_fromBag)
            return BagFailed(c, $"после «Починить всё» прочность не выросла{ServerSaid(c)}", close: true);
        else
            Remember(c, $"после «Починить всё» прочность не выросла{ServerSaid(c)} — хватает ли денег, чинит ли этот NPC?");
        return Next(c, Stage.Close, "ремонт: закрываю окно");
    }

    // Окно NPC — как кнопкой «Назад»; лавку — как крестиком. Лавку закрываем один раз: не ушло — она просто остаётся открытой
    private bool Close(BrainContext c)
    {
        Status = "ремонт: закрываю окно";
        if (_fromBag)
        {
            if (c.Send(new ShopDialogCloseAction()) is not SubmitStatus.Sent and var status)
                c.Log.Debug($"Ремонт: лавку не закрыл ({status})");
            _stage = Stage.Idle;
            return false;
        }
        if (c.Send(new NpcDialogCloseAction()) != SubmitStatus.Sent)
            return true;
        return GoBack(c);
    }

    // Из сумки не вышло: бьём дальше и пробуем снова, всё реже; лавку (если открывали) закрыть
    private bool BagFailed(BrainContext c, string why, bool close)
    {
        var wait = TimeSpan.FromTicks(Math.Min(RetryMax.Ticks, Retry.Ticks << Math.Min(_failures, 10)));
        _failures++;
        _retryAt = c.Now + wait;
        c.Log.Warning($"Ремонт: {why} — попробую снова через {wait.TotalSeconds:0} с");
        if (close)
            return Next(c, Stage.Close, "ремонт: закрываю окно");
        _stage = Stage.Idle;
        return false;
    }

    private static int LastChat(WorldState w) => w.Chat.Count == 0 ? 0 : w.Chat.Max(m => m.Number);

    // Что сервер ответил в чате после начала ремонта (« — сервер: «Служба недоступна»»); ничего — пусто
    private string ServerSaid(BrainContext c)
    {
        var said = c.World.Chat.Where(m => m.Number > _chatSeen && m.Channel == ServerChannel).Select(m => m.Plain).LastOrDefault();
        return said is null ? "" : $" — сервер: «{said}»";
    }

    private bool GoBack(BrainContext c)
    {
        if (home(c) is not { } spot || c.World.Host.Position.HorizontalDistanceTo(spot.Position) <= WalkTo && !FlightTrip.MustTakeOff(c, spot))
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
        (_calmSince, _landedAt) = (null, null);
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
        (_failures, _retryAt) = (0, null);
        _trip = null;
        _calmSince = null;
        Status = null;
    }
}
