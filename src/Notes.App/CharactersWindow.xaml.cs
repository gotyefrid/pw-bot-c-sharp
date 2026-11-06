using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Notes.App.Panels;

namespace Notes.App;

/// <summary>
/// Окно «Персонажи». Двойной щелчок или Enter по строке — «Изменить», Delete — «Удалить». Клиент запускается только кнопкой
/// «Открыть»: случайный щелчок не должен открывать игру.
/// </summary>
public partial class CharactersWindow : Window
{
    public CharactersWindow() => InitializeComponent();

    private CharactersPanel Panel => (CharactersPanel)DataContext;

    // Двойной щелчок по строке (не по заголовку и не по пустому месту)
    private void EditRow(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement((ItemsControl)sender, (DependencyObject)e.OriginalSource) is ListViewItem
            && Panel.EditCommand.CanExecute(null))
            Panel.EditCommand.Execute(null);
    }

    private void RowKey(object sender, KeyEventArgs e)
    {
        var command = e.Key switch
        {
            Key.Enter => Panel.EditCommand,
            Key.Delete => Panel.RemoveCommand,
            _ => null,
        };
        if (command is null || !command.CanExecute(null))
            return;

        command.Execute(null);
        e.Handled = true;
    }
}
