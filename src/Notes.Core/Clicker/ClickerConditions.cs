using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Notes.Core.Brain;
using Notes.Core.Settings;
using Notes.Core.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Notes.Core.Clicker;

/// <summary>
/// Условие шага кликера: выполнено ли сейчас (<see cref="Check"/>) и его поля. У любого условия есть «НЕ» (<see cref="Not"/>).
/// Новое условие — так же, как новое действие (<see cref="ClickerAction"/>): класс-наследник (<see cref="What"/>,
/// <see cref="Holds"/>), строка в <see cref="ClickerKinds.Conditions"/>, шаблон полей в окне.
/// </summary>
public abstract class ClickerCondition : SettingsPart
{
    private bool _not;

    // Подпись меняется вместе с любым полем — окно, привязанное к ней, узнаёт об этом
    protected ClickerCondition() => PropertyChanged += (_, e) =>
    {
        if (e.PropertyName != nameof(Description))
            Changed(nameof(Description));
    };

    /// <summary>Вид в файле настроек («HpBelow»).</summary>
    [JsonProperty(Order = -2)]
    public string Kind => KindId();

    protected virtual string KindId() => ClickerKinds.Conditions.IdOf(GetType());

    /// <summary>Наоборот: выполнено, когда само условие не выполнено. В файл пишется, только когда включено.</summary>
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool Not { get => _not; set => Set(ref _not, value); }

    /// <summary>То же, что <see cref="Describe"/>, — свойством, для привязки в окне.</summary>
    [JsonIgnore]
    public string Description => Describe();

    /// <summary>Для окна: «HP меньше 50 %», с «НЕ» — «НЕ (HP меньше 50 %)».</summary>
    public string Describe() => Not ? $"НЕ ({What()})" : What();

    /// <summary>Выполнено ли сейчас (с учётом «НЕ»): да — почему (в подробный лог: «HP 42 % — меньше 50 %»), нет — null.</summary>
    public string? Check(BrainContext c)
    {
        var why = Holds(c);
        if (!Not)
            return why;
        return why is null ? $"НЕ ({What()})" : null;
    }

    /// <summary>Само условие для окна, без «НЕ».</summary>
    protected abstract string What();

    /// <summary>Выполнено ли само условие, без «НЕ»: да — почему, нет — null.</summary>
    protected abstract string? Holds(BrainContext c);

    /// <summary>Значения — в допустимые пределы.</summary>
    public virtual void Normalize()
    {
    }
}

public sealed class AlwaysCondition : ClickerCondition
{
    protected override string What() => "всегда";

    protected override string? Holds(BrainContext c) => "всегда";
}

/// <summary>
/// Что-то в процентах ниже порога: HP или MP персонажа, HP пета. Наследник говорит, что сравнивается (<see cref="Subject"/>)
/// и сколько сейчас (<see cref="Current"/>; не знаем — не выполнено).
/// </summary>
public abstract class PercentBelowCondition : ClickerCondition
{
    private int _percent = 50;

    public int Percent { get => _percent; set => Set(ref _percent, value); }

    /// <summary>Что сравнивается: «HP», «MP», «HP пета».</summary>
    protected abstract string Subject { get; }

    /// <summary>Сколько сейчас, %; null — неизвестно (поле не найдено для сервера, пет не призван).</summary>
    protected abstract int? Current(WorldState world);

    protected override string What() => $"{Subject} меньше {Percent} %";

    protected override string? Holds(BrainContext c)
        => Current(c.World) is { } now && now < Percent ? $"{Subject} {now} % — меньше {Percent} %" : null;

    public override void Normalize() => Percent = Math.Min(100, Math.Max(1, Percent));
}

/// <summary>HP персонажа ниже порога в %.</summary>
public sealed class HpBelowCondition : PercentBelowCondition
{
    protected override string Subject => "HP";

    protected override int? Current(WorldState world) => world.Host.MaxHp > 0 ? world.Host.HpPercent : null;
}

/// <summary>MP персонажа ниже порога в %.</summary>
public sealed class MpBelowCondition : PercentBelowCondition
{
    protected override string Subject => "MP";

    protected override int? Current(WorldState world) => world.Host.MpPercent;
}

/// <summary>HP призванного пета ниже порога в %; пет не призван — не выполнено.</summary>
public sealed class PetHpBelowCondition : PercentBelowCondition
{
    protected override string Subject => "HP пета";

    protected override int? Current(WorldState world)
        => world.Pet is { IsSummoned: true, ActiveCage: int cage } pet && pet.InCage(cage) is { } summoned ? summoned.HpPercent : null;
}

/// <summary>
/// HP цели персонажа в % меньше, больше или равно порогу (<see cref="Compare"/>) — кто бы в цели ни был: моб, игрок, член пати,
/// сам персонаж. Цели нет или её HP игра не показывает (максимум неизвестен) — не выполнено. Проценты целые: «равно 100» — полное.
/// </summary>
public sealed class TargetHpCondition : ClickerCondition
{
    private const string Subject = "HP цели";

    private PercentCompare _compare;
    private int _percent = 50;

    /// <summary>Как сравнивать с порогом.</summary>
    public PercentCompare Compare { get => _compare; set => Set(ref _compare, value); }

    public int Percent { get => _percent; set => Set(ref _percent, value); }

    private string Sign => Compare switch
    {
        PercentCompare.Above => "больше",
        PercentCompare.Equal => "равно",
        _ => "меньше",
    };

    protected override string What() => $"{Subject} {Sign} {Percent} %";

    protected override string? Holds(BrainContext c)
    {
        if (Current(c.World) is not { } now)
            return null;
        var holds = Compare switch
        {
            PercentCompare.Above => now > Percent,
            PercentCompare.Equal => now == Percent,
            _ => now < Percent,
        };
        return holds ? $"{Subject} {now} % — {Sign} {Percent} %" : null;
    }

    // «Больше 0» (жива) и «равно 0» — тоже смысл есть
    public override void Normalize() => Percent = Math.Min(100, Math.Max(0, Percent));

    // HP цели, %; null — цели нет или её HP игра не показывает
    private static int? Current(WorldState world)
    {
        var wid = world.Host.TargetWid;
        if (wid == 0 || wid == uint.MaxValue)
            return null;
        if (wid == world.Host.Wid)
            return Share(world.Host.Hp, world.Host.MaxHp);
        if (world.Party.Members.FirstOrDefault(m => m.Wid == wid) is { MaxHp: > 0 } mate)
            return Share(mate.Hp, mate.MaxHp);
        if (world.Players.FirstOrDefault(p => p.Wid == wid) is { MaxHp: > 0 } player)
            return Share(player.Hp, player.MaxHp);
        return world.Npcs.FirstOrDefault(n => n.Wid == wid) is { MaxHp: > 0 } npc ? Share(npc.Hp, npc.MaxHp) : null;
    }

    private static int? Share(int hp, int max) => max > 0 ? Math.Max(0, hp) * 100 / max : null;
}

/// <summary>
/// Призванный пет голоден — то же правило, по которому бот кормит пета сам (<see cref="PetInCage.IsHungry"/>). Уровней голода
/// нет: что значит каждое число сытости, в игре не сверено. Пет не призван — не выполнено.
/// </summary>
public sealed class PetHungryCondition : ClickerCondition
{
    protected override string What() => "пет голоден";

    protected override string? Holds(BrainContext c)
        => c.World.Pet is { IsSummoned: true, ActiveCage: int cage } pet && pet.InCage(cage) is { IsHungry: true } ? "пет голоден" : null;
}

/// <summary>
/// Идёт макрос (какой-нибудь); «не идёт» — то же с «НЕ». Идёт ли, игра не говорит (поле не найдено для сервера) — не выполнено.
/// </summary>
public sealed class MacroRunningCondition : ClickerCondition
{
    protected override string What() => "макрос идёт";

    protected override string? Holds(BrainContext c) => c.World.MacroRunning == true ? "макрос идёт" : null;
}

/// <summary>
/// Скилл персонажа на перезарядке; «скилл готов» — то же с «НЕ». Скилл не выбран или его нет среди изученных (сценарий
/// другого персонажа) — не выполнено.
/// </summary>
public sealed class SkillCooldownCondition : ClickerCondition
{
    private int _skill;
    private string _skillName = "";

    /// <summary>ID скилла; 0 — не выбран.</summary>
    public int Skill { get => _skill; set => Set(ref _skill, value); }

    /// <summary>Название, как в игре, — запоминается при выборе: в таблице видно, какой скилл, и без подключения к игре.</summary>
    public string SkillName { get => _skillName; set => Set(ref _skillName, value ?? ""); }

    private string Title => ClickerSkill.Title(Skill, SkillName);

    protected override string What() => Skill <= 0 ? "скилл не выбран" : $"{Title} на перезарядке";

    protected override string? Holds(BrainContext c)
        => Skill > 0 && c.World.Skill(Skill) is { IsReady: false } s ? $"{Title} на перезарядке ещё {s.CooldownLeftMs / 1000.0:0.0} с" : null;

    public override void Normalize()
    {
        Skill = Math.Max(0, Skill);
        SkillName = SkillName.Trim();
    }
}

/// <summary>Как сравнивать с порогом в процентах.</summary>
public enum PercentCompare
{
    Below,
    Above,
    Equal,
}

public enum TargetIs
{
    /// <summary>Цели нет.</summary>
    None,
    /// <summary>Цель есть — кто угодно: моб, игрок, NPC.</summary>
    Any,
}

/// <summary>Цель персонажа: нет её или есть (<see cref="TargetIs"/>).</summary>
public sealed class TargetCondition : ClickerCondition
{
    private TargetIs _is;

    public TargetIs Is { get => _is; set => Set(ref _is, value); }

    protected override string What() => Is == TargetIs.None ? "цели нет" : "цель есть";

    protected override string? Holds(BrainContext c)
    {
        var has = c.World.Host.TargetWid != 0;
        return Is switch
        {
            TargetIs.None => has ? null : "цели нет",
            _ when !has => null,
            _ => c.World.Target is { } target ? $"цель — {target.Name}" : "цель есть",
        };
    }
}

/// <summary>
/// Рядом с персонажем (не дальше <see cref="Radius"/>) лежит предмет из <see cref="Items"/> — название или группа, как у лута;
/// список пуст — любой предмет или монеты (<see cref="GroundItemChoice"/>).
/// </summary>
public sealed class ItemNearbyCondition : ClickerCondition
{
    private ObservableCollection<string> _items = [];
    private int _radius = 10;

    public ItemNearbyCondition() => _items.CollectionChanged += ItemsChanged;

    /// <summary>Названия или группы предметов. Список, который меняют на месте (выбор в окне), — о правке говорит сам.</summary>
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

    /// <summary>Не дальше этого от персонажа, м.</summary>
    public int Radius { get => _radius; set => Set(ref _radius, value); }

    private void ItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Changed(nameof(Items));

    protected override string What()
        => Items.Count == 0 ? $"лежит любой предмет (до {Radius} м)" : $"лежит: {string.Join(", ", Items)} (до {Radius} м)";

    protected override string? Holds(BrainContext c)
    {
        var item = GroundItemChoice.NearestItem(c.World, Items, Radius);
        return item is null ? null : $"лежит «{item.Name}» в {item.Offset}";
    }

    public override void Normalize()
    {
        Radius = GroundItemChoice.CleanRadius(Radius);
        if (!Items.SequenceEqual(MobNameFilter.Clean(Items)))
            Items = new ObservableCollection<string>(MobNameFilter.Clean(Items));
    }
}

/// <summary>
/// Рядом с персонажем (не дальше <see cref="Radius"/>) есть что копать из <see cref="Resources"/> — ресурс или «нересурс»
/// (квестовый, ящик), без проверки инструмента, уровня, квеста; список пуст — что угодно (<see cref="GroundItemChoice"/>).
/// </summary>
public sealed class ResourceNearbyCondition : ClickerCondition
{
    private ObservableCollection<string> _resources = [];
    private int _radius = 10;

    public ResourceNearbyCondition() => _resources.CollectionChanged += ResourcesChanged;

    /// <summary>Названия ресурсов. Список, который меняют на месте (выбор в окне), — о правке говорит сам.</summary>
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

    /// <summary>Не дальше этого от персонажа, м.</summary>
    public int Radius { get => _radius; set => Set(ref _radius, value); }

    private void ResourcesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Changed(nameof(Resources));

    protected override string What()
        => Resources.Count == 0 ? $"рядом любой ресурс (до {Radius} м)" : $"рядом ресурс: {string.Join(", ", Resources)} (до {Radius} м)";

    protected override string? Holds(BrainContext c)
        => GroundItemChoice.NearestResource(c.World, Resources, Radius) is { } resource ? $"рядом «{resource.Name}» в {resource.Offset}" : null;

    public override void Normalize()
    {
        Radius = GroundItemChoice.CleanRadius(Radius);
        if (!Resources.SequenceEqual(MobNameFilter.Clean(Resources)))
            Resources = new ObservableCollection<string>(MobNameFilter.Clean(Resources));
    }
}

/// <summary>
/// Рядом с персонажем (не дальше <see cref="Radius"/>) есть кого взять в цель из <see cref="Targets"/> — моб, NPC, чужой пет;
/// список пуст — кто угодно из них (<see cref="TargetChoice"/>). Тот, кто уже в цели, тоже в счёт.
/// </summary>
public sealed class TargetNearbyCondition : ClickerCondition
{
    private ObservableCollection<string> _targets = [];
    private int _radius = 10;

    public TargetNearbyCondition() => _targets.CollectionChanged += TargetsChanged;

    /// <summary>Названия мобов, NPC, петов. Список, который меняют на месте (выбор в окне), — о правке говорит сам.</summary>
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

    /// <summary>Не дальше этого от персонажа, м.</summary>
    public int Radius { get => _radius; set => Set(ref _radius, value); }

    private void TargetsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Changed(nameof(Targets));

    protected override string What()
        => Targets.Count == 0 ? $"рядом любая цель (до {Radius} м)" : $"рядом цель: {string.Join(", ", Targets)} (до {Radius} м)";

    protected override string? Holds(BrainContext c)
        => TargetChoice.Nearest(c.World, Targets, Radius) is { } target ? $"рядом «{target.Name}» в {target.Offset}" : null;

    public override void Normalize()
    {
        Radius = GroundItemChoice.CleanRadius(Radius);
        if (!Targets.SequenceEqual(MobNameFilter.Clean(Targets)))
            Targets = new ObservableCollection<string>(MobNameFilter.Clean(Targets));
    }
}

/// <summary>
/// Член пати <see cref="Member"/> (по нику) дальше <see cref="Radius"/> от персонажа — напрямую, с высотой (лидер может лететь).
/// Координаты участника игра знает не всегда (далеко) — тогда считаем, что далеко. Персонаж не в пати, выбранного в ней нет или
/// выбран сам персонаж — не выполнено.
/// </summary>
public sealed class PartyMemberFarCondition : ClickerCondition
{
    private string _member = "";
    private int _radius = 15;

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

    /// <summary>Дальше этого от персонажа, м.</summary>
    public int Radius { get => _radius; set => Set(ref _radius, value); }

    protected override string What()
        => Member.Length == 0 ? "член пати не выбран" : $"{Member} дальше {Radius} м";

    protected override string? Holds(BrainContext c)
    {
        var world = c.World;
        if (Member.Length == 0 || string.Equals(world.Host.Name, Member, StringComparison.OrdinalIgnoreCase))
            return null;

        var mate = world.Party.Members.FirstOrDefault(m => m.Wid != world.Host.Wid && string.Equals(m.Name, Member, StringComparison.OrdinalIgnoreCase));
        if (mate is null)
            return null;
        if (mate.Position is not { } position)
            return $"где «{mate.Name}» — игра не знает, считаем далеко";

        var offset = Offset.Between(world.Host.Position, position);
        return offset.Direct > Radius ? $"«{mate.Name}» в {offset} — дальше {Radius} м" : null;
    }

    public override void Normalize()
    {
        Member = Member.Trim();
        Radius = GroundItemChoice.CleanRadius(Radius);
    }
}

/// <summary>
/// HP членов пати в % — для условия «Пати: HP» и действия «Пати: взять в цель». Сам персонаж — тоже член пати (его HP — из персонажа:
/// свежее). Максимум HP неизвестен — такого нет в списке.
/// </summary>
public static class PartyHp
{
    /// <summary>Члены пати с HP в %: <paramref name="member"/> — только он (по нику), пусто — все, и сам персонаж.</summary>
    public static IEnumerable<(PartyMember Mate, int Percent)> Of(WorldState world, string member)
    {
        foreach (var mate in world.Party.Members)
        {
            var self = mate.Wid == world.Host.Wid;
            var name = self && mate.Name.Length == 0 ? world.Host.Name : mate.Name;
            if (member.Length > 0 && !string.Equals(name, member, StringComparison.OrdinalIgnoreCase))
                continue;

            var (hp, max) = self ? (world.Host.Hp, world.Host.MaxHp) : (mate.Hp, mate.MaxHp);
            if (max > 0)
                yield return (mate with { Name = name }, Math.Max(0, hp) * 100 / max);
        }
    }

    public static bool Fits(int now, PercentCompare compare, int percent) => compare switch
    {
        PercentCompare.Above => now > percent,
        PercentCompare.Equal => now == percent,
        _ => now < percent,
    };

    public static string Sign(PercentCompare compare) => compare switch
    {
        PercentCompare.Above => "больше",
        PercentCompare.Equal => "равно",
        _ => "меньше",
    };
}

/// <summary>
/// HP члена пати <see cref="Member"/> (по нику; пусто — любого, и самого персонажа) в % меньше, больше или равно порогу.
/// С «любым» выполнено, когда подходит хоть один; в лог — самый раненый из подходящих. Персонаж не в пати или выбранного в ней
/// нет — не выполнено.
/// </summary>
public sealed class PartyHpCondition : ClickerCondition
{
    private string _member = "";
    private PercentCompare _compare;
    private int _percent = 50;

    /// <summary>Ник члена пати; пусто — любой.</summary>
    public string Member
    {
        get => _member;
        set
        {
            Set(ref _member, value ?? "");
            Changed(nameof(MemberShown));
        }
    }

    /// <summary>Для окна: что показать в поле, когда член пати не выбран.</summary>
    [JsonIgnore]
    public string MemberShown => Member.Length == 0 ? "Любой (и сам персонаж)" : Member;

    public PercentCompare Compare { get => _compare; set => Set(ref _compare, value); }

    public int Percent { get => _percent; set => Set(ref _percent, value); }

    protected override string What()
        => $"HP {(Member.Length == 0 ? "любого в пати" : Member)} {PartyHp.Sign(Compare)} {Percent} %";

    protected override string? Holds(BrainContext c)
    {
        var fit = PartyHp.Of(c.World, Member).Where(m => PartyHp.Fits(m.Percent, Compare, Percent)).OrderBy(m => m.Percent).ToList();
        return fit.Count == 0 ? null : $"HP «{fit[0].Mate.Name}» {fit[0].Percent} % — {PartyHp.Sign(Compare)} {Percent} %";
    }

    public override void Normalize()
    {
        Member = Member.Trim();
        Percent = Math.Min(100, Math.Max(0, Percent));
    }
}

/// <summary>
/// Группа условий — внутри свой список (<see cref="Conditions"/>), в нём могут быть и группы. Правка внутри — правка группы
/// (окно и сохранение узнают о ней).
/// </summary>
public abstract class ConditionGroup : ClickerCondition
{
    private ConditionList _conditions = new();

    protected ConditionGroup() => _conditions.Edited += InnerEdited;

    public ConditionList Conditions
    {
        get => _conditions;
        set
        {
            _conditions.Edited -= InnerEdited;
            _conditions = value ?? new ConditionList();
            _conditions.Edited += InnerEdited;
            Changed(nameof(Conditions));
        }
    }

    private void InnerEdited() => Changed(nameof(Conditions));

    public override void Normalize() => Conditions = ConditionList.Clean(Conditions);

    /// <summary>Выполнены ли все: да — причины через запятую, пусто — «всегда»; нет — null.</summary>
    public static string? AllHold(BrainContext c, IReadOnlyCollection<ClickerCondition> conditions)
    {
        var reasons = new List<string>();
        foreach (var condition in conditions)
        {
            if (condition.Check(c) is not { } why)
                return null;
            reasons.Add(why);
        }

        return reasons.Count == 0 ? "всегда" : string.Join(", ", reasons);
    }

    /// <summary>Для окна: условия через «И» / «ИЛИ»; группа из нескольких внутри — в скобках.</summary>
    public static string Join(IReadOnlyCollection<ClickerCondition> conditions, string word, string empty)
        => conditions.Count == 0
            ? empty
            : string.Join($" {word} ", conditions.Select(c => c is ConditionGroup { Not: false, Conditions.Count: > 1 } ? $"({c.Describe()})" : c.Describe()));
}

/// <summary>«Все условия (И)»: выполнено, когда выполнены все внутри; пустая — всегда.</summary>
public sealed class AllCondition : ConditionGroup
{
    protected override string What() => Join(Conditions, "И", "всегда");

    protected override string? Holds(BrainContext c) => AllHold(c, Conditions);
}

/// <summary>«Любое из условий (ИЛИ)»: выполнено, когда выполнено хоть одно внутри; пустая — никогда.</summary>
public sealed class AnyCondition : ConditionGroup
{
    protected override string What() => Join(Conditions, "ИЛИ", "никогда (нет условий)");

    protected override string? Holds(BrainContext c) => Conditions.Select(condition => condition.Check(c)).FirstOrDefault(why => why is not null);
}

/// <summary>
/// Список условий (у шага, в группе), который сам говорит о любой правке внутри (<see cref="Edited"/>): добавили, убрали,
/// заменили условие, поменяли поле у условия — и у вложенных групп тоже.
/// </summary>
public sealed class ConditionList : ObservableCollection<ClickerCondition>
{
    public ConditionList()
    {
    }

    public ConditionList(IEnumerable<ClickerCondition> conditions)
    {
        foreach (var condition in conditions)
            Add(condition);
    }

    public event Action? Edited;

    /// <summary>Без пустых; значения у всех (и во вложенных группах) — в пределы.</summary>
    public static ConditionList Clean(IEnumerable<ClickerCondition?>? conditions)
    {
        var clean = new ConditionList();
        foreach (var condition in conditions ?? [])
        {
            if (condition is null)
                continue;
            condition.Normalize();
            clean.Add(condition);
        }

        return clean;
    }

    protected override void InsertItem(int index, ClickerCondition item)
    {
        base.InsertItem(index, item);
        Watch(item);
    }

    protected override void SetItem(int index, ClickerCondition item)
    {
        Unwatch(this[index]);
        base.SetItem(index, item);
        Watch(item);
    }

    protected override void RemoveItem(int index)
    {
        Unwatch(this[index]);
        base.RemoveItem(index);
    }

    protected override void ClearItems()
    {
        foreach (var condition in this)
            Unwatch(condition);
        base.ClearItems();
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnCollectionChanged(e);
        Edited?.Invoke();
    }

    private void Watch(ClickerCondition? condition)
    {
        if (condition is not null)
            condition.PropertyChanged += ConditionChanged;
    }

    private void Unwatch(ClickerCondition? condition)
    {
        if (condition is not null)
            condition.PropertyChanged -= ConditionChanged;
    }

    // Подпись меняется вместе с полем — одна правка, а не две
    private void ConditionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ClickerCondition.Description))
            Edited?.Invoke();
    }
}

/// <summary>Условие вида, которого эта версия бота не знает: никогда не выполнено, в файл пишется как было.</summary>
public sealed class UnknownCondition(string kind) : ClickerCondition
{
    [JsonExtensionData]
    public IDictionary<string, JToken> Fields { get; set; } = new Dictionary<string, JToken>();

    protected override string KindId() => kind;

    protected override string What() => $"«{kind}» — неизвестно этой версии бота";

    protected override string? Holds(BrainContext c) => null;
}
