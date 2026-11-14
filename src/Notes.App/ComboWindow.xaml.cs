using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Notes.App.Panels;

namespace Notes.App;

/// <summary>Окно «Связка скиллов». Правки сразу уходят в общий файл связок и персонажу — «Готово» только закрывает.</summary>
public partial class ComboWindow : Window
{
    // Под полосу прокрутки и рамки ячеек: иначе последняя колонка вылезает за край и появляется прокрутка вбок
    private const double Reserve = 32;

    public ComboWindow() => InitializeComponent();

    private void Done(object sender, RoutedEventArgs e) => Close();

    // Двойной щелчок по строке (не по заголовку, не по пустому месту и не по галочке «вкл») — окно строки
    private void EditRow(object sender, MouseButtonEventArgs e)
    {
        var source = (DependencyObject)e.OriginalSource;
        if (ItemsControl.ContainerFromElement((ItemsControl)sender, source) is not ListViewItem || InToggle(source))
            return;
        if (DataContext is ComboPanel panel)
            panel.EditSelected();
    }

    private static bool InToggle(DependencyObject? element)
    {
        for (; element is not null and not ListViewItem; element = element is Visual ? VisualTreeHelper.GetParent(element) : null)
        {
            if (element is ToggleButton)
                return true;
        }
        return false;
    }

    // «Когда» — вся ширина, что осталась от других колонок
    private void TableSized(object sender, SizeChangedEventArgs e)
    {
        if (Table.View is not GridView grid)
            return;
        var others = grid.Columns.Where(c => c != WhenColumn).Sum(c => double.IsNaN(c.Width) ? c.ActualWidth : c.Width);
        WhenColumn.Width = Math.Max(120, Table.ActualWidth - others - Reserve);
    }
}
