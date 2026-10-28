using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using Notes.Core.Clients;

namespace Notes.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // До первого обращения к типам из вшитых dll
        EmbeddedAssemblies.Register();
        Run();
    }

    // Отдельный метод: JIT компилирует его уже после регистрации загрузчика
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        // Из одной папки — один бот: у копии свои настройки и персонажи. Второй клиент — копия папки с ботом
        var exe = typeof(Program).Assembly.Location;
        var folder = Path.GetDirectoryName(exe)!.ToLowerInvariant().Replace('\\', '/');
        using var single = new Mutex(initiallyOwned: false, MutexNames.For("app", folder), out var first);
        if (!first)
        {
            // Имя exe — как у запущенного файла: его могли переименовать
            MessageBox.Show(
                $"Программа из этой папки уже запущена.\n\nДля второго клиента скопируйте папку с программой и запустите {Path.GetFileName(exe)} из копии — у неё будут свои настройки.",
                "Заметки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
