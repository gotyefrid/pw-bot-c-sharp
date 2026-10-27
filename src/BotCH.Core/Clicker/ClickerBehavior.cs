using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Brain;
using BotCH.Core.Logging;

namespace BotCH.Core.Clicker;

/// <summary>Какой шаг кликера сейчас (с 0) — окно выделяет его в списке.</summary>
public interface IClickerProgress
{
    int? Current { get; }
}

/// <summary>
/// Кликер — сценарий из шагов (<see cref="ClickerStep"/>) по кругу сверху вниз. Условие шага не выполнено — сразу к
/// следующему; выполнено — действие, и следующий шаг — когда оно закончится (клавиша — сразу, пауза — через N с). За снимок —
/// не больше одного круга: мгновенные шаги идут подряд, но ни один не начинается дважды, — без пауз круг крутится 4 раза
/// в секунду.
/// <para>
/// Шаг, который выполнить нечего (клавиша не выбрана), пропускается; таких все — стоп. Шаги поменяли на ходу — идущий
/// доделывается по-старому, дальше — новые.
/// </para>
/// </summary>
public sealed class ClickerBehavior : IBehavior, IClickerProgress
{
    private int _index;
    private IClickerRun? _run;
    private string _running = "";
    private bool _started;

    public string Name => "кликер";
    public string? Status { get; private set; }

    public int? Current => _started ? _index : null;

    public bool Tick(BrainContext c)
    {
        var steps = c.Settings.Clicker.Steps;
        if (steps.All(s => s.Action.Problem is not null))
            return c.RequestStop("в кликере нет ни одного готового действия");

        if (!_started)
        {
            _started = true;
            c.Log.Info($"Кликер: шагов в списке — {steps.Count}, иду по кругу");
        }

        // Шаги убрали, пока ничего не идёт, — с первого; идущий шаг доделывается, следующим будет первый
        if (_index >= steps.Count && _run is null)
            _index = 0;

        string? last = null;
        var started = 0;
        while (true)
        {
            if (_run is null)
            {
                // Каждый шаг — не больше раза за снимок (доделанный с прошлого не в счёт)
                if (started == steps.Count)
                    break;
                started++;

                var step = steps[_index];
                if (step.Action.Problem is not null || step.Condition.Check(c) is not { } why)
                {
                    Next(steps.Count);
                    continue;
                }

                _running = $"шаг {_index + 1}: {step.Action.Describe()}";
                c.Log.Debug($"Кликер: {_running} — {why}");
                _run = step.Action.Start(c);
            }

            last = _running;
            var state = _run.Tick(c);
            if (state.Status == StepStatus.Running)
            {
                Status = _running;
                return true;
            }

            if (state.Status == StepStatus.Failed)
                c.Say("clicker-" + _index, $"Кликер: {_running} — не вышло: {state.Details}", LogLevel.Warning);
            _run = null;
            Next(steps.Count);
        }

        Status = last ?? "жду: условия шагов не выполнены";
        return last is not null;
    }

    private void Next(int count) => _index = _index + 1 >= count ? 0 : _index + 1;

    public void OnOutcome(BrainContext c, ActionOutcome outcome) => _run?.OnOutcome(c, outcome);

    public void Reset()
    {
        _index = 0;
        _run = null;
        _started = false;
        Status = null;
    }
}
