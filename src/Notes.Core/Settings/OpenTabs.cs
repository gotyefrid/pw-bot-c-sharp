using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Notes.Core.Settings;

/// <summary>Вкладка окна, которую открыть при следующем запуске: сервер (id профиля) и ник; ника нет — пустая вкладка.</summary>
public sealed record OpenTab(string Server, string? Nick);

/// <summary>
/// Какие вкладки персонажей были открыты (tabs.json в папке бота) — при запуске окно открывает их снова. Пишет только окно
/// (один на процесс), поэтому без склейки, как у settings.json. Нет файла или испорчен — пусто: одна новая вкладка, как раньше.
/// </summary>
public sealed class OpenTabsFile(string folder)
{
    public const string FileName = "tabs.json";

    private sealed class FileData
    {
        public List<OpenTab> Tabs { get; set; } = new();
        public int Selected { get; set; }
    }

    public string Path { get; } = System.IO.Path.Combine(folder, FileName);

    /// <summary>Вкладки по порядку и какая была выбрана (номер с 0).</summary>
    public (IReadOnlyList<OpenTab> Tabs, int Selected) Load(out string? problem)
    {
        var data = JsonFile.Load(Path, text => JsonConvert.DeserializeObject<FileData>(text) ?? new FileData(), () => new FileData(),
            out problem);
        var tabs = data.Tabs.Where(t => t is not null && !string.IsNullOrEmpty(t.Server)).ToList();
        return (tabs, Math.Max(0, Math.Min(data.Selected, tabs.Count - 1)));
    }

    public void Save(IReadOnlyList<OpenTab> tabs, int selected)
        => JsonFile.Save(Path, JsonConvert.SerializeObject(new FileData { Tabs = tabs.ToList(), Selected = selected }, Formatting.Indented));

    /// <summary>Сохранить; не вышло — причина (окно пишет её в лог и работает дальше).</summary>
    public string? TrySave(IReadOnlyList<OpenTab> tabs, int selected)
    {
        try
        {
            Save(tabs, selected);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }
}
