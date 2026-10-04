using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace BotCH.Core.Settings;

/// <summary>
/// Файл данных бота (настройки, точки ресурсов), UTF-8 без BOM. Запись атомарная: во временный файл и подмена — если бот
/// упадёт посреди записи, останется прежний целый файл. Испорченный файл при чтении копируется в <c>.bad</c>, чтобы не потерять.
/// </summary>
public static class JsonFile
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Прочитать и разобрать. Файла нет — <paramref name="empty"/>. Не читается или не разбирается — копия <c>.bad</c>,
    /// <paramref name="empty"/>, а в <paramref name="problem"/> — где копия и какая ошибка.
    /// </summary>
    public static T Load<T>(string path, Func<string, T> parse, Func<T> empty, out string? problem)
    {
        problem = null;
        if (!File.Exists(path))
            return empty();

        try
        {
            return parse(File.ReadAllText(path, Utf8));
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            var bad = path + ".bad";
            try
            {
                File.Copy(path, bad, overwrite: true);
            }
            catch (IOException)
            {
                bad = "(не удалось сохранить копию)";
            }

            problem = $"Копия: {bad}. Ошибка: {e.Message}";
            return empty();
        }
    }

    public static void Save(string path, string text)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        File.WriteAllText(temp, text, Utf8);

        if (File.Exists(path))
            File.Replace(temp, path, destinationBackupFileName: null);
        else
            File.Move(temp, path);
    }
}
