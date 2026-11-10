using System.Windows;
using Notes.App.Panels;

namespace Notes.App;

/// <summary>Окно правки точки маршрута: координаты, «в воздухе», радиус и что копать. «Отмена» ничего не меняет.</summary>
public partial class RoutePointWindow : Window
{
    public RoutePointWindow() => InitializeComponent();

    private void Save(object sender, RoutedEventArgs e)
    {
        if (DataContext is RoutePointDraft draft && draft.Save())
            DialogResult = true;
    }
}
