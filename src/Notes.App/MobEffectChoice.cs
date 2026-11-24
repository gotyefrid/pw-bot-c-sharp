using System;
using System.Collections.Generic;
using System.Linq;
using Notes.App.Mvvm;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.App;

/// <summary>
/// Галочка «бить первыми» у эффекта моба (<see cref="TargetSettings.FirstEffects"/>). Настройки берёт каждый раз заново: у
/// персонажа их меняют при смене ника — тогда <see cref="Rebind"/>.
/// </summary>
public sealed class MobEffectChoice(int id, string name, Func<BotSettings> settings) : ObservableObject
{
    public int Id { get; } = id;

    public string Name { get; } = name;

    public bool IsChecked
    {
        get => settings().Target.FirstEffects.Contains(Id);
        set
        {
            var target = settings().Target;
            if (target.FirstEffects.Contains(Id) == value)
                return;
            // Новый список, а не правка старого: так настройки замечают правку и сохраняют её
            target.FirstEffects = value ? [.. target.FirstEffects, Id] : target.FirstEffects.Where(e => e != Id).ToList();
            OnPropertyChanged();
        }
    }

    public void Rebind() => OnPropertyChanged(nameof(IsChecked));

    public static IReadOnlyList<MobEffectChoice> All(Func<BotSettings> settings)
        => MobEffects.All.Select(e => new MobEffectChoice(e.Id, e.Name, settings)).ToList();
}
