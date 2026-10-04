using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using BotCH.Core.Actions;
using BotCH.Core.Calls;
using BotCH.Core.Logging;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using BotCH.Core.Session;
using BotCH.Core.World;

namespace BotCH.Probe;

/// <summary>
/// Поиск поля перезарядки «до/после»: снимаем области памяти, кормим пета (вызов в игре — запускает владелец),
/// 15 с следим. Перезарядка — поле, которое после кормления стало больше нуля и дальше равномерно убывает.
/// </summary>
internal static class CooldownFinder
{
    private sealed record Region(string Name, uint Address, int Size);

    public static int Find(IServerProfile profile, string[] args)
    {
        using var game = Program.OpenClient(args, GameProcessRights.Execute);
        var data = profile.Data;
        var reader = new WorldReader(game, game.MainModuleBase, data);
        var world = reader.Read();

        // Что использовать: pet-food (по умолчанию), potion-hp, potion-mp
        var what = args.FirstOrDefault(a => a.StartsWith("p", StringComparison.Ordinal)) ?? "pet-food";
        InventoryItem? food;
        ItemUse use;
        if (what == "pet-food")
        {
            food = world.Inventory.Where(i => i.IsPetFood).OrderBy(i => i.FoodLoyalty).FirstOrDefault();
            use = ItemUse.PetFood;
            if (food is null || world.Pet is not { IsSummoned: true })
            {
                Console.WriteLine("❌ Нужны призванный пет и корм в сумке");
                return 2;
            }
        }
        else
        {
            var kind = what == "potion-mp" ? PotionKind.Mp : PotionKind.Hp;
            food = world.Inventory.Where(i => i.Potion is { } p && (kind == PotionKind.Hp ? p.Hp : p.Mp) > 0 && p.RequiredLevel <= world.Host.Level)
                .OrderBy(i => kind == PotionKind.Hp ? i.Potion!.Hp : i.Potion!.Mp).FirstOrDefault();
            use = ItemUse.Potion;
            if (food is null)
            {
                Console.WriteLine($"❌ Нет банок {kind}");
                return 2;
            }
        }

        // Где искать: персонаж (там у клиентов массив перезарядок), предмет корма в сумке, менеджер петов
        var bag = game.ReadUInt32(world.Host.Address + data.Host.Inventory);
        var item = game.ReadUInt32(game.ReadUInt32(bag + data.Inventory.Items) + (uint)food.Slot * 4);
        var regions = new List<Region>
        {
            new("перс", world.Host.Address, 0x2000),
            new("корм в сумке", item, 0x200),
            new("менеджер петов", game.ReadUInt32(world.Host.Address + data.Host.PetManager), 0x200),
        };

        var samples = new List<(double Seconds, Dictionary<string, byte[]> Bytes)>();
        var watch = Stopwatch.StartNew();
        void Sample() => samples.Add((watch.Elapsed.TotalSeconds, regions.ToDictionary(r => r.Name, r => game.ReadBytes(r.Address, r.Size))));

        Sample();
        Console.WriteLine($"Использую ({what}): tid {food.Tid} из ячейки {food.Slot} (×{food.Count})");
        // Как бот: в главном потоке игры через её окно (отдельным потоком клиент 1.4.6 падал)
        using var calls = GameCalls.Open(game, data, CallTransport.Window, out var callsProblem);
        if (calls is null)
        {
            Console.WriteLine($"❌ вызовы не подключились: {callsProblem}");
            return 3;
        }

        var runner = new ActionRunner(calls.Actions, NullLogger.Instance);
        var submit = runner.Submit(new UseItemAction(food, use), world);
        if (!submit.Sent)
        {
            Console.WriteLine($"❌ не отправлено: {submit.Outcome?.Details}");
            return 3;
        }

        while (watch.Elapsed < TimeSpan.FromSeconds(15))
        {
            Thread.Sleep(250);
            Sample();
            var outcome = runner.Update(reader.Read()).FirstOrDefault();
            if (outcome is not null)
                Console.WriteLine($"Результат: {outcome}");
        }

        foreach (var region in regions)
            Report(region, samples.Select(s => (s.Seconds, s.Bytes[region.Name])).ToList());
        return 0;
    }

    private static void Report(Region region, List<(double Seconds, byte[] Bytes)> samples)
    {
        var found = 0;
        for (var offset = 0; offset + 4 <= region.Size; offset += 4)
        {
            var values = samples.Select(s => BitConverter.ToInt32(s.Bytes, offset)).ToList();
            var after = values.Skip(1).ToList();
            var peak = after.Max();
            var last = after.Last();
            if (peak <= 0 || peak > 3_600_000 || last >= peak)
                continue;

            // После пика значение только убывает (или стоит) и в целом убыло заметно — похоже на отсчёт
            var peakIndex = after.IndexOf(peak);
            var tail = after.Skip(peakIndex).ToList();
            var monotonic = tail.Zip(tail.Skip(1), (a, b) => b <= a).All(x => x);
            if (!monotonic || tail.Count < 8 || peak - last < 500)
                continue;

            found++;
            var rate = (peak - last) / (samples.Last().Seconds - samples[peakIndex + 1].Seconds);
            Console.WriteLine($"  {region.Name} +0x{offset:X3}: до {values[0]}, пик {peak}, в конце {last}, убывает ~{rate:0}/с"
                              + (Math.Abs(rate - 1000) < 150 ? "  ← похоже на миллисекунды" : ""));
        }

        Console.WriteLine(found == 0 ? $"{region.Name}: ничего похожего на отсчёт" : $"{region.Name}: кандидатов {found}");
    }
}
