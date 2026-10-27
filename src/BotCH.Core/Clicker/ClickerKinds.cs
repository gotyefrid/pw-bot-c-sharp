using System;
using System.Collections.Generic;
using System.Linq;

namespace BotCH.Core.Clicker;

/// <summary>Вид действия или условия: имя в файле настроек, название в окне, класс.</summary>
public sealed record ClickerKind(string Id, string Title, Type Class);

/// <summary>Виды одного рода (действия или условия) — и фабрика: создать по имени из файла или по выбору в окне.</summary>
public sealed class ClickerKindList<T> where T : class
{
    private readonly List<ClickerKind> _kinds = [];
    private readonly Dictionary<string, Func<T>> _create = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>По порядку — как в выпадающем списке окна.</summary>
    public IReadOnlyList<ClickerKind> All => _kinds;

    /// <param name="id">Имя в файле настроек — его не менять: старые файлы перестанут читаться.</param>
    /// <param name="title">Как вид называется в окне.</param>
    public ClickerKindList<T> Add<TKind>(string id, string title) where TKind : T, new()
    {
        if (_create.ContainsKey(id) || _kinds.Any(k => k.Class == typeof(TKind)))
            throw new ArgumentException($"Вид «{id}» ({typeof(TKind).Name}) уже есть");

        _kinds.Add(new ClickerKind(id, title, typeof(TKind)));
        _create[id] = () => new TKind();
        return this;
    }

    /// <summary>Новый (с полями по умолчанию) по имени; такого вида нет — null.</summary>
    public T? Create(string? id) => id is not null && _create.TryGetValue(id, out var create) ? create() : null;

    public string IdOf(Type type)
        => _kinds.FirstOrDefault(k => k.Class == type)?.Id
           ?? throw new InvalidOperationException($"{type.Name} нет в ClickerKinds — допишите его туда");
}

/// <summary>
/// Все виды действий и условий кликера. Новый вид — строка здесь (порядок — как в окне) и класс-наследник
/// (<see cref="ClickerAction"/>, <see cref="ClickerCondition"/>).
/// </summary>
public static class ClickerKinds
{
    public static ClickerKindList<ClickerAction> Actions { get; } = new ClickerKindList<ClickerAction>()
        .Add<PressKeyAction>("PressKey", "Нажать клавишу")
        .Add<PauseAction>("Pause", "Пауза")
        .Add<PickUpItemAction>("PickUpItem", "Поднять предмет")
        .Add<GatherResourceAction>("Gather", "Копать")
        .Add<TakeTargetAction>("TakeTarget", "Взять цель");

    public static ClickerKindList<ClickerCondition> Conditions { get; } = new ClickerKindList<ClickerCondition>()
        .Add<AlwaysCondition>("Always", "Всегда")
        .Add<HpBelowCondition>("HpBelow", "HP персонажа меньше, %")
        .Add<MpBelowCondition>("MpBelow", "MP персонажа меньше, %")
        .Add<PetHpBelowCondition>("PetHpBelow", "HP пета меньше, %")
        .Add<TargetCondition>("Target", "Цель персонажа")
        .Add<ItemNearbyCondition>("ItemNearby", "Рядом лежит предмет")
        .Add<ResourceNearbyCondition>("ResourceNearby", "Рядом можно копать")
        .Add<TargetNearbyCondition>("TargetNearby", "Рядом можно взять цель")
        .Add<AllCondition>("All", "Все условия (И)")
        .Add<AnyCondition>("Any", "Любое из условий (ИЛИ)");
}
