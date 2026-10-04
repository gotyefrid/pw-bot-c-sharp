using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BotCH.Core.Settings;

/// <summary>
/// Часть настроек, которая сама сообщает об изменениях (INotifyPropertyChanged — это System.ComponentModel, не WPF). Окно
/// привязано к полям напрямую и узнаёт о правке отсюда, а не из событий своих контейнеров; правка тем же значением — не правка.
/// </summary>
public abstract class SettingsPart : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        Changed(name);
    }

    protected void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
