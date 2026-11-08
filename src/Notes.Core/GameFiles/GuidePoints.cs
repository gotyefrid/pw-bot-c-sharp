using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Core.GameFiles;

/// <summary>
/// Места мобов, NPC и ресурсов по виду (tid) — те, по которым игра ведёт автопуть заданий. Файл игры element\configs.pck →
/// configs\coord_data.txt: строки «tid карта x высота y» в координатах мира (как позиции в памяти). У ресурса — все места, где
/// он появляется (центр места, сам ресурс встаёт рядом), у моба — несколько точек (где водится, не все места спавна), у NPC
/// обычно одна.
/// <para>
/// В боте вшит свой такой файл (с Comeback 1.4.6, tid у серверов общие) — справочник на случай, когда файла в клиенте нет или он
/// не прочитался. Файл клиента читается поверх вшитого: вид, который есть в клиенте, берётся из клиента целиком. У PW Classic
/// ресурсы — в configs2.pck, по несколько точек на место (в configs.pck — только мобы и NPC заданий).
/// </para>
/// </summary>
public sealed class GuidePoints
{
    /// <summary>Основная карта в файле; у подземелий свои названия (a01, a02…).</summary>
    public const string MainWorld = "world";

    private const string BuiltInResource = "Notes.Core.GameFiles.coord_data.txt.gz";

    // Порядок важен: что прочитано позже, то и верно (configs2 — поверх configs)
    private static readonly string[] Archives = ["configs.pck", "configs2.pck"];

    private static readonly Lazy<Dictionary<int, List<Position>>> BuiltInPoints = new(ReadBuiltIn);

    private readonly Dictionary<int, List<Position>> _points;

    public GuidePoints(Dictionary<int, List<Position>> points) => _points = points;

    public static GuidePoints Empty { get; } = new([]);

    /// <summary>Только вшитый в бота справочник.</summary>
    public static GuidePoints BuiltIn => new(BuiltInPoints.Value);

    /// <summary>Сколько видов знаем.</summary>
    public int Count => _points.Count;

    /// <summary>Все точки вида на основной карте; нет — пусто.</summary>
    public IReadOnlyList<Position> Of(int tid) => _points.TryGetValue(tid, out var list) ? list : [];

    /// <summary>Ближайшая к <paramref name="from"/> точка вида (по горизонтали); нет — null.</summary>
    public Position? Nearest(int tid, Position from) =>
        Of(tid).Select(p => (Position?)p).OrderBy(p => p!.Value.HorizontalDistanceTo(from)).FirstOrDefault();

    /// <summary>Все точки любых видов не дальше <paramref name="radius"/> от <paramref name="from"/> (по горизонтали), ближние — первыми.</summary>
    public IReadOnlyList<(int Tid, Position Point)> Within(Position from, float radius) =>
        _points.SelectMany(kind => kind.Value.Select(p => (Tid: kind.Key, Point: p)))
            .Select(e => (Entry: e, Distance: e.Point.HorizontalDistanceTo(from)))
            .Where(e => e.Distance <= radius)
            .OrderBy(e => e.Distance)
            .Select(e => e.Entry)
            .ToList();

    /// <summary>
    /// Вшитый справочник и поверх него — файл клиента. Файл клиента не прочитан — только вшитый и причина в
    /// <paramref name="problem"/>; <paramref name="fromClient"/> — сколько видов взято из клиента.
    /// </summary>
    public static GuidePoints LoadFromGameDirectory(string gameDirectory, GameFilesData files, out string? problem, out int fromClient)
    {
        var client = ReadClient(gameDirectory, files, out problem);
        fromClient = client?.Count ?? 0;
        return Merge(BuiltInPoints.Value, client);
    }

    /// <summary>Виды из <paramref name="client"/> заменяют такие же из <paramref name="builtIn"/> целиком; остальные — из вшитого.</summary>
    internal static GuidePoints Merge(Dictionary<int, List<Position>> builtIn, Dictionary<int, List<Position>>? client)
    {
        var points = new Dictionary<int, List<Position>>(builtIn);
        if (client is not null)
            foreach (var pair in client)
                points[pair.Key] = pair.Value;
        return new GuidePoints(points);
    }

    private static Dictionary<int, List<Position>>? ReadClient(string gameDirectory, GameFilesData files, out string? problem)
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

                points ??= [];
                foreach (var pair in Parse(Decode(data)))
                    points[pair.Key] = pair.Value;
                problem = null;
            }
            catch (Exception e) when (e is IOException or InvalidDataException)
            {
                if (points is null)
                    problem = $"{archive}: {e.Message}";
            }
        }

        return points;
    }

    private static Dictionary<int, List<Position>> ReadBuiltIn()
    {
        using var stream = typeof(GuidePoints).Assembly.GetManifestResourceStream(BuiltInResource)
                           ?? throw new InvalidOperationException("В сборке нет " + BuiltInResource);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var text = new MemoryStream();
        gzip.CopyTo(text);
        return Parse(Decode(text.ToArray()));
    }

    // Файл в GBK; нужны только числа и латинское название карты — кодировка не важна
    private static string Decode(byte[] data) => Encoding.GetEncoding(28591).GetString(data);

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
