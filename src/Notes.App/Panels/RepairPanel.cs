using System;
using System.Collections.Generic;
using System.Linq;
using Notes.App.Mvvm;
using Notes.Core.Logging;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.App.Panels;

/// <summary>
/// Карточка «Ремонт»: у какого NPC чинить. Список с поиском (<see cref="ChoicePicker"/>) — NPC вокруг персонажа, ближние первыми
/// (одноимённые — один, ближний), и уже выбранный, если его рядом нет. Выбрали — запоминаем имя, вид (если сервер его
/// показывает) и где он стоит: туда бот и полетит. По имени, а не по виду: на 1.3.6 вид NPC не читается.
/// </summary>
public sealed class RepairPanel : ObservableObject
{
    private readonly Func<BotSettings> _settings;
    private readonly ILogger _log;
    private readonly Action _edited;
    private WorldState? _world;
    private string _info = "";

    public RepairPanel(Func<BotSettings> settings, ILogger log, Action edited)
    {
        _settings = settings;
        _log = log;
        _edited = edited;
        NpcOptions = Options;
    }

    private BotSettings Settings => _settings();

    /// <summary>Варианты списка — заново при каждом открытии: NPC вокруг меняются.</summary>
    public Func<IReadOnlyList<PickOption>> NpcOptions { get; }

    /// <summary>Имя выбранного NPC; пусто — не выбран.</summary>
    public string NpcName
    {
        get => Settings.Repair.Npc?.Name ?? "";
        set
        {
            value ??= "";
            if (Same(value, NpcName))
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
            OnPropertyChanged();
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
        OnPropertyChanged(nameof(NpcName));
        UpdateInfo();
    }

    /// <summary>Где персонаж и кто вокруг — для списка и «N м отсюда».</summary>
    public void Observe(WorldState w)
    {
        _world = w;
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

    private IReadOnlyList<PickOption> Options()
    {
        var nearby = Nearby(_world).Select(n => new PickOption(n.Name.Trim(), n.Name.Trim())).ToList();
        // Подпись пустого выбора — одна и та же: поле показывает её и когда список закрыт, а NPC вокруг к тому времени другие
        var list = new List<PickOption> { new("", "— не выбран —") };
        if (Settings.Repair.Npc is { } saved && nearby.All(o => !Same(o.Name, saved.Name)))
            list.Add(new PickOption(saved.Name, saved.Name));
        list.AddRange(nearby);
        return list;
    }
}
