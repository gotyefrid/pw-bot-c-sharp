using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using BotCH.App.Mvvm;
using BotCH.Core.Clicker;
using BotCH.Core.Settings;

namespace BotCH.App.Panels;

/// <summary>
/// Вкладка «Кликер» — сценарий: шаги по порядку, у каждого действие и условие. Шаги — в настройках персонажа; о правке
/// говорит модели окна (та сохраняет и отдаёт боту). Выбранный шаг правится в своём окне (<see cref="EditRequested"/>): поля
/// действия и условия показывают их шаблоны (по классу) и правят их напрямую — о правке поля говорят сами.
/// </summary>
public sealed class ClickerPanel : ObservableObject
{
    private readonly Func<BotSettings> _settings;
    private readonly Func<int?> _current;
    private readonly Action _edited;
    private ClickerRow? _selected;
    private ClickerAction? _watchedAction;
    private ClickerCondition? _watchedCondition;

    /// <param name="settings">Настройки текущего персонажа (меняются при смене персонажа).</param>
    /// <param name="current">Номер шага, который бот выполняет сейчас; null — бот не работает.</param>
    /// <param name="lootOptions">Варианты предметов (рядом, встречались, группы) — как у лута.</param>
    /// <param name="edited">Правка настроек из этой вкладки.</param>
    public ClickerPanel(Func<BotSettings> settings, Func<int?> current, Func<IReadOnlyList<PickOption>> lootOptions, Action edited)
    {
        _settings = settings;
        _current = current;
        LootOptions = lootOptions;
        _edited = edited;
        AddCommand = new RelayCommand(Add);
        RemoveCommand = new RelayCommand(Remove, () => Selected is not null);
        UpCommand = new RelayCommand(() => Move(-1), () => Selected is { } r && Rows.IndexOf(r) > 0);
        DownCommand = new RelayCommand(() => Move(1), () => Selected is { } r && Rows.IndexOf(r) is var i && i >= 0 && i < Rows.Count - 1);
    }

    private List<ClickerStep> Steps => _settings().Clicker.Steps;

    /// <summary>Виды действий и условий — для выпадающих списков.</summary>
    public IReadOnlyList<ClickerKind> ActionKinds => ClickerKinds.Actions.All;

    public IReadOnlyList<ClickerKind> ConditionKinds => ClickerKinds.Conditions.All;

    /// <summary>Варианты для условия «рядом лежит предмет».</summary>
    public Func<IReadOnlyList<PickOption>> LootOptions { get; }

    /// <summary>Открыть окно выбранного шага (двойной щелчок, новый шаг). Окно открывает вид.</summary>
    public event Action? EditRequested;

    /// <summary>Шаги по порядку: после последнего — снова первый.</summary>
    public ObservableCollection<ClickerRow> Rows { get; } = new();

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand DownCommand { get; }

    public ClickerRow? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
                return;
            Watch();
            OnPropertyChanged(nameof(SelectedTitle));
            OnPropertyChanged(nameof(SelectedActionKind));
            OnPropertyChanged(nameof(SelectedConditionKind));
        }
    }

    public string SelectedTitle => Selected is { } row ? $"Шаг {Rows.IndexOf(row) + 1}" : "";

    /// <summary>Вид действия выбранного шага; сменили — действие нового вида с полями по умолчанию.</summary>
    public string? SelectedActionKind
    {
        get => Selected?.Step.Action.Kind;
        set
        {
            if (Selected is not { } row || value is null || value == row.Step.Action.Kind || ClickerKinds.Actions.Create(value) is not { } action)
                return;
            row.Step.Action = action;
            PartReplaced(row);
        }
    }

    /// <summary>Действие выбранного шага — его поля показывает шаблон окна для его класса.</summary>
    public ClickerAction? SelectedAction => _watchedAction;

    public string? SelectedConditionKind
    {
        get => Selected?.Step.Condition.Kind;
        set
        {
            if (Selected is not { } row || value is null || value == row.Step.Condition.Kind || ClickerKinds.Conditions.Create(value) is not { } condition)
                return;
            row.Step.Condition = condition;
            PartReplaced(row);
        }
    }

    public ClickerCondition? SelectedCondition => _watchedCondition;

    /// <summary>Список в окне ← настройки персонажа (выбор сбрасывается, если шага больше нет).</summary>
    public void Load()
    {
        var selected = Selected?.Step;
        Rows.Clear();
        for (var i = 0; i < Steps.Count; i++)
            Rows.Add(new ClickerRow(i + 1, Steps[i]));
        Selected = Rows.FirstOrDefault(r => r.Step == selected);
        ShowCurrent();
    }

    /// <summary>Править выбранный шаг в его окне.</summary>
    public void Edit()
    {
        if (Selected is not null)
            EditRequested?.Invoke();
    }

    /// <summary>Выделить шаг, который бот выполняет сейчас.</summary>
    public void ShowCurrent()
    {
        var current = _current();
        for (var i = 0; i < Rows.Count; i++)
            Rows[i].Current = current == i;
    }

    private void Add()
    {
        Steps.Add(new ClickerStep());
        Load();
        Selected = Rows.LastOrDefault();
        _edited();
        Edit();
    }

    // Выделение — на следующий шаг (удалили последний — на новый последний): Delete можно жать подряд
    private void Remove()
    {
        if (Selected is not { } row)
            return;

        var at = Rows.IndexOf(row);
        Steps.Remove(row.Step);
        Load();
        Selected = Rows.Count == 0 ? null : Rows[Math.Min(at, Rows.Count - 1)];
        _edited();
    }

    private void Move(int step)
    {
        if (Selected is not { } row)
            return;

        var at = Steps.IndexOf(row.Step);
        var to = at + step;
        if (at < 0 || to < 0 || to >= Steps.Count)
            return;

        (Steps[at], Steps[to]) = (Steps[to], Steps[at]);
        Load();
        Selected = Rows[to];
        OnPropertyChanged(nameof(SelectedTitle));
        _edited();
    }

    // Действие или условие шага заменили новым видом
    private void PartReplaced(ClickerRow row)
    {
        Watch();
        row.Refresh();
        _edited();
    }

    // Слушать поля действия и условия выбранного шага: правка поля — строка в списке и сохранение
    private void Watch()
    {
        if (_watchedAction is not null)
            _watchedAction.PropertyChanged -= PartEdited;
        if (_watchedCondition is not null)
            _watchedCondition.PropertyChanged -= PartEdited;

        _watchedAction = Selected?.Step.Action;
        _watchedCondition = Selected?.Step.Condition;
        if (_watchedAction is not null)
            _watchedAction.PropertyChanged += PartEdited;
        if (_watchedCondition is not null)
            _watchedCondition.PropertyChanged += PartEdited;
        OnPropertyChanged(nameof(SelectedAction));
        OnPropertyChanged(nameof(SelectedCondition));
    }

    private void PartEdited(object? sender, PropertyChangedEventArgs e)
    {
        Selected?.Refresh();
        _edited();
    }
}
