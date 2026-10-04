using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using BotCH.Core.World;
using Newtonsoft.Json;

namespace BotCH.Core.Settings;

/// <summary>
/// Все настройки бота. Значения по умолчанию — как в старом боте (BotForm + ini).
/// Окно меняет «свой» экземпляр, бот получает копию (<see cref="Clone"/>) — правки в окне не попадают в бота посреди решения.
/// О любой правке — своей или в частях (цель, бой, лут, банки, пет, обход) — сообщает <see cref="Edited"/>.
/// </summary>
public sealed class BotSettings
{
    private BotMode _mode = BotMode.FarmMobs;
    private TargetSettings _target = new();
    private CombatSettings _combat = new();
    private LootSettings _loot = new();
    private PotionSettings _potions = new();
    private PetSettings _pet = new();
    private RouteSettings _route = new();

    public BotSettings()
    {
        foreach (var part in new SettingsPart[] { _target, _combat, _loot, _potions, _pet, _route })
            part.PropertyChanged += PartChanged;
    }

    /// <summary>Что-то поменяли (окно привязкой или кодом). Подключение (<see cref="Connection"/>) — не сюда: оно своё у окна.</summary>
    public event Action? Edited;

    /// <summary>Чем занимается бот у этого персонажа.</summary>
    public BotMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
                return;
            _mode = value;
            Edited?.Invoke();
        }
    }

    public ConnectionSettings Connection { get; set; } = new();
    public TargetSettings Target { get => _target; set => Part(ref _target, value); }
    public CombatSettings Combat { get => _combat; set => Part(ref _combat, value); }
    public LootSettings Loot { get => _loot; set => Part(ref _loot, value); }
    public PotionSettings Potions { get => _potions; set => Part(ref _potions, value); }
    public PetSettings Pet { get => _pet; set => Part(ref _pet, value); }
    public RouteSettings Route { get => _route; set => Part(ref _route, value); }

    // Часть заменили целиком (чтение файла, Normalize): слушаем новую
    private void Part<T>(ref T field, T value) where T : SettingsPart
    {
        if (ReferenceEquals(field, value))
            return;
        if (field is not null)
            field.PropertyChanged -= PartChanged;
        field = value;
        if (value is not null)
            value.PropertyChanged += PartChanged;
        Edited?.Invoke();
    }

    private void PartChanged(object? sender, PropertyChangedEventArgs e) => Edited?.Invoke();

    public BotSettings Clone() => SettingsJson.Parse(SettingsJson.Serialize(this));

    /// <summary>Приводит значения к допустимым (после ручной правки файла или старой версии).</summary>
    public BotSettings Normalize()
    {
        Connection ??= new();
        Target ??= new();
        Combat ??= new();
        Loot ??= new();
        Potions ??= new();
        Pet ??= new();
        Route ??= new();

        Target.MobNames = MobNameFilter.Clean(Target.MobNames);
        Target.MobTimeoutSeconds = Clamp(Target.MobTimeoutSeconds, 10, 3600);
        Target.FarmRadius = Clamp(Target.FarmRadius, 0, 500);
        Target.FarmPoints = FarmPoint.Clean(Target.FarmPoints);
        Target.FarmCenter = Target.FarmCenter?.Trim() ?? "";
        Combat.ComeCloserDistance = Clamp(Combat.ComeCloserDistance, 1, 30);
        Loot.ItemNames = MobNameFilter.Clean(Loot.ItemNames);
        Loot.ResourceNames = MobNameFilter.Clean(Loot.ResourceNames);
        Loot.Attempts = Clamp(Loot.Attempts, 1, 20);
        Loot.Radius = Clamp(Loot.Radius, 1, 30);
        Potions.HpPercent = Clamp(Potions.HpPercent, 0, 100);
        Potions.MpBelow = Math.Max(0, Potions.MpBelow);
        // Клеток у серверов разное число (1.3.6 — 10, Comeback 1.4.6 — 20); точный предел проверяет вызов по профилю
        Pet.Cage = Clamp(Pet.Cage, 1, 32);
        Pet.HealPercent = Clamp(Pet.HealPercent, 0, 100);
        Pet.GroundPet = Pet.GroundPet?.Trim() ?? "";
        Pet.AirPet = Pet.AirPet?.Trim() ?? "";
        Pet.WaterPet = Pet.WaterPet?.Trim() ?? "";
        Route.Points = RoutePoint.Clean(Route.Points);
        Route.Radius = Clamp(Route.Radius, 5, 500);
        Route.DangerLevel = Clamp(Route.DangerLevel, 0, 150);
        Route.DangerMobs = MobNameFilter.Clean(Route.DangerMobs);
        Route.DangerMargin = Clamp(Route.DangerMargin, 1, 50);
        return this;
    }

    private static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));
    private static float Clamp(float value, float min, float max) => Math.Min(max, Math.Max(min, value));
}

public enum BotMode
{
    /// <summary>Бить мобов (основной режим).</summary>
    FarmMobs,
    /// <summary>Собирать ресурсы: обход точек маршрута (<see cref="RouteSettings"/>).</summary>
    GatherResources,
    /// <summary>Кликер — часть 10, пока заготовка.</summary>
    Clicker,
}

public sealed class ConnectionSettings
{
    /// <summary>Профиль сервера (файл в Profiles).</summary>
    public string ServerId { get; set; } = "pwclassic136";

    /// <summary>Переименовывать окна клиентов в ник персонажа.</summary>
    public bool RenameWindows { get; set; } = true;

    /// <summary>Не давать клиенту «засыпать» без фокуса (запись в данные игры раз в секунду).</summary>
    public bool Unfreeze { get; set; }

    /// <summary>С кем бот работал последним — при запуске выбирается его клиент.</summary>
    public string LastCharacter { get; set; } = "";
}

public sealed class TargetSettings : SettingsPart
{
    private bool _killMobs = true;
    /// <summary>Нападать на мобов (старое «Kill Mobs»). Выключено — бот только лечится/кормит пета.</summary>
    public bool KillMobs { get => _killMobs; set => Set(ref _killMobs, value); }

    private bool _preferAggressive = true;
    /// <summary>Сначала бить моба, который бьёт перса или пета (старое «Find agr mob»).</summary>
    public bool PreferAggressive { get => _preferAggressive; set => Set(ref _preferAggressive, value); }

    private bool _petTakesAggro = true;
    /// <summary>
    /// Снимать мобов с себя петом: моба, который бьёт перса, бьём сразу (и перс, и пет), даже посреди боя с тем, кто бьёт пета, —
    /// пет прочнее, пусть держит обоих. Без призванного пета не действует.
    /// </summary>
    public bool PetTakesAggro { get => _petTakesAggro; set => Set(ref _petTakesAggro, value); }

    private bool _useMobList;
    /// <summary>Нападать только на мобов из списка названий (старое «Check ID»).</summary>
    public bool UseMobList { get => _useMobList; set => Set(ref _useMobList, value); }

    private List<string> _mobNames = [];
    /// <summary>Названия мобов. Одно название — много мобов с разными WID.</summary>
    public List<string> MobNames { get => _mobNames; set => Set(ref _mobNames, value); }

    private int _mobTimeoutSeconds = 120;
    /// <summary>Сколько секунд биться с одним мобом, прежде чем бросить.</summary>
    public int MobTimeoutSeconds { get => _mobTimeoutSeconds; set => Set(ref _mobTimeoutSeconds, value); }

    private int _farmRadius = 60;
    /// <summary>
    /// Радиус фарма, м: новые цели (мобы, ресурсы) — только не дальше этого от центра фарма (<see cref="FarmCenter"/>).
    /// Моб, который бьёт перса или пета, — всегда. 0 — без ограничения.
    /// </summary>
    public int FarmRadius { get => _farmRadius; set => Set(ref _farmRadius, value); }

    private List<FarmPoint> _farmPoints = [];
    /// <summary>Сохранённые точки фарма персонажа.</summary>
    public List<FarmPoint> FarmPoints { get => _farmPoints; set => Set(ref _farmPoints, value); }

    private string _farmCenter = "";
    /// <summary>Центр фарма — название точки из <see cref="FarmPoints"/>; пусто (или точки нет) — точка старта (где стоял перс при «Старт»).</summary>
    public string FarmCenter { get => _farmCenter; set => Set(ref _farmCenter, value); }

    private bool _returnToCenter = true;
    /// <summary>Делать нечего — бежать в центр фарма и ждать мобов там.</summary>
    public bool ReturnToCenter { get => _returnToCenter; set => Set(ref _returnToCenter, value); }

    private ApproachPath _returnPath = ApproachPath.Smart;
    /// <summary>Как бежать в центр: с автопутём или напрямик (отдельно от подхода к мобу).</summary>
    public ApproachPath ReturnPath { get => _returnPath; set => Set(ref _returnPath, value); }

    /// <summary>Выбранная точка фарма; null — центр — точка старта.</summary>
    [JsonIgnore]
    public FarmPoint? SelectedFarmPoint
        => FarmCenter.Length == 0 ? null : FarmPoints.FirstOrDefault(p => string.Equals(p.Name, FarmCenter, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Обход ресурсов: точки по порядку (после последней — первая), у каждой копаем ресурсы в радиусе — что именно, у каждой точки своё.
/// </summary>
public sealed class RouteSettings : SettingsPart
{
    private List<RoutePoint> _points = [];
    /// <summary>Точки обхода по порядку. Записаны в полёте — бот летит на их высоте.</summary>
    public List<RoutePoint> Points { get => _points; set => Set(ref _points, value); }

    private int _radius = 50;
    /// <summary>Радиус поиска ресурсов вокруг точки, м. Участок ресурса ~110 м, виден с ~80 м — 50 м от точки хватает.</summary>
    public int Radius { get => _radius; set => Set(ref _radius, value); }

    private int _dangerLevel;
    /// <summary>Опасны агрессивные мобы от этого уровня (0 — по уровню не считаем). См. <see cref="Brain.DangerZones"/>.</summary>
    public int DangerLevel { get => _dangerLevel; set => Set(ref _dangerLevel, value); }

    private List<string> _dangerMobs = [];
    /// <summary>Агрессивные мобы с этими названиями опасны при любом уровне (боссы).</summary>
    public List<string> DangerMobs { get => _dangerMobs; set => Set(ref _dangerMobs, value); }

    private int _dangerMargin = 1;
    /// <summary>Запас к радиусу агра опасного моба, м (не меньше 1).</summary>
    public int DangerMargin { get => _dangerMargin; set => Set(ref _dangerMargin, value); }
}

/// <summary>
/// Точка маршрута и что копать у неё: всё подряд, только из списка или всё, кроме списка. Например, у точки, где второй
/// ресурс часто появляется у агрессивного моба, — «только» лёгкий или «кроме» опасного.
/// </summary>
public sealed class RoutePoint
{
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Height { get; set; }

    public LootListMode ListMode { get; set; } = LootListMode.All;

    /// <summary>Названия ресурсов для <see cref="ListMode"/>.</summary>
    public List<string> Resources { get; set; } = [];

    [JsonIgnore]
    public Position Position => new(X, Height, Y);

    public static RoutePoint At(string name, Position p) => new() { Name = name, X = p.X, Y = p.Y, Height = p.Height };

    /// <summary>Обычный ресурс копать здесь.</summary>
    public bool Wants(string? name) => LootFilter.ByList(ListMode, Resources, name);

    /// <summary>«Нересурс» (квестовый, особый) назван явно — копать без условий; «кроме списка» значит «не трогать».</summary>
    public bool Lists(string? name) => LootFilter.NamedExplicitly(ListMode, Resources, name);

    /// <summary>Для лога и окна: «все ресурсы», «только: …», «кроме: …».</summary>
    public string Describe() => ListMode switch
    {
        _ when Resources.Count == 0 && ListMode == LootListMode.OnlyListed => "ничего (список пуст)",
        LootListMode.OnlyListed => "только: " + string.Join(", ", Resources),
        LootListMode.ExceptListed when Resources.Count > 0 => "кроме: " + string.Join(", ", Resources),
        _ => "все ресурсы",
    };

    /// <summary>Без пустых названий и повторов (без учёта регистра).</summary>
    public static List<RoutePoint> Clean(IEnumerable<RoutePoint?>? points)
        => (points ?? [])
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p =>
            {
                p!.Name = p.Name.Trim();
                p.Resources = MobNameFilter.Clean(p.Resources);
                return p;
            })
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
}

/// <summary>Сохранённая точка фарма: название и где (координаты как в снимке).</summary>
public sealed class FarmPoint
{
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Height { get; set; }

    [JsonIgnore]
    public Position Position => new(X, Height, Y);

    public static FarmPoint At(string name, Position p) => new() { Name = name, X = p.X, Y = p.Y, Height = p.Height };

    /// <summary>Без пустых названий и повторов (без учёта регистра).</summary>
    public static List<FarmPoint> Clean(IEnumerable<FarmPoint?>? points)
        => (points ?? [])
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => { p!.Name = p.Name.Trim(); return p; })
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
}

public sealed class CombatSettings : SettingsPart
{
    private bool _useSkill;
    public bool UseSkill { get => _useSkill; set => Set(ref _useSkill, value); }

    private int _attackSkillId = 299;
    /// <summary>ID атакующего скилла. 299 — по умолчанию в старом боте.</summary>
    public int AttackSkillId { get => _attackSkillId; set => Set(ref _attackSkillId, value); }

    private bool _useSword;
    /// <summary>Обычная атака (раз в ~5 с).</summary>
    public bool UseSword { get => _useSword; set => Set(ref _useSword, value); }

    private bool _comeCloser;
    /// <summary>Сначала подойти к мобу на <see cref="ComeCloserDistance"/>, потом бить (скиллом, атакой или только петом).</summary>
    public bool ComeCloser { get => _comeCloser; set => Set(ref _comeCloser, value); }

    private float _comeCloserDistance = 8;
    public float ComeCloserDistance { get => _comeCloserDistance; set => Set(ref _comeCloserDistance, value); }

    private ApproachPath _approachPath = ApproachPath.Smart;
    /// <summary>Как подходить: с автопутём (в обход препятствий, если сервер умеет) или напрямик.</summary>
    public ApproachPath ApproachPath { get => _approachPath; set => Set(ref _approachPath, value); }
}

public enum ApproachPath
{
    /// <summary>Автопуть, как клик по карте: в обход препятствий. Нет у сервера — по прямой.</summary>
    Smart,
    /// <summary>По прямой, как клик по земле.</summary>
    Direct,
}

public sealed class LootSettings : SettingsPart
{
    private bool _enabled;
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    private int _attempts = 4;
    /// <summary>Сколько раз подбирать после смерти моба.</summary>
    public int Attempts { get => _attempts; set => Set(ref _attempts, value); }

    private int _radius = 5;
    /// <summary>
    /// Радиус подбора, м — от места смерти моба (лут падает в 2–3 м от трупа). Больше — бот тянется за старым лутом
    /// прошлых мобов по цепочке.
    /// </summary>
    public int Radius { get => _radius; set => Set(ref _radius, value); }

    private bool _pickMoney = true;
    public bool PickMoney { get => _pickMoney; set => Set(ref _pickMoney, value); }
    private bool _pickItems = true;
    public bool PickItems { get => _pickItems; set => Set(ref _pickItems, value); }

    private bool _pickResources;
    /// <summary>
    /// Копать ресурсы в радиусе фарма: сначала все ресурсы, потом мобы. Нужна кирка в сумке. Не зависит от <see cref="Enabled"/>:
    /// можно копать, не подбирая лут с мобов, и наоборот. Что копать — <see cref="ResourceMode"/> и <see cref="ResourceNames"/>.
    /// </summary>
    public bool PickResources { get => _pickResources; set => Set(ref _pickResources, value); }

    private LootListMode _listMode = LootListMode.All;
    /// <summary>Как использовать <see cref="ItemNames"/>: не использовать / только они / все, кроме них. Монет не касается — у них <see cref="PickMoney"/>.</summary>
    public LootListMode ListMode { get => _listMode; set => Set(ref _listMode, value); }

    private List<string> _itemNames = [];
    /// <summary>Названия лута с мобов (как в игре: «Мягкий мех»). Ресурсов не касается — у них свой <see cref="ResourceNames"/>.</summary>
    public List<string> ItemNames { get => _itemNames; set => Set(ref _itemNames, value); }

    private LootListMode _resourceMode = LootListMode.All;
    /// <summary>Какие ресурсы копать в радиусе фарма: все / только из <see cref="ResourceNames"/> / все, кроме них.</summary>
    public LootListMode ResourceMode { get => _resourceMode; set => Set(ref _resourceMode, value); }

    private List<string> _resourceNames = [];
    /// <summary>Названия ресурсов для <see cref="ResourceMode"/> («Железная руда»).</summary>
    public List<string> ResourceNames { get => _resourceNames; set => Set(ref _resourceNames, value); }
}

public enum LootListMode
{
    /// <summary>Подбирать всё (список не используется).</summary>
    All,
    /// <summary>Белый список: только предметы из списка.</summary>
    OnlyListed,
    /// <summary>Чёрный список: всё, кроме предметов из списка.</summary>
    ExceptListed,
}

public sealed class PotionSettings : SettingsPart
{
    private int _hpPercent = 80;
    /// <summary>Пить банку HP, когда HP ниже этого процента.</summary>
    public int HpPercent { get => _hpPercent; set => Set(ref _hpPercent, value); }

    private int _mpBelow = 100;
    /// <summary>Пить банку MP, когда MP ниже этого числа (абсолютные единицы, как в старом боте).</summary>
    public int MpBelow { get => _mpBelow; set => Set(ref _mpBelow, value); }
}

public sealed class PetSettings : SettingsPart
{
    private bool _enabled = true;
    /// <summary>Пользоваться петом. Выключено или пета нет (не друид) — всё про пета пропускается.</summary>
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    private int _cage = 1;
    /// <summary>Клетка 1..N — если сервер не говорит, где питомец живёт (иначе — <see cref="GroundPet"/>/<see cref="AirPet"/>).</summary>
    public int Cage { get => _cage; set => Set(ref _cage, value); }

    private string _groundPet = "";
    /// <summary>Кого звать на земле — название питомца; пусто — первого, кто живёт на земле.</summary>
    public string GroundPet { get => _groundPet; set => Set(ref _groundPet, value); }

    private string _airPet = "";
    /// <summary>Кого звать в воздухе (персонаж летит) — название питомца; пусто — первого, кто летает.</summary>
    public string AirPet { get => _airPet; set => Set(ref _airPet, value); }

    private string _waterPet = "";
    /// <summary>Кого звать в воде — название питомца; пусто — первого, кто живёт в воде.</summary>
    public string WaterPet { get => _waterPet; set => Set(ref _waterPet, value); }

    private bool _onlyForFight;
    /// <summary>
    /// Пет только на время боя: напали (или бот начал бой) — призвать, боя нет несколько секунд — отозвать. Отозванного не
    /// кормим. Для обхода ресурсов: пет не ест между боями.
    /// </summary>
    public bool OnlyForFight { get => _onlyForFight; set => Set(ref _onlyForFight, value); }

    private int _healPercent = 70;
    /// <summary>Лечить пета, когда его HP ниже этого процента.</summary>
    public int HealPercent { get => _healPercent; set => Set(ref _healPercent, value); }
}

/// <summary>Подбирать ли предмет с земли — единственное место, где это решается.</summary>
public static class LootFilter
{
    public static bool Allows(LootSettings loot, GroundItemKind kind, string? name)
    {
        // Ресурс копается, а не подбирается — подбором никогда (см. AllowsGather)
        if (kind == GroundItemKind.Resource)
            return false;
        // Монеты — только галкой «Монеты»: список лута — про предметы (в «только из списка» монеты не пропадают)
        if (kind == GroundItemKind.Money)
            return loot.PickMoney;
        if (kind == GroundItemKind.Item && !loot.PickItems)
            return false;

        return ByList(loot.ListMode, loot.ItemNames, name);
    }

    public static bool Allows(LootSettings loot, GroundItem item) => Allows(loot, item.Kind, item.Name);

    /// <summary>Копать ли ресурс в радиусе фарма: «Копать ресурсы» включено, и список ресурсов разрешает (подбор лута ни при чём).</summary>
    public static bool AllowsGather(LootSettings loot, string? name)
        => loot.PickResources && ByList(loot.ResourceMode, loot.ResourceNames, name);

    /// <summary>
    /// Копать ли «нересурс» (квестовый, особый): «Копать ресурсы» включено, и название явно в списке ресурсов — в режимах «все»
    /// и «только из списка». «Кроме списка» значит «эти не трогать».
    /// </summary>
    public static bool ListsForGather(LootSettings loot, string? name)
        => loot.PickResources && NamedExplicitly(loot.ResourceMode, loot.ResourceNames, name);

    /// <summary>Пропускает ли список: «все» — всех, «только» — названных, «кроме» — всех, кроме названных.</summary>
    public static bool ByList(LootListMode mode, IReadOnlyCollection<string> names, string? name)
        => mode switch
        {
            LootListMode.OnlyListed => MobNameFilter.Contains(names, name),
            LootListMode.ExceptListed => !MobNameFilter.Contains(names, name),
            _ => true,
        };

    /// <summary>Название явно выбрано (в режимах «все» и «только»); «кроме списка» — значит «не трогать».</summary>
    public static bool NamedExplicitly(LootListMode mode, IReadOnlyCollection<string> names, string? name)
        => mode != LootListMode.ExceptListed && MobNameFilter.Contains(names, name);
}

/// <summary>Сравнение названий (мобов, предметов): без учёта регистра и пробелов по краям.</summary>
public static class MobNameFilter
{
    public static List<string> Clean(IEnumerable<string>? names)
        => (names ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Можно ли нападать на моба с таким названием при этих настройках.</summary>
    public static bool Allows(TargetSettings target, string? mobName)
    {
        if (!target.UseMobList || target.MobNames.Count == 0)
            return true;

        return Contains(target.MobNames, mobName);
    }

    public static bool Contains(IEnumerable<string> names, string? name)
    {
        var trimmed = name?.Trim() ?? "";
        return trimmed.Length > 0 && names.Any(n => string.Equals(n.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
