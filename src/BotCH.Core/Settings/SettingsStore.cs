using System;
using System.IO;
using System.Text;

namespace BotCH.Core.Settings;

/// <summary>
/// Файл настроек рядом с exe. Сохранение атомарное: пишем во временный файл и подменяем им старый —
/// если бот упадёт посреди записи, останется прежний целый файл.
/// </summary>
public sealed class SettingsStore(string path)
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public string Path { get; } = path;

    /// <summary>
    /// Загружает настройки. Файла нет — значения по умолчанию. Файл испорчен — он переименовывается в <c>.bad</c>
    /// (чтобы не потерять), а бот стартует с настройками по умолчанию; причина — в <paramref name="problem"/>.
    /// </summary>
    public BotSettings Load(out string? problem)
    {
        problem = null;
        if (!File.Exists(Path))
            return new BotSettings();

        try
        {
            return SettingsJson.Parse(File.ReadAllText(Path, Utf8));
        }
        catch (Exception e) when (e is Newtonsoft.Json.JsonException or IOException)
        {
            var bad = Path + ".bad";
            try
            {
                File.Copy(Path, bad, overwrite: true);
            }
            catch (IOException)
            {
                bad = "(не удалось сохранить копию)";
            }

            problem = $"Файл настроек испорчен, взяты значения по умолчанию. Копия: {bad}. Ошибка: {e.Message}";
            return new BotSettings();
        }
    }

    public void Save(BotSettings settings)
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = Path + ".tmp";
        File.WriteAllText(temp, SettingsJson.Serialize(settings), Utf8);

        if (File.Exists(Path))
            File.Replace(temp, Path, destinationBackupFileName: null);
        else
            File.Move(temp, Path);
    }
}
