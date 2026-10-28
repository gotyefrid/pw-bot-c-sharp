using System.Windows;

namespace Notes.App;

/// <summary>Фильтры лога в окне: «только важное» и источники записей. Выбор сразу применяется и сохраняется — «Готово» только закрывает.</summary>
public partial class LogFilterWindow : Window
{
    public LogFilterWindow() => InitializeComponent();

    private void Done(object sender, RoutedEventArgs e) => Close();
}
