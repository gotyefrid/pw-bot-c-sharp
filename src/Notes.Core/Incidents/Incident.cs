using System;
using System.Collections.Generic;
using Notes.Core.World;

namespace Notes.Core.Incidents;

/// <summary>Что случилось: строка, как в логе («Вас убил …»), и снимок мира в этот момент.</summary>
public sealed record IncidentEvent(DateTime Time, string Text, WorldState World);

/// <summary>
/// Записанный случай (смерть персонажа, гибель пета): события и снимки мира до первого и после последнего — по порядку,
/// без пропусков. Снимки неизменяемые: случай держит их самих, а не копии.
/// </summary>
/// <param name="Snapshots">Все снимки от начала записи до конца, старые первыми.</param>
public sealed record Incident(IReadOnlyList<IncidentEvent> Events, IReadOnlyList<WorldState> Snapshots)
{
    public DateTime Start => Snapshots.Count > 0 ? Snapshots[0].Time : Events[0].Time;

    public DateTime End => Snapshots.Count > 0 ? Snapshots[Snapshots.Count - 1].Time : Events[Events.Count - 1].Time;
}
