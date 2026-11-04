using System.Collections.Generic;

namespace Notes.Core.World;

/// <summary>Пати персонажа: лидер и участники (сам персонаж — тоже в списке). Нет пати — <see cref="None"/>.</summary>
public sealed record PartyInfo(uint LeaderWid, IReadOnlyList<PartyMember> Members)
{
    public static readonly PartyInfo None = new(0, []);

    public bool InParty => Members.Count > 0;
}

/// <summary>
/// Участник пати. HP и MP игра знает про всех участников (не только про выделенного, как у игроков вокруг). Координаты — если
/// игра их знает (у самого персонажа в этой записи их нет — <c>null</c>).
/// </summary>
public sealed record PartyMember(uint Wid, string Name, int Level, int Hp, int MaxHp, int Mp, int MaxMp, Position? Position)
{
    /// <summary>Класс, номер как в игре (<see cref="PlayerClasses"/>); null — поле не найдено для сервера.</summary>
    public int? Class { get; init; }
}
