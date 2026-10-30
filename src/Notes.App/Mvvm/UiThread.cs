using System;
using System.Windows;

namespace Notes.App.Mvvm;

/// <summary>Сделать в потоке окна: уже в нём — сразу, из фона — в очередь окна (снимки, бот, фоновые проверки).</summary>
public static class UiThread
{
    public static void Run(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }
}
