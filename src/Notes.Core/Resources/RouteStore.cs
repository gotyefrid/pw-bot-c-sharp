using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Notes.Core.Resources;

/// <summary>Сохранённый маршрут обхода: название и точки по порядку (у каждой — что копать).</summary>
public sealed class SavedRoute
{
    /// <summary>Постоянный номер: по нему маршрут выбран у персонажа, переименование выбор не сбивает.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<RoutePoint> Points { get; set; } = [];
}

/// <summary>
/// Файл маршрутов в общей папке (<c>routes.json</c>, <see cref="SharedFolder"/>) — один на все копии бота, персонажей и серверы.
/// Каждая правка перечитывает файл и пишет его целиком (атомарно, <see cref="JsonFile"/>): правка одного маршрута не затирает
/// другой, записанный другой копией. Удалённые маршруты помнятся по номеру — персонаж, у которого такой был выбран, остаётся
/// без маршрута, а не возвращает его из своей копии точек.
/// </summary>
public sealed class RouteStore(string path)
{
    public const string FileName = "routes.json";
    private const string NamePrefix = "Маршрут";

    private static readonly JsonSerializerSettings Options = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        MissingMemberHandling = MissingMemberHandling.Ignore,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Formatting = Formatting.Indented,
        // Режим «что копать» — словом, как в настройках
        Converters = { new StringEnumConverter() },
    };

    public string Path { get; } = path;

    /// <summary>Почему файл не прочитался в последний раз (испорчен — копия <c>.bad</c>); null — всё хорошо.</summary>
    public string? Problem { get; private set; }

    /// <summary>Все маршруты по названию.</summary>
    public IReadOnlyList<SavedRoute> All() => Read().Routes.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public SavedRoute? Find(string id) => id.Length == 0 ? null : Read().Routes.FirstOrDefault(r => r.Id == id);

    /// <summary>Новый пустой маршрут «Маршрут N».</summary>
    public SavedRoute Create() => Change(file => Add(file, NamePrefix, []));

    /// <summary>Записать точки маршрута (копию). Маршрута в файле нет (удалили) — false, ничего не пишется.</summary>
    public bool SavePoints(string id, IEnumerable<RoutePoint> points)
    {
        var copy = Copy(points);
        return Change(file =>
        {
            if (file.Routes.FirstOrDefault(r => r.Id == id) is not { } route)
                return false;
            route.Points = copy;
            return true;
        });
    }

    /// <summary>Переименовать. Пустое название или такое уже есть у другого маршрута — false.</summary>
    public bool Rename(string id, string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            return false;
        return Change(file =>
        {
            if (file.Routes.FirstOrDefault(r => r.Id == id) is not { } route
                || file.Routes.Any(r => r.Id != id && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
                return false;
            route.Name = name;
            return true;
        });
    }

    /// <summary>Удалить у всех: номер запоминается как удалённый.</summary>
    public bool Remove(string id) => Change(file =>
    {
        if (file.Routes.RemoveAll(r => r.Id == id) == 0)
            return false;
        if (!file.Deleted.Contains(id))
            file.Deleted.Add(id);
        return true;
    });

    /// <summary>
    /// Точки персонажа ← файл. Что вышло:
    /// <list type="bullet">
    /// <item>маршрут не выбран, а точки есть (настройки от прошлой версии) — они становятся общим маршрутом «<paramref name="name"/>»
    /// (такой же уже есть — выбирается он);</item>
    /// <item>выбран и в файле точки другие — берутся из файла;</item>
    /// <item>выбран и удалён — без маршрута, точек нет;</item>
    /// <item>выбран, но в файле его нет и не удаляли (файл потерян) — возвращается в файл из точек персонажа.</item>
    /// </list>
    /// </summary>
    public RouteSync Sync(RouteSettings route, string name)
    {
        if (route.RouteId.Length == 0)
        {
            if (route.Points.Count == 0)
                return RouteSync.Unchanged;

            var points = Copy(route.Points);
            var imported = Change(file => file.Routes.FirstOrDefault(r => Same(r.Points, points)) ?? Add(file, name, points));
            route.RouteId = imported.Id;
            return RouteSync.Imported;
        }

        var data = Read();
        if (data.Routes.FirstOrDefault(r => r.Id == route.RouteId) is { } saved)
        {
            if (Same(saved.Points, route.Points))
                return RouteSync.Unchanged;
            route.Points = Copy(saved.Points);
            return RouteSync.Loaded;
        }

        if (data.Deleted.Contains(route.RouteId))
        {
            route.RouteId = "";
            route.Points = [];
            return RouteSync.Deleted;
        }

        var id = route.RouteId;
        var restored = Copy(route.Points);
        Change(file => file.Routes.FirstOrDefault(r => r.Id == id) ?? Add(file, name, restored, id));
        return RouteSync.Imported;
    }

    /// <summary>Те же точки в том же порядке, с тем же «что копать».</summary>
    public static bool Same(IEnumerable<RoutePoint> a, IEnumerable<RoutePoint> b)
        => JsonConvert.SerializeObject(a, Options) == JsonConvert.SerializeObject(b, Options);

    /// <summary>Глубокая копия точек: у персонажа и в файле — разные объекты.</summary>
    public static List<RoutePoint> Copy(IEnumerable<RoutePoint> points)
        => JsonConvert.DeserializeObject<List<RoutePoint>>(JsonConvert.SerializeObject(points, Options), Options) ?? [];

    // Новый маршрут с названием «name» (занято — «name 2», «name 3»…; у «Маршрут» — сразу «Маршрут 1», «Маршрут 2»…)
    private static SavedRoute Add(RouteFile file, string name, List<RoutePoint> points, string? id = null)
    {
        bool Taken(string n) => file.Routes.Any(r => string.Equals(r.Name, n, StringComparison.OrdinalIgnoreCase));
        var number = 1;
        var unique = name == NamePrefix ? $"{name} {number}" : name;
        while (Taken(unique))
            unique = $"{name} {++number}";

        var route = new SavedRoute { Id = id ?? Guid.NewGuid().ToString("N"), Name = unique, Points = points };
        file.Routes.Add(route);
        file.Deleted.Remove(route.Id);
        return route;
    }

    // Перечитать файл, поменять, записать; ничего не поменялось (false / null) — не писать
    private T Change<T>(Func<RouteFile, T> change)
    {
        var file = Read();
        var result = change(file);
        if ((object?)result is not (false or null))
            JsonFile.Save(Path, JsonConvert.SerializeObject(file, Options));
        return result;
    }

    private RouteFile Read()
    {
        var file = JsonFile.Load(Path, json => JsonConvert.DeserializeObject<RouteFile>(json, Options) ?? new RouteFile(), () => new RouteFile(),
            out var details);
        Problem = details is null ? null : $"Файл маршрутов испорчен, начинаю с пустого. {details}";
        file.Routes = (file.Routes ?? []).Where(r => r is not null && r.Id.Length > 0 && !string.IsNullOrWhiteSpace(r.Name)).ToList();
        foreach (var route in file.Routes)
        {
            route.Name = route.Name.Trim();
            route.Points = RoutePoint.Clean(route.Points);
        }
        file.Deleted ??= [];
        return file;
    }

    private sealed class RouteFile
    {
        public int Version { get; set; } = 1;
        public List<SavedRoute> Routes { get; set; } = [];
        public List<string> Deleted { get; set; } = [];
    }
}

/// <summary>Что сделала сверка точек персонажа с файлом (<see cref="RouteStore.Sync"/>).</summary>
public enum RouteSync
{
    /// <summary>Ничего не поменялось.</summary>
    Unchanged,

    /// <summary>Точки персонажа взяты из файла (маршрут поменяли в другой копии).</summary>
    Loaded,

    /// <summary>Точки персонажа записаны в файл: маршрут перенесён из настроек или возвращён в потерянный файл.</summary>
    Imported,

    /// <summary>Выбранный маршрут удалён — у персонажа больше нет маршрута.</summary>
    Deleted,
}
