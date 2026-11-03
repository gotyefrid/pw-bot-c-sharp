using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Notes.App.Mvvm;
using Notes.Core.Logging;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.App.Panels;

/// <summary>
/// Выбор сохранённой точки с кнопками «добавить», «изменить», «удалить» (<see cref="PointPickerView"/>): центр фарма, место для
/// смертей. У каждого списка свои точки (<c>points</c>), свой выбор и свой первый пункт вместо точки («Точка старта», «На месте
/// фарма»). Поменялись точки — <c>pointsChanged</c>.
/// </summary>
public sealed class PointPicker : ObservableObject
{
    private readonly Func<BotSettings> _settings;
    private readonly Func<WorldState?> _world;
    private readonly ILogger _log;
    private readonly Func<BotSettings, List<FarmPoint>> _points;
    private readonly Func<BotSettings, string> _get;
    private readonly Action<BotSettings, string> _set;
    private readonly string _prefix;
    private readonly string _what;
    private readonly Action _pointsChanged;
    private bool _syncing;
    private string _info = "";

    /// <param name="none">Первый пункт — без точки.</param>
    /// <param name="points">Точки этого списка в настройках.</param>
    /// <param name="get">Выбранная точка в настройках; пусто — <paramref name="none"/>.</param>
    /// <param name="prefix">Название новой точки: «Точка N».</param>
    /// <param name="what">Для лога: «точка фарма».</param>
    public PointPicker(Func<BotSettings> settings, Func<WorldState?> world, ILogger log, string none, Func<BotSettings, List<FarmPoint>> points,
        Func<BotSettings, string> get, Action<BotSettings, string> set, string prefix, string what, Action pointsChanged)
    {
        _points = points;
        _settings = settings;
        _world = world;
        _log = log;
        None = none;
        _get = get;
        _set = set;
        _prefix = prefix;
        _what = what;
        _pointsChanged = pointsChanged;
        AddCommand = new RelayCommand(Add, () => _world() is not null);
        EditCommand = new RelayCommand(Edit, () => Point is not null);
        RemoveCommand = new RelayCommand(Remove, () => Point is not null);
    }

    /// <summary>Правка точки: окну открыть <see cref="PointWindow"/> с этим черновиком.</summary>
    public event Action<PointDraft>? EditRequested;

    public string None { get; }

    /// <summary>Подсказка у «+».</summary>
    public string AddHint { get; init; } = "";

    /// <summary><see cref="None"/> и сохранённые точки.</summary>
    public ObservableCollection<string> Items { get; } = new();

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand RemoveCommand { get; }

    private BotSettings Settings => _settings();

    /// <summary>Выбранная точка; null — <see cref="None"/> или точки с таким названием нет.</summary>
    public FarmPoint? Point => _get(Settings) is { Length: > 0 } name ? BotSettings.FindPoint(_points(Settings), name) : null;

    public bool HasPoint => Point is not null;

    // Пока список пересобирается — null: окно, сбросив выбор, тут же перечитывает его, и пункт, которого ещё нет в списке,
    // остался бы «выбранным», но невидимым (поле пустое). После пересборки выбор меняется null → пункт, и окно его находит
    public string? Selected
    {
        get => _syncing ? null : Point?.Name ?? None;
        set
        {
            // Пересборка списка сбрасывает выбор — это не выбор пользователя
            if (_syncing || value is null)
                return;
            _set(Settings, value == None ? "" : value);
            Changed();
        }
    }

    /// <summary>Сколько до выбранной точки отсюда.</summary>
    public string Info { get => _info; private set => SetProperty(ref _info, value); }

    /// <summary>Список ← точки персонажа.</summary>
    public void Load()
    {
        _syncing = true;
        try
        {
            Items.Clear();
            Items.Add(None);
            foreach (var point in _points(Settings))
                Items.Add(point.Name);
        }
        finally
        {
            _syncing = false;
        }
        Changed();
    }

    public void UpdateInfo()
        => Info = Point is { } p && _world() is { } w ? $"{w.Host.Position.HorizontalDistanceTo(p.Position):0} м отсюда" : "";

    private void Changed()
    {
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasPoint));
        UpdateInfo();
    }

    // Запомнить, где стоит перс, как новую точку, и выбрать её здесь
    private void Add()
    {
        if (_world() is not { } w)
            return;
        var point = BotSettings.AddPoint(_points(Settings), _prefix, w.Host.Position);
        _set(Settings, point.Name);
        _log.Info($"Сохранена {_what} «{point.Name}»: {Show(point.Position)}");
        _pointsChanged();
    }

    private void Edit()
    {
        if (Point is not { } point)
            return;
        var draft = new PointDraft(Settings, _points(Settings), point, () => _world()?.Host.Position, None);
        draft.Saved += () =>
        {
            _log.Info($"Точка «{point.Name}» изменена: {Show(point.Position)}");
            _pointsChanged();
        };
        EditRequested?.Invoke(draft);
    }

    private void Remove()
    {
        if (Point is not { } point)
            return;
        Settings.RemovePoint(point);
        _log.Info($"Точка «{point.Name}» удалена");
        _pointsChanged();
    }

    private static string Show(Position p)
    {
        var map = MapCoords.From(p);
        return $"{map.X:0} {map.Y:0}, высота {map.Height:0}";
    }
}
