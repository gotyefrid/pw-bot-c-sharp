using System;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Profiles;

namespace Notes.Core.Brain;

/// <summary>Чем кончилось всплытие.</summary>
public enum WaterExitStep
{
    /// <summary>Ещё выбираемся.</summary>
    Going,
    /// <summary>Выбрались: можно взлетать.</summary>
    Out,
    /// <summary>Не выбрались — причина в <see cref="WaterExit.Failed"/>.</summary>
    Failed,
}

/// <summary>
/// Выбраться из воды, чтобы взлететь: сначала игровым движением «плыть в точку» вверх (<see cref="SwimUpAction"/>), на
/// поверхности — «Полёт». Взлететь так и не вышло — бот останавливается. Не поведение, а деталь обхода: шаги — из его Tick,
/// итоги — из его OnOutcome.
/// </summary>
public sealed class WaterExit
{
    // Всплываем шагами по метру; в памяти высота в 10 раз больше
    private const int MaxSwim = 100;
    // Столько «Полётов» не вышло — сдаёмся
    private const int MaxFlyFails = 3;
    private static readonly TimeSpan FlyPause = TimeSpan.FromSeconds(1);

    private enum Phase
    {
        Swim,
        Fly,
    }

    private Phase _phase = Phase.Swim;
    private bool _started;
    private int _swims;
    private int _flyFails;
    private DateTime _nextAt = DateTime.MinValue;

    /// <summary>Что делаем сейчас — для окна.</summary>
    public string? Status { get; private set; }

    /// <summary>Почему не выбрались; null — ещё выбираемся или уже вышли.</summary>
    public string? Failed { get; private set; }

    public WaterExitStep Step(BrainContext c)
    {
        Status = null;
        var host = c.World.Host;
        if (host.InWater != true || host.Flying == true)
        {
            // Вышли из воды, пока плыли вверх: недоделанный ход больше не нужен
            foreach (var pending in c.Mine.Where(a => a is MoveAction or FlyAction or SwimUpAction).ToList())
                c.Forget(pending);
            Reset();
            return WaterExitStep.Out;
        }
        if (Failed is not null)
            return WaterExitStep.Failed;

        if (!_started)
        {
            _started = true;
            _phase = Phase.Swim;
            // Прежний ход и попытки взлёта из воды не помогут — дальше ведёт всплытие
            foreach (var pending in c.Mine.Where(a => a is MoveAction or FlyAction).ToList())
                c.Forget(pending);
            c.Log.Info("В воде — всплываю, потом взлечу");
        }

        if (c.Mine.Count > 0)
        {
            Status = PhaseStatus();
            return WaterExitStep.Going;
        }

        if (c.Now < _nextAt)
        {
            Status = PhaseStatus();
            return WaterExitStep.Going;
        }

        switch (_phase)
        {
            case Phase.Swim:
                if (!c.Swimming || _swims >= MaxSwim)
                {
                    _phase = Phase.Fly;
                    return Step(c);
                }

                _swims++;
                Status = "всплываю";
                c.Submit(new SwimUpAction(host.Position));
                return WaterExitStep.Going;

            default:
                if (_flyFails >= MaxFlyFails)
                {
                    Failed = $"не взлетел из воды за {MaxFlyFails} попытки";
                    return WaterExitStep.Failed;
                }

                _flyFails++;
                Status = "на поверхности — взлетаю";
                c.Submit(new FlyAction(up: true));
                return WaterExitStep.Going;
        }
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (!_started || Failed is not null || outcome.Status == ActionStatus.Confirmed)
            return;

        switch (outcome.Action)
        {
            // Движение вверх не подняло: на поверхности (или выше не пускает) — «Полёт»
            case SwimUpAction:
                _phase = Phase.Fly;
                if (outcome.Status != ActionStatus.Rejected)
                    c.Log.Warning($"Всплыть движением не вышло ({outcome.Details}) — пробую взлететь");
                break;

            // Взлёт не подтвердился — ещё попытка
            case FlyAction { Up: true }:
                _nextAt = c.Now + FlyPause;
                break;
        }
    }

    public void Reset()
    {
        _started = false;
        _phase = Phase.Swim;
        _swims = 0;
        _flyFails = 0;
        _nextAt = DateTime.MinValue;
        Status = null;
        Failed = null;
    }

    private string PhaseStatus() => _phase switch
    {
        Phase.Fly => "на поверхности — взлетаю",
        _ => "всплываю",
    };
}
