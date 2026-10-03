using System;
using BotCH.Core.World;
using Newtonsoft.Json;

namespace BotCH.Core.Resources;

/// <summary>
/// Место на карте, где появляется один ресурс («Высохший древесный корень» у этого камня). Ресурс после копки
/// появляется снова на том же месте с разбросом — центр точки уточняется по каждому разу, когда его видели.
/// Точек одного названия на карте много — это разные точки.
/// </summary>
public sealed class ResourceSpot
{
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Height { get; set; }

    /// <summary>Самое дальнее, где ресурс этой точки появлялся от центра, м.</summary>
    public float Spread { get; set; }

    /// <summary>Сколько раз ресурс здесь видели (появлений, а не снимков) — вес центра.</summary>
    public int Seen { get; set; }

    /// <summary>Когда видели последний раз.</summary>
    public DateTime? LastSeen { get; set; }

    /// <summary>Когда ресурс пропал у нас на глазах (выкопали); null — на месте или не знаем.</summary>
    public DateTime? GoneAt { get; set; }

    /// <summary>
    /// Номер ресурса в игре (0 — не знаем). Выкопанный корень появился снова через 10 мин с тем же номером, но в 18 м
    /// от прежнего места (2026-10-03, 1.4.6) — по номеру точка узнаётся надёжнее, чем по расстоянию.
    /// </summary>
    public uint ResourceId { get; set; }

    /// <summary>Добавлена кнопкой «Добавить здесь», а не по увиденному ресурсу.</summary>
    public bool Manual { get; set; }

    [JsonIgnore]
    public Position Position
    {
        get => new(X, Height, Y);
        set => (X, Height, Y) = (value.X, value.Height, value.Y);
    }
}
