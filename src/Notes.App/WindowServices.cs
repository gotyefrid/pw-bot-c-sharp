using System;
using Notes.Core.Notify;
using Notes.Core.Settings;

namespace Notes.App;

/// <summary>
/// Одно на окно, общее у всех вкладок: общие настройки (settings.json) и отправка в Telegram. Освобождается окном после того,
/// как закрыты все вкладки.
/// </summary>
public sealed class WindowServices(string appDirectory) : IDisposable
{
    /// <summary>Папка бота.</summary>
    public string AppDirectory { get; } = appDirectory;

    public AppSettings Settings { get; } = new(appDirectory);

    public TelegramSender Telegram { get; } = new();

    public void Dispose() => Telegram.Dispose();
}
