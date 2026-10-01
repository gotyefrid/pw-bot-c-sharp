using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using BotCH.Core.Settings;

namespace BotCH.App;

/// <summary>true → скрыть, false → показать (обратный BooleanToVisibilityConverter).</summary>
public sealed class InverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Режим списка лута по-русски.</summary>
public sealed class LootModeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        LootListMode.OnlyListed => "только из списка",
        LootListMode.ExceptListed => "всё, кроме списка",
        _ => "всё подряд",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
