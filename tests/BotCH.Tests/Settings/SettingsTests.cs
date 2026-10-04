using System;
using System.IO;
using BotCH.Core.Settings;
using BotCH.Core.World;
using Xunit;

namespace BotCH.Tests.Settings;

public class SettingsTests
{
    [Fact]
    public void DefaultsMatchOldBot()
    {
        var s = new BotSettings();

        Assert.Equal(80, s.Potions.HpPercent);
        Assert.Equal(100, s.Potions.MpBelow);
        Assert.Equal(70, s.Pet.HealPercent);
        Assert.Equal(1, s.Pet.Cage);
        Assert.Equal(8f, s.Combat.ComeCloserDistance);
        Assert.Equal(4, s.Loot.Attempts);
        Assert.Equal(299, s.Combat.AttackSkillId);
        Assert.True(s.Connection.RenameWindows);
        Assert.Equal(120, s.Target.MobTimeoutSeconds);
        Assert.Equal(60, s.Target.FarmRadius);
    }

    [Fact]
    public void CloneIsIndependent()
    {
        var window = new BotSettings();
        window.Target.MobNames.Add("Волк");

        var bot = window.Clone();
        window.Target.MobNames.Add("Медведь");
        window.Pet.Enabled = false;

        Assert.Equal(["Волк"], bot.Target.MobNames);
        Assert.True(bot.Pet.Enabled);
    }

    [Fact]
    public void MissingFieldsGetDefaultsAndUnknownAreIgnored()
    {
        // Файл от другой версии бота: части полей нет, есть лишнее
        var s = SettingsJson.Parse("""{ "pet": { "cage": 3 }, "oldThing": 1, "potions": { "hpPercent": 50, "extra": true } }""");

        Assert.Equal(3, s.Pet.Cage);
        Assert.True(s.Pet.Enabled);
        Assert.Equal(50, s.Potions.HpPercent);
        Assert.Equal(100, s.Potions.MpBelow);
        Assert.Equal(4, s.Loot.Attempts);
    }

    [Fact]
    public void NullGroupBecomesDefault()
    {
        var s = SettingsJson.Parse("""{ "pet": null, "target": { "mobNames": null } }""");

        Assert.Equal(1, s.Pet.Cage);
        Assert.Empty(s.Target.MobNames);
    }

    [Fact]
    public void OutOfRangeValuesAreClamped()
    {
        var s = SettingsJson.Parse("""{ "pet": { "cage": 42, "healPercent": -5 }, "potions": { "hpPercent": 300 } }""");

        Assert.Equal(32, s.Pet.Cage);
        Assert.Equal(0, s.Pet.HealPercent);
        Assert.Equal(100, s.Potions.HpPercent);
    }

    [Fact]
    public void MobNamesAreCleaned()
    {
        var s = SettingsJson.Parse("""{ "target": { "mobNames": [" Волк ", "волк", "", "  ", "Медведь"] } }""");

        Assert.Equal(["Волк", "Медведь"], s.Target.MobNames);
    }

    [Theory]
    [InlineData(false, "Медведь", true)] // список выключен — любой моб
    [InlineData(true, "Волк", true)]
    [InlineData(true, "  волк ", true)] // регистр и пробелы не важны
    [InlineData(true, "Медведь", false)]
    [InlineData(true, null, false)] // имя не прочиталось — не нападаем
    public void MobListFiltersByName(bool useList, string? mobName, bool allowed)
    {
        var target = new TargetSettings { UseMobList = useList, MobNames = ["Волк", "Кабан"] };

        Assert.Equal(allowed, MobNameFilter.Allows(target, mobName));
    }

    [Fact]
    public void EmptyMobListAllowsAnyone()
    {
        Assert.True(MobNameFilter.Allows(new TargetSettings { UseMobList = true }, "Медведь"));
    }

    [Theory]
    [InlineData(LootListMode.All, "Монета", GroundItemKind.Money, true)]
    [InlineData(LootListMode.All, "Залежь меди", GroundItemKind.Resource, false)] // ресурс — никогда
    [InlineData(LootListMode.OnlyListed, "мягкий мех ", GroundItemKind.Item, true)]
    [InlineData(LootListMode.OnlyListed, "Разорванный мех", GroundItemKind.Item, false)]
    [InlineData(LootListMode.OnlyListed, "", GroundItemKind.Item, false)] // название не прочиталось — не в белом списке
    [InlineData(LootListMode.ExceptListed, "Мягкий мех", GroundItemKind.Item, false)]
    [InlineData(LootListMode.ExceptListed, "Разорванный мех", GroundItemKind.Item, true)]
    public void LootListFiltersByName(LootListMode mode, string name, GroundItemKind kind, bool allowed)
    {
        var loot = new LootSettings { ListMode = mode, ItemNames = ["Мягкий мех"] };

        Assert.Equal(allowed, LootFilter.Allows(loot, kind, name));
    }

    [Theory]
    [InlineData(LootListMode.OnlyListed)]
    [InlineData(LootListMode.ExceptListed)]
    public void MoneyIgnoresItemList(LootListMode mode)
    {
        // Монеты решает только галка: «только мех» их не отсекает, «кроме монет» в списке — тоже
        var loot = new LootSettings { ListMode = mode, ItemNames = ["Мягкий мех", "Монета"] };

        Assert.True(LootFilter.Allows(loot, GroundItemKind.Money, "Монета"));
        loot.PickMoney = false;
        Assert.False(LootFilter.Allows(loot, GroundItemKind.Money, "Монета"));
    }

    [Fact]
    public void LootKindSwitches()
    {
        var onlyMoney = new LootSettings { PickItems = false };

        Assert.True(LootFilter.Allows(onlyMoney, GroundItemKind.Money, "Монета"));
        Assert.False(LootFilter.Allows(onlyMoney, GroundItemKind.Item, "Мягкий мех"));
        Assert.False(LootFilter.Allows(new LootSettings { PickMoney = false }, GroundItemKind.Money, "Монета"));
    }

    [Fact]
    public void LootSettingsSurviveSaveAndLoad()
    {
        var s = SettingsJson.Parse("""{ "loot": { "listMode": "ExceptListed", "itemNames": ["Разорванный мех", " разорванный мех"] } }""");

        Assert.Equal(LootListMode.ExceptListed, s.Loot.ListMode);
        Assert.Equal(["Разорванный мех"], s.Loot.ItemNames);
        Assert.Equal(LootListMode.ExceptListed, SettingsJson.Parse(SettingsJson.Serialize(s)).Loot.ListMode);
    }

    [Fact]
    public void FarmResourceListIsSeparateFromLoot()
    {
        var s = SettingsJson.Parse("""{ "loot": { "resourceMode": "OnlyListed", "resourceNames": ["Железная руда", " железная руда"] } }""");

        Assert.Equal(LootListMode.OnlyListed, s.Loot.ResourceMode);
        Assert.Equal(["Железная руда"], s.Loot.ResourceNames);
        Assert.Empty(s.Loot.ItemNames);
        var again = SettingsJson.Parse(SettingsJson.Serialize(s)).Loot;
        Assert.Equal(LootListMode.OnlyListed, again.ResourceMode);
        Assert.Equal(["Железная руда"], again.ResourceNames);
    }

    [Fact]
    public void BotWorksWithoutPet()
    {
        // Не друид: пет выключен, остальные настройки не зависят от него
        var s = SettingsJson.Parse("""{ "pet": { "enabled": false } }""");

        Assert.False(s.Pet.Enabled);
        Assert.True(s.Target.KillMobs);
    }
}

public class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "botch-settings-" + Guid.NewGuid().ToString("N"));

    private string File(string name) => Path.Combine(_directory, name);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void NoFileGivesDefaults()
    {
        var s = new SettingsStore(File("settings.json")).Load(out var problem);

        Assert.Null(problem);
        Assert.Equal(80, s.Potions.HpPercent);
    }

    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        var store = new SettingsStore(File("settings.json"));
        var saved = new BotSettings();
        saved.Target.UseMobList = true;
        saved.Target.MobNames = ["Волк", "Кабан"];
        saved.Pet.Enabled = false;
        saved.Combat.ComeCloserDistance = 5.5f;
        saved.Target.FarmPoints = [FarmPoint.At("Поляна", new Position(-1800.5f, 220f, -110f))];
        saved.Target.FarmCenter = "Поляна";

        store.Save(saved);
        store.Save(saved); // второй раз — подмена существующего файла
        var loaded = store.Load(out var problem);

        Assert.Null(problem);
        Assert.Equal(["Волк", "Кабан"], loaded.Target.MobNames);
        Assert.False(loaded.Pet.Enabled);
        Assert.Equal(5.5f, loaded.Combat.ComeCloserDistance);
        Assert.Equal(new Position(-1800.5f, 220f, -110f), loaded.Target.SelectedFarmPoint?.Position);
        Assert.False(System.IO.File.Exists(File("settings.json.tmp")));
    }

    [Fact]
    public void SavedFileIsReadableJsonWithRussianNames()
    {
        var store = new SettingsStore(File("settings.json"));
        var s = new BotSettings();
        s.Target.MobNames = ["Волк"];

        store.Save(s);

        var text = System.IO.File.ReadAllText(File("settings.json"));
        Assert.Contains("\"Волк\"", text);
        Assert.Contains("\"mobNames\"", text);
    }

    [Fact]
    public void BrokenFileIsKeptAndDefaultsUsed()
    {
        Directory.CreateDirectory(_directory);
        System.IO.File.WriteAllText(File("settings.json"), "{ это не json");

        var s = new SettingsStore(File("settings.json")).Load(out var problem);

        Assert.NotNull(problem);
        Assert.Equal(80, s.Potions.HpPercent);
        Assert.Equal("{ это не json", System.IO.File.ReadAllText(File("settings.json.bad")));
    }

    [Fact]
    public void AnyEditIsReportedOnceAndSameValueIsNot()
    {
        // Окно привязано к полям напрямую и узнаёт о правке от самих настроек
        var s = new BotSettings();
        var edits = 0;
        s.Edited += () => edits++;

        s.Target.FarmRadius = 30;
        s.Route.Radius = 80;
        s.Pet.GroundPet = "Пчела";
        s.Mode = BotMode.GatherResources;
        s.Target.FarmRadius = 30;

        Assert.Equal(4, edits);
    }

    [Fact]
    public void ReplacedPartIsWatchedAndOldOneIsNot()
    {
        var s = new BotSettings();
        var old = s.Loot;
        var edits = 0;
        s.Edited += () => edits++;

        s.Loot = new LootSettings();
        old.Attempts = 9;
        s.Loot.Attempts = 9;

        Assert.Equal(2, edits);
    }

    [Fact]
    public void LoadedSettingsReportEditsToo()
    {
        var s = SettingsJson.Parse("""{ "target": { "farmRadius": 40 } }""");
        var edits = 0;
        s.Edited += () => edits++;

        s.Target.FarmRadius = 50;

        Assert.Equal(1, edits);
    }
}
