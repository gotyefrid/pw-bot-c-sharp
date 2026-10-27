using System;
using System.Collections.Generic;

namespace BotCH.App.Panels;

/// <summary>
/// Варианты для выбора в окнах кликера (шаг, условие, группа условий) — одним объектом: окна и списки условий передают его
/// целиком, шаблон вида берёт нужное. Каждое — функция: зовётся при открытии списка (видно то, что вокруг сейчас).
/// Новый вид выбора — свойство здесь.
/// </summary>
/// <param name="Loot">Предметы: рядом, встречались, группы — как у лута.</param>
/// <param name="Resources">Что копается: рядом (и «нересурсы») и известное по блокноту ресурсов — как в точках маршрута.</param>
/// <param name="Targets">Кого можно взять в цель — мобы, NPC, чужие петы: рядом и встречались.</param>
/// <param name="Foods">Корм пета из сумки и «любой» — как в настройках пета.</param>
/// <param name="Skills">Изученные скиллы персонажа.</param>
public sealed record ClickerChoices(
    Func<IReadOnlyList<PickOption>> Loot,
    Func<IReadOnlyList<PickOption>> Resources,
    Func<IReadOnlyList<PickOption>> Targets,
    Func<IReadOnlyList<PickOption>> Foods,
    Func<IReadOnlyList<PickOption>> Skills);
