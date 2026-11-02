using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Core.GameFiles;

/// <summary>
/// Опорные точки мобов и NPC по виду (tid) — те, по которым игра ведёт автопуть заданий. Файл игры element\configs.pck →
/// configs\coord_data.txt: строки «tid карта x высота y» в координатах мира (как позиции в памяти). У моба точек несколько (где
/// водится), у NPC обычно одна. Не все места спавна — только для заданий.
/// </summary>
public sealed class GuidePoints
{
    /// <summary>Основная карта в файле; у подземелий свои названия (a01, a02…).</summary>
    public const string MainWorld = "world";

    // Порядок важен: что прочитано позже, то и верно (configs2 — поверх configs)
    private static readonly string[] Archives = ["configs.pck", "configs2.pck"];

    private readonly Dictionary<int, List<Position>> _points;

    public GuidePoints(Dictionary<int, List<Position>> points) => _points = points;

    public static GuidePoints Empty { get; } = new([]);

    /// <summary>Сколько видов знаем (0 — файл игры не прочитан).</summary>
    public int Count => _points.Count;

    /// <summary>Все точки вида на основной карте; нет — пусто.</summary>
    public IReadOnlyList<Position> Of(int tid) => _points.TryGetValue(tid, out var list) ? list : [];

    /// <summary>Ближайшая к <paramref name="from"/> точка вида (по горизонтали); нет — null.</summary>
    public Position? Nearest(int tid, Position from) =>
        Of(tid).Select(p => (Position?)p).OrderBy(p => p!.Value.HorizontalDistanceTo(from)).FirstOrDefault();

    /// <summary>Читает точки из папки клиента; не получилось — <see cref="Empty"/> и причина.</summary>
    public static GuidePoints LoadFromGameDirectory(string gameDirectory, GameFilesData files, out string? problem)
    {
        problem = "Не найдены configs.pck/configs2.pck в " + gameDirectory;
        Dictionary<int, List<Position>>? points = null;
        foreach (var archive in Archives)
        {
            var path = Path.Combine(gameDirectory, archive);
            if (!File.Exists(path))
                continue;

            try
            {
                var data = PckArchive.ReadFile(path, "\\coord_data.txt", files.Pck);
                if (data is null)
                {
                    if (points is null)
                        problem = $"В {archive} нет coord_data.txt";
                    continue;
                }

                // Файл в GBK; нужны только числа и латинское название карты — кодировка не важна
                points ??= [];
                foreach (var pair in Parse(Encoding.GetEncoding(28591).GetString(data)))
                    points[pair.Key] = pair.Value;
                problem = null;
            }
            catch (Exception e) when (e is IOException or InvalidDataException)
            {
                if (points is null)
                    problem = $"{archive}: {e.Message}";
            }
        }

        return points is null ? Empty : new GuidePoints(points);
    }

    /// <summary>Строки «tid карта x высота y» основной карты; заголовок, подземелья и строки не по формату пропускаются.</summary>
    internal static Dictionary<int, List<Position>> Parse(string text)
    {
        var points = new Dictionary<int, List<Position>>();
        foreach (var line in text.Split('\n'))
        {
            var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 5 || f[1] != MainWorld || !int.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out var tid)
                || !TryFloat(f[2], out var x) || !TryFloat(f[3], out var height) || !TryFloat(f[4], out var y))
                continue;

            if (!points.TryGetValue(tid, out var list))
                points[tid] = list = [];
            list.Add(new Position(x, height, y));
        }

        return points;
    }

    private static bool TryFloat(string s, out float value) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
