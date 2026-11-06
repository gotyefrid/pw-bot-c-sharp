using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Notes.App.Panels;

namespace Notes.App;

/// <summary>Окно «Персонажи». Двойной щелчок или Enter по строке — «Открыть», Delete — «Удалить».</summary>
public partial class CharactersWindow : Window
{
    public CharactersWindow() => InitializeComponent();

    private CharactersPanel Panel => (CharactersPanel)DataContext;

    // Двойной щелчок по строке (не по заголовку и не по пустому месту)
    private void OpenRow(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement((ItemsControl)sender, (DependencyObject)e.OriginalSource) is ListViewItem
            && Panel.OpenCommand.CanExecute(null))
            Panel.OpenCommand.Execute(null);
    }

    private void RowKey(object sender, KeyEventArgs e)
    {
        var command = e.Key switch
        {
            Key.Enter => Panel.OpenCommand,
            Key.Delete => Panel.RemoveCommand,
            _ => null,
        };
        if (command is null || !command.CanExecute(null))
            return;

        command.Execute(null);
        e.Handled = true;
    }
}
