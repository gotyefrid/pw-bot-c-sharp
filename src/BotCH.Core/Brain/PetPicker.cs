using System;
using System.Linq;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>
/// Кого из питомцев звать: персонаж летит — того, кто живёт в воздухе, иначе — того, кто живёт на земле (наземного в
/// воздухе сервер не призовёт). По названию из настроек, а если оно пустое — первого подходящего по порядку клеток.
/// Сервер не говорит, где питомцы живут (поля не найдены), — как раньше, по номеру клетки.
/// </summary>
public static class PetPicker
{
    public static PetInCage? Pick(PetState pet, PetSettings settings, bool inAir, out string? problem)
    {
        problem = null;
        if (pet.Cages.All(p => p.Habitat is null))
        {
            var byCage = pet.InCage(settings.Cage);
            if (byCage is null)
                problem = $"в клетке {settings.Cage} нет пета";
            return byCage;
        }

        var where = inAir ? "в воздухе" : "на земле";
        var name = inAir ? settings.AirPet : settings.GroundPet;
        if (name.Length > 0)
        {
            var named = pet.Cages.FirstOrDefault(p => string.Equals(p.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (named is null)
                problem = $"питомца «{name}» ({where}) нет в клетках";
            return named;
        }

        var habitat = Habitat(inAir);
        var first = pet.Cages.Where(p => p.Lives(habitat)).OrderBy(p => p.Cage).FirstOrDefault();
        if (first is null)
            problem = $"нет питомца, которого можно призвать {where}";
        return first;
    }

    public static PetHabitat Habitat(bool inAir) => inAir ? PetHabitat.Air : PetHabitat.Ground;
}
