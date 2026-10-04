using System;
using System.Collections.Generic;
using BotCH.Core.World;
using Newtonsoft.Json;

namespace BotCH.Core.Resources;

/// <summary>
/// Участок на карте, где появляется один ресурс. После копки он появляется снова через 10 мин 15 с с тем же номером,
/// но в случайном месте участка: от прошлого места обычно ~30 м, бывает до ~110 м (журнал 2026-10-03, оба сервера,
/// 116 возрождений). Центр — среднее появлений. Участков одного названия на карте много — это разные точки.
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

    /// <summary>Когда видели последний раз (UTC, как время снимка).</summary>
    public DateTime? LastSeen { get; set; }

    /// <summary>
    /// Когда ресурс пропал у нас на глазах (выкопали), по серверам (id профиля → время UTC). У каждого сервера свой мир:
    /// копка на 1.3.6 ничего не значит для 1.4.6. Нет записи — на месте или не знаем.
    /// </summary>
    public Dictionary<string, DateTime> Gone { get; set; } = [];

    /// <summary>Когда ресурс выкопали на этом сервере; null — на месте или не знаем.</summary>
    public DateTime? GoneOn(string server) => Gone.TryGetValue(server, out var time) ? time : null;

    /// <summary>
    /// Номер ресурса в игре по серверам (id профиля → номер). После копки ресурс появляется с тем же номером (111 из 111),
    /// но в другом месте участка — по номеру точка узнаётся надёжнее, чем по расстоянию.
    /// У каждого сервера номера свои: тот же корень на 1.3.6 — 0xC0100AE4, на 1.4.6 — 0xC0100E80.
    /// </summary>
    public Dictionary<string, uint> Ids { get; set; } = [];

    /// <summary>Добавлена кнопкой «Добавить здесь», а не по увиденному ресурсу.</summary>
    public bool Manual { get; set; }

    [JsonIgnore]
    public Position Position
    {
        get => new(X, Height, Y);
        set => (X, Height, Y) = (value.X, value.Height, value.Y);
    }
}
