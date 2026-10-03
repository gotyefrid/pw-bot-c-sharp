using System;
using System.Collections.Generic;
using System.IO;
using BotCH.Core.Resources;
using BotCH.Core.World;
using Xunit;

namespace BotCH.Tests.Resources;

public class ResourceYieldsTests : IDisposable
{
    private const string Root = "Высохший древесный корень";
    private readonly ResourceYields _yields = new();
    private readonly string _file = Path.Combine(Path.GetTempPath(), "botch-yields-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        foreach (var f in new[] { _file, _file + ".bad", _file + ".tmp" })
            if (File.Exists(f))
                File.Delete(f);
    }

    private static InventoryItem Stack(uint tid, int count, int max) => new(0, tid, 8, count, null, null) { MaxCount = max };

    [Fact]
    public void LearnKeepsLargestCatchPerServer()
    {
        Assert.True(_yields.Learn("comeback146", Root, new Dictionary<uint, int> { [795] = 2 }));
        Assert.True(_yields.Learn("comeback146", Root, new Dictionary<uint, int> { [795] = 4 }));
        Assert.False(_yields.Learn("comeback146", Root, new Dictionary<uint, int> { [795] = 3 }));

        Assert.Equal(4, _yields.Of("comeback146", Root)[795]);
        Assert.Empty(_yields.Of("pwclassic136", Root));
        Assert.True(_yields.Changed);
    }

    [Fact]
    public void FitsOnlyWhenEveryProductHasRoomForLargestCatch()
    {
        _yields.Learn("s", Root, new Dictionary<uint, int> { [795] = 4 });

        Assert.True(_yields.FitsInStacks("s", Root, [Stack(795, 95, 99)]));
        Assert.True(_yields.FitsInStacks("s", Root, [Stack(795, 97, 99), Stack(795, 97, 99)]));
        Assert.False(_yields.FitsInStacks("s", Root, [Stack(795, 96, 99)]));
        Assert.False(_yields.FitsInStacks("s", Root, [Stack(796, 1, 99)]));
        Assert.False(_yields.FitsInStacks("s", "Шалфей", [Stack(795, 1, 99)])); // не знаем, что даёт

        _yields.Learn("s", Root, new Dictionary<uint, int> { [3074] = 1 });
        Assert.False(_yields.FitsInStacks("s", Root, [Stack(795, 1, 99)]));
    }

    [Fact]
    public void StoreKeepsNamesAsInGameAndMerges()
    {
        var store = new ResourceYieldsStore(_file);
        Assert.Empty(store.Load(out var none));
        Assert.Null(none);

        _yields.Learn("comeback146", Root, new Dictionary<uint, int> { [795] = 4 });
        store.Save(_yields);
        Assert.Contains(Root, File.ReadAllText(_file));

        var other = new ResourceYields();
        other.Learn("comeback146", Root, new Dictionary<uint, int> { [795] = 2 });
        other.Learn("pwclassic136", Root, new Dictionary<uint, int> { [512] = 3 });
        other.Merge(store.Load(out _));
        Assert.Equal(4, other.Of("comeback146", Root)[795]);
        Assert.Equal(3, other.Of("pwclassic136", Root)[512]);

        File.WriteAllText(_file, "{ испорчен");
        Assert.Empty(store.Load(out var problem));
        Assert.NotNull(problem);
        Assert.True(File.Exists(_file + ".bad"));
    }
}
