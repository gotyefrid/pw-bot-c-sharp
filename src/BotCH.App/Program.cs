using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using BotCH.Core.Clients;

namespace BotCH.App;

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
        var folder = Path.GetDirectoryName(typeof(Program).Assembly.Location)!.ToLowerInvariant().Replace('\\', '/');
        using var single = new Mutex(initiallyOwned: false, MutexNames.For("app", folder), out var first);
        if (!first)
        {
            MessageBox.Show(
                "Программа из этой папки уже запущена.\n\nДля второго клиента скопируйте папку с программой и запустите Notes.exe из копии — у неё будут свои настройки.",
                "Заметки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
