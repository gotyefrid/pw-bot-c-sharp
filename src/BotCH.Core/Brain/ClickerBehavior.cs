using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Logging;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>
/// Кликер: правила из настроек сверху вниз — жмёт клавишу первого, у которого выполнено условие и прошло время повтора.
/// Одно нажатие за ход. Целей не выбирает, боя и пета не ведёт — только то, что в списке. Нет ни одного правила с
/// клавишей — просит остановку.
/// </summary>
public sealed class ClickerBehavior : IBehavior
{
    // По номеру правила: настройки приходят копией (правила — новые объекты), номер — тот же
    private readonly Dictionary<int, DateTime> _pressed = [];
    private readonly HashSet<int> _holding = [];

    public string Name => "кликер";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        var rules = c.Settings.Clicker.Rules;
        if (!rules.Any(r => r.Stroke is not null))
            return c.RequestStop("у кликера нет правил с клавишей");

        // Условия — у всех правил на каждом снимке: в лог (Info) — только когда условие наступило, сами нажатия — подробно
        int? press = null;
        string? pressWhy = null, waiting = null;
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (rule.Stroke is not { } key || Why(rule, c.World) is not { } why)
            {
                _holding.Remove(i);
                continue;
            }

            if (_holding.Add(i))
                c.Log.Info($"Кликер: {why} — жму {key} раз в {rule.RepeatSeconds:0.##} с");
            if (press is not null)
                continue;

            if (_pressed.TryGetValue(i, out var last) && c.Now - last < TimeSpan.FromSeconds(rule.RepeatSeconds))
            {
                waiting ??= $"{key}: {why}";
                continue;
            }

            press = i;
            pressWhy = why;
        }

        if (press is not { } index)
        {
            Status = waiting ?? "жду: условия не выполнены";
            return false;
        }

        // Нажатие не ушло (окно игры не нашлось) — следующая попытка тоже через время повтора; отказ в лог пишет исполнитель
        var stroke = rules[index].Stroke!.Value;
        _pressed[index] = c.Now;
        if (c.Send(new KeyAction(stroke)) != SubmitStatus.Sent)
            return true;

        c.Log.Debug($"Кликер: {stroke} — {pressWhy}");
        Status = $"нажал {stroke}: {pressWhy}";
        return true;
    }

    /// <summary>Почему правило сейчас жмётся («HP 42 % — меньше 50 %»); null — условие не выполнено.</summary>
    public static string? Why(ClickerRule rule, WorldState world)
    {
        var host = world.Host;
        switch (rule.Condition)
        {
            case ClickerCondition.HpBelow:
                return host.MaxHp > 0 && host.HpPercent < rule.HpPercent ? $"HP {host.HpPercent} % — меньше {rule.HpPercent} %" : null;
            case ClickerCondition.ItemNearby:
                var item = world.GroundItems
                    .Where(i => i.Kind != GroundItemKind.Resource && i.Offset.Direct <= rule.ItemRadius)
                    .Where(i => rule.Items.Count == 0 || MobNameFilter.Contains(rule.Items, i.Name) || MobNameFilter.Contains(rule.Items, i.Group))
                    .OrderBy(i => i.Offset.Direct)
                    .FirstOrDefault();
                return item is null ? null : $"лежит «{item.Name}» в {item.Offset}";
            default:
                return "всегда";
        }
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
    }

    public void Reset()
    {
        _pressed.Clear();
        _holding.Clear();
        Status = null;
    }
}
