using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Notes.Core.Resources;

/// <summary>Сохранённая связка скиллов: название и строки по порядку.</summary>
public sealed class SavedCombo
{
    /// <summary>Постоянный номер: по нему связка выбрана у персонажа, переименование выбор не сбивает.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<ComboRow> Rows { get; set; } = [];
}

/// <summary>
/// Файл связок скиллов в общей папке (<c>combos.json</c>, <see cref="SharedFolder"/>) — один на все копии бота и персонажей,
/// как файл маршрутов (<see cref="RouteStore"/>). Каждая правка перечитывает файл и пишет его целиком: правка одной связки
/// не затирает другую, записанную другой копией. Удалённые связки помнятся по номеру — персонаж, у которого такая была
/// выбрана, остаётся без связки, а не возвращает её из своей копии строк.
/// </summary>
public sealed class ComboStore(string path)
{
    public const string FileName = "combos.json";
    private const string NamePrefix = "Связка";

    private static readonly JsonSerializerSettings Options = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        MissingMemberHandling = MissingMemberHandling.Ignore,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Formatting = Formatting.Indented,
        // Проверки и повтор — словом, как в настройках
        Converters = { new StringEnumConverter() },
    };

    public string Path { get; } = path;

    /// <summary>Почему файл не прочитался в последний раз (испорчен — копия <c>.bad</c>); null — всё хорошо.</summary>
    public string? Problem { get; private set; }

    /// <summary>Все связки по названию.</summary>
    public IReadOnlyList<SavedCombo> All() => Read().Combos.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public SavedCombo? Find(string id) => id.Length == 0 ? null : Read().Combos.FirstOrDefault(r => r.Id == id);

    /// <summary>Новая связка «Связка N» с этими строками (копия; пусто — пустая связка).</summary>
    public SavedCombo Create(IEnumerable<ComboRow>? rows = null)
    {
        var copy = Copy(rows ?? []);
        return Change(file => Add(file, NamePrefix, copy));
    }

    /// <summary>Записать строки связки (копию). Связки в файле нет (удалили) — false, ничего не пишется.</summary>
    public bool SaveRows(string id, IEnumerable<ComboRow> rows)
    {
        var copy = Copy(rows);
        return Change(file =>
        {
            if (file.Combos.FirstOrDefault(r => r.Id == id) is not { } combo)
                return false;
            combo.Rows = copy;
            return true;
        });
    }

    /// <summary>Переименовать. Пустое название или такое уже есть у другой связки — false.</summary>
    public bool Rename(string id, string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            return false;
        return Change(file =>
        {
            if (file.Combos.FirstOrDefault(r => r.Id == id) is not { } combo
                || file.Combos.Any(r => r.Id != id && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
                return false;
            combo.Name = name;
            return true;
        });
    }

    /// <summary>Удалить у всех: номер запоминается как удалённый.</summary>
    public bool Remove(string id) => Change(file =>
    {
        if (file.Combos.RemoveAll(r => r.Id == id) == 0)
            return false;
        if (!file.Deleted.Contains(id))
            file.Deleted.Add(id);
        return true;
    });

    /// <summary>
    /// Строки персонажа ← файл. Связка не выбрана — ничего; выбрана и в файле строки другие — берутся из файла; выбрана
    /// и удалена — без связки; выбрана, но в файле её нет и не удаляли (файл потерян) — возвращается в файл из строк персонажа.
    /// </summary>
    public ComboSync Sync(CombatSettings combat)
    {
        if (combat.ComboId.Length == 0)
            return ComboSync.Unchanged;

        var data = Read();
        if (data.Combos.FirstOrDefault(r => r.Id == combat.ComboId) is { } saved)
        {
            if (Same(saved.Rows, combat.Combo))
                return ComboSync.Unchanged;
            combat.Combo = Copy(saved.Rows);
            return ComboSync.Loaded;
        }

        if (data.Deleted.Contains(combat.ComboId))
        {
            combat.ComboId = "";
            combat.Combo = [];
            return ComboSync.Deleted;
        }

        var id = combat.ComboId;
        var restored = Copy(combat.Combo);
        Change(file => file.Combos.FirstOrDefault(r => r.Id == id) ?? Add(file, NamePrefix, restored, id));
        return ComboSync.Restored;
    }

    /// <summary>Те же строки в том же порядке с теми же настройками.</summary>
    public static bool Same(IEnumerable<ComboRow> a, IEnumerable<ComboRow> b)
        => JsonConvert.SerializeObject(a, Options) == JsonConvert.SerializeObject(b, Options);

    /// <summary>Глубокая копия строк: у персонажа и в файле — разные объекты.</summary>
    public static List<ComboRow> Copy(IEnumerable<ComboRow> rows)
        => JsonConvert.DeserializeObject<List<ComboRow>>(JsonConvert.SerializeObject(rows, Options), Options) ?? [];

    // Новая связка «Связка 1», «Связка 2»… (первый свободный номер)
    private static SavedCombo Add(ComboFile file, string name, List<ComboRow> rows, string? id = null)
    {
        bool Taken(string n) => file.Combos.Any(r => string.Equals(r.Name, n, StringComparison.OrdinalIgnoreCase));
        var number = 1;
        while (Taken($"{name} {number}"))
            number++;

        var combo = new SavedCombo { Id = id ?? Guid.NewGuid().ToString("N"), Name = $"{name} {number}", Rows = rows };
        file.Combos.Add(combo);
        file.Deleted.Remove(combo.Id);
        return combo;
    }

    // Перечитать файл, поменять, записать; ничего не поменялось (false / null) — не писать
    private T Change<T>(Func<ComboFile, T> change)
    {
        var file = Read();
        var result = change(file);
        if ((object?)result is not (false or null))
            JsonFile.Save(Path, JsonConvert.SerializeObject(file, Options));
        return result;
    }

    private ComboFile Read()
    {
        var file = JsonFile.Load(Path, json => JsonConvert.DeserializeObject<ComboFile>(json, Options) ?? new ComboFile(), () => new ComboFile(),
            out var details);
        Problem = details is null ? null : $"Файл связок скиллов испорчен, начинаю с пустого. {details}";
        file.Combos = (file.Combos ?? []).Where(r => r is not null && r.Id.Length > 0 && !string.IsNullOrWhiteSpace(r.Name)).ToList();
        foreach (var combo in file.Combos)
        {
            combo.Name = combo.Name.Trim();
            combo.Rows = ComboRow.Clean(combo.Rows);
        }
        file.Deleted ??= [];
        return file;
    }

    private sealed class ComboFile
    {
        public int Version { get; set; } = 1;
        public List<SavedCombo> Combos { get; set; } = [];
        public List<string> Deleted { get; set; } = [];
    }
}

/// <summary>Что сделала сверка строк персонажа с файлом (<see cref="ComboStore.Sync"/>).</summary>
public enum ComboSync
{
    /// <summary>Ничего не поменялось.</summary>
    Unchanged,

    /// <summary>Строки персонажа взяты из файла (связку поменяли в другой копии).</summary>
    Loaded,

    /// <summary>Связки не было в файле (файл потерян) — возвращена из строк персонажа.</summary>
    Restored,

    /// <summary>Выбранная связка удалена — у персонажа больше нет связки.</summary>
    Deleted,
}
