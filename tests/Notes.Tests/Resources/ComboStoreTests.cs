using System;
using System.Collections.Generic;
using System.IO;
using Notes.Core.Resources;
using Notes.Core.Settings;
using Xunit;

namespace Notes.Tests.Resources;

/// <summary>Общий файл связок скиллов: видят все копии бота, правка одной связки не затирает другие, удалённая не возвращается.</summary>
public class ComboStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "notes-combos-" + Guid.NewGuid().ToString("N"));

    public ComboStoreTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // Две копии бота — два своих объекта на один файл
    private ComboStore Store() => new(Path.Combine(_folder, ComboStore.FileName));

    private static List<ComboRow> Rows(params int[] skills)
    {
        var rows = new List<ComboRow>();
        foreach (var skill in skills)
            rows.Add(new ComboRow
            {
                SkillId = skill,
                SkillName = $"с{skill}",
                Repeat = ComboRepeat.Interval,
                IntervalSeconds = 15,
                Conditions = [new ComboCondition { Check = ComboCheck.MobHp, Below = true, Value = 30 }],
            });
        return rows;
    }

    [Fact]
    public void NamesGetNextFreeNumber()
    {
        var store = Store();
        var first = store.Create();
        var second = store.Create();
        Assert.Equal("Связка 1", first.Name);
        Assert.Equal("Связка 2", second.Name);

        Assert.True(store.Rename(first.Id, "Мили"));
        Assert.False(store.Rename(second.Id, "мили"));
        Assert.False(store.Rename(second.Id, "  "));
        Assert.Equal("Связка 1", store.Create().Name);
    }

    [Fact]
    public void RowsRoundTripWithSettings()
    {
        var store = Store();
        var combo = store.Create();
        Assert.True(store.SaveRows(combo.Id, Rows(1, 2)));

        var saved = Store().Find(combo.Id)!;

        Assert.True(ComboStore.Same(Rows(1, 2), saved.Rows));
        Assert.Equal(ComboRepeat.Interval, saved.Rows[0].Repeat);
        Assert.Equal(ComboCheck.MobHp, saved.Rows[0].Conditions[0].Check);
        Assert.Contains("\"mobHp\"", File.ReadAllText(store.Path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateCopiesRows()
    {
        var rows = Rows(7);
        var combo = Store().Create(rows);
        rows[0].SkillId = 8;

        Assert.Equal(7, Store().Find(combo.Id)!.Rows[0].SkillId);
    }

    [Fact]
    public void SyncLoadsRowsChangedByOtherCopy()
    {
        var combo = Store().Create(Rows(1));
        var combat = new CombatSettings { ComboId = combo.Id, Combo = Rows(1) };
        Assert.Equal(ComboSync.Unchanged, Store().Sync(combat));

        Store().SaveRows(combo.Id, Rows(3, 4));

        Assert.Equal(ComboSync.Loaded, Store().Sync(combat));
        Assert.Equal([3, 4], combat.Combo.ConvertAll(r => r.SkillId));
    }

    [Fact]
    public void DeletedComboIsNotRestored()
    {
        var store = Store();
        var combo = store.Create(Rows(1));
        var combat = new CombatSettings { ComboId = combo.Id, Combo = Rows(1) };
        Assert.True(store.Remove(combo.Id));

        Assert.Equal(ComboSync.Deleted, Store().Sync(combat));
        Assert.Equal("", combat.ComboId);
        Assert.Empty(combat.Combo);
        Assert.Null(store.Find(combo.Id));
    }

    [Fact]
    public void LostFileGetsComboBackFromCharacter()
    {
        var combat = new CombatSettings { ComboId = "abc", Combo = Rows(5) };

        Assert.Equal(ComboSync.Restored, Store().Sync(combat));

        var restored = Store().Find("abc")!;
        Assert.Equal("Связка 1", restored.Name);
        Assert.Equal(5, restored.Rows[0].SkillId);
    }

    [Fact]
    public void NotSelectedIsUnchanged()
    {
        var combat = new CombatSettings { Combo = Rows(5) };
        Assert.Equal(ComboSync.Unchanged, Store().Sync(combat));
        Assert.Empty(Store().All());
    }
}
