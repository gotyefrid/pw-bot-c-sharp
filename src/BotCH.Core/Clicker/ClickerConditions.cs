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
/// Условие шага кликера: выполнено ли сейчас (<see cref="Check"/>) и его поля. Новое условие — так же, как новое действие
/// (<see cref="ClickerAction"/>): класс-наследник, строка в <see cref="ClickerKinds.Conditions"/>, шаблон полей в окне.
/// </summary>
public abstract class ClickerCondition : SettingsPart
{
    /// <summary>Вид в файле настроек («HpBelow»).</summary>
    [JsonProperty(Order = -2)]
    public string Kind => KindId();

    protected virtual string KindId() => ClickerKinds.Conditions.IdOf(GetType());

    /// <summary>Для списка в окне: «всегда», «HP меньше 50 %».</summary>
    public abstract string Describe();

    /// <summary>Выполнено ли сейчас: да — почему (в подробный лог: «HP 42 % — меньше 50 %»), нет — null.</summary>
    public abstract string? Check(BrainContext c);

    /// <summary>Значения — в допустимые пределы.</summary>
    public virtual void Normalize()
    {
    }
}

public sealed class AlwaysCondition : ClickerCondition
{
    public override string Describe() => "всегда";

    public override string? Check(BrainContext c) => "всегда";
}

/// <summary>HP персонажа ниже порога в %.</summary>
public sealed class HpBelowCondition : ClickerCondition
{
    private int _percent = 50;

    public int Percent { get => _percent; set => Set(ref _percent, value); }

    public override string Describe() => $"HP меньше {Percent} %";

    public override string? Check(BrainContext c)
    {
        var host = c.World.Host;
        return host.MaxHp > 0 && host.HpPercent < Percent ? $"HP {host.HpPercent} % — меньше {Percent} %" : null;
    }

    public override void Normalize() => Percent = Math.Min(100, Math.Max(1, Percent));
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

    public override string Describe() => Is == TargetIs.None ? "цели нет" : "цель есть";

    public override string? Check(BrainContext c)
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
/// список пуст — любой предмет или монеты. Ресурсы не в счёт: их копают, а не подбирают.
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

    public override string Describe()
        => Items.Count == 0 ? $"лежит любой предмет (до {Radius} м)" : $"лежит: {string.Join(", ", Items)} (до {Radius} м)";

    public override string? Check(BrainContext c)
    {
        var item = Find(c.World);
        return item is null ? null : $"лежит «{item.Name}» в {item.Offset}";
    }

    /// <summary>Ближайший подходящий предмет; null — нет.</summary>
    public GroundItem? Find(WorldState world)
        => world.GroundItems
            .Where(i => i.Kind != GroundItemKind.Resource && i.Offset.Direct <= Radius)
            .Where(i => Items.Count == 0 || MobNameFilter.Contains(Items, i.Name) || MobNameFilter.Contains(Items, i.Group))
            .OrderBy(i => i.Offset.Direct)
            .FirstOrDefault();

    public override void Normalize()
    {
        Radius = Math.Min(200, Math.Max(1, Radius));
        if (!Items.SequenceEqual(MobNameFilter.Clean(Items)))
            Items = new ObservableCollection<string>(MobNameFilter.Clean(Items));
    }
}

/// <summary>Условие вида, которого эта версия бота не знает: никогда не выполнено, в файл пишется как было.</summary>
public sealed class UnknownCondition(string kind) : ClickerCondition
{
    [JsonExtensionData]
    public IDictionary<string, JToken> Fields { get; set; } = new Dictionary<string, JToken>();

    protected override string KindId() => kind;

    public override string Describe() => $"«{kind}» — неизвестно этой версии бота";

    public override string? Check(BrainContext c) => null;
}
