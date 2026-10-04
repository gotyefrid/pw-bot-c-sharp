using BotCH.Core.Actions;
using BotCH.Core.Logging;

namespace BotCH.Core.Brain;

/// <summary>
/// «Центр фарма: …» — в лог при старте и когда сменили точку или радиус (режим фарма мобов). Ход не занимает и в игре
/// ничего не делает: стоит в наборе первым, чтобы смотреть на каждом снимке.
/// </summary>
public sealed class FarmCenterLog : IBehavior
{
    private string? _text;

    public string Name => "центр фарма";
    public string? Status => null;

    public bool Tick(BrainContext c)
    {
        var text = Text(c);
        if (text != _text)
        {
            _text = text;
            var distance = c.FarmCenter is { } center ? $", до него {c.World.Host.Position.HorizontalDistanceTo(center):0} м" : "";
            c.Log.Info(text + distance);
        }

        return false;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
    }

    public void Reset() => _text = null;

    private static string Text(BrainContext c)
    {
        var target = c.Settings.Target;
        var point = target.SelectedFarmPoint;
        var where = point is null ? $"точка старта {c.StartPosition}" : $"«{point.Name}» {point.Position}";
        return target.FarmRadius > 0 ? $"Центр фарма: {where}, радиус {target.FarmRadius} м" : $"Центр фарма: {where}, радиус не ограничен";
    }
}
