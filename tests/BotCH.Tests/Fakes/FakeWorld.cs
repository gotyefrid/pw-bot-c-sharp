using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Calls;
using BotCH.Core.Profiles;
using BotCH.Core.World;

namespace BotCH.Tests.Fakes;

/// <summary>
/// Подставной мир для сценариев: меняем поля и берём снимок (<see cref="Snapshot"/>), время двигаем <see cref="Wait"/>.
/// </summary>
internal sealed class FakeWorld
{
    public const uint HostWid = 0x0015A420;
    public const uint PetWid = 0x80116200;

    public DateTime Time { get; private set; } = new(2026, 10, 1, 12, 0, 0);
    public int Hp { get; set; } = 400;
    public int MaxHp { get; set; } = 500;
    public int Mp { get; set; } = 300;
    public int Level { get; set; } = 10;
    public Position Position { get; set; } = new(0, 0, 0);
    public uint TargetWid { get; set; }
    public int PetFoodCooldownMs { get; set; }
    public bool Casting { get; set; }

    /// <summary>Какой скилл кастуется (0 — никакой, null — сервер не показывает).</summary>
    public int? CastingSkillId { get; set; } = 0;
    public List<NpcInfo> Npcs { get; } = [];
    public List<GroundItem> Ground { get; } = [];
    public List<InventoryItem> Bag { get; } = [];
    public List<SkillInfo> Skills { get; } = [];

    /// <summary>Полоска копания (null — сервер её не показывает).</summary>
    public GatherProgress? Gather { get; set; }

    /// <summary>В воздухе (null — сервер не показывает).</summary>
    public bool? Flying { get; set; } = false;

    /// <summary>В воде (null — сервер не показывает).</summary>
    public bool? InWater { get; set; } = false;

    /// <summary>Ячеек в сумке (0 — неизвестно).</summary>
    public int BagSlots { get; set; }

    /// <summary>null — петов нет совсем (не друид).</summary>
    public PetState? Pet { get; set; }

    public FakeWorld Wait(double seconds)
    {
        Time = Time.AddSeconds(seconds);
        return this;
    }

    /// <summary>
    /// Снимок. Расстояния — из позиций, от того места, где сейчас персонаж (как в игре): у моба по горизонтали, у предмета
    /// на земле — в 3D. Сами тесты расстояние не задают — только позицию.
    /// </summary>
    public WorldState Snapshot() => new(
        Time, TimeSpan.Zero,
        new HostState(0x1FA1F868, HostWid, "Перс", Level, Hp, MaxHp, Mp, 500, Position, TargetWid, Casting, PetFoodCooldownMs)
            { Gather = Gather, CastingSkillId = CastingSkillId, Flying = Flying, InWater = InWater },
        Npcs.Select(n => n with { Distance = n.Position.HorizontalDistanceTo(Position) }).ToList(),
        Ground.Select(i => i with { Distance = i.Position.DistanceTo(Position) }).ToList(),
        Bag.ToList(), Skills.ToList(), Pet) { InventorySlots = BagSlots };

    /// <param name="state">Не задано: с целью — бьёт (иначе по правилу «напал» стоящий моб с нашей целью не считается), без — стоит.</param>
    public NpcInfo AddMob(uint wid, string name, float distance, uint targetWid = 0, int? state = null, int hp = 0)
    {
        var mob = new NpcInfo(wid, wid, NpcKind.Mob, state ?? (targetWid != 0 ? NpcInfo.StateAttacking : 1), targetWid,
            new Position(distance, 0, 0), distance, name, hp);
        Npcs.Add(mob);
        return mob;
    }

    public void Replace(NpcInfo npc, Func<NpcInfo, NpcInfo> change)
    {
        var index = Npcs.FindIndex(n => n.Wid == npc.Wid);
        Npcs[index] = change(Npcs[index]);
    }

    public InventoryItem AddPotion(int slot, uint tid, int count, int hp = 0, int mp = 0, int level = 0, int seconds = 10)
    {
        var item = new InventoryItem(slot, tid, 9, count, new PotionInfo(level, hp, hp > 0 ? seconds : 0, mp, mp > 0 ? seconds : 0), null);
        Bag.Add(item);
        return item;
    }

    public InventoryItem AddFood(int slot, uint tid, int count, int loyalty)
    {
        var item = new InventoryItem(slot, tid, 27, count, null, loyalty);
        Bag.Add(item);
        return item;
    }

    /// <summary>Стопка в ячейке стала на 1 меньше (игра приняла предмет).</summary>
    public void Consume(int slot)
    {
        var index = Bag.FindIndex(i => i.Slot == slot);
        Bag[index] = Bag[index] with { Count = Bag[index].Count - 1 };
    }

    public SkillInfo AddSkill(int id, int cooldownLeftMs = 0)
    {
        var skill = new SkillInfo(id, 1, cooldownLeftMs, 3000, 0, $"скилл {id}");
        Skills.Add(skill);
        return skill;
    }

    public void SetCooldown(int id, int ms)
    {
        var index = Skills.FindIndex(s => s.Id == id);
        Skills[index] = Skills[index] with { CooldownLeftMs = ms };
    }

    /// <summary>Пет в клетке; summoned — призван (появляется в списке мобов с типом 9).</summary>
    /// <summary>Несколько петов с названиями и средой; active — призванная клетка.</summary>
    public void SetPets(int? active, params PetInCage[] cages)
        => Pet = new PetState(active, active is null ? 0 : PetWid, cages);

    public void SetPet(int cage, float hpRatio = 1, int hunger = 0, bool summoned = true)
    {
        Pet = new PetState(summoned ? cage : null, summoned ? PetWid : 0, [new PetInCage(cage, hpRatio, hunger)]);
        Npcs.RemoveAll(n => n.Kind == NpcKind.Pet);
        if (summoned)
            Npcs.Add(new NpcInfo(1, PetWid, NpcKind.Pet, 1, 0, new Position(2, 0, 0), 2, "Пет", 0));
    }
}

/// <summary>Действия, которые ничего не делают в игре, а запоминают, что их просили.</summary>
internal sealed class FakeActions : IGameActions
{
    public List<string> Calls { get; } = [];
    public CallResult Result { get; set; } = CallResult.Done;

    /// <summary>Вызовы, которые начинаются с этих слов («move», «gather»), не отправляются — отказ, как у клиента без функции.</summary>
    public HashSet<string> Refuse { get; } = [];

    public string Mode => "тест";

    public Capabilities Capabilities { get; set; } = Capabilities.All;

    private CallResult Record(string call)
    {
        Calls.Add(call);
        return Refuse.Any(call.StartsWith) ? CallResult.Refused($"тест: «{call}» не отправлен") : Result;
    }

    public CallResult SelectTarget(uint wid) => Record($"select {wid:X}");
    public CallResult Unselect() => Record("unselect");
    public CallResult NormalAttack() => Record("attack");
    public CallResult CastSkill(int skillId, uint targetWid) => Record($"cast {skillId} {targetWid:X}");
    public CallResult ApplySkill(HostState host, int skillId, uint targetWid) => Record($"apply {skillId} {targetWid:X}");
    public CallResult PetAttack(uint targetWid) => Record($"pet-attack {targetWid:X}");
    public CallResult CancelAction() => Record("cancel");
    public CallResult Pickup(GroundItem item) => Record($"pickup {item.Id:X}");
    public CallResult PickupObject(HostState host, GroundItem item) => Record($"pickup-approach {item.Id:X}");
    public CallResult UseItem(InventoryItem item) => Record($"use {item.Slot}");
    public CallResult SummonPet(int cage) => Record($"summon {cage}");
    public CallResult RecallPet() => Record("recall");
    public CallResult MoveTo(HostState host, Position point, bool smart) => Record(smart ? $"move {point} умно" : $"move {point}");
    public CallResult Gather(HostState host, GroundItem resource) => Record($"gather {resource.Id:X}");
    public CallResult ToggleFly(HostState host) => Record("fly-toggle");
    public CallResult FlyTo(HostState host, Position point) => Record($"fly {point}");
}
