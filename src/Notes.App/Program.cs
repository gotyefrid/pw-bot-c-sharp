using System;
using System.Runtime.CompilerServices;
using System.Windows;

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
        // Копий из одной папки — сколько угодно: настройки у каждого персонажа свои (characters\<сервер>\Ник.json), а один
        // клиент двумя окнами не занять (ClientLock)
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
