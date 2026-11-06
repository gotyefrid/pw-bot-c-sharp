using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Notes.Core.Clients;
using Notes.Core.Clicker;
using Notes.Core.World;
using Newtonsoft.Json;

namespace Notes.Core.Settings;

/// <summary>
/// Все настройки бота. Значения по умолчанию — как в старом боте (BotForm + ini).
/// Окно меняет «свой» экземпляр, бот получает копию (<see cref="Clone"/>) — правки в окне не попадают в бота посреди решения.
/// О любой правке — своей или в частях (цель, бой, лут, банки, пет, обход, кликер) — сообщает <see cref="Edited"/>.
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
    private ClickerSettings _clicker = new();
    private QuestSettings _quests = new();
    private ExpDrainSettings _expDrain = new();
    private RepairSettings _repair = new();
    private string _iconColor = "";

    public BotSettings()
    {
        foreach (var part in new SettingsPart[] { _target, _combat, _loot, _potions, _pet, _route, _clicker, _quests, _expDrain, _repair })
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

    /// <summary>Цвет значка бота на панели задач у этого персонажа, «#RRGGBB»; пусто — по нику (из палитры окна).</summary>
    public string IconColor
    {
        get => _iconColor;
        set
        {
            value ??= "";
            if (_iconColor == value)
                return;
            _iconColor = value;
            Edited?.Invoke();
        }
    }

    public ConnectionSettings Connection { get; set; } = new();

    /// <summary>Что показывать в логе окна. Своё у окна, как <see cref="Connection"/>: в <see cref="Edited"/> не идёт.</summary>
    public LogViewSettings LogView { get; set; } = new();

    /// <summary>Уведомления о сообщениях в чате — в Telegram. Свои у окна, одни на всех персонажей: в <see cref="Edited"/> не идут.</summary>
    public ChatNotifySettings ChatNotify { get; set; } = new();
    public TargetSettings Target { get => _target; set => Part(ref _target, value); }
    public CombatSettings Combat { get => _combat; set => Part(ref _combat, value); }
    public LootSettings Loot { get => _loot; set => Part(ref _loot, value); }
    public PotionSettings Potions { get => _potions; set => Part(ref _potions, value); }
    public PetSettings Pet { get => _pet; set => Part(ref _pet, value); }
    public RouteSettings Route { get => _route; set => Part(ref _route, value); }
    public ClickerSettings Clicker { get => _clicker; set => Part(ref _clicker, value); }
    public QuestSettings Quests { get => _quests; set => Part(ref _quests, value); }
    public ExpDrainSettings ExpDrain { get => _expDrain; set => Part(ref _expDrain, value); }
    public RepairSettings Repair { get => _repair; set => Part(ref _repair, value); }

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

    /// <summary>Цвет «#RRGGBB».</summary>
    public static bool IsColor(string? text)
        => text is { Length: 7 } && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit);

    // ── Сохранённые точки: свои у центра фарма (TargetSettings.FarmPoints) и у места для смертей (ExpDrainSettings.DeathPoints) ──

    /// <summary>Новая точка «<paramref name="prefix"/> N» (первый свободный номер в этом списке) в <paramref name="at"/>.</summary>
    public static FarmPoint AddPoint(List<FarmPoint> points, string prefix, Position at, bool flying = false, bool inWater = false)
    {
        var n = 1;
        while (FindPoint(points, $"{prefix} {n}") is not null)
            n++;
        var point = FarmPoint.At($"{prefix} {n}", at, flying, inWater);
        points.Add(point);
        return point;
    }

    public static FarmPoint? FindPoint(List<FarmPoint> points, string name)
        => points.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Почему точке нельзя дать это название; null — можно. Повтор — только в своём списке; <paramref name="reserved"/> — пункт
    /// списка вместо точки («Точка старта»).
    /// </summary>
    public static string? WhyNotName(List<FarmPoint> points, FarmPoint point, string name, params string[] reserved)
    {
        name = name.Trim();
        if (name.Length == 0)
            return "название пустое";
        if (reserved.Any(r => string.Equals(r, name, StringComparison.OrdinalIgnoreCase))
            || FindPoint(points, name) is { } other && other != point)
            return $"точка «{name}» уже есть";
        return null;
    }

    /// <summary>Переименовать точку: если она выбрана в своём списке (центр фарма, место для смертей) — там новое название.</summary>
    public void RenamePoint(FarmPoint point, string name)
    {
        name = name.Trim();
        if (Target.SelectedFarmPoint == point)
            Target.FarmCenter = name;
        if (ExpDrain.FindDeathPoint() == point)
            ExpDrain.DeathPoint = name;
        point.Name = name;
    }

    /// <summary>Удалить точку: если она была выбрана — центр фарма станет точкой старта, смерти — на месте фарма.</summary>
    public void RemovePoint(FarmPoint point)
    {
        if (Target.SelectedFarmPoint == point)
            Target.FarmCenter = "";
        if (ExpDrain.FindDeathPoint() == point)
            ExpDrain.DeathPoint = "";
        Target.FarmPoints.Remove(point);
        ExpDrain.DeathPoints.Remove(point);
    }

    /// <summary>Приводит значения к допустимым (после ручной правки файла или старой версии).</summary>
    public BotSettings Normalize()
    {
        // «Прокачать друида» не доделан и скрыт в окне (MainViewModel.Modes): сохранённый — как «Бить мобов»
        if (Mode == BotMode.LevelUp)
            Mode = BotMode.FarmMobs;
        IconColor = IsColor(IconColor) ? IconColor.ToUpperInvariant() : "";
        Connection ??= new();
        Connection.ClientPaths ??= new Dictionary<string, string>();
        LogView ??= new();
        LogView.Hidden ??= [];
        ChatNotify ??= new();
        ChatNotify.Token = ChatNotify.Token?.Trim() ?? "";
        ChatNotify.ChatId = ChatNotify.ChatId?.Trim() ?? "";
        Target ??= new();
        Combat ??= new();
        Loot ??= new();
        Potions ??= new();
        Pet ??= new();
        Route ??= new();
        Clicker ??= new();
        Quests ??= new();
        ExpDrain ??= new();
        Repair ??= new();

        Target.MobNames = MobNameFilter.Clean(Target.MobNames);
        Target.MobTimeoutSeconds = Clamp(Target.MobTimeoutSeconds, 10, 3600);
        Target.FarmRadius = Clamp(Target.FarmRadius, 0, 500);
        Target.TravelFrom = Clamp(Target.TravelFrom, 0, 3000);
        Target.TravelHeight = Clamp(Target.TravelHeight, 0, 800);
        Target.FarmPoints = FarmPoint.Clean(Target.FarmPoints);
        Target.FarmCenter = Target.FarmCenter?.Trim() ?? "";
        Combat.ComeCloserDistance = Clamp(Combat.ComeCloserDistance, 1, 30);
        Loot.ItemNames = MobNameFilter.Clean(Loot.ItemNames);
        Loot.ResourceNames = MobNameFilter.Clean(Loot.ResourceNames);
        Loot.StashNames = MobNameFilter.Clean(Loot.StashNames);
        Loot.Attempts = Clamp(Loot.Attempts, 1, 20);
        Loot.Radius = Clamp(Loot.Radius, 1, 30);
        Potions.HpPercent = Clamp(Potions.HpPercent, 0, 100);
        Potions.MpBelow = Math.Max(0, Potions.MpBelow);
        Potions.HpPotion = Potions.HpPotion?.Trim() ?? "";
        Potions.MpPotion = Potions.MpPotion?.Trim() ?? "";
        // Клеток у серверов разное число (1.3.6 — 10, Comeback 1.4.6 — 20); точный предел проверяет вызов по профилю
        Pet.Cage = Clamp(Pet.Cage, 1, 32);
        Pet.HealPercent = Clamp(Pet.HealPercent, 0, 100);
        Pet.GroundPet = Pet.GroundPet?.Trim() ?? "";
        Pet.AirPet = Pet.AirPet?.Trim() ?? "";
        Pet.WaterPet = Pet.WaterPet?.Trim() ?? "";
        Pet.Food = Pet.Food?.Trim() ?? "";
        Route.Points = RoutePoint.Clean(Route.Points);
        Route.Radius = Clamp(Route.Radius, 5, 500);
        Route.DangerLevel = Clamp(Route.DangerLevel, 0, 150);
        Route.DangerMobs = MobNameFilter.Clean(Route.DangerMobs);
        Route.DangerMargin = Clamp(Route.DangerMargin, 1, 50);
        Clicker.Steps = ClickerStep.Clean(Clicker.Steps);
        Quests.SkipNames = MobNameFilter.Clean(Quests.SkipNames);
        ExpDrain.MaxLevel = Clamp(ExpDrain.MaxLevel, 1, 150);
        ExpDrain.StartPercent = Clamp(ExpDrain.StartPercent, 1, 100);
        // «До» не меньше «от» — не правим молча (окно поменяло бы второе поле само), слив пишет об этом в лог
        ExpDrain.TargetPercent = Clamp(ExpDrain.TargetPercent, 0, 99);
        ExpDrain.Mobs = Clamp(ExpDrain.Mobs, 1, 10);
        ExpDrain.FirstFlightHeight = Clamp(ExpDrain.FirstFlightHeight, 0, 800);
        ExpDrain.FlightHeight = Clamp(ExpDrain.FlightHeight, 0, 800);
        ExpDrain.FarmFlightHeight = Clamp(ExpDrain.FarmFlightHeight, 0, 800);
        ExpDrain.DeathPoint = ExpDrain.DeathPoint?.Trim() ?? "";
        ExpDrain.DeathPoints = FarmPoint.Clean(ExpDrain.DeathPoints);
        MoveOldDeathPoint();
        Repair.BelowPercent = Clamp(Repair.BelowPercent, 1, 99);
        Repair.FlightHeight = Clamp(Repair.FlightHeight, 0, 800);
        if (Repair.Npc is { } npc)
            npc.Name = npc.Name?.Trim() ?? "";
        if (Repair.Npc is { Name.Length: 0 })
            Repair.Npc = null;
        return this;
    }

    // Раньше точка для смертей была одной из точек фарма: выбранную — в свой список. У фарма остаётся, только если она там центр
    private void MoveOldDeathPoint()
    {
        var name = ExpDrain.DeathPoint;
        if (name.Length == 0 || FindPoint(ExpDrain.DeathPoints, name) is not null || FindPoint(Target.FarmPoints, name) is not { } old)
            return;
        ExpDrain.DeathPoints.Add(FarmPoint.At(old.Name, old.Position, old.Flying, old.InWater));
        if (Target.SelectedFarmPoint != old)
            Target.FarmPoints.Remove(old);
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
    /// <summary>Кликер: шаги сценария по кругу (<see cref="ClickerSettings"/>).</summary>
    Clicker,
    /// <summary>Прокачать друида: стартовые задания «убить» и «поговорить» (<see cref="Brain.QuestBehavior"/>).</summary>
    LevelUp,
}

public sealed class ConnectionSettings
{
    /// <summary>Профиль сервера (файл в Profiles).</summary>
    public string ServerId { get; set; } = "pwclassic136";

    /// <summary>Переименовывать окна клиентов в ник персонажа.</summary>
    public bool RenameWindows { get; set; } = true;

    /// <summary>С кем бот работал последним — при запуске выбирается его клиент.</summary>
    public string LastCharacter { get; set; } = "";

    /// <summary>
    /// «Игра» сервера: путь к его exe по id сервера — чтобы патчить и проверять патч, когда клиент не запущен.
    /// Заполняется сама (у запущенных клиентов сервера один exe; подключение) или кнопкой «Выбрать…». В списке
    /// клиентов — только запущенные из этого exe.
    /// </summary>
    public Dictionary<string, string> ClientPaths { get; set; } = new();
}

/// <summary>
/// Кому и о каких сообщениях в чате писать в Telegram, пока бот запущен. Пустой токен или chat_id — не пишем.
/// </summary>
public sealed class ChatNotifySettings
{
    /// <summary>Токен Telegram-бота (от @BotFather).</summary>
    public string Token { get; set; } = "";

    /// <summary>Кому писать: chat_id (свой — у @userinfobot).</summary>
    public string ChatId { get; set; } = "";

    /// <summary>Личные сообщения.</summary>
    public bool Private { get; set; } = true;

    /// <summary>Сообщения в группе.</summary>
    public bool Group { get; set; }

    /// <summary>Сообщения в клане.</summary>
    public bool Clan { get; set; }

    /// <summary>Сообщения в общем чате.</summary>
    public bool Common { get; set; }

    /// <summary>Есть кому и через кого писать.</summary>
    [JsonIgnore]
    public bool CanSend => Token.Length > 0 && ChatId.Length > 0;
}

/// <summary>Что показывать в логе окна (в файл пишется всё).</summary>
public sealed class LogViewSettings
{
    /// <summary>Только предупреждения и ошибки.</summary>
    public bool OnlyImportant { get; set; }

    /// <summary>Скрытые источники (<see cref="Logging.LogSources"/>): скрытые, а не показанные — новый источник сразу виден.</summary>
    public List<string> Hidden { get; set; } = [Logging.LogSources.Damage];
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

    private bool _reviveAndReturn;
    /// <summary>Умер (не слив опыта) — встать в ближнем городе и вернуться в центр фарма; выключено — бот останавливается.</summary>
    public bool ReviveAndReturn { get => _reviveAndReturn; set => Set(ref _reviveAndReturn, value); }

    private int _travelFrom = 100;
    /// <summary>
    /// «Старт» без полёта дальше этого от центра фарма (сохранённой точки) — сначала дойти до него автопутём; с полётом летим
    /// всегда. 0 — никуда не летим и не идём.
    /// </summary>
    public int TravelFrom { get => _travelFrom; set => Set(ref _travelFrom, value); }

    private float _travelHeight = 23;
    /// <summary>На какой высоте лететь к центру фарма при «Старт» — как в игре (в памяти — в 10 раз больше).</summary>
    public float TravelHeight { get => _travelHeight; set => Set(ref _travelHeight, value); }

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

/// <summary>Прокачка (<see cref="Brain.QuestBehavior"/>): какие задания из «Доступных» не брать никогда.</summary>
public sealed class QuestSettings : SettingsPart
{
    // Сбор тысяч ресурсов, задания других городов и «Функции» (расширения банка, сумки, гардероба)
    private List<string> _skipNames =
    [
        "Рудокоп", "Небо иллюзий", "Небо наваждений", "Странствующий дух", "Пять стихий", "Сила зооморфов", "[24]Дилемма банкира",
        "Расширение гардероба", "Склад", "[40]Увелич. сумки (отвяз.)", "[40]Увелич. сумки (привяз.)",
    ];

    /// <summary>Названия заданий, как в окне «Задания» (без учёта регистра).</summary>
    public List<string> SkipNames { get => _skipNames; set => Set(ref _skipNames, value); }

    /// <summary>Задание с таким названием не брать.</summary>
    public bool Skips(string name) => SkipNames.Any(s => string.Equals(s.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Кликер: сценарий — шаги по порядку, по кругу (<see cref="ClickerBehavior"/>).</summary>
public sealed class ClickerSettings : SettingsPart
{
    private List<ClickerStep> _steps = [];
    public List<ClickerStep> Steps { get => _steps; set => Set(ref _steps, value); }
}

/// <summary>Сохранённая точка фарма: название и где (координаты как в снимке).</summary>
public sealed class FarmPoint
{
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Height { get; set; }

    /// <summary>Персонаж был в воздухе, когда точку запомнили: прилетев, не садиться, а зависнуть на её высоте.</summary>
    public bool Flying { get; set; }

    /// <summary>Персонаж был в воде: прилетев, нырнуть до её высоты.</summary>
    public bool InWater { get; set; }

    [JsonIgnore]
    public Position Position => new(X, Height, Y);

    public static FarmPoint At(string name, Position p, bool flying = false, bool inWater = false)
        => new() { Name = name, X = p.X, Y = p.Y, Height = p.Height, Flying = flying, InWater = inWater };

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

    private int _attackMacro;
    /// <summary>Макрос персонажа 1..N вместо атакующего скилла; 0 — бьём скиллом <see cref="AttackSkillId"/>.</summary>
    public int AttackMacro { get => _attackMacro; set => Set(ref _attackMacro, value); }

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

    private bool _stashToBank;
    /// <summary>
    /// Убирать в банк предметы из <see cref="StashNames"/>: открыть банк из сумки, переложить, закрыть. Только где банк открывается из
    /// сумки (Comeback). Не зависит от <see cref="Enabled"/>.
    /// </summary>
    public bool StashToBank { get => _stashToBank; set => Set(ref _stashToBank, value); }

    private List<string> _stashNames = [];
    /// <summary>Что убирать в банк: названия предметов или группы лута («Предметы ДК»). Пусто — ничего.</summary>
    public List<string> StashNames { get => _stashNames; set => Set(ref _stashNames, value); }
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
    private bool _useHp = true;
    /// <summary>Пить банки HP.</summary>
    public bool UseHp { get => _useHp; set => Set(ref _useHp, value); }

    private int _hpPercent = 80;
    /// <summary>Пить банку HP, когда HP ниже этого процента.</summary>
    public int HpPercent { get => _hpPercent; set => Set(ref _hpPercent, value); }

    private string _hpPotion = "";
    /// <summary>Какую банку HP пить — название; пусто — «Авто»: самую слабую из банок с перезарядкой 10 с.</summary>
    public string HpPotion { get => _hpPotion; set => Set(ref _hpPotion, value); }

    private bool _useMp = true;
    /// <summary>Пить банки MP.</summary>
    public bool UseMp { get => _useMp; set => Set(ref _useMp, value); }

    private int _mpBelow = 100;
    /// <summary>Пить банку MP, когда MP ниже этого числа (абсолютные единицы, как в старом боте).</summary>
    public int MpBelow { get => _mpBelow; set => Set(ref _mpBelow, value); }

    private string _mpPotion = "";
    /// <summary>Какую банку MP пить — название; пусто — «Авто», как у <see cref="HpPotion"/>.</summary>
    public string MpPotion { get => _mpPotion; set => Set(ref _mpPotion, value); }
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

    private bool _feed = true;
    /// <summary>Кормить голодного пета. Выключено — не кормим совсем.</summary>
    public bool Feed { get => _feed; set => Set(ref _feed, value); }

    private string _food = "";
    /// <summary>
    /// Чем кормить — название корма; пусто — любым. Выбранного нет в сумке или пет его не ест — любым другим, сначала самым
    /// простым (меньше верности).
    /// </summary>
    public string Food { get => _food; set => Set(ref _food, value); }
}

/// <summary>
/// Слив опыта (режим «Бить мобов»): не качаться выше <see cref="MaxLevel"/>. На этом уровне при опыте от
/// <see cref="StartPercent"/> % бот собирает на себя мобов, умирает, встаёт в ближнем городе и прилетает обратно — пока опыт
/// не станет не больше <see cref="TargetPercent"/> %. Как — <see cref="Brain.ExpDrainBehavior"/>.
/// </summary>
public sealed class ExpDrainSettings : SettingsPart
{
    private bool _enabled;
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    private int _maxLevel = 30;
    /// <summary>Выше этого уровня не качаться: на нём и сливаем.</summary>
    public int MaxLevel { get => _maxLevel; set => Set(ref _maxLevel, value); }

    private int _startPercent = 80;
    /// <summary>Начинать слив, когда опыта столько процентов от нужного до уровня.</summary>
    public int StartPercent { get => _startPercent; set => Set(ref _startPercent, value); }

    private int _targetPercent = 10;
    /// <summary>Сливать, пока опыта больше этого процента.</summary>
    public int TargetPercent { get => _targetPercent; set => Set(ref _targetPercent, value); }

    private int _mobs = 5;
    /// <summary>Сколько мобов собрать на себя (по одному выстрелу в каждого): амулет восстанавливает HP, один моб не убьёт.</summary>
    public int Mobs { get => _mobs; set => Set(ref _mobs, value); }

    private float? _firstFlightHeight;
    /// <summary>
    /// На какой высоте лететь к месту слива из города у фарма (умерли на месте фарма: первая смерть слива; точки для смертей нет —
    /// каждый раз) — как в игре. В файле от прошлых версий поля нет — как <see cref="FlightHeight"/>.
    /// </summary>
    public float FirstFlightHeight { get => _firstFlightHeight ?? _flightHeight; set => Set(ref _firstFlightHeight, value); }

    private float _flightHeight = 23;
    /// <summary>
    /// На какой высоте лететь к месту слива из города у точки для смертей (умерли там — основной круг слива) — как в игре (в памяти —
    /// в 10 раз больше): выше домов, деревьев и гор по пути.
    /// </summary>
    public float FlightHeight { get => _flightHeight; set => Set(ref _flightHeight, value); }

    private float? _farmFlightHeight;
    /// <summary>
    /// На какой высоте лететь из города на место фарма после слива (слили или слив бросили) — как в игре. В файле от прошлых
    /// версий поля нет — как <see cref="FlightHeight"/>.
    /// </summary>
    public float FarmFlightHeight { get => _farmFlightHeight ?? _flightHeight; set => Set(ref _farmFlightHeight, value); }

    private string _deathPoint = "";
    /// <summary>
    /// Где умирать — название точки из <see cref="DeathPoints"/>; пусто (или точки нет) — на месте фарма. Первая смерть —
    /// всё равно там, где фармим (уже на месте), сюда летим со второй; слили — летим на место фарма.
    /// </summary>
    public string DeathPoint { get => _deathPoint; set => Set(ref _deathPoint, value); }

    private List<FarmPoint> _deathPoints = [];
    /// <summary>Точки для смертей — свои, не точки фарма (<see cref="TargetSettings.FarmPoints"/>).</summary>
    public List<FarmPoint> DeathPoints { get => _deathPoints; set => Set(ref _deathPoints, value); }

    private DrainMobs _deathMobs = DrainMobs.Any;
    /// <summary>От каких мобов умирать; заодно — где: летающих ждём в воздухе (не садимся), водных — в воде, любых — как стоим.</summary>
    [JsonConverter(typeof(DrainMobsJson))]
    public DrainMobs DeathMobs { get => _deathMobs; set => Set(ref _deathMobs, value); }

    /// <summary>Точка для смертей; null — умирать на месте фарма.</summary>
    public FarmPoint? FindDeathPoint()
        => DeathPoint.Length == 0 ? null : DeathPoints.FirstOrDefault(p => string.Equals(p.Name, DeathPoint, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Ремонт надетого (режим «Бить мобов»): самая поношенная вещь ниже <see cref="BelowPercent"/> % прочности — починить всё у
/// <see cref="Npc"/> (долететь, поговорить, «Починить всё», обратно на фарм) или из сумки (<see cref="FromBag"/>). Как —
/// <see cref="Brain.RepairBehavior"/>.
/// </summary>
public sealed class RepairSettings : SettingsPart
{
    private bool _enabled;
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    private int _belowPercent = 5;
    /// <summary>Чинить, когда прочность какой-нибудь надетой вещи ниже этого процента (вещи «не чинится» не в счёт).</summary>
    public int BelowPercent { get => _belowPercent; set => Set(ref _belowPercent, value); }

    private bool _fromBag;
    /// <summary>Чинить в лавке из сумки, без NPC: только где сервер это умеет (<see cref="Profiles.Capability.RemoteRepair"/>).</summary>
    public bool FromBag { get => _fromBag; set => Set(ref _fromBag, value); }

    private RepairNpc? _npc;
    /// <summary>У кого чинить: NPC, которого выбрали, стоя рядом с ним; null — не выбран.</summary>
    public RepairNpc? Npc { get => _npc; set => Set(ref _npc, value); }

    private float _flightHeight = 23;
    /// <summary>На какой высоте лететь к NPC и обратно на фарм — как в игре (в памяти — в 10 раз больше).</summary>
    public float FlightHeight { get => _flightHeight; set => Set(ref _flightHeight, value); }
}

/// <summary>
/// NPC для ремонта: имя, вид (tid; 0 — сервер не показывает, как 1.3.6) и где стоял, когда выбрали. На месте ищем по виду, если
/// он известен, иначе по имени.
/// </summary>
public sealed class RepairNpc
{
    public int Tid { get; set; }
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Height { get; set; }

    [JsonIgnore]
    public Position Position => new(X, Height, Y);

    /// <summary>Это он: тот же вид (оба известны) или то же имя.</summary>
    public bool Is(NpcInfo npc)
        => Tid != 0 && npc.Tid != 0 ? npc.Tid == Tid : string.Equals(npc.Name?.Trim(), Name.Trim(), StringComparison.CurrentCultureIgnoreCase);

    public static RepairNpc At(int tid, string name, Position p) => new() { Tid = tid, Name = name, X = p.X, Y = p.Y, Height = p.Height };
}

/// <summary>От каких мобов умирать при сливе опыта.</summary>
public enum DrainMobs
{
    /// <summary>От любых: ближайшие, ждём смерть как стоим (в точке для смертей — как стоял перс, когда её запоминали).</summary>
    Any,
    Ground,
    Air,
    Water,
}

/// <summary>«От каких мобов» из файла: прежнее «AsFarm» («как при фарме») читается как <see cref="DrainMobs.Any"/>.</summary>
internal sealed class DrainMobsJson : Newtonsoft.Json.Converters.StringEnumConverter
{
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        => reader.TokenType == JsonToken.String && (string?)reader.Value == "AsFarm"
            ? DrainMobs.Any
            : base.ReadJson(reader, objectType, existingValue, serializer);
}

/// <summary>Подбирать ли предмет с земли — единственное место, где это решается.</summary>
public static class LootFilter
{
    /// <param name="group">Группа лута предмета («Оружие», «Предметы ДК»): в списке может стоять она вместо названия.</param>
    public static bool Allows(LootSettings loot, GroundItemKind kind, string? name, string? group = null)
    {
        // Ресурс копается, а не подбирается — подбором никогда (см. AllowsGather)
        if (kind == GroundItemKind.Resource)
            return false;
        // Монеты — только галкой «Монеты»: список лута — про предметы (в «только из списка» монеты не пропадают)
        if (kind == GroundItemKind.Money)
            return loot.PickMoney;
        if (kind == GroundItemKind.Item && !loot.PickItems)
            return false;

        return ByList(loot.ListMode, loot.ItemNames, name, group);
    }

    public static bool Allows(LootSettings loot, GroundItem item) => Allows(loot, item.Kind, item.Name, item.Group);

    /// <summary>Убрать ли предмет сумки в банк: «Убирать в банк» включено, и предмет (название или группа) в его списке.</summary>
    public static bool Stashes(LootSettings loot, InventoryItem item)
        => loot.StashToBank && Listed(loot.StashNames, item.Name, item.Group);

    /// <summary>Копать ли ресурс в радиусе фарма: «Копать ресурсы» включено, и список ресурсов разрешает (подбор лута ни при чём).</summary>
    public static bool AllowsGather(LootSettings loot, string? name)
        => loot.PickResources && ByList(loot.ResourceMode, loot.ResourceNames, name);

    /// <summary>
    /// Копать ли «нересурс» (квестовый, особый): «Копать ресурсы» включено, и название явно в списке ресурсов — в режимах «все»
    /// и «только из списка». «Кроме списка» значит «эти не трогать».
    /// </summary>
    public static bool ListsForGather(LootSettings loot, string? name)
        => loot.PickResources && NamedExplicitly(loot.ResourceMode, loot.ResourceNames, name);

    /// <summary>
    /// Пропускает ли список: «все» — всех, «только» — названных, «кроме» — всех, кроме названных. Названо — само название
    /// или группа предмета (<paramref name="group"/>).
    /// </summary>
    public static bool ByList(LootListMode mode, IReadOnlyCollection<string> names, string? name, string? group = null)
        => mode switch
        {
            LootListMode.OnlyListed => Listed(names, name, group),
            LootListMode.ExceptListed => !Listed(names, name, group),
            _ => true,
        };

    private static bool Listed(IReadOnlyCollection<string> names, string? name, string? group)
        => MobNameFilter.Contains(names, name) || MobNameFilter.Contains(names, group);

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

    /// <summary>
    /// Можно ли нападать на моба с таким названием при этих настройках. «Только из списка» с пустым списком — никого:
    /// новые цели бот не выбирает, бьёт только напавших (их список не касается).
    /// </summary>
    public static bool Allows(TargetSettings target, string? mobName)
        => !target.UseMobList || Contains(target.MobNames, mobName);

    /// <summary>«Только из списка», а список пуст — новых целей нет.</summary>
    public static bool ListIsEmpty(TargetSettings target) => target.UseMobList && target.MobNames.Count == 0;

    public static bool Contains(IEnumerable<string> names, string? name)
    {
        var trimmed = name?.Trim() ?? "";
        return trimmed.Length > 0 && names.Any(n => string.Equals(n.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
