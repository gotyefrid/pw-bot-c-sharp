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
/// действия и условий показывают их шаблоны (по классу) и правят их напрямую — о правке говорят сами действие и список условий.
/// </summary>
public sealed class ClickerPanel : ObservableObject
{
    private readonly Func<BotSettings> _settings;
    private readonly Func<int?> _current;
    private readonly Action _edited;
    private ClickerRow? _selected;
    private ClickerAction? _watchedAction;
    private ConditionList? _watchedConditions;

    /// <param name="settings">Настройки текущего персонажа (меняются при смене персонажа).</param>
    /// <param name="current">Номер шага, который бот выполняет сейчас; null — бот не работает.</param>
    /// <param name="choices">Варианты для выбора в окнах шага и условий (предметы…).</param>
    /// <param name="edited">Правка настроек из этой вкладки.</param>
    public ClickerPanel(Func<BotSettings> settings, Func<int?> current, ClickerChoices choices, Action edited)
    {
        _settings = settings;
        _current = current;
        Choices = choices;
        _edited = edited;
        AddCommand = new RelayCommand(Add);
        RemoveCommand = new RelayCommand(Remove, () => Selected is not null);
        UpCommand = new RelayCommand(() => Move(-1), () => Selected is { } r && Rows.IndexOf(r) > 0);
        DownCommand = new RelayCommand(() => Move(1), () => Selected is { } r && Rows.IndexOf(r) is var i && i >= 0 && i < Rows.Count - 1);
    }

    private List<ClickerStep> Steps => _settings().Clicker.Steps;

    /// <summary>Виды действий — для выбора с поиском.</summary>
    public IReadOnlyList<PickOption> ActionKinds { get; } = PickLists.Kinds(ClickerKinds.Actions);

    /// <summary>Варианты для выбора в окнах шага и условий.</summary>
    public ClickerChoices Choices { get; }

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

    /// <summary>То же действие, если итог его виден по игре — для галочки «Ждать подтверждения выполнения»; иначе null.</summary>
    public ConfirmableAction? SelectedConfirmable => _watchedAction as ConfirmableAction;

    /// <summary>Условия выбранного шага — должны выполниться все; список правится в окне шага.</summary>
    public ConditionList? SelectedConditions => _watchedConditions;

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

    // Действие шага заменили новым видом
    private void PartReplaced(ClickerRow row)
    {
        Watch();
        row.Refresh();
        _edited();
    }

    // Слушать действие и условия выбранного шага: правка — строка в списке и сохранение
    private void Watch()
    {
        if (_watchedAction is not null)
            _watchedAction.PropertyChanged -= ActionEdited;
        if (_watchedConditions is not null)
            _watchedConditions.Edited -= PartEdited;

        _watchedAction = Selected?.Step.Action;
        _watchedConditions = Selected?.Step.Conditions;
        if (_watchedAction is not null)
            _watchedAction.PropertyChanged += ActionEdited;
        if (_watchedConditions is not null)
            _watchedConditions.Edited += PartEdited;
        OnPropertyChanged(nameof(SelectedAction));
        OnPropertyChanged(nameof(SelectedConfirmable));
        OnPropertyChanged(nameof(SelectedConditions));
    }

    private void ActionEdited(object? sender, PropertyChangedEventArgs e) => PartEdited();

    private void PartEdited()
    {
        Selected?.Refresh();
        _edited();
    }
}
