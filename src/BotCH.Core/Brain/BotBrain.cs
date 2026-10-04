using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Logging;
using BotCH.Core.Profiles;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>
/// Мозг бота: на каждом снимке — проверить ждущие действия, затем поведения по приоритету
/// (выжить → пет → копать ресурсы → бой → вернуться в центр фарма), первое занявшее ход останавливает перебор. Один поток решений: <see cref="Tick"/>
/// вызывается из потока снимков. Режим «фарм мобов»; другие режимы (сбор ресурсов) — другим набором поведений.
/// </summary>
public sealed class BotBrain : IBotRunner
{
    private readonly object _lock = new();
    private readonly BrainContext _context;
    private readonly IReadOnlyList<IBehavior> _behaviors;
    private BotSettings? _newSettings;
    private string _status = "ожидание";
    private string? _centerText;

    /// <param name="mode">Фарм мобов или обход ресурсов (<see cref="BotMode.GatherResources"/>): у обхода бой — только защита,
    /// копание — у точек маршрута, вместо возврата в центр — переход к следующей точке.</param>
    public BotBrain(ActionRunner runner, ClassSkills skills, BotSettings settings, ILogger log, Random? random = null,
        IReadOnlyCollection<uint>? gatherTools = null, BotMode mode = BotMode.FarmMobs)
    {
        _mode = mode;
        _context = new BrainContext(runner, skills, log, random ?? new Random()) { Settings = Effective(settings) };
        Survival = new SurvivalBehavior();
        if (mode == BotMode.GatherResources)
        {
            Combat = new CombatBehavior(defendOnly: true);
            Pet = new PetBehavior(Combat);
            Route = new RouteBehavior(gatherTools ?? []);
            Gather = new GatherBehavior(Combat, gatherTools ?? [], Route.Scope);
            Escape = new EscapeBehavior(Route, Combat);
            _behaviors = [Survival, Escape, Pet, Gather, Combat, Route];
            return;
        }

        Combat = new CombatBehavior();
        Pet = new PetBehavior(Combat);
        Gather = new GatherBehavior(Combat, gatherTools ?? []);
        Return = new ReturnBehavior(Combat);
        _behaviors = [Survival, Pet, Gather, Combat, Return];
    }

    private readonly BotMode _mode;

    // Копия (окно может менять свои дальше); у обхода — напавших бьём всегда
    private BotSettings Effective(BotSettings settings) => _mode == BotMode.GatherResources ? settings.ForGathering() : settings.Clone();

    public SurvivalBehavior Survival { get; }
    public PetBehavior Pet { get; }
    public CombatBehavior Combat { get; }
    public GatherBehavior Gather { get; }
    /// <summary>Возврат в центр фарма; null — в режиме обхода.</summary>
    public ReturnBehavior? Return { get; }

    /// <summary>Обход маршрута; null — в режиме фарма мобов.</summary>
    public RouteBehavior? Route { get; }

    /// <summary>Уход вверх от опасного моба (обход); null — в режиме фарма мобов.</summary>
    public EscapeBehavior? Escape { get; }

    /// <summary>Что делает бот — для окна.</summary>
    public string Status => _status;

    public event Action<string>? StatusChanged;

    /// <summary>Мозг сам просит остановку (персонаж погиб).</summary>
    public event Action<string>? StopRequested;

    /// <summary>Новые настройки применяются на следующем шаге (копия — окно может менять свои дальше).</summary>
    public void UpdateSettings(BotSettings settings)
    {
        lock (_lock)
            _newSettings = Effective(settings);
    }

    public void Tick(WorldState world)
    {
        string? stop = null;
        lock (_lock)
        {
            if (_newSettings is not null)
            {
                _context.Settings = _newSettings;
                _newSettings = null;
            }

            _context.World = world;
            _context.StartPosition ??= world.Host.Position;
            var center = _mode == BotMode.FarmMobs ? CenterText() : _centerText;
            if (center != _centerText)
            {
                _centerText = center;
                var distance = _context.FarmCenter is { } c ? $", до него {world.Host.Position.HorizontalDistanceTo(c):0} м" : "";
                _context.Log.Info(center + distance);
            }

            Deliver(_context.Runner.Update(world));

            if (world.Host.IsDead)
            {
                stop = "персонаж погиб";
                SetStatus("персонаж погиб — стоп");
            }
            else
            {
                IBehavior? acted = null;
                foreach (var behavior in _behaviors)
                {
                    if (behavior.Tick(_context))
                    {
                        acted = behavior;
                        break;
                    }
                }

                Deliver(_context.TakeNotSent());
                SetStatus((acted ?? Combat).Status ?? _behaviors.Select(b => b.Status).FirstOrDefault(s => s is not null) ?? "ожидание");
                if (_context.StopReason is { } reason)
                {
                    _context.StopReason = null;
                    stop = reason;
                    SetStatus(reason + " — стоп");
                }
            }
        }

        if (stop is not null)
        {
            _context.Log.Warning($"{char.ToUpper(stop[0])}{stop.Substring(1)} — бот остановлен");
            StopRequested?.Invoke(stop);
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _context.Runner.Clear();
            _context.TakeNotSent();
            _context.StartPosition = null;
            _context.StopReason = null;
            _centerText = null;
            foreach (var behavior in _behaviors)
                behavior.Reset();
            SetStatus("ожидание");
        }
    }

    // Итоги действий — всем поведениям (каждое берёт своё): и завершившиеся по снимку, и «не отправлено»
    private void Deliver(IReadOnlyList<ActionOutcome> outcomes)
    {
        foreach (var outcome in outcomes)
        {
            foreach (var behavior in _behaviors)
                behavior.OnOutcome(_context, outcome);
        }
    }

    // «Центр фарма …» — в лог при старте и когда сменили точку или радиус
    private string CenterText()
    {
        var target = _context.Settings.Target;
        var point = target.SelectedFarmPoint;
        var where = point is null ? $"точка старта {_context.StartPosition}" : $"«{point.Name}» {point.Position}";
        return target.FarmRadius > 0 ? $"Центр фарма: {where}, радиус {target.FarmRadius} м" : $"Центр фарма: {where}, радиус не ограничен";
    }

    private void SetStatus(string status)
    {
        if (status == _status)
            return;

        _status = status;
        StatusChanged?.Invoke(status);
    }
}
