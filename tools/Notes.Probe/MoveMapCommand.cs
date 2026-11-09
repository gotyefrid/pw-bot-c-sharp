using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using Notes.Core.GameFiles;
using Notes.Core.Memory;
using Notes.Core.Navigation;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Probe;

/// <summary>
/// Карта проходимости (element\maps\…\movemap) и камни с постройками вокруг персонажа — картинкой, с мобами, петом и целью.
/// Только чтение: память игры и файлы клиента.
/// </summary>
internal static class MoveMapCommand
{
    public const string Usage =
        "  movemap   [карта] [радиус м] [файл.png]  карта проходимости вокруг перса картинкой (светлое — можно ходить, тёмное — нельзя,\n"
        + "            коричневое — в файлах клиента карты нет, чёрное — камни и постройки); карта по умолчанию world, радиус 100 м";

    // Точек картинки на метр
    private const int Scale = 4;

    private static readonly Color Open = Color.FromArgb(232, 232, 232);
    private static readonly Color Closed = Color.FromArgb(48, 48, 64);
    private static readonly Color Unknown = Color.FromArgb(96, 80, 48);
    private static readonly Color Rock = Color.FromArgb(20, 20, 28);

    public static int Run(ServerProfile profile, GameProcess game, string[] args)
    {
        // Аргументы, кроме «--server id»; числа до 1000 — радиус (больше — PID клиента)
        var words = args.Where((a, i) => a != "--server" && (i == 0 || args[i - 1] != "--server")).ToList();
        var file = words.FirstOrDefault(a => a.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) ?? "movemap.png";
        var radius = words.Select(a => int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var r) ? r : 0)
            .FirstOrDefault(r => r is > 0 and <= 1000);
        radius = radius > 0 ? radius : 100;
        var mapName = words.FirstOrDefault(a => !a.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !int.TryParse(a, out _))
                      ?? GuidePoints.MainWorld;

        var reader = new WorldReader(game, GameRoots.Find(game, game.MainModuleBase, profile.Data), profile.Data, _ => null);
        var world = reader.Read();
        var me = world.Host.Position;
        if (Load(profile, game, mapName, me, [new PathPoint(me.X, me.Y)], radius) is not { } scene)
            return 2;

        Draw(scene, world, new PathPoint(me.X, me.Y), radius, [], file);
        return 0;
    }

    /// <summary>Карта и тела столкновений вокруг точек (с запасом <paramref name="margin"/> м); null — карты нет (причина напечатана).</summary>
    public static Scene? Load(ServerProfile profile, GameProcess game, string mapName, Position me, IReadOnlyList<PathPoint> around, float margin,
        bool useMoveMap = true)
    {
        var directory = Path.GetDirectoryName(game.MainModulePath)!;
        var map = MoveMap.LoadFromGameDirectory(directory, mapName, out var problem);
        if (map is null)
        {
            Console.Error.WriteLine("❌ " + problem);
            return null;
        }

        if (problem is not null)
            Console.WriteLine("⚠️ " + problem);
        Console.WriteLine($"Карта {map.Name}: {map.Columns}×{map.Rows} квадратов по {MoveMap.TileSize} м, кусков с проходимостью: {map.Regions.Count}");

        var minX = around.Min(p => p.X) - margin;
        var maxX = around.Max(p => p.X) + margin;
        var minY = around.Min(p => p.Y) - margin;
        var maxY = around.Max(p => p.Y) + margin;
        var tiles = new List<int>();
        for (var x = minX; x <= maxX + MoveMap.TileSize; x += MoveMap.TileSize)
            for (var y = minY; y <= maxY + MoveMap.TileSize; y += MoveMap.TileSize)
                if (SceneObstacles.TileNumber(Math.Min(x, maxX), Math.Min(y, maxY), map.Rows, map.Columns) is { } tile && !tiles.Contains(tile))
                    tiles.Add(tile);

        var all = SceneObstacles.LoadFromGameDirectory(directory, mapName, tiles, profile.Data.GameFiles.Pck, out var obstaclesProblem);
        if (obstaclesProblem is not null)
            Console.WriteLine("⚠️ модели: " + obstaclesProblem);
        var obstacles = new SceneObstacles(all.Brushes.Where(b => b.MaxX > minX && b.MinX < maxX && b.MaxY > minY && b.MinY < maxY
                                                                  && b.MaxHeight > me.Height - WalkArea.Below
                                                                  && b.MinHeight < me.Height + WalkArea.Above).ToList());
        Console.WriteLine($"Тел столкновений (камни, постройки) в квадратах {string.Join(", ", tiles)}: {all.Brushes.Count}, рядом: {obstacles.Brushes.Count}");
        if (!useMoveMap)
        {
            Console.WriteLine("Карта проходимости клиента не используется (--no-movemap): только камни и постройки");
            map = new MoveMap(map.Name, map.Rows, map.Columns, []);
        }

        return new Scene(map, obstacles, new WalkArea(map, obstacles, me.Height));
    }

    public sealed record Scene(MoveMap Map, SceneObstacles Obstacles, WalkArea Area);

    /// <summary>Картинка с центром <paramref name="center"/>: карта, тела, мобы, пет, цель и путь (синяя ломаная).</summary>
    public static void Draw(Scene scene, WorldState world, PathPoint center, int radius, IReadOnlyList<PathPoint> path, string file)
    {
        var me = world.Host.Position;
        Console.WriteLine($"Перс {world.Host.Name} в ({me.X:0.0}; {me.Y:0.0}): " + scene.Map.Walkable(me.X, me.Y) switch
        {
            true => "клетка проходима",
            false => "клетка непроходима (стоит у края препятствия, в воздухе или карта не та)",
            _ => "здесь у клиента карты нет",
        } + (scene.Area.IsFree(me.X, me.Y) ? "" : "; вплотную к препятствию"));

        var size = radius * 2 * Scale;
        using (var image = new Bitmap(size, size, PixelFormat.Format24bppRgb))
        {
            // Север вверх, восток вправо: точка картинки (px, py) — мир (center.X − R + px/S, center.Y + R − py/S)
            for (var py = 0; py < size; py++)
            {
                for (var px = 0; px < size; px++)
                {
                    var x = center.X - radius + (px + 0.5f) / Scale;
                    var y = center.Y + radius - (py + 0.5f) / Scale;
                    var color = scene.Obstacles.Blocks(x, y, me.Height - WalkArea.Below, me.Height + WalkArea.Above) ? Rock
                        : scene.Map.Walkable(x, y) switch { true => Open, false => Closed, _ => Unknown };
                    image.SetPixel(px, py, color);
                }
            }

            using var g = Graphics.FromImage(image);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            PointF At(float x, float y) => new((x - center.X + radius) * Scale, (center.Y + radius - y) * Scale);

            using (var grid = new Pen(Color.FromArgb(40, 128, 128, 128)))
                for (var m = 10; m < radius * 2; m += 10)
                {
                    g.DrawLine(grid, m * Scale, 0, m * Scale, size);
                    g.DrawLine(grid, 0, m * Scale, size, m * Scale);
                }

            void Dot(float x, float y, Color color, float r)
            {
                var c = At(x, y);
                using var brush = new SolidBrush(color);
                g.FillEllipse(brush, c.X - r, c.Y - r, r * 2, r * 2);
            }

            foreach (var item in world.GroundItems)
                Dot(item.Position.X, item.Position.Y, Color.FromArgb(60, 180, 60), 3);
            foreach (var npc in world.Npcs.Where(n => !n.IsDead))
                Dot(npc.Position.X, npc.Position.Y, npc.Wid == world.Pet?.ActiveWid ? Color.FromArgb(40, 140, 255)
                    : npc.Kind == NpcKind.Mob ? Color.FromArgb(255, 150, 0) : Color.FromArgb(180, 80, 220), 5);
            if (world.Npcs.FirstOrDefault(n => n.Wid == world.Host.TargetWid) is { } target)
                using (var ring = new Pen(Color.FromArgb(255, 40, 40), 2))
                {
                    var c = At(target.Position.X, target.Position.Y);
                    g.DrawEllipse(ring, c.X - 9, c.Y - 9, 18, 18);
                }

            if (path.Count > 1)
            {
                using var line = new Pen(Color.FromArgb(0, 120, 255), 3);
                g.DrawLines(line, path.Select(p => At(p.X, p.Y)).ToArray());
                foreach (var p in path)
                    Dot(p.X, p.Y, Color.FromArgb(0, 90, 220), 4);
                Dot(path[path.Count - 1].X, path[path.Count - 1].Y, Color.FromArgb(0, 200, 120), 7);
            }

            Dot(me.X, me.Y, Color.FromArgb(230, 30, 30), 6);
            image.Save(file, ImageFormat.Png);
        }

        Console.WriteLine($"Картинка {size}×{size} ({Scale} точки на метр, север вверху): {Path.GetFullPath(file)}");
        Console.WriteLine("Красная точка — перс, синяя — пет, оранжевые — мобы, фиолетовые — NPC, зелёные — предметы; красное кольцо — цель"
                          + (path.Count > 1 ? "; синяя ломаная — путь, зелёная точка — куда" : ""));
    }
}
