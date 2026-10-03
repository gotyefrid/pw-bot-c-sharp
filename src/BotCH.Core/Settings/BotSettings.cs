using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.World;
using Newtonsoft.Json;

namespace BotCH.Core.Settings;

/// <summary>
/// Все настройки бота. Значения по умолчанию — как в старом боте (BotForm + ini).
/// Окно меняет «свой» экземпляр, бот получает копию (<see cref="Clone"/>) — правки в окне не попадают в бота посреди решения.
/// </summary>
public sealed class BotSettings
{
    /// <summary>Чем занимается бот у этого персонажа.</summary>
    public BotMode Mode { get; set; } = BotMode.FarmMobs;

    public ConnectionSettings Connection { get; set; } = new();
    public TargetSettings Target { get; set; } = new();
    public CombatSettings Combat { get; set; } = new();
    public LootSettings Loot { get; set; } = new();
    public PotionSettings Potions { get; set; } = new();
    public PetSettings Pet { get; set; } = new();
    public RouteSettings Route { get; set; } = new();

    public BotSettings Clone() => SettingsJson.Parse(SettingsJson.Serialize(this));

    /// <summary>
    /// С чем работает обход ресурсов: бой, лут, банки и пет — общие с фармом (вкладка «Общее»); напавших бьём всегда,
    /// поэтому «сначала тех, кто бьёт меня» (настройка фарма мобов) включено.
    /// </summary>
    public BotSettings ForGathering()
    {
        var s = Clone();
        s.Target.PreferAggressive = true;
        return s;
    }

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

public sealed class TargetSettings
{
    /// <summary>Нападать на мобов (старое «Kill Mobs»). Выключено — бот только лечится/кормит пета.</summary>
    public bool KillMobs { get; set; } = true;

    /// <summary>Сначала бить моба, который бьёт перса или пета (старое «Find agr mob»).</summary>
    public bool PreferAggressive { get; set; } = true;

    /// <summary>
    /// Снимать мобов с себя петом: моба, который бьёт перса, бьём сразу (и перс, и пет), даже посреди боя с тем, кто бьёт пета, —
    /// пет прочнее, пусть держит обоих. Без призванного пета не действует.
    /// </summary>
    public bool PetTakesAggro { get; set; } = true;

    /// <summary>Нападать только на мобов из списка названий (старое «Check ID»).</summary>
    public bool UseMobList { get; set; }

    /// <summary>Названия мобов. Одно название — много мобов с разными WID.</summary>
    public List<string> MobNames { get; set; } = [];

    /// <summary>Сколько секунд биться с одним мобом, прежде чем бросить.</summary>
    public int MobTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Радиус фарма, м: новые цели (мобы, ресурсы) — только не дальше этого от центра фарма (<see cref="FarmCenter"/>).
    /// Моб, который бьёт перса или пета, — всегда. 0 — без ограничения.
    /// </summary>
    public int FarmRadius { get; set; } = 60;

    /// <summary>Сохранённые точки фарма персонажа.</summary>
    public List<FarmPoint> FarmPoints { get; set; } = [];

    /// <summary>Центр фарма — название точки из <see cref="FarmPoints"/>; пусто (или точки нет) — точка старта (где стоял перс при «Старт»).</summary>
    public string FarmCenter { get; set; } = "";

    /// <summary>Делать нечего — бежать в центр фарма и ждать мобов там.</summary>
    public bool ReturnToCenter { get; set; } = true;

    /// <summary>Как бежать в центр: с автопутём или напрямик (отдельно от подхода к мобу).</summary>
    public ApproachPath ReturnPath { get; set; } = ApproachPath.Smart;

    /// <summary>Выбранная точка фарма; null — центр — точка старта.</summary>
    [JsonIgnore]
    public FarmPoint? SelectedFarmPoint
        => FarmCenter.Length == 0 ? null : FarmPoints.FirstOrDefault(p => string.Equals(p.Name, FarmCenter, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Обход ресурсов: точки по порядку (после последней — первая), у каждой копаем ресурсы в радиусе — что именно, у каждой точки своё.
/// </summary>
public sealed class RouteSettings
{
    /// <summary>Точки обхода по порядку. Записаны в полёте — бот летит на их высоте.</summary>
    public List<RoutePoint> Points { get; set; } = [];

    /// <summary>Радиус поиска ресурсов вокруг точки, м. Участок ресурса ~110 м, виден с ~80 м — 50 м от точки хватает.</summary>
    public int Radius { get; set; } = 50;

    /// <summary>С какой точки начинать обход при «Старт» (с 0): окно ставит выбранную в списке.</summary>
    public int StartIndex { get; set; }
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
    public bool Wants(string? name) => ListMode switch
    {
        LootListMode.OnlyListed => MobNameFilter.Contains(Resources, name),
        LootListMode.ExceptListed => !MobNameFilter.Contains(Resources, name),
        _ => true,
    };

    /// <summary>«Нересурс» (квестовый, особый) назван явно — копать без условий; «кроме списка» значит «не трогать».</summary>
    public bool Lists(string? name) => ListMode != LootListMode.ExceptListed && MobNameFilter.Contains(Resources, name);

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

public sealed class CombatSettings
{
    public bool UseSkill { get; set; }

    /// <summary>ID атакующего скилла. 299 — по умолчанию в старом боте.</summary>
    public int AttackSkillId { get; set; } = 299;

    /// <summary>Обычная атака (раз в ~5 с).</summary>
    public bool UseSword { get; set; }

    /// <summary>Сначала подойти к мобу на <see cref="ComeCloserDistance"/>, потом бить (скиллом, атакой или только петом).</summary>
    public bool ComeCloser { get; set; }

    public float ComeCloserDistance { get; set; } = 8;

    /// <summary>Как подходить: с автопутём (в обход препятствий, если сервер умеет) или напрямик.</summary>
    public ApproachPath ApproachPath { get; set; } = ApproachPath.Smart;
}

public enum ApproachPath
{
    /// <summary>Автопуть, как клик по карте: в обход препятствий. Нет у сервера — по прямой.</summary>
    Smart,
    /// <summary>По прямой, как клик по земле.</summary>
    Direct,
}

public sealed class LootSettings
{
    public bool Enabled { get; set; }

    /// <summary>Сколько раз подбирать после смерти моба.</summary>
    public int Attempts { get; set; } = 4;

    /// <summary>
    /// Радиус подбора, м — от места смерти моба (лут падает в 2–3 м от трупа). Больше — бот тянется за старым лутом
    /// прошлых мобов по цепочке.
    /// </summary>
    public int Radius { get; set; } = 5;

    public bool PickMoney { get; set; } = true;
    public bool PickItems { get; set; } = true;

    /// <summary>Копать ресурсы в радиусе фарма: сначала все ресурсы, потом мобы. Нужна кирка в сумке. Список — общий с лутом.</summary>
    public bool PickResources { get; set; }

    /// <summary>Как использовать <see cref="ItemNames"/>: не использовать / только они / все, кроме них.</summary>
    public LootListMode ListMode { get; set; } = LootListMode.All;

    /// <summary>Названия предметов и ресурсов на земле (как в игре: «Мягкий мех», «Железная руда»).</summary>
    public List<string> ItemNames { get; set; } = [];
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

public sealed class PotionSettings
{
    /// <summary>Пить банку HP, когда HP ниже этого процента.</summary>
    public int HpPercent { get; set; } = 80;

    /// <summary>Пить банку MP, когда MP ниже этого числа (абсолютные единицы, как в старом боте).</summary>
    public int MpBelow { get; set; } = 100;
}

public sealed class PetSettings
{
    /// <summary>Пользоваться петом. Выключено или пета нет (не друид) — всё про пета пропускается.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Клетка 1..N — если сервер не говорит, где питомец живёт (иначе — <see cref="GroundPet"/>/<see cref="AirPet"/>).</summary>
    public int Cage { get; set; } = 1;

    /// <summary>Кого звать на земле — название питомца; пусто — первого, кто живёт на земле.</summary>
    public string GroundPet { get; set; } = "";

    /// <summary>Кого звать в воздухе (персонаж летит) — название питомца; пусто — первого, кто летает.</summary>
    public string AirPet { get; set; } = "";

    /// <summary>Кого звать в воде — название питомца; пусто — первого, кто живёт в воде.</summary>
    public string WaterPet { get; set; } = "";

    /// <summary>Лечить пета, когда его HP ниже этого процента.</summary>
    public int HealPercent { get; set; } = 70;
}

/// <summary>Подбирать ли предмет с земли — единственное место, где это решается.</summary>
public static class LootFilter
{
    public static bool Allows(LootSettings loot, GroundItemKind kind, string? name)
    {
        // Ресурс копается, а не подбирается — подбором никогда (см. AllowsGather)
        if (kind == GroundItemKind.Resource)
            return false;
        if (kind == GroundItemKind.Money && !loot.PickMoney)
            return false;
        if (kind == GroundItemKind.Item && !loot.PickItems)
            return false;

        return ByList(loot, name);
    }

    public static bool Allows(LootSettings loot, GroundItem item) => Allows(loot, item.Kind, item.Name);

    /// <summary>Копать ли ресурс: лут включён, «Ресурсы» включены, и список (общий с лутом) разрешает.</summary>
    public static bool AllowsGather(LootSettings loot, string? name)
        => loot.Enabled && loot.PickResources && ByList(loot, name);

    /// <summary>
    /// Копать ли «нересурс» (квестовый, особый): лут и «Ресурсы» включены, и название явно в списке — в режимах «все» и
    /// «только из списка». «Кроме списка» значит «эти не трогать».
    /// </summary>
    public static bool ListsForGather(LootSettings loot, string? name)
        => loot.Enabled && loot.PickResources && loot.ListMode != LootListMode.ExceptListed && MobNameFilter.Contains(loot.ItemNames, name);

    private static bool ByList(LootSettings loot, string? name)
        => loot.ListMode switch
        {
            LootListMode.OnlyListed => MobNameFilter.Contains(loot.ItemNames, name),
            LootListMode.ExceptListed => !MobNameFilter.Contains(loot.ItemNames, name),
            _ => true,
        };
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
