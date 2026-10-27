using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BotCH.Core.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BotCH.Core.Clicker;

/// <summary>
/// Сценарий кликера в своём файле — экспорт и импорт: не потерять сложный сценарий, перенести на другого персонажа, переслать.
/// Внутри то же, что в настройках персонажа, — <c>{ "steps": [ … ] }</c>: читается блокнотом; шаги из более новой версии
/// бота не теряются (<see cref="UnknownAction"/>, <see cref="UnknownCondition"/>).
/// </summary>
public static class ClickerFile
{
    public static string Serialize(IEnumerable<ClickerStep> steps) => SettingsJson.SerializePart(new ClickerSettings { Steps = [.. steps] });

    /// <summary>Шаги из текста файла (значения — в пределы); испорчен или сценария в нём нет — null, а в <paramref name="problem"/> — почему.</summary>
    public static List<ClickerStep>? Parse(string json, out string? problem)
    {
        problem = null;
        try
        {
            if (JToken.Parse(json) is not JObject root || root.GetValue("steps", StringComparison.OrdinalIgnoreCase) is not JArray)
            {
                problem = "в файле нет сценария кликера";
                return null;
            }

            return ClickerStep.Clean(SettingsJson.ParsePart<ClickerSettings>(json)?.Steps);
        }
        catch (JsonException e)
        {
            problem = $"файл испорчен ({e.Message})";
            return null;
        }
    }

    /// <summary>Записать (атомарно — как настройки: прежний файл целый, пока новый не записан).</summary>
    public static void Save(string path, IEnumerable<ClickerStep> steps) => JsonFile.Save(path, Serialize(steps));

    /// <summary>Шаги из файла; не читается, испорчен или сценария в нём нет — null, а в <paramref name="problem"/> — почему.</summary>
    public static List<ClickerStep>? Load(string path, out string? problem)
    {
        string json;
        try
        {
            // Файл может держать открытым другая программа (блокнот, другая копия бота)
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Encoding.UTF8);
            json = reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = $"не удалось прочитать файл ({e.Message})";
            return null;
        }

        return Parse(json, out problem);
    }
}
