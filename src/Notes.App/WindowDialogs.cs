using System.Windows;
using Microsoft.Win32;
using Notes.App.Mvvm;

namespace Notes.App;

/// <summary>Диалоги поверх окна бота: MessageBox и обычные окна Windows «Открыть» и «Сохранить как».</summary>
public sealed class WindowDialogs(Window owner) : IDialogs
{
    private const string Caption = "Заметки";

    public bool AskYesNo(string text)
        => MessageBox.Show(owner, text, Caption, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Tell(string text) => MessageBox.Show(owner, text, Caption, MessageBoxButton.OK, MessageBoxImage.Error);

    public string? AskOpenPath(string title, string filter) => Ask(new OpenFileDialog { Title = title, Filter = filter });

    public string? AskSavePath(string title, string filter, string fileName)
        => Ask(new SaveFileDialog { Title = title, Filter = filter, FileName = fileName });

    private string? Ask(FileDialog dialog) => dialog.ShowDialog(owner) == true ? dialog.FileName : null;
}
