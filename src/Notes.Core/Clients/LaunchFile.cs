using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Notes.Core.Clients;

/// <summary>
/// Как запускается клиент по файлу запуска (.bat/.cmd или ярлык .lnk): exe, рабочая папка и аргументы командной строки.
/// Логин и пароль остаются только в памяти: <see cref="ToString"/> пароль не показывает. В репозиторий и логи они не
/// попадают — файл запуска читается при каждом запуске клиента.
/// </summary>
public sealed class ClientLaunch
{
    private const string PasswordKey = "pwd";

    public ClientLaunch(string sourcePath, string exePath, string workingDirectory, IReadOnlyList<string> arguments)
    {
        SourcePath = sourcePath;
        ExePath = exePath;
        WorkingDirectory = workingDirectory;
        Arguments = arguments;
        Login = Value("user");
        Role = Value("role");
        HasPassword = Value(PasswordKey) is not null;
    }

    /// <summary>Откуда прочитана команда (bat или ярлык).</summary>
    public string SourcePath { get; }

    /// <summary>Exe клиента, как его запускает файл.</summary>
    public string ExePath { get; }

    /// <summary>Рабочая папка клиента (рядом с exe: оттуда он берёт файлы игры).</summary>
    public string WorkingDirectory { get; }

    /// <summary>Аргументы как есть; в них может быть пароль, поэтому наружу целиком не печатаем.</summary>
    public IReadOnlyList<string> Arguments { get; }

    public string? Login { get; }

    public string? Role { get; }

    public bool HasPassword { get; }

    /// <summary>Значение аргумента вида <c>ключ:значение</c>; null — такого аргумента нет.</summary>
    public string? Value(string key)
    {
        foreach (var argument in Arguments)
        {
            var colon = argument.IndexOf(':');
            if (colon > 0 && argument.Substring(0, colon).Equals(key, StringComparison.OrdinalIgnoreCase))
                return argument.Substring(colon + 1);
        }

        return null;
    }

    /// <summary>Запустить клиент. Пароль уходит в командную строку процесса, но нигде не сохраняется.</summary>
    public Process Start()
    {
        if (!File.Exists(ExePath))
            throw new FileNotFoundException($"Нет exe клиента: {ExePath}", ExePath);

        var info = new ProcessStartInfo(ExePath, ArgumentsLine(Arguments))
        {
            WorkingDirectory = WorkingDirectory,
            UseShellExecute = false,
        };
        return Process.Start(info) ?? throw new InvalidOperationException("Windows не запустила клиент");
    }

    /// <summary>Аргументы одной строкой, как их ждёт Windows: с кавычками вокруг пробелов и кавычек.</summary>
    internal static string ArgumentsLine(IEnumerable<string> arguments) => string.Join(" ", arguments.Select(Quote));

    /// <summary>Один аргумент по правилам разбора командной строки Windows (обратные слэши перед кавычкой удваиваются).</summary>
    internal static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            return argument;

        var text = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                slashes++;
            }
            else if (c == '"')
            {
                text.Append('\\', slashes * 2 + 1).Append('"');
                slashes = 0;
            }
            else
            {
                text.Append('\\', slashes).Append(c);
                slashes = 0;
            }
        }

        text.Append('\\', slashes * 2).Append('"');
        return text.ToString();
    }

    /// <summary>Для лога: логин и пароль не печатаем — только есть они или нет.</summary>
    public override string ToString()
        => $"{ExePath} (роль {Role ?? "—"}, логин {(Login is null ? "—" : "есть")}, пароль {(HasPassword ? "есть" : "нет")})";
}

/// <summary>
/// Чтение файла запуска клиента. Bat бывает в UTF-8 (так сохранён «Тихоня 136») или в 1251 (старые файлы);
/// командная строка Windows читает bat в 866 и портит кириллицу, поэтому строку с ролью берём из файла сами.
/// </summary>
public static class LaunchFile
{
    public static ClientLaunch Read(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new FileNotFoundException("Файл запуска не найден", full);

        if (IsExtension(full, ".lnk"))
        {
            var (target, arguments, workingDirectory) = Shortcut.Resolve(full);
            if (target.Length == 0)
                throw new InvalidDataException($"Ярлык никуда не ведёт: {full}");

            if (!Path.IsPathRooted(target))
                target = Path.Combine(Path.GetDirectoryName(full)!, target);
            target = Path.GetFullPath(target);

            return IsBat(target)
                ? ReadBat(target, workingDirectory, arguments)
                : new ClientLaunch(full, target,
                    string.IsNullOrWhiteSpace(workingDirectory) ? DirectoryOf(target) : Path.GetFullPath(workingDirectory),
                    Split(arguments));
        }

        return IsBat(full) ? ReadBat(full, null, null) : new ClientLaunch(full, full, DirectoryOf(full), []);
    }

    private static ClientLaunch ReadBat(string path, string? shortcutWorkingDirectory, string? shortcutArguments)
    {
        var text = Decode(ReadAllBytes(path));
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('@').Trim();
            if (line.Length == 0 || line.StartsWith("::", StringComparison.Ordinal)
                                 || line.StartsWith("rem ", StringComparison.OrdinalIgnoreCase))
                continue;

            var tokens = Split(line);
            var start = tokens.FindIndex(t => t.Equals("start", StringComparison.OrdinalIgnoreCase));
            if (start < 0)
                continue;

            var index = start + 1;
            while (index < tokens.Count && tokens[index].Length == 0)
                index++;

            // Ключи start (/wait, /d папка, /min…) — не аргументы клиента
            while (index < tokens.Count && tokens[index].StartsWith("/", StringComparison.Ordinal))
            {
                var takesValue = tokens[index].Equals("/d", StringComparison.OrdinalIgnoreCase);
                index += takesValue ? 2 : 1;
            }

            if (index >= tokens.Count)
                continue;

            // start "заголовок" exe — первый токен бывает заголовком окна
            var exeToken = tokens[index];
            if (!IsExtension(exeToken, ".exe") && index + 1 < tokens.Count && IsExtension(tokens[index + 1], ".exe"))
                exeToken = tokens[++index];

            if (!IsExtension(exeToken, ".exe"))
                continue;

            var arguments = tokens.Skip(index + 1).ToList();
            if (!string.IsNullOrWhiteSpace(shortcutArguments))
                arguments.AddRange(Split(shortcutArguments!));

            var batDirectory = DirectoryOf(path);
            var exePath = Path.IsPathRooted(exeToken) ? exeToken : Path.Combine(batDirectory, exeToken);
            var workingDirectory = string.IsNullOrWhiteSpace(shortcutWorkingDirectory)
                ? batDirectory
                : Path.GetFullPath(shortcutWorkingDirectory!);
            return new ClientLaunch(path, Path.GetFullPath(exePath), workingDirectory, arguments);
        }

        throw new InvalidDataException($"В файле нет команды запуска клиента (.exe): {path}");
    }

    private static byte[] ReadAllBytes(string path)
    {
        // Файл может держать другая копия бота или сам клиент — читаем, не запрещая запись
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>UTF-8 с BOM или без, UTF-16, иначе 1251 — так сохранены файлы запуска на русской Windows.</summary>
    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1251).GetString(bytes);
        }
    }

    /// <summary>Разбор строки на слова: кавычки убираются, пробелы внутри кавычек — часть слова.</summary>
    private static List<string> Split(string line)
    {
        var result = new List<string>();
        var word = new StringBuilder();
        var quoted = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && char.IsWhiteSpace(c))
            {
                if (word.Length > 0)
                {
                    result.Add(word.ToString());
                    word.Clear();
                }
            }
            else
            {
                word.Append(c);
            }
        }

        if (word.Length > 0)
            result.Add(word.ToString());
        return result;
    }

    private static bool IsBat(string path) => IsExtension(path, ".bat") || IsExtension(path, ".cmd");

    private static bool IsExtension(string path, string extension)
        => string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);

    private static string DirectoryOf(string path) => Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";

    /// <summary>Ярлык Windows читается через WScript.Shell — того же, чем Windows открывает .lnk.</summary>
    private static class Shortcut
    {
        public static (string Target, string Arguments, string WorkingDirectory) Resolve(string path)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell")
                       ?? throw new InvalidOperationException("Windows не умеет читать ярлыки (нет WScript.Shell)");
            object? shell = null;
            object? link = null;
            try
            {
                shell = Activator.CreateInstance(type);
                link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                var linkType = link!.GetType();
                string Get(string name)
                    => linkType.InvokeMember(name, BindingFlags.GetProperty, null, link, null) as string ?? "";
                return (Get("TargetPath"), Get("Arguments"), Get("WorkingDirectory"));
            }
            finally
            {
                if (link is not null && Marshal.IsComObject(link))
                    Marshal.FinalReleaseComObject(link);
                if (shell is not null && Marshal.IsComObject(shell))
                    Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
