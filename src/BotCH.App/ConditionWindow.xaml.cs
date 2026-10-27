using System.Windows;

namespace BotCH.App;

/// <summary>Окно условия кликера: вид, его поля и «НЕ». Правки сразу уходят в настройки — «Готово» только закрывает.</summary>
public partial class ConditionWindow : Window
{
    public ConditionWindow() => InitializeComponent();

    private void Done(object sender, RoutedEventArgs e) => Close();
}
