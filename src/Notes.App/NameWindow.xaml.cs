using System.Windows;

namespace Notes.App;

/// <summary>Окно «введите название»: текущее название выделено, «Сохранить» — новое, «Отмена» — null.</summary>
public partial class NameWindow : Window
{
    public NameWindow() => InitializeComponent();

    public static string? Ask(Window? owner, string title, string current)
    {
        var window = new NameWindow { Owner = owner, Title = title };
        window.NameBox.Text = current;
        window.Loaded += (_, _) =>
        {
            window.NameBox.Focus();
            window.NameBox.SelectAll();
        };
        return window.ShowDialog() == true ? window.NameBox.Text : null;
    }

    private void Save(object sender, RoutedEventArgs e) => DialogResult = true;
}
