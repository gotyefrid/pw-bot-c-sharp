using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BotCH.App.Mvvm;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.App.Panels;

/// <summary>
/// Кого звать петом — по среде (земля, воздух, вода), если сервер говорит, где питомцы живут; иначе — номер клетки, как раньше.
/// Выбор — в настройках персонажа; о правке говорит модели окна.
/// </summary>
public sealed class PetsPanel : ObservableObject
{
    private readonly Func<BotSettings> _settings;
    private readonly Action _edited;
    private bool _hasPets = true;
    private bool _knowsPetHabitats;
    private bool _syncing;

    /// <param name="settings">Настройки текущего персонажа.</param>
    /// <param name="edited">Правка выбора пета.</param>
    public PetsPanel(Func<BotSettings> settings, Action edited)
    {
        _settings = settings;
        _edited = edited;
    }

    private BotSettings Settings => _settings();

    /// <summary>Пункт списка «кого звать»: значение — название питомца, пусто — «авто».</summary>
    public sealed record PetChoice(string Value, string Title);

    /// <summary>Есть ли у персонажа петы вообще (не друид — нет). Пока неизвестно — считаем, что есть.</summary>
    public bool HasPets { get => _hasPets; private set => SetProperty(ref _hasPets, value); }

    /// <summary>Сервер говорит, где питомцы живут: выбор питомцами; иначе — номер клетки, как раньше.</summary>
    public bool KnowsPetHabitats { get => _knowsPetHabitats; private set => SetProperty(ref _knowsPetHabitats, value); }

    /// <summary>Кого звать на земле: «авто» и питомцы, которые живут на земле.</summary>
    public ObservableCollection<PetChoice> GroundPets { get; } = new();

    /// <summary>Кого звать в воздухе: «авто» и питомцы, которые летают.</summary>
    public ObservableCollection<PetChoice> AirPets { get; } = new();

    /// <summary>Кого звать в воде: «авто» и питомцы, которые живут в воде.</summary>
    public ObservableCollection<PetChoice> WaterPets { get; } = new();

    public string GroundPet
    {
        get => Settings.Pet.GroundPet;
        set => SetPet(value, () => Settings.Pet.GroundPet, v => Settings.Pet.GroundPet = v);
    }

    public string AirPet
    {
        get => Settings.Pet.AirPet;
        set => SetPet(value, () => Settings.Pet.AirPet, v => Settings.Pet.AirPet = v);
    }

    public string WaterPet
    {
        get => Settings.Pet.WaterPet;
        set => SetPet(value, () => Settings.Pet.WaterPet, v => Settings.Pet.WaterPet = v);
    }

    /// <summary>Выбор в окне ← настройки (сменился персонаж, пересобрались списки).</summary>
    public void Rebind()
    {
        OnPropertyChanged(nameof(GroundPet));
        OnPropertyChanged(nameof(AirPet));
        OnPropertyChanged(nameof(WaterPet));
    }

    /// <summary>Списки питомцев ← клетки (пересобираются, только когда что-то поменялось: питомцы, названия, «авто»).</summary>
    public void Observe(WorldState w)
    {
        HasPets = w.Pet is not null;
        var cages = w.Pet?.Cages ?? [];
        KnowsPetHabitats = cages.Any(p => p.Habitat is not null);
        if (!KnowsPetHabitats)
            return;

        Sync(GroundPets, Choices(cages, PetHabitat.Ground, Settings.Pet.GroundPet), nameof(GroundPet));
        Sync(AirPets, Choices(cages, PetHabitat.Air, Settings.Pet.AirPet), nameof(AirPet));
        Sync(WaterPets, Choices(cages, PetHabitat.Water, Settings.Pet.WaterPet), nameof(WaterPet));
    }

    // Пересборка списка сбрасывает выбор — это не выбор пользователя
    private void SetPet(string? value, Func<string> get, Action<string> set)
    {
        if (_syncing || value is null || value == get())
            return;
        set(value);
        _edited();
    }

    private static List<PetChoice> Choices(IReadOnlyList<PetInCage> cages, PetHabitat where, string chosen)
    {
        var fit = cages.Where(p => p.Lives(where) && p.Name is not null).OrderBy(p => p.Cage).ToList();
        var auto = fit.FirstOrDefault() is { } first ? $"авто — {first.Name} (клетка {first.Cage})" : "авто — подходящего нет";
        var list = new List<PetChoice> { new("", auto) };
        list.AddRange(fit.Select(p => new PetChoice(p.Name!, $"{p.Name} (клетка {p.Cage})")));
        if (chosen.Length > 0 && !list.Any(c => string.Equals(c.Value, chosen, StringComparison.OrdinalIgnoreCase)))
            list.Add(new PetChoice(chosen, $"{chosen} — нет в клетках"));
        return list;
    }

    private void Sync(ObservableCollection<PetChoice> target, List<PetChoice> choices, string property)
    {
        if (choices.SequenceEqual(target))
            return;

        _syncing = true;
        try
        {
            target.Clear();
            foreach (var choice in choices)
                target.Add(choice);
        }
        finally
        {
            _syncing = false;
        }

        OnPropertyChanged(property);
    }
}
