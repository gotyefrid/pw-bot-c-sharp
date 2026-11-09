using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Notes.Core.GameFiles;
using Notes.Core.World;

namespace Notes.Core.Navigation;

/// <summary>
/// Препятствия вокруг персонажа для своего пути: камни, дома, заборы (модели сцены) и деревья из файлов клиента, по квадратам
/// карты 1024 м. Квадрат читается в фоне при первой надобности (пара секунд — большой архив моделей) и дальше держится в памяти;
/// пока нужные квадраты не прочитаны, <see cref="Around"/> отвечает null — ходим как раньше.
/// Карту проходимости клиента (movemap) не берём: во дворах за заборами она «закрыта», хотя там ходят (проверено 09.11 на 1.3.6).
/// </summary>
public sealed class ObstacleMap
{
    /// <summary>Дальше этого свой путь не строим: длинные переходы — как раньше (по прямой, полёт), м.</summary>
    public const float MaxRange = 200f;

    private readonly string _gameDirectory;
    private readonly string _map;
    private readonly PckFormat _format;
    private readonly Action<string>? _report;
    private readonly ConcurrentDictionary<int, IReadOnlyList<ConvexBrush>> _tiles = new();
    private readonly ConcurrentDictionary<int, Task> _loading = new();
    private (int Rows, int Columns)? _size;
    private (string Key, WalkArea Tight, WalkArea Comfort)? _last;

    /// <param name="report">Куда сказать, что квадрат прочитан или не прочитался (в лог).</param>
    public ObstacleMap(string gameDirectory, string map, PckFormat format, Action<string>? report = null)
    {
        _gameDirectory = gameDirectory;
        _map = map;
        _format = format;
        _report = report;
    }

    /// <summary>Поле вокруг точки: впритирку и с запасом от препятствий; null — квадраты ещё читаются (или карты нет).</summary>
    public (IWalkArea Tight, IWalkArea Comfort)? Around(Position me)
    {
        if (Size() is not { } size)
            return null;

        var reach = MaxRange + PathFinder.Margin;
        var tiles = TilesAround(me.X, me.Y, reach, size.Rows, size.Columns);
        if (tiles.Count == 0)
            return null;

        var missing = tiles.Where(t => !_tiles.ContainsKey(t)).ToList();
        foreach (var tile in missing)
            _loading.GetOrAdd(tile, t => Task.Run(() => Load(t, size.Rows, size.Columns)));
        if (missing.Count > 0)
            return null;

        // Поле строится заново, когда перс ушёл на 50 м или сменилась высота ног на метр (полоса «мешает ли» — от ног)
        var key = $"{string.Join(",", tiles)}|{Math.Round(me.X / 50)}|{Math.Round(me.Y / 50)}|{Math.Round(me.Height)}";
        if (_last is { } last && last.Key == key)
            return (last.Tight, last.Comfort);

        var brushes = new SceneObstacles(tiles.SelectMany(t => _tiles[t])
            .Where(b => b.MaxX > me.X - reach && b.MinX < me.X + reach && b.MaxY > me.Y - reach && b.MinY < me.Y + reach)
            .ToList());
        var tight = new WalkArea(null, brushes, me.Height);
        var comfort = new WalkArea(null, brushes, me.Height, WalkArea.ComfortRadius);
        _last = (key, tight, comfort);
        return (tight, comfort);
    }

    /// <summary>Квадраты карты, которые задевает квадрат со стороной 2·<paramref name="reach"/> вокруг точки.</summary>
    internal static List<int> TilesAround(float x, float y, float reach, int rows, int columns)
    {
        var tiles = new List<int>();
        foreach (var dx in new[] { -reach, 0, reach })
            foreach (var dy in new[] { -reach, 0, reach })
                if (SceneObstacles.TileNumber(x + dx, y + dy, rows, columns) is { } tile && !tiles.Contains(tile))
                    tiles.Add(tile);
        return tiles;
    }

    private (int Rows, int Columns)? Size()
    {
        if (_size is not null)
            return _size;

        try
        {
            var terrain = Path.Combine(_gameDirectory, "maps", _map, _map + ".trn2");
            using var stream = new FileStream(terrain, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var header = new byte[16];
            if (stream.Read(header, 0, header.Length) < header.Length)
                return null;
            var (width, height) = MoveMap.ReadTerrainSize(header);
            _size = ((int)Math.Round(height / MoveMap.TileSize), (int)Math.Round(width / MoveMap.TileSize));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _report?.Invoke($"Препятствия: размер карты {_map} не прочитан — {e.Message}");
            _size = (0, 0);
        }

        return _size is { Rows: > 0 } ? _size : null;
    }

    private void Load(int tile, int rows, int columns)
    {
        try
        {
            LoadTile(tile, rows, columns);
        }
        catch (Exception e)
        {
            // Не прочитался — квадрат без препятствий: свой путь там просто идёт по прямой, как раньше
            _tiles[tile] = [];
            _report?.Invoke($"Препятствия: квадрат {tile} не прочитан — {e.Message}");
        }
    }

    private void LoadTile(int tile, int rows, int columns)
    {
        var models = SceneObstacles.LoadFromGameDirectory(_gameDirectory, _map, [tile], _format, out var modelsProblem);
        var (minX, minY, maxX, maxY) = TileBounds(tile, rows, columns);
        var trees = SceneTrees.LoadFromGameDirectory(_gameDirectory, _map, minX, minY, maxX, maxY, _format, out var treesProblem);
        _tiles[tile] = models.Brushes.Concat(trees.Brushes).ToList();
        _report?.Invoke($"Препятствия: квадрат {tile} — тел моделей {models.Brushes.Count}, деревьев {trees.TreeCount}"
                        + (modelsProblem is null ? "" : $"; модели: {modelsProblem}")
                        + (treesProblem is null ? "" : $"; деревья: {treesProblem}"));
    }

    /// <summary>Границы квадрата карты по номеру (как у папок litmodels: с северо-запада, с 1).</summary>
    internal static (float MinX, float MinY, float MaxX, float MaxY) TileBounds(int tile, int rows, int columns)
    {
        var row = (tile - 1) / columns;
        var column = (tile - 1) % columns;
        var minX = column * MoveMap.TileSize - columns * MoveMap.TileSize / 2f;
        var minY = (rows - row - 1) * MoveMap.TileSize - rows * MoveMap.TileSize / 2f;
        return (minX, minY, minX + MoveMap.TileSize, minY + MoveMap.TileSize);
    }
}
