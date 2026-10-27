using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using BotCH.Core.Brain;
using BotCH.Core.Settings;
using BotCH.Core.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BotCH.Core.Clicker;

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
        var item = GroundItemChoice.Nearest(c.World, Items, Radius);
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
