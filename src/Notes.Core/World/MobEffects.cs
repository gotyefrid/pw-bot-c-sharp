using System.Collections.Generic;
using System.Linq;

namespace Notes.Core.World;

/// <summary>
/// Эффект моба — надпись под рамкой цели («Усиленная атака», «Берсерк»): номер в структуре моба (<see cref="NpcInfo.Effect"/>).
/// Названия — как у клиента в interfaces\ingame.stf под номером 340 + эффект (344 «Усиленная магическая защита», 345 «Усиленная
/// атака»). Проверено 24.11 на Comeback 1.4.6: эффекты 4 и 5 — как в окне цели; остальные — только по ingame.stf.
/// </summary>
public static class MobEffects
{
    /// <summary>Эффект и название, как под рамкой цели; 10 в ingame.stf нет.</summary>
    public static IReadOnlyList<(int Id, string Name)> All { get; } =
    [
        (1, "Ускорение"),
        (2, "Монолог"),
        (3, "Усиленная защита"),
        (4, "Усиленная магическая защита"),
        (5, "Усиленная атака"),
        (6, "Усиленная магия"),
        (7, "Берсерк"),
        (8, "Улучшенное здоровье"),
        (9, "Слабость"),
        (11, "Физический иммунитет"),
        (12, "Иммунитет к металлам"),
        (13, "Иммунитет к дереву"),
        (14, "Иммунитет к воде"),
        (15, "Иммунитет к огню"),
        (16, "Иммунитет к земле"),
        (17, "Иммунитет к магии"),
    ];

    /// <summary>Усиленная атака.</summary>
    public const int StrongAttack = 5;

    /// <summary>Берсерк.</summary>
    public const int Berserk = 7;

    /// <summary>Название эффекта; 0 — эффекта нет (null); неизвестный номер — «эффект N».</summary>
    public static string? Name(int effect)
        => effect == 0 ? null : All.FirstOrDefault(e => e.Id == effect).Name ?? $"эффект {effect}";
}
