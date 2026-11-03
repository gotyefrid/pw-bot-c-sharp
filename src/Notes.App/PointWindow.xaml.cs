using System.Windows;
using Notes.App.Panels;

namespace Notes.App;

/// <summary>Окно правки точки: название и координаты как на миникарте, «Текущие» — где стоит персонаж. «Отмена» ничего не меняет.</summary>
public partial class PointWindow : Window
{
    public PointWindow() => InitializeComponent();

    private void Save(object sender, RoutedEventArgs e)
    {
        if (DataContext is PointDraft draft && draft.Save())
            DialogResult = true;
    }
}
