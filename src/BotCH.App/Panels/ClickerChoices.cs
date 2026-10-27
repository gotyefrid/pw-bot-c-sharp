using System;
using System.Collections.Generic;

namespace BotCH.App.Panels;

/// <summary>
/// Варианты для выбора в окнах кликера (шаг, условие, группа условий) — одним объектом: окна и списки условий передают его
/// целиком, шаблон вида берёт нужное. Каждое — функция: зовётся при открытии списка (видно то, что вокруг сейчас).
/// Новый вид выбора — свойство здесь.
/// </summary>
/// <param name="Loot">Предметы: рядом, встречались, группы — как у лута.</param>
public sealed record ClickerChoices(Func<IReadOnlyList<PickOption>> Loot);
