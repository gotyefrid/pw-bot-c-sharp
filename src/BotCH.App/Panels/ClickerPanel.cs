using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using BotCH.App.Mvvm;
using BotCH.Core.Settings;

namespace BotCH.App.Panels;

/// <summary>
/// Вкладка «Кликер» — правила по порядку: какую клавишу жать, когда и как часто. Правила — в настройках персонажа; о правке
/// говорит модели окна (та сохраняет и отдаёт боту). Поля правки — у выбранного правила.
/// </summary>
public sealed class ClickerPanel : ObservableObject
{
    private readonly Func<BotSettings> _settings;
    private readonly Action _edited;
    private ClickerRow? _selected;
    private bool _syncing;

    /// <param name="settings">Настройки текущего персонажа (меняются при смене персонажа).</param>
    /// <param name="edited">Правка настроек из этой вкладки.</param>
    public ClickerPanel(Func<BotSettings> settings, Action edited)
    {
        _settings = settings;
        _edited = edited;
        ItemNames.CollectionChanged += (_, _) => ItemNamesEdited();
        AddCommand = new RelayCommand(Add);
        RemoveCommand = new RelayCommand(Remove, () => Selected is not null);
        UpCommand = new RelayCommand(() => Move(-1), () => Selected is { } r && Rows.IndexOf(r) > 0);
        DownCommand = new RelayCommand(() => Move(1), () => Selected is { } r && Rows.IndexOf(r) is var i && i >= 0 && i < Rows.Count - 1);
    }

    private List<ClickerRule> Rules => _settings().Clicker.Rules;

    public sealed record ConditionChoice(ClickerCondition Condition, string Title);

    public IReadOnlyList<ConditionChoice> Conditions { get; } =
    [
        new(ClickerCondition.Always, "Всегда"),
        new(ClickerCondition.HpBelow, "HP персонажа меньше, %"),
        new(ClickerCondition.ItemNearby, "Рядом лежит предмет"),
    ];

    /// <summary>Правила по порядку: верхние важнее.</summary>
    public ObservableCollection<ClickerRow> Rows { get; } = new();

    /// <summary>Предметы выбранного правила «Рядом лежит предмет»: названия или группы, как у лута.</summary>
    public ObservableCollection<string> ItemNames { get; } = new();

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
            Sync(ItemNames, value?.Rule.Items ?? []);
            foreach (var name in new[]
                     {
                         nameof(HasSelected), nameof(SelectedTitle), nameof(SelectedKey), nameof(SelectedCondition), nameof(ShowHp),
                         nameof(ShowItems), nameof(HpPercent), nameof(ItemRadius), nameof(RepeatText),
                     })
                OnPropertyChanged(name);
        }
    }

    public bool HasSelected => Selected is not null;

    public string SelectedTitle => Selected is { } row ? $"Правило {Rows.IndexOf(row) + 1}" : "";

    /// <summary>Клавиша выбранного правила строкой («F1», «Ctrl+2») — её ставит поле «нажмите клавишу».</summary>
    public string SelectedKey
    {
        get => Selected?.Rule.Key ?? "";
        set => Edit(value ?? "", r => r.Key, (r, v) => r.Key = v);
    }

    public ClickerCondition SelectedCondition
    {
        get => Selected?.Rule.Condition ?? ClickerCondition.Always;
        set
        {
            Edit(value, r => r.Condition, (r, v) => r.Condition = v);
            OnPropertyChanged(nameof(ShowHp));
            OnPropertyChanged(nameof(ShowItems));
        }
    }

    public bool ShowHp => Selected?.Rule.Condition == ClickerCondition.HpBelow;

    public bool ShowItems => Selected?.Rule.Condition == ClickerCondition.ItemNearby;

    public int HpPercent
    {
        get => Selected?.Rule.HpPercent ?? 0;
        set => Edit(value, r => r.HpPercent, (r, v) => r.HpPercent = v);
    }

    public int ItemRadius
    {
        get => Selected?.Rule.ItemRadius ?? 0;
        set => Edit(value, r => r.ItemRadius, (r, v) => r.ItemRadius = v);
    }

    /// <summary>
    /// Повтор, с — текстом: дробь пишут и через запятую, и через точку. Не число — правило не меняется. Поле не
    /// переписывается на ходу (иначе «0,» по дороге к «0,5» стало бы «0»): пределы применяет копия для бота.
    /// </summary>
    public string RepeatText
    {
        get => Selected?.Rule.RepeatSeconds.ToString("0.##") ?? "";
        set
        {
            if (double.TryParse((value ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                Edit(seconds, r => r.RepeatSeconds, (r, v) => r.RepeatSeconds = v);
        }
    }

    /// <summary>Список в окне ← настройки персонажа (выбор сбрасывается, если правила больше нет).</summary>
    public void Load()
    {
        var selected = Selected?.Rule;
        Rows.Clear();
        for (var i = 0; i < Rules.Count; i++)
            Rows.Add(new ClickerRow(i + 1, Rules[i]));
        Selected = Rows.FirstOrDefault(r => r.Rule == selected);
    }

    private void Add()
    {
        Rules.Add(new ClickerRule());
        Load();
        Selected = Rows.LastOrDefault();
        _edited();
    }

    private void Remove()
    {
        if (Selected is not { } row)
            return;

        Rules.Remove(row.Rule);
        Load();
        Selected = null;
        _edited();
    }

    private void Move(int step)
    {
        if (Selected is not { } row)
            return;

        var at = Rules.IndexOf(row.Rule);
        var to = at + step;
        if (at < 0 || to < 0 || to >= Rules.Count)
            return;

        (Rules[at], Rules[to]) = (Rules[to], Rules[at]);
        Load();
        Selected = Rows[to];
        OnPropertyChanged(nameof(SelectedTitle));
        _edited();
    }

    // Правка выбранного правила; то же значение — не правка (окно перечитывает поля при смене выбора)
    private void Edit<T>(T value, Func<ClickerRule, T> get, Action<ClickerRule, T> set)
    {
        if (Selected is not { } row || EqualityComparer<T>.Default.Equals(get(row.Rule), value))
            return;

        set(row.Rule, value);
        row.Refresh();
        _edited();
    }

    // Список в окне ← настройки (это не правка пользователя)
    private void Sync(ObservableCollection<string> target, IEnumerable<string> names)
    {
        _syncing = true;
        try
        {
            target.Clear();
            foreach (var name in names)
                target.Add(name);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void ItemNamesEdited()
    {
        if (_syncing || Selected is not { } row)
            return;

        row.Rule.Items = MobNameFilter.Clean(ItemNames);
        row.Refresh();
        _edited();
    }
}
