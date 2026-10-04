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

/// <summary>Вкладка (int) == параметр → показать.</summary>
public sealed class TabVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => Equals(value?.ToString(), parameter?.ToString()) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Кнопка вкладки: отмечена, если выбрана её вкладка; при нажатии выбирает её.</summary>
public sealed class TabCheckedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => Equals(value?.ToString(), parameter?.ToString());

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? int.Parse((string)parameter, CultureInfo.InvariantCulture) : Binding.DoNothing;
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
