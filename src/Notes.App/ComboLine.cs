using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using Notes.App.Mvvm;
using Notes.Core.Settings;

namespace Notes.App;

/// <summary>Скилл в списке окна связки: иконка (только в игре), название и пометка «не изучен».</summary>
/// <param name="Missing">Персонаж в игре, а этого скилла у него нет.</param>
public sealed record ComboSkillChoice(int Id, string Name, ImageSource? Icon, bool Missing)
{
    public string Title => Missing ? $"{Name} (не изучен)" : Name;
}

/// <summary>
/// Строка таблицы связки: номер, «вкл», скилл, условия одной строкой, как часто, «ждать». Галочка «вкл» правится прямо в
/// таблице — о правке говорит панели.
/// </summary>
public sealed class ComboLine(int number, ComboRow row, Action edited) : ObservableObject
{
    private ComboSkillChoice? _skill;

    public ComboRow Row { get; } = row;

    public string Number { get; } = $"{number}.";

    public bool Enabled
    {
        get => Row.Enabled;
        set
        {
            if (Row.Enabled == value)
                return;
            Row.Enabled = value;
            OnPropertyChanged();
            edited();
        }
    }

    /// <summary>Название скилла; нет у персонажа в игре — «(не изучен)».</summary>
    public string Title => _skill?.Title ?? (Row.SkillName.Length > 0 ? Row.SkillName : $"скилл {Row.SkillId}");

    public ImageSource? Icon => _skill?.Icon;

    /// <summary>Скилла нет у персонажа — строка серая: бот её пропускает.</summary>
    public bool Missing => _skill?.Missing == true;

    public string When => ComboEditing.ConditionsText(Row);

    public string RepeatText => ComboEditing.RepeatText(Row);

    public string WaitText => Row.WaitReady ? "ждать" : "";

    /// <summary>Скилл строки из списка окна (иконка, изучен ли).</summary>
    public void Show(ComboSkillChoice? skill)
    {
        _skill = skill;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(Missing));
    }

    /// <summary>Поменяли поля строки.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(When));
        OnPropertyChanged(nameof(RepeatText));
        OnPropertyChanged(nameof(WaitText));
    }
}

/// <summary>Проверка в списке условия: «HP моба, %».</summary>
public sealed record ComboCheckChoice(ComboCheck Check, string Title);

/// <summary>Больше или меньше: «&lt;» / «&gt;».</summary>
public sealed record ComboOpChoice(bool Below, string Title);

/// <summary>
/// Условие выбранной строки связки в окне: что проверяем, больше/меньше и число или про какой скилл. Правка — сразу в условие
/// строки, о ней говорит панели.
/// </summary>
public sealed class ComboConditionLine(ComboCondition condition, ObservableCollection<ComboSkillChoice> skills, Action edited) : ObservableObject
{
    private static readonly IReadOnlyList<ComboCheckChoice> AllChecks =
        ComboEditing.Checks.Select(c => new ComboCheckChoice(c, ComboEditing.CheckTitle(c))).ToList();

    private static readonly IReadOnlyList<ComboOpChoice> AllOps = [new(true, "<"), new(false, ">")];

    // Списки — свойства экземпляра: привязка окна к статическому свойству через строку не находит его
    public IReadOnlyList<ComboCheckChoice> Checks => AllChecks;

    public IReadOnlyList<ComboOpChoice> Ops => AllOps;

    public ComboCondition Condition { get; } = condition;

    /// <summary>Скиллы для проверок про скилл — все изученные (список общий у условий, его пересобирает панель).</summary>
    public ObservableCollection<ComboSkillChoice> Skills { get; } = skills;

    public ComboCheck Check
    {
        get => Condition.Check;
        set
        {
            if (Condition.Check == value)
                return;
            ComboEditing.ChangeCheck(Condition, value);
            // Новая проверка про скилл, а скилл не выбран — первый из списка
            if (Condition.AboutSkill && Condition.SkillId <= 0 && Skills.FirstOrDefault(s => !s.Missing) is { } first)
            {
                Condition.SkillId = first.Id;
                Condition.SkillName = first.Name;
            }
            Reselect();
            edited();
        }
    }

    public bool AboutSkill => Condition.AboutSkill;

    public bool AboutNumber => !Condition.AboutSkill;

    public bool Below
    {
        get => Condition.Below;
        set
        {
            if (Condition.Below == value)
                return;
            Condition.Below = value;
            OnPropertyChanged();
            edited();
        }
    }

    public int Value
    {
        get => Condition.Value;
        set
        {
            // Границы — как у файла связок: иначе файл и персонаж разойдутся и строки перечитаются посреди ввода
            value = Math.Min(100000, Math.Max(0, value));
            if (Condition.Value == value)
                return;
            Condition.Value = value;
            OnPropertyChanged();
            edited();
        }
    }

    public int SkillId
    {
        get => Condition.SkillId;
        set
        {
            // 0 — пересборка списка скиллов сбросила выбор: это не выбор игрока
            if (value <= 0 || Condition.SkillId == value)
                return;
            Condition.SkillId = value;
            Condition.SkillName = Skills.FirstOrDefault(s => s.Id == value)?.Name ?? Condition.SkillName;
            OnPropertyChanged();
            edited();
        }
    }

    /// <summary>Перечитать всё (поменялась проверка или пересобран список скиллов).</summary>
    public void Reselect()
    {
        OnPropertyChanged(nameof(Check));
        OnPropertyChanged(nameof(AboutSkill));
        OnPropertyChanged(nameof(AboutNumber));
        OnPropertyChanged(nameof(Below));
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(SkillId));
    }
}
