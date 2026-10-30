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

    /// <summary>
    /// Выполнить чуть позже, когда окно закончит текущее обновление (привязки, перестройку списков): то, что сказано окну
    /// посреди его же обновления, оно пропускает.
    /// </summary>
    public static void Later(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            action();
        else
            dispatcher.BeginInvoke(action, System.Windows.Threading.DispatcherPriority.DataBind);
    }
}
