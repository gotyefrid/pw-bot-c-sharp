using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.World;

namespace BotCH.Core.Resources;

public enum SpotEventKind
{
    /// <summary>Ресурс увиден впервые — новая точка.</summary>
    New,

    /// <summary>Известная точка снова видна (подошли), а как её копали — не видели.</summary>
    InView,

    /// <summary>Пропал на глазах — выкопали (мы или другой игрок).</summary>
    Dug,

    /// <summary>Появился снова после «выкопан»: через сколько и на сколько метров от центра.</summary>
    Respawned,

    /// <summary>Подошли к точке, ресурса нет, а копки не видели — выкопал кто-то другой или ещё не появился.</summary>
    Empty,
}

/// <summary>Одно наблюдение за точкой ресурса — строка журнала.</summary>
/// <param name="At">Где ресурс (для «пусто» и «выкопан» — центр точки).</param>
/// <param name="Center">Центр точки на момент события (до уточнения этим появлением).</param>
/// <param name="FromCenter">На сколько метров от центра появился (для «появился» и «в поле зрения»).</param>
/// <param name="SinceDug">Через сколько после копки появился (только «появился»).</param>
/// <param name="HostDistance">Сколько от персонажа до ресурса.</param>
public sealed record SpotEvent(
    DateTime Time, SpotEventKind Kind, string Name, uint ResourceId, Position At, Position Center,
    float FromCenter, TimeSpan? SinceDug, float HostDistance)
{
    /// <summary>Кто выкопал («выкопан»): «мы» — шла наша полоска копания у этого ресурса; «другой»; пусто — сервер полоску не показывает.</summary>
    public string Who { get; init; } = "";

    /// <summary>Что прибавилось в сумке после нашей копки: tid → сколько.</summary>
    public IReadOnlyDictionary<uint, int> Gained { get; init; } = new Dictionary<uint, int>();

    /// <summary>То же для журнала: «tid×сколько», через запятую.</summary>
    public string Loot => string.Join(", ", Gained.Select(g => $"{g.Key}×{g.Value}"));

    /// <summary>Сколько шла наша полоска копания, с.</summary>
    public double? DigSeconds { get; init; }
}
