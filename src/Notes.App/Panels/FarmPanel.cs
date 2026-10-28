using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Notes.App.Mvvm;
using Notes.Core.GameFiles;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.App.Panels;

/// <summary>
/// Вкладки «Мобы» и «Общее» сверх простых полей настроек: центр фарма и сохранённые точки, списки мобов, лута и ресурсов
/// (выбор из того, что вокруг и встречалось), атакующий скилл из изученных или макрос персонажа (с иконками из игры). Всё — в
/// настройках персонажа; о правке говорит модели окна.
/// </summary>
public sealed class FarmPanel : ObservableObject
{
    /// <summary>Пункт списка «центр фарма» вместо сохранённой точки.</summary>
    public const string StartCenter = "Точка старта";

    private readonly Func<BotSettings> _settings;
    private readonly Func<WorldState?> _world;
    private readonly Func<ServerProfile> _profile;
    private readonly Func<int, IconImage?> _skillIcon;
    private readonly Func<MacroInfo, IconImage?> _macroIcon;
    private readonly ILogger _log;
    private readonly Action _edited;
    private readonly Action _skillsRebuilt;

    // Всё, что бот видел за сессию: можно выбрать моба, который сейчас ушёл из виду
    private readonly Dictionary<string, NameCount> _seenMobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NameCount> _seenTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenItems = new(StringComparer.OrdinalIgnoreCase);
    // Одна картинка окна на иконку: список скиллов пересобирается, только когда что-то в нём правда поменялось
    private readonly Dictionary<IconImage, ImageSource> _icons = new();
    private bool _syncingLists;
    private bool _syncingCenters;
    private string _farmPointInfo = "";

    /// <param name="settings">Настройки текущего персонажа.</param>
    /// <param name="world">Последний снимок; null — мир не читается.</param>
    /// <param name="profile">Профиль сервера: какие скиллы не атакующие, какой атакующий по умолчанию.</param>
    /// <param name="skillIcon">Иконка скилла из игры; null — нет (ещё не загрузились или на этом сервере не найдены).</param>
    /// <param name="macroIcon">Иконка макроса персонажа; null — нет.</param>
    /// <param name="edited">Правка настроек из этих вкладок.</param>
    /// <param name="skillsRebuilt">Список скиллов пересобран — окну перечитать выбор.</param>
    public FarmPanel(Func<BotSettings> settings, Func<WorldState?> world, Func<ServerProfile> profile, Func<int, IconImage?> skillIcon,
        Func<MacroInfo, IconImage?> macroIcon, ILogger log, Action edited, Action skillsRebuilt)
    {
        _settings = settings;
        _world = world;
        _profile = profile;
        _skillIcon = skillIcon;
        _macroIcon = macroIcon;
        _log = log;
        _edited = edited;
        _skillsRebuilt = skillsRebuilt;
        MobNames.CollectionChanged += (_, _) => NameListsEdited();
        LootNames.CollectionChanged += (_, _) => NameListsEdited();
        FarmResourceNames.CollectionChanged += (_, _) => NameListsEdited();
        AddFarmPointCommand = new RelayCommand(AddFarmPoint, () => _world() is not null);
        RemoveFarmPointCommand = new RelayCommand(RemoveFarmPoint, () => HasFarmPoint);
    }

    private BotSettings Settings => _settings();

    // ── Списки и скилл ───────────────────────────────────────────────────────

    /// <param name="Key">ID скилла; у макроса — минус его номер.</param>
    public sealed record AttackChoice(int Key, string Title, ImageSource? Icon);

    /// <summary>
    /// Чем бить: атакующие скиллы персонажа (изученные, кроме лечения/воскрешения/портала) с названиями из игры, за ними —
    /// его макросы.
    /// </summary>
    public ObservableCollection<AttackChoice> AttackSkills { get; } = new();

    /// <summary>Выбор в списке «Атакующий скилл»: ID скилла или минус номер макроса (<see cref="AttackChoice.Key"/>).</summary>
    public int SelectedAttack
    {
        get => Settings.Combat.AttackMacro > 0 ? -Settings.Combat.AttackMacro : Settings.Combat.AttackSkillId;
        set
        {
            // 0 — пересборка списка сбросила выбор: это не выбор пользователя
            if (value < 0)
            {
                Settings.Combat.AttackMacro = -value;
            }
            else if (value > 0)
            {
                Settings.Combat.AttackMacro = 0;
                Settings.Combat.AttackSkillId = value;
            }
        }
    }

    public sealed record PathChoice(ApproachPath Path, string Title);

    public IReadOnlyList<PathChoice> ApproachPaths { get; } = [new(ApproachPath.Smart, "Умно"), new(ApproachPath.Direct, "Прямо")];

    public IReadOnlyList<LootListMode> LootModes { get; } = [LootListMode.All, LootListMode.OnlyListed, LootListMode.ExceptListed];

    /// <summary>Мобы для белого списка — выбираются из списка (TagPicker), не вводятся руками.</summary>
    public ObservableCollection<string> MobNames { get; } = new();

    /// <summary>Предметы для белого/чёрного списка лута.</summary>
    public ObservableCollection<string> LootNames { get; } = new();

    /// <summary>Ресурсы для белого/чёрного списка копания в радиусе фарма (варианты — как у точки обхода).</summary>
    public ObservableCollection<string> FarmResourceNames { get; } = new();

    public Func<IReadOnlyList<PickOption>> MobOptions
        => () => PickLists.Options(_world() is { } w ? NearbyNames.Mobs(w) : [], _seenMobs.Values);

    /// <summary>Кого можно взять в цель (мобы, NPC, чужие петы) — для кликера.</summary>
    public Func<IReadOnlyList<PickOption>> TargetOptions
        => () => PickLists.Options(_world() is { } w ? NearbyNames.Targets(w) : [], _seenTargets.Values);

    public Func<IReadOnlyList<PickOption>> LootOptions
        => () => PickLists.Options(_world() is { } w ? NearbyNames.GroundItems(w) : [], _seenItems.Select(n => new NameCount(n, 0, 0)),
            _profile().Data.LootGroups.Select(g => g.Name));

    // ── Центр фарма ──────────────────────────────────────────────────────────

    /// <summary>«Точка старта» и сохранённые точки персонажа.</summary>
    public ObservableCollection<string> FarmCenters { get; } = new();

    public ICommand AddFarmPointCommand { get; }
    public ICommand RemoveFarmPointCommand { get; }

    public string SelectedFarmCenter
    {
        get => Settings.Target.SelectedFarmPoint?.Name ?? StartCenter;
        set
        {
            // Пересборка списка сбрасывает выбор — это не выбор пользователя
            if (_syncingCenters || value is null)
                return;

            Settings.Target.FarmCenter = value == StartCenter ? "" : value;
            FarmCenterChanged();
        }
    }

    /// <summary>Выбрана сохранённая точка (а не точка старта).</summary>
    public bool HasFarmPoint => Settings.Target.SelectedFarmPoint is not null;

    /// <summary>Название выбранной точки; правка — переименование.</summary>
    public string FarmPointName
    {
        get => Settings.Target.SelectedFarmPoint?.Name ?? "";
        set
        {
            var point = Settings.Target.SelectedFarmPoint;
            var name = value?.Trim() ?? "";
            if (point is null || name.Length == 0 || name == point.Name)
                return;
            if (name.Equals(StartCenter, StringComparison.OrdinalIgnoreCase)
                || Settings.Target.FarmPoints.Any(p => p != point && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                _log.Warning($"Точка «{name}» уже есть — название не меняю");
                OnPropertyChanged();
                return;
            }

            point.Name = name;
            Settings.Target.FarmCenter = name;
            LoadFarmCenters();
            _edited();
        }
    }

    /// <summary>Сколько до выбранной точки отсюда.</summary>
    public string FarmPointInfo { get => _farmPointInfo; private set => SetProperty(ref _farmPointInfo, value); }

    // ── Снимок и персонаж ────────────────────────────────────────────────────

    /// <summary>Окно ← настройки персонажа (при запуске и смене персонажа); скиллы пересоберутся по снимку.</summary>
    public void Load()
    {
        LoadNameLists();
        LoadFarmCenters();
        AttackSkills.Clear();
        OnPropertyChanged(nameof(SelectedAttack));
    }

    /// <summary>Новый снимок: кого и что встречали, изученные скиллы, сколько до точки фарма.</summary>
    public void Observe(WorldState w)
    {
        foreach (var kind in NearbyNames.Mobs(w))
            Remember(_seenMobs, kind);
        foreach (var kind in NearbyNames.Targets(w))
            Remember(_seenTargets, kind);
        foreach (var item in w.GroundItems.Where(NearbyNames.IsLootItem))
            _seenItems.Add(item.Name.Trim());
    }

    // Уровни вида копятся за сессию: «Волк (ур. 10–12)», даже если сейчас рядом только один
    private static void Remember(Dictionary<string, NameCount> seen, NameCount kind)
        => seen[kind.Name] = seen.TryGetValue(kind.Name, out var before) && before.MaxLevel > 0
            ? kind with { MinLevel = Math.Min(before.MinLevel, kind.MinLevel), MaxLevel = Math.Max(before.MaxLevel, kind.MaxLevel) }
            : kind;

    /// <summary>После смены персонажа (если была) — его скиллы и расстояние до его точки.</summary>
    public void ShowFor(WorldState w)
    {
        UpdateAttackSkills(w);
        UpdateFarmPointInfo();
    }

    public void UpdateFarmPointInfo()
        => FarmPointInfo = Settings.Target.SelectedFarmPoint is { } p && _world() is { } w
            ? $"{w.Host.Position.HorizontalDistanceTo(p.Position):0} м отсюда"
            : "";

    private void UpdateAttackSkills(WorldState w)
    {
        // Названия и иконки догружаются в фоне — список пересобирается, когда изменились скиллы, макросы, названия или иконки
        var profile = _profile().Data;
        var choices = w.Skills
            .Where(s => !profile.Skills.NotAttack.Contains(s.Id))
            .Select(s => new AttackChoice(s.Id, s.Name is null ? $"скилл {s.Id}" : $"{s.Name} ({s.Id})", ToImage(_skillIcon(s.Id))))
            .Concat(w.Macros.Select(m => new AttackChoice(-m.Number, $"Макрос {m.Number}", ToImage(_macroIcon(m)))))
            .ToList();
        if (choices.SequenceEqual(AttackSkills))
            return;

        AttackSkills.Clear();
        foreach (var choice in choices)
            AttackSkills.Add(choice);

        // Выбранного нет среди изученных — атакующий скилл класса по умолчанию, иначе первый. Выбранного макроса больше нет — скилл
        var ids = choices.Where(c => c.Key > 0).Select(c => c.Key).ToList();
        if (ids.Count > 0 && !ids.Contains(Settings.Combat.AttackSkillId))
        {
            Settings.Combat.AttackSkillId = ids.Contains(profile.Skills.DefaultAttack) ? profile.Skills.DefaultAttack : ids[0];
            _edited();
        }

        if (Settings.Combat.AttackMacro > 0 && w.Macros.Count > 0 && w.Macros.All(m => m.Number != Settings.Combat.AttackMacro))
        {
            Settings.Combat.AttackMacro = 0;
            _edited();
        }

        OnPropertyChanged(nameof(SelectedAttack));
        _skillsRebuilt();
    }

    private ImageSource? ToImage(IconImage? icon)
    {
        if (icon is null)
            return null;

        if (!_icons.TryGetValue(icon, out var image))
        {
            var bitmap = BitmapSource.Create(icon.Width, icon.Height, 96, 96, PixelFormats.Bgra32, null, icon.Pixels, icon.Width * 4);
            bitmap.Freeze();
            _icons[icon] = image = bitmap;
        }

        return image;
    }

    /// <summary>Списки в окне ← настройки персонажа.</summary>
    private void LoadNameLists()
    {
        _syncingLists = true;
        try
        {
            Fill(MobNames, Settings.Target.MobNames);
            Fill(LootNames, Settings.Loot.ItemNames);
            Fill(FarmResourceNames, Settings.Loot.ResourceNames);
        }
        finally
        {
            _syncingLists = false;
        }
    }

    private static void Fill(ObservableCollection<string> target, IEnumerable<string> names)
    {
        target.Clear();
        foreach (var name in names)
            target.Add(name);
    }

    /// <summary>Выбрали/убрали название в окне → в настройки персонажа.</summary>
    private void NameListsEdited()
    {
        if (_syncingLists)
            return;

        Settings.Target.MobNames = MobNameFilter.Clean(MobNames);
        Settings.Loot.ItemNames = MobNameFilter.Clean(LootNames);
        Settings.Loot.ResourceNames = MobNameFilter.Clean(FarmResourceNames);
        _edited();
    }

    /// <summary>Запомнить, где стоит персонаж, как новую точку фарма, и сделать её центром.</summary>
    private void AddFarmPoint()
    {
        if (_world() is not { } w)
            return;

        var points = Settings.Target.FarmPoints;
        var n = 1;
        while (points.Any(p => p.Name.Equals($"Точка {n}", StringComparison.OrdinalIgnoreCase)))
            n++;
        var point = FarmPoint.At($"Точка {n}", w.Host.Position);
        points.Add(point);
        Settings.Target.FarmCenter = point.Name;
        _log.Info($"Точка фарма «{point.Name}» сохранена: {point.Position}");
        LoadFarmCenters();
        _edited();
    }

    private void RemoveFarmPoint()
    {
        if (Settings.Target.SelectedFarmPoint is not { } point)
            return;

        Settings.Target.FarmPoints.Remove(point);
        Settings.Target.FarmCenter = "";
        _log.Info($"Точка фарма «{point.Name}» удалена — центр: точка старта");
        LoadFarmCenters();
        _edited();
    }

    /// <summary>Список в окне ← точки персонажа.</summary>
    private void LoadFarmCenters()
    {
        _syncingCenters = true;
        try
        {
            FarmCenters.Clear();
            FarmCenters.Add(StartCenter);
            foreach (var point in Settings.Target.FarmPoints)
                FarmCenters.Add(point.Name);
        }
        finally
        {
            _syncingCenters = false;
        }

        FarmCenterChanged();
    }

    private void FarmCenterChanged()
    {
        OnPropertyChanged(nameof(SelectedFarmCenter));
        OnPropertyChanged(nameof(HasFarmPoint));
        OnPropertyChanged(nameof(FarmPointName));
        UpdateFarmPointInfo();
    }
}
