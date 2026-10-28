using System;
using System.IO;
using Notes.Core.Logging;

namespace Notes.Core.Resources;

/// <summary>
/// Общая папка всех копий приложения в %AppData%: блокнот ресурсов и журнал их появления. Папку под прежним именем
/// первый запуск новой версии переносит сюда целиком.
/// </summary>
public static class SharedFolder
{
    public const string Name = "Notes";

    // Прежнее имя папки — у тех, кто обновился со старой версии
    internal const string OldName = "BotCH";

    /// <summary>
    /// Папка внутри <paramref name="appData"/>. Перенести не вышло (файлы держит копия старой версии) — пока работаем
    /// в старой, чтобы точки не разошлись по двум папкам; перенос повторится при следующем запуске.
    /// </summary>
    public static string Prepare(string appData, ILogger log)
    {
        var folder = Path.Combine(appData, Name);
        var old = Path.Combine(appData, OldName);
        if (!Directory.Exists(old) || Directory.Exists(folder))
            return folder;

        try
        {
            Directory.Move(old, folder);
            log.Info($"Блокнот ресурсов перенесён в {folder}");
            return folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Другая копия, запущенная одновременно, уже перенесла
            if (Directory.Exists(folder))
                return folder;

            log.Warning($"Блокнот ресурсов не перенесён из {old}: {e.Message} Пока работаем со старой папкой");
            return old;
        }
    }
}
