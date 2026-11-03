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
    private readonly HashSet<string> _seenBagItems = new(StringComparer.OrdinalIgnoreCase);
    // Одна картинка окна на иконку: список скиллов пересобирается, только когда что-то в нём правда поменялось
    private readonly Dictionary<IconImage, ImageSource> _icons = new();
    private bool _syncingLists;

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
        StashNames.CollectionChanged += (_, _) => NameListsEdited();
        string[] reserved = [StartCenter, AtFarm];
        FarmCenter = new PointPicker(settings, world, log, StartCenter, s => s.Target.FarmCenter, (s, v) => s.Target.FarmCenter = v,
            "Точка", "точка фарма", PointsChanged, reserved)
        {
            AddHint = "Запомнить, где стоит персонаж сейчас, как новую точку фарма",
        };
        DeathPoint = new PointPicker(settings, world, log, AtFarm, s => s.ExpDrain.DeathPoint, (s, v) => s.ExpDrain.DeathPoint = v,
            "Смерть", "точка для смертей", PointsChanged, reserved)
        {
            AddHint = "Запомнить, где стоит персонаж сейчас, как точку для смертей",
        };
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

    /// <summary>Что убирать в банк: названия предметов или группы.</summary>
    public ObservableCollection<string> StashNames { get; } = new();

    public Func<IReadOnlyList<PickOption>> StashOptions
        => () => PickLists.BagItems(_world()?.Inventory ?? [], _seenBagItems, _profile().Data.LootGroups.Select(g => g.Name));

    public Func<IReadOnlyList<PickOption>> MobOptions
        => () => PickLists.Options(_world() is { } w ? NearbyNames.Mobs(w) : [], _seenMobs.Values);

    /// <summary>Кого можно взять в цель (мобы, NPC, чужие петы) — для кликера.</summary>
    public Func<IReadOnlyList<PickOption>> TargetOptions
        => () => PickLists.Options(_world() is { } w ? NearbyNames.Targets(w) : [], _seenTargets.Values);

    public Func<IReadOnlyList<PickOption>> LootOptions
        => () => PickLists.Options(_world() is { } w ? NearbyNames.GroundItems(w) : [], _seenItems.Select(n => new NameCount(n, 0, 0)),
            _profile().Data.LootGroups.Select(g => g.Name));

    // ── Центр фарма ──────────────────────────────────────────────────────────

    /// <summary>Центр фарма: «Точка старта» или сохранённая точка.</summary>
    public PointPicker FarmCenter { get; }

    // ── Слив опыта ───────────────────────────────────────────────────────────

    public const string AtFarm = "На месте фарма";

    /// <summary>Где умирать при сливе опыта: «На месте фарма» или одна из тех же сохранённых точек, что у центра фарма.</summary>
    public PointPicker DeathPoint { get; }

    public sealed record DrainMobsChoice(DrainMobs Mobs, string Title);

    public IReadOnlyList<DrainMobsChoice> DrainMobsChoices { get; } =
        [new(DrainMobs.AsFarm, "Как при фарме"), new(DrainMobs.Ground, "Земных"), new(DrainMobs.Air, "Летающих"), new(DrainMobs.Water, "Водных")];

    // ── Снимок и персонаж ────────────────────────────────────────────────────

    /// <summary>Окно ← настройки персонажа (при запуске и смене персонажа); скиллы пересоберутся по снимку.</summary>
    public void Load()
    {
        LoadNameLists();
        LoadPoints();
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
        foreach (var item in w.Inventory)
            if (item.Name is { } name)
                _seenBagItems.Add(name.Trim());
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
        FarmCenter.UpdateInfo();
        DeathPoint.UpdateInfo();
    }

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
            Fill(StashNames, Settings.Loot.StashNames);
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
        Settings.Loot.StashNames = MobNameFilter.Clean(StashNames);
        _edited();
    }

    // Точки общие: добавили, изменили или удалили в одном списке — пересобрать оба
    private void PointsChanged()
    {
        LoadPoints();
        _edited();
    }

    /// <summary>Списки точек в окне ← точки персонажа.</summary>
    private void LoadPoints()
    {
        FarmCenter.Load();
        DeathPoint.Load();
    }
}
