using System.Windows;
using Microsoft.Win32;
using Notes.App.Panels;

namespace Notes.App;

/// <summary>
/// Окно правки персонажа. Пароль — в <see cref="System.Windows.Controls.PasswordBox"/>: к нему нельзя привязаться, поэтому
/// он переносится в модель здесь. «Сохранить» закрывает окно, только если всё заполнено.
/// </summary>
public partial class CharacterWindow : Window
{
    public CharacterWindow() => InitializeComponent();

    private CharacterDraft Draft => (CharacterDraft)DataContext;

    private void PasswordEdited(object sender, RoutedEventArgs e) => Draft.Password = PasswordInput.Password;

    private void ChooseExe(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Клиент игры для персонажа", Filter = "Клиент игры (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) == true)
            Draft.ExePath = dialog.FileName;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (Draft.Validate())
            DialogResult = true;
    }
}
