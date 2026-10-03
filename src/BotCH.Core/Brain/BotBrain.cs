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

    public BotBrain(ActionRunner runner, ClassSkills skills, BotSettings settings, ILogger log, Random? random = null,
        IReadOnlyCollection<uint>? gatherTools = null, Func<string, WorldState, bool>? gatherFitsInStacks = null)
    {
        _context = new BrainContext(runner, skills, log, random ?? new Random()) { Settings = settings.Clone() };
        Survival = new SurvivalBehavior();
        Pet = new PetBehavior();
        Combat = new CombatBehavior();
        Gather = new GatherBehavior(Combat, gatherTools ?? [], gatherFitsInStacks);
        Return = new ReturnBehavior(Combat);
        _behaviors = [Survival, Pet, Gather, Combat, Return];
    }

    public SurvivalBehavior Survival { get; }
    public PetBehavior Pet { get; }
    public CombatBehavior Combat { get; }
    public GatherBehavior Gather { get; }
    public ReturnBehavior Return { get; }

    /// <summary>Что делает бот — для окна.</summary>
    public string Status => _status;

    public event Action<string>? StatusChanged;

    /// <summary>Мозг сам просит остановку (персонаж погиб).</summary>
    public event Action<string>? StopRequested;

    /// <summary>Новые настройки применяются на следующем шаге (копия — окно может менять свои дальше).</summary>
    public void UpdateSettings(BotSettings settings)
    {
        lock (_lock)
            _newSettings = settings.Clone();
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
            var center = CenterText();
            if (center != _centerText)
            {
                _centerText = center;
                var distance = _context.FarmCenter is { } c ? $", до него {world.Host.Position.HorizontalDistanceTo(c):0} м" : "";
                _context.Log.Info(center + distance);
            }

            foreach (var outcome in _context.Runner.Update(world))
            {
                foreach (var behavior in _behaviors)
                    behavior.OnOutcome(_context, outcome);
            }

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

                SetStatus((acted ?? Combat).Status ?? _behaviors.Select(b => b.Status).FirstOrDefault(s => s is not null) ?? "ожидание");
            }
        }

        if (stop is not null)
        {
            _context.Log.Warning("Персонаж погиб — бот остановлен");
            StopRequested?.Invoke(stop);
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _context.Runner.Clear();
            _context.StartPosition = null;
            _centerText = null;
            foreach (var behavior in _behaviors)
                behavior.Reset();
            SetStatus("ожидание");
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
