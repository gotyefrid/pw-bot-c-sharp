using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BotCH.Core.Profiles;

/// <summary>
/// Список известных серверов. Профили вшиты в сборку (Profiles/*.jsonc), поэтому бот — один exe.
/// Файл profiles/&lt;id&gt;.jsonc рядом с exe перекрывает вшитый — так можно поправить смещение без пересборки.
/// </summary>
public sealed class ProfileCatalog
{
    private const string ResourcePrefix = "BotCH.Core.Profiles.";
    private const string Extension = ".jsonc";

    private readonly string? _overrideDirectory;

    /// <param name="overrideDirectory">Папка с переопределёнными профилями; null — только вшитые.</param>
    public ProfileCatalog(string? overrideDirectory = null)
    {
        _overrideDirectory = overrideDirectory;
    }

    /// <summary>Каталог с переопределениями в папке profiles рядом с exe.</summary>
    public static ProfileCatalog Default() =>
        new(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles"));

    public IReadOnlyList<string> Ids =>
        Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix) && n.EndsWith(Extension))
            .Select(n => n.Substring(ResourcePrefix.Length, n.Length - ResourcePrefix.Length - Extension.Length))
            .OrderBy(n => n)
            .ToList();

    public IServerProfile Load(string id)
    {
        var json = ReadOverride(id) ?? ReadEmbedded(id)
                   ?? throw new ArgumentException($"Неизвестный сервер «{id}». Есть: {string.Join(", ", Ids)}");

        ProfileData data;
        try
        {
            data = ProfileJson.Parse(json);
        }
        catch (Exception e)
        {
            throw new FormatException($"Ошибка в профиле «{id}»: {e.Message}", e);
        }

        if (data.Id != id)
            throw new FormatException($"В профиле «{id}» указан id «{data.Id}»");

        return new DataServerProfile(data);
    }

    private static Assembly Assembly => typeof(ProfileCatalog).Assembly;

    private string? ReadOverride(string id)
    {
        if (_overrideDirectory is null)
            return null;

        var path = Path.Combine(_overrideDirectory, id + Extension);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static string? ReadEmbedded(string id)
    {
        using var stream = Assembly.GetManifestResourceStream(ResourcePrefix + id + Extension);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
