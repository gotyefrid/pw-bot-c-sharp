using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace BotCH.App;

public partial class App : Application
{
    public App()
    {
        // Ошибка в окне не должна молча закрывать бота: пишем в logs\crash-*.txt и показываем сообщение
        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrash(e.ExceptionObject as Exception);
    }

    private static void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var file = WriteCrash(e.Exception);
        MessageBox.Show(
            $"Ошибка в окне бота: {e.Exception.Message}\n\nПодробности: {file}\n\nБот продолжит работу, но лучше его перезапустить.",
            "BotCH", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static string WriteCrash(Exception? error)
    {
        try
        {
            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, $"crash-{DateTime.Now:yyyy-MM-dd-HHmmss}.txt");
            File.WriteAllText(file, error?.ToString() ?? "неизвестная ошибка", new UTF8Encoding(true));
            return file;
        }
        catch (Exception)
        {
            return "(не удалось записать файл)";
        }
    }
}
