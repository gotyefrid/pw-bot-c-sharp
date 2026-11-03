using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Notes.App.Mvvm;
using Notes.Core.Actions;
using Notes.Core.Brain;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.App.Panels;

/// <summary>
/// Какие банки пить: «Авто» и банки HP / MP из сумки с перезарядкой 10 с. Выбор — в настройках персонажа; о правке говорит
/// модели окна. Бот переключил выбранную на «Авто» (её нет в сумке) — ставим «Авто» и здесь.
/// </summary>
public sealed class PotionsPanel : ObservableObject
{
    private readonly Func<BotSettings> _settings;
    private readonly Action _edited;
    private bool _syncing;
    private IReadOnlyList<InventoryItem> _bag = Array.Empty<InventoryItem>();
    private int _level;

    /// <param name="settings">Настройки текущего персонажа.</param>
    /// <param name="edited">Правка выбора банки.</param>
    public PotionsPanel(Func<BotSettings> settings, Action edited)
    {
        _settings = settings;
        _edited = edited;
        Fill();
    }

    private BotSettings Settings => _settings();

    /// <summary>Пункт списка: значение — название банки, пусто — «Авто».</summary>
    public sealed record PotionChoice(string Value, string Title);

    public ObservableCollection<PotionChoice> HpPotions { get; } = new();
    public ObservableCollection<PotionChoice> MpPotions { get; } = new();

    // Пока список пересобирается — null: окно, сбросив выбор, тут же перечитывает его, и «Авто», которого ещё нет в списке,
    // осталось бы «выбранным», но невидимым (поле пустое). После пересборки выбор меняется null → пункт, и окно его находит
    public string? HpPotion
    {
        get => _syncing ? null : Settings.Potions.HpPotion;
        set => Choose(value, () => Settings.Potions.HpPotion, v => Settings.Potions.HpPotion = v);
    }

    public string? MpPotion
    {
        get => _syncing ? null : Settings.Potions.MpPotion;
        set => Choose(value, () => Settings.Potions.MpPotion, v => Settings.Potions.MpPotion = v);
    }

    /// <summary>Выбор в окне ← настройки (сменился персонаж).</summary>
    public void Rebind()
    {
        Fill();
        OnPropertyChanged(nameof(HpPotion));
        OnPropertyChanged(nameof(MpPotion));
    }

    /// <summary>Списки ← сумка (пересобираются, только когда что-то поменялось); переключения бота на «Авто» ← бот.</summary>
    public void Observe(WorldState w, IPotionFallback? bot)
    {
        while (bot is not null && bot.TryTakeSwitch(out var kind, out var name))
        {
            // Пока бот решал, выбор в окне могли сменить — тогда не трогаем
            if (kind == PotionKind.Hp && Same(Settings.Potions.HpPotion, name))
                HpPotion = "";
            else if (kind == PotionKind.Mp && Same(Settings.Potions.MpPotion, name))
                MpPotion = "";
            OnPropertyChanged(kind == PotionKind.Hp ? nameof(HpPotion) : nameof(MpPotion));
        }

        (_bag, _level) = (w.Inventory, w.Host.Level);
        Fill();
    }

    // Списки ← последняя прочитанная сумка; мира ещё нет — в списке хотя бы «Авто» и выбранная: поле не пустое
    private void Fill()
    {
        Sync(HpPotions, Choices(_bag, _level, PotionKind.Hp, Settings.Potions.HpPotion), nameof(HpPotion));
        Sync(MpPotions, Choices(_bag, _level, PotionKind.Mp, Settings.Potions.MpPotion), nameof(MpPotion));
    }

    // Пересборка списка сбрасывает выбор — это не выбор пользователя
    private void Choose(string? value, Func<string> get, Action<string> set)
    {
        if (_syncing || value is null || value == get())
            return;
        set(value);
        _edited();
    }

    // Банки из сумки по названию (стопки складываем), сначала самая слабая; не по уровню — с пометкой «нужен ур.» (выбрать
    // можно: бот начнёт её пить с этого уровня); выбранная, которой в сумке нет, — с пометкой «нет в сумке»
    private static List<PotionChoice> Choices(IEnumerable<InventoryItem> bag, int level, PotionKind kind, string chosen)
    {
        var fit = bag.Where(i => PotionPolicy.Fits(i, kind) && i.Name is not null)
            .GroupBy(i => i.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Min(i => kind == PotionKind.Hp ? i.Potion!.Hp : i.Potion!.Mp))
            .ToList();
        var auto = fit.FirstOrDefault(g => NeedsLevel(g) <= level);
        var list = new List<PotionChoice> { new("", auto is not null ? $"Авто — самая слабая ({auto.Key})" : "Авто — самая слабая") };
        list.AddRange(fit.Select(g => new PotionChoice(g.Key,
            $"{g.Key} ×{g.Sum(i => i.Count)}{(NeedsLevel(g) > level ? $" — нужен ур. {NeedsLevel(g)}" : "")}")));
        if (chosen.Length > 0 && !list.Any(c => Same(c.Value, chosen)))
            list.Add(new PotionChoice(chosen, $"{chosen} — нет в сумке"));
        return list;
    }

    private static int NeedsLevel(IEnumerable<InventoryItem> potions) => potions.Min(i => i.Potion!.RequiredLevel);

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private void Sync(ObservableCollection<PotionChoice> target, List<PotionChoice> choices, string property)
    {
        if (choices.SequenceEqual(target))
            return;

        _syncing = true;
        try
        {
            target.Clear();
            foreach (var choice in choices)
                target.Add(choice);
        }
        finally
        {
            _syncing = false;
        }

        OnPropertyChanged(property);
    }
}
