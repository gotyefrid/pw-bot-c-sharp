using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using Notes.Core.GameFiles;
using Notes.Core.Memory;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Probe;

/// <summary>
/// Карта проходимости (element\maps\…\movemap) вокруг персонажа — картинкой, с мобами, петом и целью. Только чтение: память
/// игры и файлы клиента.
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

        var directory = Path.GetDirectoryName(game.MainModulePath)!;
        var map = MoveMap.LoadFromGameDirectory(directory, mapName, out var problem);
        if (map is null)
        {
            Console.Error.WriteLine("❌ " + problem);
            return 2;
        }

        if (problem is not null)
            Console.WriteLine("⚠️ " + problem);

        var reader = new WorldReader(game, GameRoots.Find(game, game.MainModuleBase, profile.Data), profile.Data, _ => null);
        var world = reader.Read();
        var me = world.Host.Position;
        Console.WriteLine($"Карта {map.Name}: {map.Columns}×{map.Rows} квадратов по {MoveMap.TileSize} м, кусков с проходимостью: {map.Regions.Count}");
        Console.WriteLine($"Перс {world.Host.Name} в ({me.X:0.0}; {me.Y:0.0}): " + map.Walkable(me.X, me.Y) switch
        {
            true => "клетка проходима",
            false => "клетка непроходима (стоит у края препятствия или карта не та)",
            _ => "здесь у клиента карты нет",
        });

        // Камни и постройки: модели квадратов, которые задевает картинка; «мешает» — тело на высоте от 2 м ниже ног до 2,5 м
        // выше (рельеф ещё не читаем, а подножия камней утоплены в землю)
        var tiles = new[] { (-1, -1), (-1, 1), (1, -1), (1, 1), (0, 0) }
            .Select(c => SceneObstacles.TileNumber(me.X + c.Item1 * radius, me.Y + c.Item2 * radius, map.Rows, map.Columns))
            .OfType<int>()
            .Distinct()
            .ToList();
        var all = SceneObstacles.LoadFromGameDirectory(directory, mapName, tiles, profile.Data.GameFiles.Pck, out var obstaclesProblem);
        if (obstaclesProblem is not null)
            Console.WriteLine("⚠️ модели: " + obstaclesProblem);
        var obstacles = new SceneObstacles(all.Brushes.Where(b => b.MaxX > me.X - radius && b.MinX < me.X + radius
                                                                  && b.MaxY > me.Y - radius && b.MinY < me.Y + radius
                                                                  && b.MaxHeight > me.Height - 2 && b.MinHeight < me.Height + 2.5f).ToList());
        Console.WriteLine($"Тел столкновений (камни, постройки) в квадратах {string.Join(", ", tiles)}: {all.Brushes.Count}, на картинке: {obstacles.Brushes.Count}");

        var size = radius * 2 * Scale;
        using (var image = new Bitmap(size, size, PixelFormat.Format24bppRgb))
        {
            // Север вверх, восток вправо: точка картинки (px, py) — мир (me.X − R + px/S, me.Y + R − py/S)
            for (var py = 0; py < size; py++)
            {
                for (var px = 0; px < size; px++)
                {
                    var x = me.X - radius + (px + 0.5f) / Scale;
                    var y = me.Y + radius - (py + 0.5f) / Scale;
                    var color = obstacles.Blocks(x, y, me.Height - 2, me.Height + 2.5f) ? Rock
                        : map.Walkable(x, y) switch { true => Open, false => Closed, _ => Unknown };
                    image.SetPixel(px, py, color);
                }
            }

            using var g = Graphics.FromImage(image);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            PointF At(Position p) => new((p.X - me.X + radius) * Scale, (me.Y + radius - p.Y) * Scale);

            using (var grid = new Pen(Color.FromArgb(40, 128, 128, 128)))
                for (var m = 10; m < radius * 2; m += 10)
                {
                    g.DrawLine(grid, m * Scale, 0, m * Scale, size);
                    g.DrawLine(grid, 0, m * Scale, size, m * Scale);
                }

            void Dot(Position p, Color color, float r)
            {
                var c = At(p);
                using var brush = new SolidBrush(color);
                g.FillEllipse(brush, c.X - r, c.Y - r, r * 2, r * 2);
            }

            foreach (var item in world.GroundItems)
                Dot(item.Position, Color.FromArgb(60, 180, 60), 3);
            foreach (var npc in world.Npcs.Where(n => !n.IsDead))
                Dot(npc.Position, npc.Wid == world.Pet?.ActiveWid ? Color.FromArgb(40, 140, 255)
                    : npc.Kind == NpcKind.Mob ? Color.FromArgb(255, 150, 0) : Color.FromArgb(180, 80, 220), 5);
            if (world.Npcs.FirstOrDefault(n => n.Wid == world.Host.TargetWid) is { } target)
                using (var ring = new Pen(Color.FromArgb(255, 40, 40), 2))
                {
                    var c = At(target.Position);
                    g.DrawEllipse(ring, c.X - 9, c.Y - 9, 18, 18);
                }

            Dot(me, Color.FromArgb(230, 30, 30), 6);
            image.Save(file, ImageFormat.Png);
        }

        Console.WriteLine($"Картинка {size}×{size} ({Scale} точки на метр, север вверху): {Path.GetFullPath(file)}");
        Console.WriteLine("Красная точка — перс, синяя — пет, оранжевые — мобы, фиолетовые — NPC, зелёные — предметы; красное кольцо — цель");
        return 0;
    }
}
