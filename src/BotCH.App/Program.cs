using System;
using System.Runtime.CompilerServices;

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
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
