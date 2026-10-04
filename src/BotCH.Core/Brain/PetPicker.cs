using System;
using System.Linq;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>
/// Кого из питомцев звать: персонаж летит — того, кто живёт в воздухе, в воде — водного, иначе — наземного (не того
/// сервер не призовёт). По названию из настроек, а если оно пустое — первого подходящего по порядку клеток.
/// Сервер не говорит, где питомцы живут (поля не найдены), — как раньше, по номеру клетки.
/// </summary>
public static class PetPicker
{
    public static PetInCage? Pick(PetState pet, PetSettings settings, PetHabitat where, out string? problem)
    {
        problem = null;
        if (pet.Cages.All(p => p.Habitat is null))
        {
            var byCage = pet.InCage(settings.Cage);
            if (byCage is null)
                problem = $"в клетке {settings.Cage} нет пета";
            return byCage;
        }

        var text = Text(where);
        var name = where switch
        {
            PetHabitat.Air => settings.AirPet,
            PetHabitat.Water => settings.WaterPet,
            _ => settings.GroundPet,
        };
        if (name.Length > 0)
        {
            var named = pet.Cages.FirstOrDefault(p => string.Equals(p.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (named is null)
                problem = $"питомца «{name}» ({text}) нет в клетках";
            return named;
        }

        var first = pet.Cages.Where(p => p.Lives(where)).OrderBy(p => p.Cage).FirstOrDefault();
        if (first is null)
            problem = $"нет питомца, которого можно призвать {text}";
        return first;
    }

    /// <summary>Где персонаж: летит — воздух, в воде — вода, иначе (и если не знаем) — земля.</summary>
    public static PetHabitat Where(HostState host)
        => host.Flying == true ? PetHabitat.Air : host.InWater == true ? PetHabitat.Water : PetHabitat.Ground;

    public static string Text(PetHabitat where) => where switch
    {
        PetHabitat.Air => "в воздухе",
        PetHabitat.Water => "в воде",
        _ => "на земле",
    };
}
