using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Notes.App.Mvvm;
using Notes.Core.Logging;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.App.Panels;

/// <summary>
/// Карточка «Ремонт»: у какого NPC чинить. Список — NPC вокруг персонажа, ближние первыми (по имени — один, ближний), и уже
/// выбранный, если его рядом нет; в поле можно ввести имя. Выбрали — запоминаем имя, вид (если сервер его показывает) и где он
/// стоит: туда бот и полетит. По имени, а не по виду: на 1.3.6 вид NPC не читается.
/// </summary>
public sealed class RepairPanel : ObservableObject
{
    private readonly Func<BotSettings> _settings;
    private readonly ILogger _log;
    private readonly Action _edited;
    private WorldState? _world;
    private bool _syncing;
    private string _info = "";

    public RepairPanel(Func<BotSettings> settings, ILogger log, Action edited)
    {
        _settings = settings;
        _log = log;
        _edited = edited;
        Fill();
    }

    /// <summary>Пункт списка: имя NPC (пусто — не выбран) и текст пункта.</summary>
    public sealed record NpcChoice(string Name, string Title);

    public ObservableCollection<NpcChoice> Npcs { get; } = new();

    private BotSettings Settings => _settings();

    // Пока список пересобирается — null: окно, сбросив выбор, тут же перечитывает его, и пункт, которого ещё нет в списке,
    // остался бы «выбранным», но невидимым (поле пустое). После пересборки выбор меняется null → пункт, и окно его находит
    public string? NpcName
    {
        get => _syncing ? null : Settings.Repair.Npc?.Name ?? "";
        set
        {
            if (_syncing || value is null || Same(value, Settings.Repair.Npc?.Name ?? ""))
                return;
            if (value.Length == 0)
            {
                Settings.Repair.Npc = null;
                _log.Info("NPC для ремонта не выбран");
            }
            else if (Nearby(_world).FirstOrDefault(n => Same(n.Name, value)) is { } npc)
            {
                Settings.Repair.Npc = RepairNpc.At(npc.Tid, npc.Name.Trim(), npc.Position);
                _log.Info($"NPC для ремонта: {npc.Name.Trim()}");
            }
            else
                return;
            _edited();
            UpdateInfo();
        }
    }

    /// <summary>Сколько до выбранного NPC отсюда.</summary>
    public string Info
    {
        get => _info;
        private set
        {
            if (SetProperty(ref _info, value))
                OnPropertyChanged(nameof(HasInfo));
        }
    }

    public bool HasInfo => Info.Length > 0;

    /// <summary>Выбор в окне ← настройки (сменился персонаж).</summary>
    public void Rebind()
    {
        Fill();
        OnPropertyChanged(nameof(NpcName));
        UpdateInfo();
    }

    /// <summary>Список ← NPC вокруг (пересобирается, только когда кто-то появился или ушёл).</summary>
    public void Observe(WorldState w)
    {
        _world = w;
        Fill();
        UpdateInfo();
    }

    private void UpdateInfo()
        => Info = Settings.Repair.Npc is { } npc && _world is { } w ? $"{w.Host.Position.HorizontalDistanceTo(npc.Position):0} м отсюда" : "";

    // NPC с именем, ближние первыми; одноимённые — один, ближний
    private static IEnumerable<NpcInfo> Nearby(WorldState? w)
        => (w?.Npcs ?? [])
            .Where(n => n.Kind == NpcKind.Npc && !string.IsNullOrWhiteSpace(n.Name))
            .GroupBy(n => n.Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Select(g => g.OrderBy(n => n.Offset.Horizontal).First())
            .OrderBy(n => n.Offset.Horizontal);

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.CurrentCultureIgnoreCase);

    private void Fill()
    {
        var nearby = Nearby(_world).Select(n => new NpcChoice(n.Name.Trim(), n.Name.Trim())).ToList();
        // Рядом никого — подсказать, откуда берётся список
        var list = new List<NpcChoice> { new("", nearby.Count == 0 ? "— рядом NPC нет: встаньте у того, кто чинит —" : "— не выбран —") };
        list.AddRange(nearby);
        if (Settings.Repair.Npc is { } saved && list.All(c => !Same(c.Name, saved.Name)))
            list.Insert(1, new NpcChoice(saved.Name, saved.Name));
        // Пересобираем, только когда кто-то появился или ушёл: от того, что перс сдвинулся, порядок не прыгает под рукой
        if (list.Select(c => c.Title).OrderBy(t => t).SequenceEqual(Npcs.Select(c => c.Title).OrderBy(t => t)))
            return;

        _syncing = true;
        try
        {
            Npcs.Clear();
            foreach (var choice in list)
                Npcs.Add(choice);
        }
        finally
        {
            _syncing = false;
        }
        OnPropertyChanged(nameof(NpcName));
    }
}
