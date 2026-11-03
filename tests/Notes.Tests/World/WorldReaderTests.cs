using System.Linq;
using Notes.Core.Memory;
using Notes.Core.Profiles;
using Notes.Core.World;
using Xunit;

namespace Notes.Tests.World;

/// <summary>Особые случаи чтения снимка на «бумажной» памяти: то, чего нет в дампе.</summary>
public class WorldReaderTests
{
    // Где лежит указатель на базу — в тестах любой адрес (в клиенте его находит BasePointerSearch)
    private const uint BasePointer = 0x009B_3EEC;
    private static readonly ProfileData Profile = new ProfileCatalog().Load("pwclassic136").Data;

    private readonly MemoryImage _memory = new();

    // Минимальный мир: база → game → перс с ником; мир с пустыми списками
    private uint BuildWorld(uint petManager = 0, ProfileData? profile = null)
    {
        var Profile = profile ?? WorldReaderTests.Profile;
        const uint basePtr = 0x1000_0000, game = 0x1001_0000, host = 0x1002_0000, world = 0x1003_0000, name = 0x1004_0000;
        _memory.WriteUInt32(BasePointer, basePtr);
        _memory.WriteUInt32(basePtr + Profile.Base.Game, game);
        _memory.WriteUInt32(game + Profile.Host.Struct, host);
        _memory.WriteUInt32(game + Profile.World.World, world);
        _memory.Map(host, 0x1000);
        _memory.WriteUInt32(host + Profile.Host.NamePointer, name);
        _memory.WriteBytes(name, System.Text.Encoding.Unicode.GetBytes("Воин\0"));
        _memory.WriteUInt32(host + Profile.Host.Hp, 100);
        _memory.WriteUInt32(host + Profile.Host.PetManager, petManager);
        _memory.Map(world, 0x100);
        return host;
    }

    private WorldReader Reader(ProfileData? profile = null, uint? chatList = null, uint? expTable = null)
        => new(_memory, new GameRoots(_memory, BasePointer, profile ?? Profile, chatList, expTable: expTable), profile ?? Profile);

    [Fact]
    public void MacrosAreTheNonEmptyOnes()
    {
        // Макросы — в настройках игры: [база + configs] + list, по size байт, первый — иконка (0 — макроса нет)
        var comeback = new ProfileCatalog().Load("comeback136").Data;
        var host = BuildWorld(profile: comeback);
        _memory.Map(host, 0x2000);
        const uint configs = 0x1005_0000;
        _memory.WriteUInt32(0x1000_0000 + comeback.Base.Configs, configs);
        _memory.Map(configs, 0x1000);
        var m = comeback.Macros;
        // 1-й: иконка 1, «Жалящий рой» и обычная атака; 2-й пустой, хоть в нём и мусор; 3-й: иконка 5
        _memory.WriteBytes(configs + m.List, [1, 0x2B, 0x01, 0xFF, 0xFF]);
        _memory.WriteBytes(configs + m.List + m.Size + 1, [0xCE, 0xC3]);
        _memory.WriteBytes(configs + m.List + 2 * m.Size, [5]);

        var macros = Reader(comeback).Read().Macros;

        Assert.Equal([new MacroInfo(1, 1), new MacroInfo(3, 5)], macros);
    }

    [Fact]
    public void QuestsAreReadWithParentGoalAndKills()
    {
        // [перс + quests] → объект заданий, [+active] → список: байт 0 — число записей, записи по entrySize; запись ссылается
        // на запись задания с тем же номером. Родитель «Воинское Звание!», под ним «убить» с одним мобом, убито 3 из 10.
        // Мобы цели в профилях пока пустые (не сверены в игре) — смещения по коду 1.4.6 задаём здесь
        var c146 = new ProfileCatalog().Load("comeback146").Data;
        var comeback = new ProfileData
        {
            Base = c146.Base, Host = c146.Host, World = c146.World,
            Quests = new QuestOffsets
            {
                Active = c146.Quests.Active, Entries = c146.Quests.Entries, EntrySize = c146.Quests.EntrySize, EntryId = c146.Quests.EntryId,
                EntryParent = c146.Quests.EntryParent, EntryTemplate = c146.Quests.EntryTemplate, EntryKills = c146.Quests.EntryKills,
                Id = c146.Quests.Id, Name = c146.Quests.Name, Goal = c146.Quests.Goal, AwardNpc = c146.Quests.AwardNpc,
                MonsterCount = 0x34D, Monsters = 0x351, MonsterSize = 0x1E,
            },
        };
        var host = BuildWorld(profile: comeback);
        _memory.Map(host, 0x2000);
        const uint book = 0x1005_0000, list = 0x1006_0000, parent = 0x1007_0000, child = 0x1008_0000, monsters = 0x1009_0000;
        var q = comeback.Quests;
        _memory.WriteUInt32(host + comeback.Host.Quests, book);
        _memory.Map(book, 0x10);
        _memory.WriteUInt32(book + q.Active, list);
        _memory.Map(list, 0x100);
        _memory.WriteBytes(list, [2]);
        Entry(0, 6516, 0xFF, parent);
        Entry(1, 6933, 0, child);
        _memory.WriteBytes(list + q.Entries + q.EntrySize + q.EntryKills, [3, 0]);

        Template(parent, 6516, "^ffffffВоинское Звание!", 0);
        Template(child, 6933, "Убить волков", QuestGoals.Kill);
        _memory.WriteUInt32(child + q.AwardNpc, 12473);
        _memory.WriteUInt32(child + q.MonsterCount, 1);
        _memory.WriteUInt32(child + q.Monsters, monsters);
        _memory.Map(monsters, 0x40);
        _memory.WriteUInt32(monsters, 2357);
        _memory.WriteUInt32(monsters + 4, 10);

        var quests = Reader(comeback).ReadQuests()!;

        Assert.Equal(2, quests.Count);
        Assert.Equal((6516, "Воинское Звание!", 0, QuestGoals.Parts), (quests[0].Id, quests[0].Name, quests[0].ParentId, quests[0].Goal));
        var kill = quests[1];
        Assert.Equal((6933, 6516, QuestGoals.Kill, 12473u), (kill.Id, kill.ParentId, kill.Goal, kill.AwardNpc));
        Assert.Equal([new QuestTarget(2357, 10, 3)], kill.Monsters);
        Assert.Empty(kill.Items);

        void Entry(int index, ushort id, byte parentIndex, uint template)
        {
            var at = list + q.Entries + (uint)index * q.EntrySize;
            _memory.WriteBytes(at + q.EntryId, System.BitConverter.GetBytes(id));
            _memory.WriteBytes(at + q.EntryParent, [parentIndex]);
            _memory.WriteUInt32(at + q.EntryTemplate, template);
        }

        void Template(uint at, int id, string name, int goal)
        {
            _memory.Map(at, 0x400);
            _memory.WriteUInt32(at + q.Id, (uint)id);
            _memory.WriteBytes(at + q.Name, System.Text.Encoding.Unicode.GetBytes(name + "\0"));
            _memory.WriteUInt32(at + q.Goal, (uint)goal);
        }
    }

    [Fact]
    public void QuestsWithStaleTemplateAreSkipped()
    {
        // Запись списка указывает на запись другого задания (место освободилось) — такое задание не показываем
        var comeback = new ProfileCatalog().Load("comeback146").Data;
        var host = BuildWorld(profile: comeback);
        _memory.Map(host, 0x2000);
        const uint book = 0x1005_0000, list = 0x1006_0000, template = 0x1007_0000;
        var q = comeback.Quests;
        _memory.WriteUInt32(host + comeback.Host.Quests, book);
        _memory.Map(book, 0x10);
        _memory.WriteUInt32(book + q.Active, list);
        _memory.Map(list, 0x100);
        _memory.WriteBytes(list, [1]);
        _memory.WriteBytes(list + q.Entries + q.EntryId, System.BitConverter.GetBytes((ushort)6933));
        _memory.WriteUInt32(list + q.Entries + q.EntryTemplate, template);
        _memory.Map(template, 0x400);
        _memory.WriteUInt32(template + q.Id, 1855);

        Assert.Empty(Reader(comeback).ReadQuests()!);
    }

    [Fact]
    public void QuestWithoutAwardNpcInProfileHasNone()
    {
        // В профиле 1.3.6 «кому сдать» пусто (не сверено) — не читать на его месте начало записи (там vtable)
        var comeback = new ProfileCatalog().Load("comeback136").Data;
        var host = BuildWorld(profile: comeback);
        _memory.Map(host, 0x2000);
        const uint book = 0x1005_0000, list = 0x1006_0000, template = 0x1007_0000;
        var q = comeback.Quests;
        _memory.WriteUInt32(host + comeback.Host.Quests, book);
        _memory.Map(book, 0x10);
        _memory.WriteUInt32(book + q.Active, list);
        _memory.Map(list, 0x100);
        _memory.WriteBytes(list, [1]);
        _memory.WriteBytes(list + q.Entries + q.EntryId, System.BitConverter.GetBytes((ushort)6933));
        _memory.WriteBytes(list + q.Entries + q.EntryParent, [0xFF]);
        _memory.WriteUInt32(list + q.Entries + q.EntryTemplate, template);
        _memory.Map(template, 0x400);
        _memory.WriteUInt32(template, 0x0169_EEE8);
        _memory.WriteUInt32(template + q.Id, 6933);

        var quest = Assert.Single(Reader(comeback).ReadQuests()!);

        Assert.Equal(0u, quest.AwardNpc);
        Assert.Empty(quest.Monsters);
    }

    [Fact]
    public void PlayersAreReadWithDistance()
    {
        // Игроки — хэш-таблица мира, как мобы: [мир + manager] → массив ячеек, узел {+0 следующий, +4 объект}
        var comeback = new ProfileCatalog().Load("comeback146").Data;
        var host = BuildWorld(profile: comeback);
        const uint world = 0x1003_0000, manager = 0x1006_0000, slots = 0x1007_0000, node = 0x1008_0000, player = 0x1009_0000, name = 0x100A_0000;
        var w = comeback.World.Players;
        _memory.WriteUInt32(world + w.Manager, manager);
        _memory.Map(manager, 0x100);
        _memory.WriteUInt32(manager + w.SlotArray, slots);
        _memory.WriteUInt32(manager + w.Count, 1);
        _memory.Map(slots, comeback.World.SlotCount * 4);
        _memory.WriteUInt32(slots + 5 * 4, node);
        _memory.Map(node, 0x10);
        _memory.WriteUInt32(node + comeback.World.ObjectInSlot, player);
        _memory.Map(player, 0x1000);
        var p = comeback.Player;
        _memory.WriteUInt32(player + p.Wid, 0x0019_0AF0);
        _memory.WriteUInt32(player + p.NamePointer, name);
        _memory.WriteBytes(name, System.Text.Encoding.Unicode.GetBytes("ГДЗшница "));
        _memory.WriteBytes(player + p.Location, System.BitConverter.GetBytes(6f));
        _memory.WriteBytes(player + p.Location + 8, System.BitConverter.GetBytes(8f));
        _memory.WriteUInt32(player + p.Level, 6);
        _memory.WriteUInt32(player + p.Hp, 150);
        _memory.WriteUInt32(player + p.MaxHp, 150);
        _memory.WriteUInt32(player + p.Target, 0x0130_ECE0);
        _memory.WriteUInt32(player + p.Class, 7);

        var read = Reader(comeback).Read();

        var only = Assert.Single(read.Players);
        Assert.Equal((0x0019_0AF0u, "ГДЗшница", 6, 150, 150, 0x0130_ECE0u), (only.Wid, only.Name, only.Level, only.Hp, only.MaxHp, only.TargetWid));
        Assert.Equal(10, only.Offset.Direct, 3);
        Assert.Equal("жрец", PlayerClasses.Name(only.Class));
        Assert.Equal(1, read.PlayerCountInGame);
        Assert.Empty(read.Npcs);
    }

    [Fact]
    public void NoPlayersWhereTheListIsNotFound()
    {
        // В профиле PW Classic список игроков не проверен ("") — не читается
        BuildWorld();

        var read = Reader().Read();

        Assert.Empty(read.Players);
        Assert.Null(read.PlayerCountInGame);
    }

    // Список чата: [список] начало, [список + 4] конец; запись — канал, текст, номер, отправитель
    private void WriteChat(ProfileData profile, uint list, params (int Number, int Channel, string Text)[] messages)
    {
        const uint entries = 0x100C_0000, texts = 0x100D_0000;
        var c = profile.Chat;
        _memory.Map(list, 0x10);
        _memory.WriteUInt32(list, entries);
        _memory.WriteUInt32(list + 4, entries + (uint)messages.Length * c.EntrySize);
        _memory.Map(entries, 0x1000);
        _memory.Map(texts, 0x4000);
        for (var i = 0; i < messages.Length; i++)
        {
            var entry = entries + (uint)i * c.EntrySize;
            var text = texts + (uint)i * 0x200;
            _memory.WriteUInt32(entry + c.Channel, 0xFF00_0000 | (uint)messages[i].Channel);
            _memory.WriteUInt32(entry + c.Text, text);
            _memory.WriteUInt32(entry + c.Number, (uint)messages[i].Number);
            _memory.WriteUInt32(entry + c.Sender, uint.MaxValue);
            _memory.WriteBytes(text, System.Text.Encoding.Unicode.GetBytes(messages[i].Text + " "));
        }
    }

    [Fact]
    public void ChatIsReadOldFirst()
    {
        var comeback = new ProfileCatalog().Load("comeback146").Data;
        BuildWorld(profile: comeback);
        WriteChat(comeback, 0x100B_0000, (0x31E, 8, "^BED293Пустынный разбойник. Запомните это имя. Так зовут вашего убийцу."),
            (0x31F, 5, "^C0C0C0Пустынный разбойник наносит вам 879 ед. урона."));

        var chat = Reader(comeback, 0x100B_0000).Read().Chat;

        Assert.Equal([(0x31E, 8, "Пустынный разбойник. Запомните это имя. Так зовут вашего убийцу.", uint.MaxValue),
            (0x31F, 5, "Пустынный разбойник наносит вам 879 ед. урона.", uint.MaxValue)], chat.Select(m => (m.Number, m.Channel, m.Plain, m.SenderWid)));
    }

    [Fact]
    public void ChatTextIsReadOncePerMessage()
    {
        // Текст сообщения не меняется: уже прочитанное по номеру берётся из памяти бота, а не из игры
        var comeback = new ProfileCatalog().Load("comeback146").Data;
        BuildWorld(profile: comeback);
        WriteChat(comeback, 0x100B_0000, (7, 1, "старое"));
        var reader = Reader(comeback, 0x100B_0000);
        reader.Read();
        WriteChat(comeback, 0x100B_0000, (7, 1, "переписано"), (8, 1, "новое"));

        Assert.Equal(["старое", "новое"], reader.Read().Chat.Select(m => m.Text));
    }

    [Fact]
    public void NoChatWhereTheListIsNotFound()
    {
        BuildWorld();

        Assert.Empty(Reader().Read().Chat);
    }

    [Fact]
    public void ExpAndExpToLevelFromTableByLevel()
    {
        // Таблица в данных exe: [таблица + уровень × 4] — сколько нужно до следующего уровня (проверено 03.11: 48 ур. — 221040)
        var comeback = new ProfileCatalog().Load("comeback146").Data;
        var host = BuildWorld(profile: comeback);
        _memory.Map(host, 0x2000);
        _memory.WriteUInt32(host + comeback.Host.Level, 48);
        _memory.WriteUInt32(host + comeback.Host.Exp, 132198);
        const uint table = 0x1006_0000;
        _memory.Map(table, 0x400);
        _memory.WriteUInt32(table + 47 * 4, 207270);
        _memory.WriteUInt32(table + 48 * 4, 221040);

        var h = Reader(comeback, expTable: table).Read().Host;

        Assert.Equal(132198, h.Exp);
        Assert.Equal(221040, h.ExpToLevel);
        Assert.Equal(59.8, h.ExpPercent!.Value, 1);
    }

    [Fact]
    public void ExpUnknownWhereNotFound()
    {
        // В профиле pwclassic136 опыт не найден ("exp": ""), таблицы нет
        BuildWorld();

        var h = Reader().Read().Host;

        Assert.Null(h.Exp);
        Assert.Null(h.ExpToLevel);
        Assert.Null(h.ExpPercent);
    }

    [Fact]
    public void NoPetIsNormal()
    {
        BuildWorld(petManager: 0);

        var world = Reader().Read();

        Assert.Null(world.Pet);
        Assert.Equal("Воин", world.Host.Name);
        Assert.Empty(world.Npcs);
    }

    [Fact]
    public void EmptyCagesMeanNoPet()
    {
        const uint manager = 0x2000_0000;
        BuildWorld(petManager: manager);
        _memory.Map(manager, 0x100);
        _memory.WriteUInt32(manager + Profile.PetManager.ActiveCage, unchecked((uint)-1));

        Assert.Null(Reader().Read().Pet);
    }

    [Fact]
    public void DeadPetInCageIsKnown()
    {
        const uint manager = 0x2000_0000, pet = 0x2001_0000;
        BuildWorld(petManager: manager);
        _memory.Map(manager, 0x100);
        _memory.WriteUInt32(manager + Profile.PetManager.ActiveCage, unchecked((uint)-1));
        _memory.WriteUInt32(manager + Profile.PetManager.Cages + 2 * 4, pet); // клетка 3
        _memory.Map(pet, 0x100);

        var state = Reader().Read().Pet;

        Assert.NotNull(state);
        Assert.False(state!.IsSummoned);
        Assert.False(state.InCage(3)!.IsAlive);
    }

    [Theory]
    // Числа из живых клиентов: в окне пета PW Classic скорпион ур. 37 — 1067/1067; на Comeback 1.4.6 ур. 19 — 409 из 572
    [InlineData("pwclassic136", 37, 1.0f, 1067, 1067)]
    [InlineData("comeback146", 19, 0.716f, 409, 572)]
    public void PetHpLikeInPetWindow(string server, int level, float ratio, int hp, int maxHp)
    {
        // Макс = (уровень − levelRequire × hpB + hpC) × hpA — у скорпиона levelRequire 2, hpA 27,5, hpB 0,1, hpC 2
        var p = new ProfileCatalog().Load(server).Data;
        const uint manager = 0x2000_0000, pet = 0x2001_0000, record = 0x2100_0000;
        BuildWorld(petManager: manager, profile: p);
        _memory.Map(manager, 0x100);
        _memory.WriteUInt32(manager + p.PetManager.ActiveCage, 0);
        _memory.WriteUInt32(manager + p.PetManager.Cages, pet);
        _memory.Map(pet, 0x400);
        _memory.WriteUInt32(pet + p.Pet.Tid, 10492);
        _memory.WriteUInt32(pet + p.Pet.Essence, record);
        _memory.WriteUInt32(pet + p.Pet.Level, (uint)level);
        _memory.WriteBytes(pet + p.Pet.HpRatio, System.BitConverter.GetBytes(ratio));
        _memory.Map(record, 0x400);
        _memory.WriteUInt32(record, 10492);
        _memory.WriteUInt32(record + p.Pet.EssenceInhabit, 0);
        _memory.WriteUInt32(record + p.Pet.EssenceLevelRequire, 2);
        _memory.WriteBytes(record + p.Pet.EssenceHpA, System.BitConverter.GetBytes(27.5f));
        _memory.WriteBytes(record + p.Pet.EssenceHpB, System.BitConverter.GetBytes(0.1f));
        _memory.WriteBytes(record + p.Pet.EssenceHpC, System.BitConverter.GetBytes(2f));

        var scorpion = Reader(p).Read().Pet!.InCage(1)!;

        Assert.Equal((level, hp, maxHp), (scorpion.Level, scorpion.Hp, scorpion.MaxHp));
    }

    [Fact]
    public void PetHpUnknownWithoutRecord()
    {
        const uint manager = 0x2000_0000, pet = 0x2001_0000;
        BuildWorld(petManager: manager);
        _memory.Map(manager, 0x100);
        _memory.WriteUInt32(manager + Profile.PetManager.Cages, pet);
        _memory.Map(pet, 0x400);
        _memory.WriteUInt32(pet + Profile.Pet.Level, 37);

        Assert.Null(Reader().Read().Pet!.InCage(1)!.Hp);
    }

    [Fact]
    public void PetNameAndHabitatFromGameRecord()
    {
        // 1.4.6: [пет + essence] → запись справочника (id = tid пета), название +8, где живёт +0x1E8
        var p = new ProfileCatalog().Load("comeback146").Data;
        const uint manager = 0x2000_0000, bee = 0x2001_0000, scorpion = 0x2002_0000, beeRecord = 0x2100_0000, scorpionRecord = 0x2101_0000;
        BuildWorld(petManager: manager, profile: p);
        _memory.Map(manager, 0x100);
        _memory.WriteUInt32(manager + p.PetManager.ActiveCage, unchecked((uint)-1));
        foreach (var (cage, pet, record, tid, name, inhabit) in new[]
                 {
                     (1, scorpion, scorpionRecord, 10492u, "Молодой узорчатый скорпион", 0u),
                     (2, bee, beeRecord, 10521u, "Молодая лиственная пчела", 2u),
                 })
        {
            _memory.WriteUInt32(manager + p.PetManager.Cages + (uint)(cage - 1) * 4, pet);
            _memory.Map(pet, 0x400);
            _memory.WriteUInt32(pet + p.Pet.Tid, tid);
            _memory.WriteUInt32(pet + p.Pet.Essence, record);
            _memory.Map(record, 0x400);
            _memory.WriteUInt32(record, tid);
            _memory.WriteBytes(record + p.Pet.EssenceName, System.Text.Encoding.Unicode.GetBytes(name + "\0"));
            _memory.WriteUInt32(record + p.Pet.EssenceInhabit, inhabit);
        }

        var pets = Reader(p).Read().Pet!;

        Assert.Equal(("Молодой узорчатый скорпион", Habitat.Ground), (pets.InCage(1)!.Name, pets.InCage(1)!.Habitat));
        Assert.Equal(("Молодая лиственная пчела", Habitat.Air), (pets.InCage(2)!.Name, pets.InCage(2)!.Habitat));
    }

    [Fact]
    public void CharacterSelectScreenIsNotReady()
    {
        // game есть, персонажа нет
        _memory.WriteUInt32(BasePointer, 0x1000_0000);
        _memory.WriteUInt32(0x1000_0000 + Profile.Base.Game, 0x1001_0000);
        _memory.Map(0x1001_0000, 0x100);

        var error = Assert.Throws<WorldNotReadyException>(() => Reader().Read());
        Assert.Contains("Персонаж не в мире", error.Message);
    }

    [Fact]
    public void ClientWithoutGameIsNotReady()
    {
        Assert.Throws<WorldNotReadyException>(() => Reader().Read());
    }

    // Ресурс на земле с записью в справочнике: указатель на название ведёт в запись +8
    private void AddResource(uint slots, int slot, uint address, uint tid, uint kind, uint tool, uint quest, uint material, string name)
    {
        var g = Profile.GroundItem;
        var m = Profile.MineEssence;
        uint node = address + 0x800, record = address + 0x1000;
        _memory.Map(node, 0x10);
        _memory.WriteUInt32(slots + (uint)slot * 4, node);
        _memory.WriteUInt32(node + Profile.World.ObjectInSlot, address);
        _memory.Map(address, 0x400);
        _memory.WriteUInt32(address + g.Tid, tid);
        _memory.WriteUInt32(address + g.Kind, kind);
        _memory.WriteUInt32(address + g.NamePointer, record + m.NameInRecord);
        _memory.Map(record, (int)m.Size);
        _memory.WriteUInt32(record + m.Id, tid);
        _memory.WriteBytes(record + m.NameInRecord, System.Text.Encoding.Unicode.GetBytes(name + "\0"));
        _memory.WriteUInt32(record + m.Tool, tool);
        _memory.WriteUInt32(record + m.Quest, quest);
        _memory.WriteUInt32(record + m.LevelRequired, 20);
        if (material != 0)
        {
            _memory.WriteUInt32(record + m.Materials + 8, material); // не обязательно в первой ячейке
            _memory.WriteBytes(record + m.Materials + 12, System.BitConverter.GetBytes(1f));
        }
        _memory.WriteUInt32(record + m.Amounts, 2);
        _memory.WriteBytes(record + m.Amounts + 4, System.BitConverter.GetBytes(0.9f));
        _memory.WriteUInt32(record + m.Amounts + 8, 4);
        _memory.WriteBytes(record + m.Amounts + 12, System.BitConverter.GetBytes(0.1f));
    }

    // Предмет на земле с записью в справочнике: указатель на название ведёт в запись, перед названием — tid и тип
    private void AddTyped(uint slots, int slot, uint address, ItemEssenceOffsets m, uint tid, uint recordTid, uint type, string name, uint kind = 1)
    {
        var g = Profile.GroundItem;
        uint node = address + 0x800, record = address + 0x1000;
        _memory.Map(node, 0x10);
        _memory.WriteUInt32(slots + (uint)slot * 4, node);
        _memory.WriteUInt32(node + Profile.World.ObjectInSlot, address);
        _memory.Map(address, 0x400);
        _memory.WriteUInt32(address + g.Tid, tid);
        _memory.WriteUInt32(address + g.Kind, kind);
        _memory.WriteUInt32(address + g.NamePointer, record + m.NameInRecord);
        _memory.Map(record, 0x100);
        _memory.WriteUInt32(record + m.Id, recordTid);
        _memory.WriteUInt32(record + m.Type, type);
        _memory.WriteBytes(record + m.NameInRecord, System.Text.Encoding.Unicode.GetBytes(name + "\0"));
    }

    [Fact]
    public void ItemsGetLootGroupByTypeOrTid()
    {
        BuildWorld();
        const uint manager = 0x3000_0000, slots = 0x3001_0000;
        _memory.WriteUInt32(0x1003_0000 + Profile.World.GroundItems.Manager, manager);
        _memory.Map(manager, 0x100);
        _memory.WriteUInt32(manager + Profile.World.GroundItems.SlotArray, slots);
        _memory.WriteUInt32(manager + Profile.World.GroundItems.Count, 6);
        _memory.Map(slots, Profile.World.SlotCount * 4);
        // Материал — tid, тип, подтип, название; снаряд — tid, тип, название
        var material = Profile.ItemEssences[0];
        var projectile = Profile.ItemEssences[1];
        AddTyped(slots, 1, 0x4000_0000, material, 8099, 8099, 8077, "Бивень");
        AddTyped(slots, 2, 0x4100_0000, projectile, 1041, 1041, 8548, "Железная пуля");
        AddTyped(slots, 3, 0x4200_0000, material, 830, 830, 785, "Мех животных");
        // Записи без типа (ни в одной раскладке на месте tid не он): корм пета — по списку tid, остальное — без группы
        AddTyped(slots, 4, 0x4300_0000, material, 8796, 0, 8077, "Чистая вода");
        AddTyped(slots, 6, 0x4500_0000, material, 9999, 0, 8077, "Свиток");
        // Вид — байт: во втором байте у части вещей 1 (0x101), это тоже предмет
        AddTyped(slots, 5, 0x4400_0000, material, 341, 341, 316, "☆Корона с перьями", kind: 0x101);

        var world = Reader().Read();

        Assert.Equal([("Бивень", "Предметы ДК"), ("Железная пуля", "Снаряды"), ("Мех животных", null), ("Чистая вода", "Корм пета"),
                ("☆Корона с перьями", "Броня"), ("Свиток", null)],
            world.GroundItems.Select(i => (i.Name, i.Group)));
        Assert.All(world.GroundItems, i => Assert.Equal(GroundItemKind.Item, i.Kind));
    }

    [Fact]
    public void ResourcesReadWithTheirYieldAndNonResourcesMarked()
    {
        BuildWorld();
        const uint manager = 0x3000_0000, slots = 0x3001_0000;
        _memory.WriteUInt32(0x1003_0000 + Profile.World.GroundItems.Manager, manager);
        _memory.Map(manager, 0x100);
        _memory.WriteUInt32(manager + Profile.World.GroundItems.SlotArray, slots);
        _memory.WriteUInt32(manager + Profile.World.GroundItems.Count, 4);
        _memory.Map(slots, Profile.World.SlotCount * 4);
        AddResource(slots, 1, 0x4000_0000, 3074, 2, tool: 3073, quest: 0, material: 795, "Высохший древесный корень");
        AddResource(slots, 2, 0x4100_0000, 3405, 2, tool: 0, quest: 1093, material: 0, "Безымянный труп");
        AddResource(slots, 3, 0x4200_0000, 9999, 2, tool: 23652, quest: 0, material: 777, "Особый ресурс");
        AddResource(slots, 4, 0x4300_0000, 3044, 3, tool: 0, quest: 0, material: 0, "Монета");

        var world = Reader().Read();

        // Обычный ресурс — кирка без квеста; квестовый и под особый инструмент — «нересурсы»; монета — не ресурс вовсе
        Assert.Equal(
            [("Высохший древесный корень", false), ("Безымянный труп", true), ("Особый ресурс", true), ("Монета", false)],
            world.GroundItems.Select(i => (i.Name, i.Special)));
        var root = world.GroundItems[0].Mine!;
        Assert.Equal((3073u, 0u, 20), (root.Tool, root.Quest, root.LevelRequired));
        Assert.Equal(4, Assert.Single(root.Yields, y => y.Key == 795).Value);
        Assert.Equal(1093u, world.GroundItems[1].Mine!.Quest);
        Assert.Null(world.GroundItems[3].Mine);
        Assert.Equal(4, world.GroundItemCountInGame);
    }

    [Fact]
    public void ChainedSlotsAreAllRead()
    {
        // Две записи в одной ячейке хэш-таблицы: старый бот видел только первую
        BuildWorld();
        const uint manager = 0x3000_0000, slots = 0x3001_0000, node1 = 0x3002_0000, node2 = 0x3002_0100, mob1 = 0x3003_0000, mob2 = 0x3004_0000;
        _memory.WriteUInt32(0x1003_0000 + Profile.World.Npcs.Manager, manager);
        _memory.WriteUInt32(manager + Profile.World.Npcs.SlotArray, slots);
        _memory.WriteUInt32(manager + Profile.World.Npcs.Count, 2);
        _memory.Map(slots, Profile.World.SlotCount * 4);
        _memory.WriteUInt32(slots + 5 * 4, node1);
        _memory.WriteUInt32(node1, node2);
        _memory.WriteUInt32(node1 + Profile.World.ObjectInSlot, mob1);
        _memory.WriteUInt32(node2, 0);
        _memory.WriteUInt32(node2 + Profile.World.ObjectInSlot, mob2);
        foreach (var (mob, wid) in new[] { (mob1, 0x80000001u), (mob2, 0x80000002u) })
        {
            _memory.Map(mob, 0x400);
            _memory.WriteUInt32(mob + Profile.Npc.Wid, wid);
            _memory.WriteUInt32(mob + Profile.Npc.Type, 6);
        }

        var world = Reader().Read();

        Assert.Equal([0x80000001u, 0x80000002u], world.Npcs.Select(n => n.Wid));
        Assert.Equal(2, world.NpcCountInGame);
    }

    [Fact]
    public void AttacksAreReadWithAllTheirTargets()
    {
        // Менеджер атак: [мир + manager], +head — первый узел; узел: +0x0 следующий, удар с +event; цели — массивом
        BuildWorld();
        var a = Profile.Attacks;
        const uint manager = 0x3000_0000, first = 0x3001_0000, second = 0x3002_0000, targets1 = 0x3003_0000, targets2 = 0x3004_0000;
        const uint bee = 0x80104B25, pet = 0x80116200, me = 0x0015A420;
        _memory.WriteUInt32(0x1003_0000 + a.Manager, manager);
        _memory.WriteUInt32(manager + a.Head, first);
        void Attack(uint node, uint next, int lived, uint attacker, int skill, uint targets, params (uint Wid, int Damage, uint Flags)[] hits)
        {
            _memory.Map(node, 0x100);
            _memory.WriteUInt32(node, next);
            var e = node + a.Event;
            _memory.WriteUInt32(e + a.Lived, (uint)lived);
            _memory.WriteUInt32(e + a.Attacker, attacker);
            _memory.WriteUInt32(e + a.Skill, (uint)skill);
            _memory.WriteUInt32(e + a.Targets, targets);
            _memory.WriteUInt32(e + a.TargetCount, (uint)hits.Length);
            for (var i = 0u; i < hits.Length; i++)
            {
                var t = targets + i * a.TargetSize;
                _memory.WriteUInt32(t, hits[i].Wid);
                _memory.WriteUInt32(t + a.TargetDamage, unchecked((uint)hits[i].Damage));
                _memory.WriteUInt32(t + a.TargetFlags, hits[i].Flags);
            }
        }

        // Пчела задела пета и перса; перс лечит пета — у лечения урон −2
        Attack(first, second, 120, bee, 24, targets1, (pet, 98, 0), (me, 238, 4));
        Attack(second, 0, 30, me, 330, targets2, (pet, -2, 0));

        var attacks = Reader().Read().Attacks;

        Assert.Collection(attacks,
            hit =>
            {
                Assert.Equal((first, 120, bee, 24), (hit.Node, hit.LivedMs, hit.AttackerWid, hit.Skill));
                Assert.Equal([new AttackHit(pet, 98, 0), new AttackHit(me, 238, 4)], hit.Hits);
            },
            heal =>
            {
                Assert.Equal((second, me, 330), (heal.Node, heal.AttackerWid, heal.Skill));
                Assert.Equal([new AttackHit(pet, -2, 0)], heal.Hits);
            });
    }

    [Fact]
    public void NoAttacksManagerMeansNoAttacks()
    {
        BuildWorld();

        Assert.Empty(Reader().Read().Attacks);
    }

    [Fact]
    public void VanishedMobIsSkipped()
    {
        // Узел есть, а объект уже освобождён (моб исчез посреди чтения)
        BuildWorld();
        const uint manager = 0x3000_0000, slots = 0x3001_0000, node = 0x3002_0000;
        _memory.WriteUInt32(0x1003_0000 + Profile.World.Npcs.Manager, manager);
        _memory.WriteUInt32(manager + Profile.World.Npcs.SlotArray, slots);
        _memory.Map(slots, Profile.World.SlotCount * 4);
        _memory.WriteUInt32(slots, node);
        _memory.WriteUInt32(node + Profile.World.ObjectInSlot, 0x7000_0000);

        Assert.Empty(Reader().Read().Npcs);
    }

    [Theory]
    [InlineData(NpcState.Casting, 0xA0126CE5u)]   // кастует — цель каста
    [InlineData(NpcState.Attacking, 0x0130ECE0u)] // бьёт рукой — цель удара
    [InlineData(NpcState.Standing, 0xFFFFFFFFu)]  // стоит — запасные поля не смотрим
    public void MobWithEmptyTargetFieldTargetsWhomItHits(NpcState state, uint expected)
    {
        var p = new ProfileCatalog().Load("comeback146").Data;
        var mob = ComebackWorldWithMob(p);
        _memory.WriteUInt32(mob + p.Npc.State, (uint)state);
        _memory.WriteUInt32(mob + p.Npc.Target, 0xFFFFFFFF);
        _memory.WriteUInt32(mob + p.Npc.CastTarget, 0xA0126CE5);
        _memory.WriteUInt32(mob + p.Npc.AttackTarget, 0x0130ECE0);

        var npc = Assert.Single(Reader(p).Read().Npcs);

        Assert.Equal(expected, npc.TargetWid);
    }

    [Theory]
    [InlineData(9, 0x80100020u)] // пет — на кого идёт
    [InlineData(6, 0u)]          // у моба в этом месте другое — не читаем
    public void PetTargetIsReadOnlyForPets(int type, uint expected)
    {
        var p = new ProfileCatalog().Load("pwclassic136").Data;
        var npc = ComebackWorldWithMob(p);
        _memory.WriteUInt32(npc + p.Npc.Type, (uint)type);
        _memory.WriteUInt32(npc + p.Npc.State, (uint)NpcState.Moving);
        _memory.WriteUInt32(npc + p.Npc.PetTarget, 0x80100020);

        Assert.Equal(expected, Assert.Single(Reader(p).Read().Npcs).TargetWid);
    }

    [Theory]
    [InlineData(1u, true)]
    [InlineData(0u, false)]
    public void MobAggroComesFromItsRecord(uint aggressive, bool expected)
    {
        var p = new ProfileCatalog().Load("comeback146").Data;
        var mob = ComebackWorldWithMob(p);
        const uint record = 0x3100_0000;
        _memory.Map(record, 0x600);
        _memory.WriteUInt32(mob + p.Npc.Essence, record);
        _memory.WriteUInt32(record + p.MonsterEssence.Aggressive, aggressive);
        _memory.WriteUInt32(record + p.MonsterEssence.AggroRadius, 7);

        var npc = Assert.Single(Reader(p).Read().Npcs);

        Assert.Equal((expected, 7), (npc.Aggressive, npc.AggroRadius));
    }

    [Theory]
    [InlineData(0u, Habitat.Ground)]
    [InlineData(2u, Habitat.Air)]
    [InlineData(4u, Habitat.Ground | Habitat.Air)]
    public void MobHabitatComesFromItsRecord(uint inhabit, Habitat expected)
    {
        var p = new ProfileCatalog().Load("comeback146").Data;
        var mob = ComebackWorldWithMob(p);
        const uint record = 0x3100_0000;
        _memory.Map(record, 0x600);
        _memory.WriteUInt32(mob + p.Npc.Essence, record);
        _memory.WriteUInt32(record + p.MonsterEssence.Inhabit, inhabit);

        Assert.Equal(expected, Assert.Single(Reader(p).Read().Npcs).Habitat);
    }

    [Fact]
    public void ReturningMobIsSeen()
    {
        var p = new ProfileCatalog().Load("comeback146").Data;
        var mob = ComebackWorldWithMob(p);
        _memory.WriteUInt32(mob + p.Npc.Returning, 0x20000 | 0x1);

        Assert.True(Assert.Single(Reader(p).Read().Npcs).Returning);
        _memory.WriteUInt32(mob + p.Npc.Returning, 0x1);
        Assert.False(Assert.Single(Reader(p).Read().Npcs).Returning);
    }

    [Fact]
    public void TargetIsReadAsIsAfterReturn()
    {
        // Только факты: застрявшую после отагра цель снимает NpcTracker (нужна история), читатель отдаёт её как есть
        var p = new ProfileCatalog().Load("comeback146").Data;
        var mob = ComebackWorldWithMob(p);
        var reader = Reader(p);
        _memory.WriteUInt32(mob + p.Npc.Target, FakeHost);
        _memory.WriteUInt32(mob + p.Npc.Returning, 0x20000);
        reader.Read();

        _memory.WriteUInt32(mob + p.Npc.Returning, 0);
        _memory.WriteUInt32(mob + p.Npc.State, 1);

        Assert.Equal(FakeHost, Assert.Single(reader.Read().Npcs).TargetWid);
    }

    private const uint FakeHost = 0x0130ECE0;

    [Fact]
    public void MobWithoutRecordHasUnknownAggro()
    {
        var p = new ProfileCatalog().Load("comeback146").Data;
        ComebackWorldWithMob(p);

        var npc = Assert.Single(Reader(p).Read().Npcs);

        Assert.Null(npc.Aggressive);
    }

    [Fact]
    public void ComebackReadsGatherProgress()
    {
        var p = new ProfileCatalog().Load("comeback146").Data;
        ComebackWorldWithMob(p);
        const uint host = 0x1002_0000;
        _memory.WriteUInt32(host + p.Host.GatherTarget, 0xC01004E4);
        _memory.WriteUInt32(host + p.Host.GatherElapsed, 1800);
        _memory.WriteUInt32(host + p.Host.GatherTotal, 8000);

        Assert.Equal(new GatherProgress(true, 1800, 8000), Reader(p).Read().Host.Gather);
    }

    [Theory]
    [InlineData("pwclassic136")]
    [InlineData("comeback146")]
    [InlineData("comeback136")]
    public void GatherWorkFromListCountsOnlyWhileListIsNotEmpty(string server)
    {
        // Как в игре на всех трёх: [менеджер + 0x18] — массив работ уровня 1, [+0x24] — их число; копание — первая работа.
        // Id ресурса (Comeback) клиент обычно не пишет — копание видно только по работе
        var p = new ProfileCatalog().Load(server).Data;
        ComebackWorldWithMob(p);
        const uint host = 0x1002_0000, workMan = 0x2300_0000, array = 0x2302_0000, work = 0x2301_0000;
        _memory.WriteUInt32(host + p.Host.GatherElapsed, 42);
        _memory.WriteUInt32(host + p.Host.GatherTotal, 6000);
        _memory.WriteUInt32(host + p.Host.WorkMan, workMan);
        _memory.Map(workMan, 0x40);
        _memory.Map(array, 0x10);
        _memory.Map(work, 0x40);
        _memory.WriteUInt32(workMan + p.GatherWork.Current, array);
        _memory.WriteUInt32(array, work);
        _memory.WriteUInt32(work + p.GatherWork.Type, p.GatherWork.Gather);
        _memory.WriteBytes(work + p.GatherWork.Flag, [1]);

        // Список пуст — в массиве осталась прошлая работа, но это не копание
        Assert.False(Reader(p).Read().Host.Gather?.Active);

        _memory.WriteUInt32(workMan + p.GatherWork.CurrentCount, 1);
        Assert.True(Reader(p).Read().Host.Gather?.Active);
    }

    [Fact]
    public void ComebackInWaterIsNotGathering()
    {
        // Как в игре 08.10: в воде старый байт «копаю» (+0x28C) — 0 без всякого копания, полоска 1000 из 1000
        var p = new ProfileCatalog().Load("comeback136").Data;
        ComebackWorldWithMob(p);
        const uint host = 0x1002_0000;
        _memory.WriteBytes(host + 0x28C, [0x00, 0x4E, 0x69, 0xC1]);
        _memory.WriteUInt32(host + p.Host.MoveEnv, 1);
        _memory.WriteUInt32(host + p.Host.GatherElapsed, 1000);
        _memory.WriteUInt32(host + p.Host.GatherTotal, 1000);

        var me = Reader(p).Read().Host;

        Assert.True(me.InWater);
        Assert.False(me.Gather?.Active);
    }

    [Fact]
    public void ComebackReadsCastingSkill()
    {
        var p = new ProfileCatalog().Load("comeback146").Data;
        ComebackWorldWithMob(p);
        const uint host = 0x1002_0000, skill = 0x5000_0000;
        _memory.Map(skill, 0x40);
        _memory.WriteUInt32(skill + p.Skill.Id, 330);
        _memory.WriteUInt32(host + p.Host.CastingSkill, skill);

        Assert.Equal(330, Reader(p).Read().Host.CastingSkillId);
        _memory.WriteUInt32(host + p.Host.CastingSkill, 0);
        Assert.Equal(0, Reader(p).Read().Host.CastingSkillId);
    }

    [Fact]
    public void EquipmentIsReadWithDurability()
    {
        // Comeback 1.3.6: надетое устроено как сумка; прочность в сотых, бит «не чинится» во флагах
        var p = new ProfileCatalog().Load("comeback136").Data;
        ComebackWorldWithMob(p);
        const uint host = 0x1002_0000, equip = 0x6000_0000, cells = 0x6001_0000, sword = 0x6002_0000, charm = 0x6003_0000;
        var inv = p.Inventory;
        _memory.WriteUInt32(host + p.Host.Equipment, equip);
        _memory.Map(equip, 0x40);
        _memory.WriteUInt32(equip + inv.Items, cells);
        _memory.WriteUInt32(equip + inv.Size, 38);
        _memory.Map(cells, 38 * 4);
        _memory.WriteUInt32(cells, sword);
        _memory.WriteUInt32(cells + 20 * 4, charm);
        foreach (var (item, tid, now, max, flags) in new[] { (sword, 24386u, 4600u, 6600u, 0x13u), (charm, 26647u, 50u, 100u, 0x1000u) })
        {
            _memory.Map(item, 0x300);
            _memory.WriteUInt32(item + inv.ItemTid, tid);
            _memory.WriteUInt32(item + inv.ItemCount, 1);
            _memory.WriteUInt32(item + inv.ItemDurability, now);
            _memory.WriteUInt32(item + inv.ItemMaxDurability, max);
            _memory.WriteUInt32(item + inv.ItemFlags, flags);
            _memory.WriteUInt32(item + inv.ItemEquipMask, item == sword ? 1u : 0x100000u);
        }

        // У меча одна пустая ячейка и два свойства (номер, сколько чисел, числа)
        const uint sockets = 0x6004_0000, addons = 0x6005_0000;
        _memory.Map(sockets, 4);
        _memory.WriteUInt32(sword + inv.ItemSockets, sockets);
        _memory.WriteUInt32(sword + inv.ItemSocketCount, 1);
        _memory.Map(addons, (int)(2 * inv.AddonSize));
        _memory.WriteUInt32(sword + inv.ItemAddons, addons);
        _memory.WriteUInt32(sword + inv.ItemAddonCount, 2);
        foreach (var (i, id, value) in new[] { (0u, 441u, 3u), (1u, 815u, 29u) })
        {
            _memory.WriteUInt32(addons + i * inv.AddonSize, id);
            _memory.WriteUInt32(addons + i * inv.AddonSize + inv.AddonParamCount, 1);
            _memory.WriteUInt32(addons + i * inv.AddonSize + inv.AddonParams, value);
        }

        var w = Reader(p).Read();
        Assert.Equal([0u], w.Equipment[0].Sockets);
        Assert.Equal(["441=3", "815=29"], w.Equipment[0].Addons.Select(a => $"{a.Id}={string.Join("/", a.Params)}"));
        Assert.Empty(w.Equipment[1].Addons);

        Assert.Equal([(0, 24386u, 4600, 6600, false), (20, 26647u, 50, 100, true)],
            w.Equipment.Select(i => (i.Slot, i.Tid, i.Durability, i.MaxDurability, i.NoRepair)));
        Assert.True(w.Equipment[0].NeedsRepair);
        Assert.False(w.Equipment[1].NeedsRepair);
        Assert.True(w.NeedsRepair);

        _memory.WriteUInt32(sword + inv.ItemDurability, 6600);
        Assert.False(Reader(p).Read().NeedsRepair);
    }

    [Fact]
    public void NoEquipmentWhenContainerIsMissing()
    {
        // В мире нет контейнера надетого (персонаж ещё не в игре) — пусто, а не ошибка
        var p = new ProfileCatalog().Load("comeback146").Data;
        ComebackWorldWithMob(p);

        var w = Reader(p).Read();

        Assert.Empty(w.Equipment);
        Assert.False(w.NeedsRepair);
    }

    // Мир Comeback 1.4.6 с одним мобом; возвращает адрес моба
    private uint ComebackWorldWithMob(ProfileData p)
    {
        const uint basePtr = 0x1000_0000, game = 0x1001_0000, host = 0x1002_0000, world = 0x1003_0000, name = 0x1004_0000;
        const uint manager = 0x3000_0000, slots = 0x3001_0000, node = 0x3002_0000, mob = 0x3003_0000;
        _memory.WriteUInt32(BasePointer, basePtr);
        _memory.WriteUInt32(basePtr + p.Base.Game, game);
        _memory.WriteUInt32(game + p.Host.Struct, host);
        _memory.WriteUInt32(game + p.World.World, world);
        _memory.Map(host, 0x2000);
        _memory.WriteUInt32(host + p.Host.NamePointer, name);
        _memory.WriteBytes(name, System.Text.Encoding.Unicode.GetBytes("Кот\0"));
        _memory.Map(world, 0x100);
        _memory.WriteUInt32(world + p.World.Npcs.Manager, manager);
        _memory.Map(manager, 0x100);
        _memory.WriteUInt32(manager + p.World.Npcs.SlotArray, slots);
        _memory.WriteUInt32(manager + p.World.Npcs.Count, 1);
        _memory.Map(slots, p.World.SlotCount * 4);
        _memory.WriteUInt32(slots + 7 * 4, node);
        _memory.WriteUInt32(node + p.World.ObjectInSlot, mob);
        _memory.Map(mob, 0x400);
        _memory.WriteUInt32(mob + p.Npc.Wid, 0x80100018);
        _memory.WriteUInt32(mob + p.Npc.Type, 6);
        return mob;
    }

    [Fact]
    public void ComebackReadsMobsFromOwnLayoutAndSkipsUnknownFields()
    {
        // У Comeback 1.4.6 у менеджера мобов своя раскладка (массив +0x20, у предметов +0x1C); сумки и скиллов ещё нет (0), пета нет (менеджер 0)
        var p = new ProfileCatalog().Load("comeback146").Data;
        const uint basePtr = 0x1000_0000, game = 0x1001_0000, host = 0x1002_0000, world = 0x1003_0000, name = 0x1004_0000;
        const uint manager = 0x3000_0000, slots = 0x3001_0000, node = 0x3002_0000, mob = 0x3003_0000;
        _memory.WriteUInt32(BasePointer, basePtr);
        _memory.WriteUInt32(basePtr + p.Base.Game, game);
        _memory.WriteUInt32(game + p.Host.Struct, host);
        _memory.WriteUInt32(game + p.World.World, world);
        _memory.Map(host, 0x2000); // менеджер петов у 1.4.6 по +0x1980
        _memory.WriteUInt32(host, 0x00DE_AD00); // vtable: не должна прочитаться как цель, сумка или скиллы
        _memory.WriteUInt32(host + p.Host.NamePointer, name);
        _memory.WriteBytes(name, System.Text.Encoding.Unicode.GetBytes("Кот\0"));
        _memory.WriteUInt32(host + p.Host.Hp, 84);
        _memory.Map(world, 0x100);
        _memory.WriteUInt32(world + p.World.Npcs.Manager, manager);
        _memory.Map(manager, 0x100);
        _memory.WriteUInt32(manager + p.World.Npcs.SlotArray, slots);
        _memory.WriteUInt32(manager + p.World.Npcs.Count, 1);
        _memory.Map(slots, p.World.SlotCount * 4);
        _memory.WriteUInt32(slots + 7 * 4, node);
        _memory.WriteUInt32(node + p.World.ObjectInSlot, mob);
        _memory.Map(mob, 0x400);
        _memory.WriteUInt32(mob, 0x00BE_EF00);
        _memory.WriteUInt32(mob + p.Npc.Wid, 0x80100018);
        _memory.WriteUInt32(mob + p.Npc.Type, 6);

        var state = Reader(p).Read();

        Assert.Equal("Кот", state.Host.Name);
        Assert.Equal(84, state.Host.Hp);
        Assert.Equal(0u, state.Host.TargetWid);
        Assert.Empty(state.Inventory);
        Assert.Empty(state.Skills);
        Assert.Null(state.Pet);
        var npc = Assert.Single(state.Npcs);
        Assert.Equal(0x80100018u, npc.Wid);
        Assert.Equal(NpcState.Unknown, npc.State);
        Assert.Equal(0u, npc.TargetWid);
        Assert.Equal(1, state.NpcCountInGame);
    }
}
