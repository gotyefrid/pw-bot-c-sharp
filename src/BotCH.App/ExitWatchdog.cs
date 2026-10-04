using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;
using BotCH.Core.Logging;

namespace BotCH.App;

/// <summary>
/// Страховка выхода. Окно закрыто, бот отключён, точки сохранены, а процесс не вышел за 3 с — так бывало при «Закрыть окно»
/// из панели задач: выход WPF стоял в очереди диспетчера, а до главного потока не доходило ничего (04.10 толчок не помог,
/// главный поток ждал в GetMessage — теперь окно останавливает диспетчер на месте, см. MainWindow.OnClosed).
/// Сначала толкаем диспетчер новой операцией; не вышли и через 2 с — пишем в лог, на чём стоит главный поток, и выходим
/// принудительно. Если главный поток уже завершён, держит другой (не фоновый) поток — это тоже будет в логе.
/// </summary>
internal static class ExitWatchdog
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan AfterKick = TimeSpan.FromSeconds(2);

    /// <param name="ui">Главный поток (диспетчер окна) — его стек в лог, если выход так и не прошёл.</param>
    public static void Start(Dispatcher dispatcher, Thread ui, ILogger log)
        => new Thread(() => Watch(dispatcher, ui, log)) { IsBackground = true, Name = "BotCH: выход" }.Start();

    private static void Watch(Dispatcher dispatcher, Thread ui, ILogger log)
    {
        Thread.Sleep(Wait);
        log.Warning($"Выход не прошёл за {Wait.TotalSeconds:0} с (диспетчер: {State(dispatcher)}) — толкаю диспетчер");
        dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => { }));

        Thread.Sleep(AfterKick);
        // Снять стек — остановить главный поток; если и это повиснет, выход всё равно будет
        new Thread(() =>
        {
            Thread.Sleep(AfterKick);
            Environment.Exit(0);
        }) { IsBackground = true, Name = "BotCH: выход наверняка" }.Start();
        log.Error($"Выход не прошёл и после толчка (диспетчер: {State(dispatcher)}) — выхожу принудительно. Главный поток:\n{StackOf(ui)}");
        Environment.Exit(0);
    }

    private static string State(Dispatcher dispatcher)
        => dispatcher.HasShutdownFinished ? "остановлен" : dispatcher.HasShutdownStarted ? "останавливается" : "работает";

    // Стек чужого потока в .NET Framework — только остановив его (устаревшее API, но для разбора зависания на выходе годится)
    private static string StackOf(Thread thread)
    {
#pragma warning disable CS0618
        try
        {
            if (!thread.IsAlive)
                return "главный поток уже завершён — держит другой (не фоновый) поток";
            thread.Suspend();
            try
            {
                return new StackTrace(thread, needFileInfo: false).ToString();
            }
            finally
            {
                thread.Resume();
            }
        }
        catch (Exception e) when (e is ThreadStateException or ThreadInterruptedException or System.Security.SecurityException)
        {
            return "стек не снять: " + e.Message;
        }
#pragma warning restore CS0618
    }
}
