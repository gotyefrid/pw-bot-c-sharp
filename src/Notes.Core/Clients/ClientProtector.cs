using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Notes.Core.Profiles;

namespace Notes.Core.Clients;

/// <summary>
/// Чем кончилась проверка файла-сторожа клиента (pwprotector) рядом с exe игры.
/// </summary>
public enum ProtectorState
{
    /// <summary>Файл тот же, что проверяли: эталон из профиля совпал.</summary>
    Known,

    /// <summary>Файл изменился — версию не разбирали, что она делает, неизвестно.</summary>
    Changed,

    /// <summary>Файла нет по пути.</summary>
    Missing,

    /// <summary>Файл есть, но не читается (нет прав, занят): что в нём — неизвестно.</summary>
    Unreadable,

    /// <summary>В профиле нет эталона — проверять нечего.</summary>
    NotConfigured,
}

/// <summary>Слепок файла для лога и разбора: что нашли и какой у него sha256.</summary>
public sealed record ProtectorFile(string Path, long Length, DateTime Modified, string Sha256);

/// <summary>
/// Проверка сторожа (<see cref="ProtectorCheck"/>) при подключении: тот ли файл лежит рядом с клиентом, что разобрали
/// и занесли в профиль. Сторож — единственная часть клиента, которую античит может подменить, не трогая exe: пока
/// клиент запущен, файлы рядом заняты, поэтому проверки в момент подключения хватает на весь сеанс бота.
/// </summary>
public static class ProtectorCheck
{
    /// <summary>Положить к пути exe игры: <c>element/ElementClient.exe</c> → <c>element/reportbugs/pwprotector.exe</c>.</summary>
    public static string Resolve(string gameExePath)
        => Path.Combine(Path.GetDirectoryName(gameExePath) ?? "", ProtectorSpec.Folder, ProtectorSpec.FileName);

    /// <summary>Проверить файл сторожа у exe игры. Исключений не бросает: «не читается» — тоже ответ.</summary>
    public static (ProtectorState State, ProtectorFile? File) Check(string? gameExePath, ProtectorSpec? spec)
    {
        if (gameExePath is null || spec is null || !spec.IsConfigured)
            return (ProtectorState.NotConfigured, null);

        var path = Resolve(gameExePath);
        if (!File.Exists(path))
            return (ProtectorState.Missing, null);

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            var text = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                text.Append(b.ToString("x2"));
            var file = new ProtectorFile(path, stream.Length, File.GetLastWriteTime(path), text.ToString());
            var state = string.Equals(file.Sha256, spec.Sha256, StringComparison.OrdinalIgnoreCase)
                ? ProtectorState.Known
                : ProtectorState.Changed;
            return (state, file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (ProtectorState.Unreadable, null);
        }
    }
}
