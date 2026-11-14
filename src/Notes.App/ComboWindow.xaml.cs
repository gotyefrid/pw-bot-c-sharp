using System.Windows;

namespace Notes.App;

/// <summary>Окно «Связка скиллов». Правки сразу уходят в общий файл связок и персонажу — «Готово» только закрывает.</summary>
public partial class ComboWindow : Window
{
    public ComboWindow() => InitializeComponent();

    private void Done(object sender, RoutedEventArgs e) => Close();
}
