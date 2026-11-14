using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Notes.App.Mvvm;
using Notes.Core.Settings;

namespace Notes.App.Panels;

/// <summary>
/// Правка строки связки в окне (<see cref="ComboRowWindow"/>): скилл, условия, «ждать», как часто. Правится копия строки, в
/// строку и общий файл связок уходит только по «Сохранить» (<see cref="Save"/>), «Отмена» ничего не меняет.
/// </summary>
public sealed class ComboRowDraft : ObservableObject
{
    private readonly ComboRow _row;
    private readonly Func<ComboRow, string?> _save;
    private string _error = "";

    /// <param name="number">Номер строки в связке: для заголовка окна.</param>
    /// <param name="row">Строка связки: правится её копия.</param>
    /// <param name="skills">Скиллы для строки (общий список панели, она его пересобирает).</param>
    /// <param name="conditionSkills">Скиллы для проверок про скилл (общий список панели).</param>
    /// <param name="save">Записать правку в строку; вернуть, почему не вышло (null — записано).</param>
    public ComboRowDraft(int number, ComboRow row, ObservableCollection<ComboSkillChoice> skills,
        ObservableCollection<ComboSkillChoice> conditionSkills, Func<ComboRow, string?> save)
    {
        _row = ComboEditing.Copy(row);
        _save = save;
        Title = $"Строка {number} связки";
        SkillChoices = skills;
        ConditionSkills = conditionSkills;
        foreach (var condition in _row.Conditions)
            Conditions.Add(Line(condition));
        AddConditionCommand = new RelayCommand(AddCondition);
        RemoveConditionCommand = new RelayCommand<ComboConditionLine>(RemoveCondition);
    }

    public string Title { get; }

    /// <summary>Скиллы для строки: атакующие изученные, а скиллы строк, которых нет, — с пометкой.</summary>
    public ObservableCollection<ComboSkillChoice> SkillChoices { get; }

    /// <summary>Скиллы для проверок «скилл готов» и др.: все изученные.</summary>
    public ObservableCollection<ComboSkillChoice> ConditionSkills { get; }

    public int SelectedSkillId
    {
        get => _row.SkillId;
        set
        {
            // 0 — пересборка списка сбросила выбор: это не выбор игрока
            if (value <= 0 || _row.SkillId == value)
                return;
            _row.SkillId = value;
            _row.SkillName = SkillChoices.FirstOrDefault(s => s.Id == value)?.Name ?? "";
            OnPropertyChanged();
        }
    }

    /// <summary>Условия строки: все сразу; пусто — всегда.</summary>
    public ObservableCollection<ComboConditionLine> Conditions { get; } = new();

    public ICommand AddConditionCommand { get; }
    public ICommand RemoveConditionCommand { get; }

    public bool WaitReady
    {
        get => _row.WaitReady;
        set
        {
            if (_row.WaitReady == value)
                return;
            _row.WaitReady = value;
            OnPropertyChanged();
        }
    }

    public sealed record RepeatChoice(ComboRepeat Repeat, string Title);

    public IReadOnlyList<RepeatChoice> Repeats { get; } =
        [new(ComboRepeat.Always, "каждый раз"), new(ComboRepeat.OncePerFight, "1 раз за бой"), new(ComboRepeat.Interval, "не чаще, с")];

    public ComboRepeat SelectedRepeat
    {
        get => _row.Repeat;
        set
        {
            if (_row.Repeat == value)
                return;
            _row.Repeat = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsInterval));
        }
    }

    public bool IsInterval => _row.Repeat == ComboRepeat.Interval;

    public int IntervalSeconds
    {
        get => _row.IntervalSeconds;
        set
        {
            // Границы — как у файла связок
            value = Math.Min(3600, Math.Max(1, value));
            if (_row.IntervalSeconds == value)
                return;
            _row.IntervalSeconds = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Почему не сохранили; пусто — всё хорошо.</summary>
    public string Error { get => _error; private set => SetProperty(ref _error, value); }

    /// <summary>Списки скиллов пересобраны (новый снимок): перечитать выбор в окне.</summary>
    public void Reselect()
    {
        OnPropertyChanged(nameof(SelectedSkillId));
        foreach (var condition in Conditions)
            condition.Reselect();
    }

    /// <summary>Проверить и записать в строку; false — что не так, в <see cref="Error"/>.</summary>
    public bool Save()
    {
        if (_row.SkillId <= 0)
            return Fail("Не выбран скилл");
        if (_row.Conditions.FirstOrDefault(c => c.AboutSkill && c.SkillId <= 0) is { } empty)
            return Fail($"В условии «{ComboEditing.CheckTitle(empty.Check)}» не выбран скилл");
        if (_save(_row) is { } why)
            return Fail(why);
        Error = "";
        return true;
    }

    private bool Fail(string why)
    {
        Error = why;
        return false;
    }

    // Правка условия — только в копии строки: ничего не пересчитываем до «Сохранить»
    private ComboConditionLine Line(ComboCondition condition) => new(condition, ConditionSkills, static () => { });

    private void AddCondition()
    {
        var condition = ComboEditing.NewCondition(ComboCheck.MobHp);
        _row.Conditions.Add(condition);
        Conditions.Add(Line(condition));
    }

    private void RemoveCondition(ComboConditionLine? condition)
    {
        if (condition is null)
            return;
        _row.Conditions.Remove(condition.Condition);
        Conditions.Remove(condition);
    }
}
