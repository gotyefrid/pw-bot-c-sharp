using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Notes.App.Mvvm;
using Notes.Core.Brain;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.App.Panels;

/// <summary>
/// Вкладка «Задания» режима «Прокачать друида»: какие задания из «Доступных» не брать никогда. Список — в настройках персонажа;
/// варианты для добавления — задания вкладки «Доступные» в игре.
/// </summary>
public sealed class QuestsPanel : ObservableObject
{
    private readonly Func<BotSettings> _settings;
    private readonly Func<WorldState?> _world;
    private readonly Action _edited;
    private bool _syncing;

    /// <param name="settings">Настройки текущего персонажа.</param>
    /// <param name="world">Последний снимок (доступные задания в нём — пока выбран этот режим).</param>
    /// <param name="edited">Правка списка.</param>
    public QuestsPanel(Func<BotSettings> settings, Func<WorldState?> world, Action edited)
    {
        _settings = settings;
        _world = world;
        _edited = edited;
        SkipNames.CollectionChanged += (_, _) => Edited();
    }

    /// <summary>Задания, которые не брать.</summary>
    public ObservableCollection<string> SkipNames { get; } = new();

    /// <summary>Что можно добавить: доступные сейчас задания, кроме уже выбранных.</summary>
    public Func<IReadOnlyList<PickOption>> SkipOptions
        => () => (_world()?.AvailableQuests ?? [])
            .Select(q => q.Name)
            .Where(name => name.Length > 0 && !_settings().Quests.Skips(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new PickOption(name, $"{name} — доступно"))
            .ToList();

    /// <summary>Окно ← настройки персонажа (при запуске и смене персонажа).</summary>
    public void Load()
    {
        _syncing = true;
        try
        {
            SkipNames.Clear();
            foreach (var name in _settings().Quests.SkipNames)
                SkipNames.Add(name);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void Edited()
    {
        if (_syncing)
            return;

        _settings().Quests.SkipNames = MobNameFilter.Clean(SkipNames);
        _edited();
    }
}
