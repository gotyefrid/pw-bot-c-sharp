using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Brain;
using Notes.Core.Settings;
using Notes.Core.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Notes.Core.Clicker;

/// <summary>
/// Действие кликера: его настройки (какую клавишу, сколько ждать) и как его выполнить (<see cref="Start"/>). Поля меняются
/// через <see cref="SettingsPart.Set{T}"/> — окно привязано к ним напрямую и узнаёт о правке.
/// <para>
/// Новое действие: класс-наследник (поля, <see cref="Describe"/>, <see cref="Start"/>, при нужде <see cref="Problem"/> и
/// <see cref="Normalize"/>), строка в <see cref="ClickerKinds.Actions"/>, в окне — шаблон его полей. Остальное не трогается.
/// </para>
/// </summary>
public abstract class ClickerAction : SettingsPart
{
    public const int MaxIntervalSeconds = 24 * 3600;

    private int _minIntervalSeconds;

    /// <summary>
    /// Выполнять не чаще раза в столько секунд (общее у всех действий): раньше — шаг пропускается, как с невыполненным условием.
    /// Считается от начала прошлого выполнения. 0 — без ограничения; в файл пишется, только когда задано.
    /// </summary>
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int MinIntervalSeconds { get => _minIntervalSeconds; set => Set(ref _minIntervalSeconds, value); }

    /// <summary>Для таблицы шагов: само действие и, если задано, «не чаще раза в N с».</summary>
    public string DescribeWithInterval()
        => MinIntervalSeconds > 0 ? $"{Describe()}, не чаще раза в {MinIntervalSeconds} с" : Describe();

    /// <summary>Вид в файле настроек («PressKey») — по нему файл читается обратно (<see cref="ClickerJson"/>).</summary>
    [JsonProperty(Order = -2)]
    public string Kind => KindId();

    protected virtual string KindId() => ClickerKinds.Actions.IdOf(GetType());

    /// <summary>Для списка в окне и лога: «клавиша F1», «пауза 1 с».</summary>
    public abstract string Describe();

    /// <summary>Почему выполнять нечего (клавиша не выбрана); null — можно. Такой шаг пропускается.</summary>
    [JsonIgnore]
    public virtual string? Problem => null;

    /// <summary>Начать выполнение — новое на каждый запуск шага, со своим состоянием (<see cref="ClickerRuns"/> — готовые).</summary>
    public abstract IClickerRun Start(BrainContext c);

    /// <summary>Значения — в допустимые пределы (после ручной правки файла или старой версии).</summary>
    public virtual void Normalize()
    {
    }
}

/// <summary>Нажать клавишу в окне игры (<see cref="KeyAction"/>): ушло нажатие — сделано, следующий шаг — в тот же ход.</summary>
public sealed class PressKeyAction : ClickerAction
{
    private string _key = "";

    /// <summary>Клавиша, как на клавиатуре: «F1», «Ctrl+2» (<see cref="KeyStroke"/>). Пусто — не выбрана.</summary>
    public string Key { get => _key; set => Set(ref _key, value ?? ""); }

    /// <summary>Клавиша; null — не выбрана, не разобрать или её нельзя жать (<see cref="KeyStroke.Forbidden"/>).</summary>
    [JsonIgnore]
    public KeyStroke? Stroke => KeyStroke.TryParse(Key, out var stroke) && stroke.Forbidden is null ? stroke : null;

    public override string? Problem => Stroke is null ? "клавиша не выбрана" : null;

    public override string Describe() => Stroke is { } stroke ? $"клавиша {stroke}" : "клавиша не выбрана";

    public override IClickerRun Start(BrainContext c)
        => Stroke is { } stroke ? ClickerRuns.Send(new KeyAction(stroke), doneWhenSent: true) : ClickerRuns.Failed(Problem!);

    public override void Normalize() => Key = Stroke?.ToString() ?? "";
}

/// <summary>
/// Пауза: следующий шаг — через столько миллисекунд. Время кликер сверяет по снимкам (раз в 250 мс): пауза короче
/// снимка всё равно ждёт следующего.
/// </summary>
public sealed class PauseAction : ClickerAction
{
    public const int MaxMilliseconds = 24 * 3600 * 1000;

    private int _milliseconds = 1000;

    public int Milliseconds { get => _milliseconds; set => Set(ref _milliseconds, value); }

    public override string Describe() => $"пауза {Milliseconds} мс";

    public override IClickerRun Start(BrainContext c) => ClickerRuns.Wait(TimeSpan.FromMilliseconds(Milliseconds));

    public override void Normalize() => Milliseconds = Math.Min(MaxMilliseconds, Math.Max(0, Milliseconds));
}

/// <summary>
/// Действие, итог которого видно по игре (предмет пропал с земли), — у него есть галочка «Ждать подтверждения выполнения»
/// (<see cref="WaitConfirmed"/>). Без неё — как клавиша: ушло в игру — сделано, следующий шаг в тот же ход. С ней — следующий
/// шаг только после подтверждения; не подтвердилось — в лог «не вышло», и дальше.
/// </summary>
public abstract class ConfirmableAction : ClickerAction
{
    private bool _waitConfirmed;

    /// <summary>Ждать подтверждения. В файл пишется, только когда включено.</summary>
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool WaitConfirmed { get => _waitConfirmed; set => Set(ref _waitConfirmed, value); }

    /// <summary>Для окна: само действие, с ожиданием — «…, жду подтверждения»: в таблице видно, что шаг ждёт.</summary>
    public sealed override string Describe() => WaitConfirmed ? $"{What()}, жду подтверждения" : What();

    /// <summary>Само действие для окна, без ожидания.</summary>
    protected abstract string What();

    /// <summary>Отправить действие игры — ждать его итога или нет, по галочке.</summary>
    protected IClickerRun Send(GameAction action) => ClickerRuns.Send(action, doneWhenSent: !WaitConfirmed);
}

/// <summary>
/// Поднять с земли ближайший предмет из <see cref="Items"/> (пусто — любой, и монеты) — «как мышкой» (<see cref="PickupAction"/>):
/// подбежит и поднимет игра сама. Подтверждение — предмет пропал с земли. Поднимать нечего — сделано, без предупреждения.
/// Радиуса нет: как далеко — задаёт условие «Рядом лежит предмет».
/// </summary>
public sealed class PickUpItemAction : ConfirmableAction
{
    private ObservableCollection<string> _items = [];

    public PickUpItemAction() => _items.CollectionChanged += ItemsChanged;

    /// <summary>Названия или группы предметов (<see cref="GroundItemChoice"/>). Меняют на месте (выбор в окне) — о правке говорит сам.</summary>
    public ObservableCollection<string> Items
    {
        get => _items;
        set
        {
            _items.CollectionChanged -= ItemsChanged;
            _items = value ?? [];
            _items.CollectionChanged += ItemsChanged;
            Changed(nameof(Items));
        }
    }

    private void ItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Changed(nameof(Items));

    protected override string What() => Items.Count == 0 ? "поднять ближайший предмет" : $"поднять: {string.Join(", ", Items)}";

    public override IClickerRun Start(BrainContext c)
        => GroundItemChoice.NearestItem(c.World, Items) is { } item
            ? Send(new PickupAction(item))
            : ClickerRuns.Skipped("рядом нечего поднять");

    public override void Normalize()
    {
        if (!Items.SequenceEqual(MobNameFilter.Clean(Items)))
            Items = new ObservableCollection<string>(MobNameFilter.Clean(Items));
    }
}

/// <summary>
/// Копать ближайшее из <see cref="Resources"/> (пусто — что угодно копаемое) — «как мышкой» (<see cref="GatherAction"/>): подбежит
/// и накопает игра сама. Ресурсы и «нересурсы» (квестовые, ящики) — без проверки кирки, уровня и квеста: что выбрано, то и
/// копает, нельзя — откажет игра. Подтверждение — в сумке прибавилось или ресурс пропал с земли. Копать нечего — сделано, без
/// предупреждения. Радиуса нет: как далеко — задаёт условие «Рядом можно копать».
/// </summary>
public sealed class GatherResourceAction : ConfirmableAction
{
    private ObservableCollection<string> _resources = [];

    public GatherResourceAction() => _resources.CollectionChanged += ResourcesChanged;

    /// <summary>Названия ресурсов. Меняют на месте (выбор в окне) — о правке говорит сам.</summary>
    public ObservableCollection<string> Resources
    {
        get => _resources;
        set
        {
            _resources.CollectionChanged -= ResourcesChanged;
            _resources = value ?? [];
            _resources.CollectionChanged += ResourcesChanged;
            Changed(nameof(Resources));
        }
    }

    private void ResourcesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Changed(nameof(Resources));

    protected override string What() => Resources.Count == 0 ? "копать ближайший ресурс" : $"копать: {string.Join(", ", Resources)}";

    public override IClickerRun Start(BrainContext c)
        => GroundItemChoice.NearestResource(c.World, Resources) is { } resource
            ? Send(new GatherAction(resource))
            : ClickerRuns.Skipped("копать нечего");

    public override void Normalize()
    {
        if (!Resources.SequenceEqual(MobNameFilter.Clean(Resources)))
            Resources = new ObservableCollection<string>(MobNameFilter.Clean(Resources));
    }
}

/// <summary>
/// Взять в цель ближайшего из <see cref="Targets"/> (пусто — кого угодно: моба, NPC, чужого пета) — как кликом
/// (<see cref="SelectTargetAction"/>). Ближайший уже в цели — в игру ничего не шлёт; взять некого — сделано, без предупреждения.
/// В цели другой, а подходящий ближе — переключит: чтобы не сбивать взятую цель, шагу ставят условие «цели нет».
/// Подтверждение — в цели тот, кого брали. Радиуса нет: как далеко — задаёт условие «Рядом можно взять цель».
/// </summary>
public sealed class TakeTargetAction : ConfirmableAction
{
    private ObservableCollection<string> _targets = [];

    public TakeTargetAction() => _targets.CollectionChanged += TargetsChanged;

    /// <summary>Названия мобов, NPC, петов. Меняют на месте (выбор в окне) — о правке говорит сам.</summary>
    public ObservableCollection<string> Targets
    {
        get => _targets;
        set
        {
            _targets.CollectionChanged -= TargetsChanged;
            _targets = value ?? [];
            _targets.CollectionChanged += TargetsChanged;
            Changed(nameof(Targets));
        }
    }

    private void TargetsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Changed(nameof(Targets));

    protected override string What() => Targets.Count == 0 ? "взять ближайшую цель" : $"взять в цель: {string.Join(", ", Targets)}";

    public override IClickerRun Start(BrainContext c)
    {
        if (TargetChoice.Nearest(c.World, Targets) is not { } target)
            return ClickerRuns.Skipped("взять в цель некого");

        return c.World.Host.TargetWid == target.Wid
            ? ClickerRuns.Skipped($"«{target.Name}» уже в цели")
            : Send(new SelectTargetAction(target));
    }

    public override void Normalize()
    {
        if (!Targets.SequenceEqual(MobNameFilter.Clean(Targets)))
            Targets = new ObservableCollection<string>(MobNameFilter.Clean(Targets));
    }
}

/// <summary>
/// Покормить призванного пета кормом <see cref="Food"/> (пусто — самым простым: меньше всех даёт верности) — как из сумки
/// (<see cref="UseItemAction"/>). Голоден ли пет, не смотрит: это задаёт условие «Пет голоден». Корм на перезарядке — в игру
/// ничего не шлёт, сделано; выбранного нет в сумке — «не вышло», другим кормом не подменяет. Подтверждение — стопка корма
/// уменьшилась.
/// </summary>
public sealed class FeedPetAction : ConfirmableAction
{
    private string _food = "";

    /// <summary>Название корма, как в игре; пусто — самый простой из сумки.</summary>
    public string Food { get => _food; set => Set(ref _food, value ?? ""); }

    protected override string What() => Food.Length == 0 ? "кормить пета любым кормом" : $"кормить пета: {Food}";

    public override IClickerRun Start(BrainContext c)
    {
        var world = c.World;
        if (world.Pet is not { IsSummoned: true })
            return ClickerRuns.Failed("пет не призван");
        if (world.Host.PetFoodCooldownMs > 0)
            return ClickerRuns.Skipped($"корм на перезарядке ещё {world.Host.PetFoodCooldownMs / 1000.0:0} с");

        var food = world.Inventory
            .Where(i => i.IsPetFood && i.Count > 0 && (Food.Length == 0 || string.Equals(i.Name?.Trim(), Food, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(i => i.FoodLoyalty)
            .FirstOrDefault();
        if (food is null)
            return ClickerRuns.Failed(Food.Length == 0 ? "нет корма в сумке" : $"корма «{Food}» нет в сумке");

        return Send(new UseItemAction(food, ItemUse.PetFood));
    }

    public override void Normalize() => Food = Food.Trim();
}

/// <summary>
/// Скилл персонажа — как кнопкой на панели (<see cref="SkillButtonAction"/>): по текущей цели, до дальности персонаж подбежит
/// сам, бафы на себя цели не требуют. Итог не ждём: ушёл в игру — сделано. На перезарядке — в игру ничего не шлёт, сделано;
/// скилла нет среди изученных (сценарий другого персонажа) — «не вышло». Персонаж кастует другое — ждёт конца каста
/// (общее правило: свой каст не сбиваем).
/// </summary>
public sealed class UseSkillAction : ClickerAction
{
    private int _skill;
    private string _skillName = "";

    /// <summary>ID скилла; 0 — не выбран.</summary>
    public int Skill { get => _skill; set => Set(ref _skill, value); }

    /// <summary>Название, как в игре, — запоминается при выборе: в таблице видно, какой скилл, и без подключения к игре.</summary>
    public string SkillName { get => _skillName; set => Set(ref _skillName, value ?? ""); }

    private string Title => ClickerSkill.Title(Skill, SkillName);

    public override string? Problem => Skill <= 0 ? "скилл не выбран" : null;

    public override string Describe() => Skill <= 0 ? "скилл не выбран" : $"использовать {Title}";

    public override IClickerRun Start(BrainContext c)
        => Skill <= 0 ? ClickerRuns.Failed(Problem!)
            : c.World.Skill(Skill) switch
            {
                null => ClickerRuns.Failed($"{Title} — не изучен"),
                { IsReady: false } s => ClickerRuns.Skipped($"{Title} на перезарядке ещё {s.CooldownLeftMs / 1000.0:0.0} с"),
                _ => ClickerRuns.Send(new SkillButtonAction(Skill, $"скилл {Title}"), doneWhenSent: true),
            };

    public override void Normalize()
    {
        Skill = Math.Max(0, Skill);
        SkillName = SkillName.Trim();
    }
}

/// <summary>
/// Остановить идущий макрос (какой бы ни шёл). Не идёт — в игру ничего не шлём, сделано. Подтверждение — макрос не идёт; идёт ли,
/// игра не говорит (поле не найдено для сервера) — ушло, и сделано. Каст, который уже идёт, не прерывает.
/// </summary>
public sealed class StopMacroAction : ClickerAction
{
    public override string Describe() => "остановить макрос";

    public override IClickerRun Start(BrainContext c)
        => c.World.MacroRunning == false ? ClickerRuns.Skipped("макрос не идёт") : ClickerRuns.Send(new StopMacroGameAction());
}

/// <summary>
/// Действие с членом пати <see cref="Member"/> — по нику (сценарий не ломается после перезахода), ищется в пати из снимка; себя
/// выбрать нельзя. Ушло в игру — сделано, следующий шаг в тот же ход. Персонаж не в пати или выбранного в ней нет — «не вышло».
/// </summary>
public abstract class PartyMemberAction : ClickerAction
{
    private string _member = "";

    /// <summary>Ник члена пати; пусто — не выбран.</summary>
    public string Member
    {
        get => _member;
        set
        {
            Set(ref _member, value ?? "");
            Changed(nameof(MemberShown));
        }
    }

    /// <summary>Для окна: что показать в поле, когда выбранного сейчас нет в пати (или ещё не выбран).</summary>
    [JsonIgnore]
    public string MemberShown => Member.Length == 0 ? "Выберите члена пати" : Member;

    public override string? Problem => Member.Length == 0 ? "член пати не выбран" : null;

    public sealed override string Describe() => Member.Length == 0 ? "член пати не выбран" : What();

    /// <summary>Само действие для окна и лога, член пати выбран.</summary>
    protected abstract string What();

    /// <summary>Действие игры с найденным в пати.</summary>
    protected abstract GameAction ActionWith(PartyMember mate);

    /// <summary>Почему делать нечего (в игру ничего не шлём, шаг сделан); null — делать.</summary>
    protected virtual string? Pointless(WorldState world, PartyMember mate) => null;

    public override IClickerRun Start(BrainContext c)
    {
        if (Problem is { } problem)
            return ClickerRuns.Failed(problem);

        var world = c.World;
        if (!world.Party.InParty)
            return ClickerRuns.Failed("персонаж не в пати");
        if (string.Equals(world.Host.Name, Member, StringComparison.OrdinalIgnoreCase))
            return ClickerRuns.Failed($"«{Member}» — это сам персонаж");

        var mate = world.Party.Members.FirstOrDefault(m => m.Wid != world.Host.Wid && string.Equals(m.Name, Member, StringComparison.OrdinalIgnoreCase));
        if (mate is null)
            return ClickerRuns.Failed($"«{Member}» нет в пати");
        return Pointless(world, mate) is { } why ? ClickerRuns.Skipped(why) : RunWith(mate);
    }

    /// <summary>Как выполнять с найденным в пати; обычно — отправить <see cref="ActionWith"/> и не ждать итога.</summary>
    protected virtual IClickerRun RunWith(PartyMember mate) => ClickerRuns.Send(ActionWith(mate), doneWhenSent: true);

    public override void Normalize() => Member = Member.Trim();
}

/// <summary>
/// «Ассист» члена пати (как пунктом меню в окне пати): взять в цель то же, что в цели у него (<see cref="TeamAssistAction"/>).
/// Итог не ждём. Он рядом и видно, что цели у него нет или она та же, что у нас, — в игру ничего не шлём (иначе игра молчит, и
/// действие ждёт подтверждения впустую); его не видно — шлём.
/// </summary>
public sealed class AssistAction : PartyMemberAction
{
    // Цель игрока рядом: «никого» — все биты; 0 — неизвестно
    private const uint NoTarget = uint.MaxValue;

    protected override string What() => $"ассист {Member}";

    protected override string? Pointless(WorldState world, PartyMember mate)
    {
        if (world.Players.FirstOrDefault(p => p.Wid == mate.Wid) is not { TargetWid: not 0 } seen)
            return null;
        if (seen.TargetWid == NoTarget)
            return $"у {mate.Name} нет цели";
        return seen.TargetWid == world.Host.TargetWid ? $"у {mate.Name} та же цель" : null;
    }

    protected override GameAction ActionWith(PartyMember mate) => new TeamAssistAction(mate.Wid, mate.Name);
}

/// <summary>
/// «Следовать» за членом пати. Как — <see cref="Way"/>: ведёт игра (как пунктом меню в окне пати, <see cref="FollowAction"/>,
/// итог не ждём) или бот сам идёт к нему ходом в точку (<see cref="FollowMemberRun"/>) — до <see cref="StopDistance"/> м. Шаг по
/// кругу шлёт команду заново — как часто, задают паузой или условиями.
/// </summary>
public sealed class FollowMemberAction : PartyMemberAction
{
    public const int MaxStopDistance = 50;

    private FollowWay _way;
    private int _stopDistance = 3;

    /// <summary>Кто ведёт персонажа; «игра» в файл не пишется (так было, пока выбора не было).</summary>
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public FollowWay Way
    {
        get => _way;
        set
        {
            Set(ref _way, value);
            Changed(nameof(BotLeads));
        }
    }

    /// <summary>Когда ведёт бот — остановиться ближе этого к члену пати, м.</summary>
    public int StopDistance { get => _stopDistance; set => Set(ref _stopDistance, value); }

    /// <summary>Для окна: ведёт бот — нужно поле «остановиться ближе».</summary>
    [JsonIgnore]
    public bool BotLeads => Way != FollowWay.Game;

    protected override string What() => Way switch
    {
        FollowWay.Direct => $"идти к {Member} по прямой, до {StopDistance} м",
        FollowWay.Smart => $"идти к {Member} в обход препятствий, до {StopDistance} м",
        _ => $"следовать за {Member}",
    };

    protected override GameAction ActionWith(PartyMember mate) => new FollowAction(mate.Wid, mate.Name);

    protected override IClickerRun RunWith(PartyMember mate)
        => BotLeads ? new FollowMemberRun(mate.Wid, mate.Name, StopDistance, Way == FollowWay.Smart) : base.RunWith(mate);

    public override void Normalize()
    {
        base.Normalize();
        StopDistance = Math.Min(MaxStopDistance, Math.Max(1, StopDistance));
    }
}

/// <summary>Кто ведёт персонажа за членом пати.</summary>
public enum FollowWay
{
    /// <summary>Игра — как «Следовать» в меню пати.</summary>
    Game,
    /// <summary>Бот — по прямой.</summary>
    Direct,
    /// <summary>Бот — автопутём игры (в обход препятствий); нет его у сервера — по прямой.</summary>
    Smart,
}

/// <summary>
/// Бот идёт к члену пати: ходом в точку, где тот сейчас (в воздухе — летит с его высотой, в воде — плывёт). Уже ближе
/// <c>stop</c> м — в игру ничего не шлём. Пока идём, он отошёл от точки дальше <see cref="RetargetDistance"/> — идём к новому месту.
/// Сделано — дошли; встали, не дойдя, или не дошли за время — «не вышло». Где он, игра не знает (далеко) — «не вышло».
/// </summary>
internal sealed class FollowMemberRun(uint wid, string name, int stop, bool smart) : IClickerRun
{
    private const float RetargetDistance = 5f;
    private static readonly TimeSpan RetargetPeriod = TimeSpan.FromSeconds(1);

    private MoveAction? _move;
    private DateTime _movedAt;
    private ActionOutcome? _outcome;

    public StepState Tick(BrainContext c)
    {
        var world = c.World;
        var mate = world.Party.Members.FirstOrDefault(m => m.Wid == wid);
        var position = mate?.Position ?? world.Players.FirstOrDefault(p => p.Wid == wid)?.Position;

        if (_move is null)
        {
            if (mate is null)
                return StepState.Failed($"«{name}» нет в пати");
            if (position is not { } at)
                return StepState.Failed($"где «{name}» — игра не знает (далеко)");
            var left = Distance(c, at);
            if (left <= stop)
                return new StepState(StepStatus.Done, $"«{name}» рядом, {left:0.0} м");
            return Go(c, at, replace: false);
        }

        if (_outcome is { } outcome)
            return outcome.Status == ActionStatus.Confirmed ? StepState.Done : StepState.Failed(outcome.ToString());

        // Отошёл от точки, к которой идём, — сразу к новому месту (не чаще раза в секунду)
        if (position is { } now && now.DistanceTo(_move.Point) > RetargetDistance && c.Now - _movedAt >= RetargetPeriod)
            return Go(c, now, replace: true);
        return StepState.Running;
    }

    private StepState Go(BrainContext c, Position at, bool replace)
    {
        var move = c.World.Host.Flying == true ? new MoveAction(at, stop, fly: true)
            : c.Swimming ? new MoveAction(at, stop, swim: true)
            : new MoveAction(at, stop, smart);
        switch (replace ? c.Replace(move) : c.Send(move))
        {
            case SubmitStatus.Sent:
                (_move, _movedAt, _outcome) = (move, c.Now, null);
                return StepState.Running;
            // Тело занято чужим — попробовать на следующем снимке
            case SubmitStatus.Busy:
                return StepState.Running;
            case SubmitStatus.AlreadyPending:
                return _move is null ? StepState.Done : StepState.Running;
            default:
                return _move is null ? StepState.Failed("не отправлено (причина — в логе выше)") : StepState.Running;
        }
    }

    // Как в ходе в точку: по земле — по горизонтали, в воздухе и в воде — с высотой
    private static float Distance(BrainContext c, Position at)
        => c.World.Host.Flying == true || c.Swimming ? c.World.Host.Position.DistanceTo(at) : c.World.Host.Position.HorizontalDistanceTo(at);

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (ReferenceEquals(outcome.Action, _move))
            _outcome = outcome;
    }
}

/// <summary>
/// Действие вида, которого эта версия бота не знает (файл от более новой копии): не выполняется, но и не теряется — в файл
/// пишется как было (<see cref="Fields"/>).
/// </summary>
public sealed class UnknownAction(string kind) : ClickerAction
{
    /// <summary>Поля из файла, кроме вида, — как были.</summary>
    [JsonExtensionData]
    public IDictionary<string, JToken> Fields { get; set; } = new Dictionary<string, JToken>();

    protected override string KindId() => kind;

    public override string? Problem => $"действие «{kind}» из другой версии бота";

    public override string Describe() => $"«{kind}» — неизвестно этой версии бота";

    public override IClickerRun Start(BrainContext c) => ClickerRuns.Failed(Problem!);
}
