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
/// Карточка «Ремонт»: у какого NPC чинить. Список — NPC вокруг персонажа (у каждого вида — один, ближний) и уже выбранный, если
/// его рядом нет. Выбрали — запоминаем вид, имя и где он стоит: туда бот и полетит.
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

    /// <summary>Пункт списка: вид NPC (0 — не выбран) и имя.</summary>
    public sealed record NpcChoice(int Tid, string Title);

    public ObservableCollection<NpcChoice> Npcs { get; } = new();

    private BotSettings Settings => _settings();

    // Пока список пересобирается — null: окно, сбросив выбор, тут же перечитывает его, и пункт, которого ещё нет в списке,
    // остался бы «выбранным», но невидимым (поле пустое). После пересборки выбор меняется null → пункт, и окно его находит
    public int? NpcTid
    {
        get => _syncing ? null : Settings.Repair.Npc?.Tid ?? 0;
        set
        {
            if (_syncing || value is not { } tid || tid == (Settings.Repair.Npc?.Tid ?? 0))
                return;
            if (tid == 0)
            {
                Settings.Repair.Npc = null;
                _log.Info("NPC для ремонта не выбран");
            }
            else if (Nearby(_world).FirstOrDefault(n => n.Tid == tid) is { } npc)
            {
                Settings.Repair.Npc = RepairNpc.At(tid, npc.Name.Trim(), npc.Position);
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
        OnPropertyChanged(nameof(NpcTid));
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

    // NPC с именем и видом; у каждого вида — ближний
    private static IEnumerable<NpcInfo> Nearby(WorldState? w)
        => (w?.Npcs ?? [])
            .Where(n => n.Kind == NpcKind.Npc && n.Tid != 0 && !string.IsNullOrWhiteSpace(n.Name))
            .GroupBy(n => n.Tid)
            .Select(g => g.OrderBy(n => n.Offset.Horizontal).First());

    private void Fill()
    {
        var nearby = Nearby(_world).Select(n => new NpcChoice(n.Tid, n.Name.Trim())).OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        // Рядом никого — подсказать, откуда берётся список
        var list = new List<NpcChoice> { new(0, nearby.Count == 0 ? "— рядом NPC нет: встаньте у того, кто чинит —" : "— не выбран —") };
        list.AddRange(nearby);
        if (Settings.Repair.Npc is { } saved && list.All(c => c.Tid != saved.Tid))
            list.Insert(1, new NpcChoice(saved.Tid, saved.Name));
        if (list.SequenceEqual(Npcs))
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
        OnPropertyChanged(nameof(NpcTid));
    }
}
