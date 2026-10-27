using System.Windows;

namespace BotCH.App;

/// <summary>Окно шага кликера: что сделать и при каком условии. Правки сразу уходят в настройки — «Готово» только закрывает.</summary>
public partial class StepWindow : Window
{
    public StepWindow() => InitializeComponent();

    private void Done(object sender, RoutedEventArgs e) => Close();
}
