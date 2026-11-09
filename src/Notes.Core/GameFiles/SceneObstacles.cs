using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Notes.Core.GameFiles;

/// <summary>
/// Выпуклое тело столкновений («кирпич», CConvexHullData клиента): игра не пускает персонажа внутрь. Координаты мира:
/// X, Y — по земле (как у позиций в памяти), Height — высота. Внутри — где для всех плоскостей n·p ≤ d.
/// </summary>
public sealed class ConvexBrush
{
    private readonly Plane[] _planes;

    public ConvexBrush(float minX, float minHeight, float minY, float maxX, float maxHeight, float maxY, IReadOnlyList<Plane> planes)
    {
        MinX = minX;
        MinHeight = minHeight;
        MinY = minY;
        MaxX = maxX;
        MaxHeight = maxHeight;
        MaxY = maxY;
        _planes = planes.ToArray();
    }

    public float MinX { get; }
    public float MinHeight { get; }
    public float MinY { get; }
    public float MaxX { get; }
    public float MaxHeight { get; }
    public float MaxY { get; }

    public IReadOnlyList<Plane> Planes => _planes;

    /// <summary>То же тело, сдвинутое на (dx, dh, dy): формы деревьев в .tbh — относительно ствола.</summary>
    public ConvexBrush Moved(float dx, float dh, float dy)
        => new(MinX + dx, MinHeight + dh, MinY + dy, MaxX + dx, MaxHeight + dh, MaxY + dy,
            _planes.Select(p => p with { D = p.D + p.NX * dx + p.NH * dh + p.NY * dy }).ToList());

    /// <summary>Плоскость: нормаль (по X, по высоте, по Y) и d — точка внутри, если NX·x + NH·h + NY·y ≤ D.</summary>
    public readonly record struct Plane(float NX, float NH, float NY, float D);

    /// <summary>
    /// Где тело на вертикали над точкой (x, y): нижняя и верхняя высота; null — вертикаль тело не задевает. Считается точно по
    /// плоскостям: каждая ограничивает высоту сверху или снизу.
    /// </summary>
    public (float Low, float High)? VerticalSpan(float x, float y)
    {
        if (x < MinX || x > MaxX || y < MinY || y > MaxY)
            return null;

        float low = MinHeight, high = MaxHeight;
        foreach (var p in _planes)
        {
            var rest = p.D - p.NX * x - p.NY * y;
            if (Math.Abs(p.NH) < 1e-6f)
            {
                if (rest < -1e-4f)
                    return null;
                continue;
            }

            var bound = rest / p.NH;
            if (p.NH > 0)
                high = Math.Min(high, bound);
            else
                low = Math.Max(low, bound);
        }

        return low <= high ? (low, high) : null;
    }
}

/// <summary>
/// Камни, дома, мосты и прочие модели сцены — их формы столкновений из файлов клиента. Модели лежат в element\litmodels.pck
/// (больше 2 ГБ — вместе с .pkx) по квадратам карты: litmodels\&lt;карта&gt;\&lt;номер квадрата&gt;\litmodel_N.bmd, номер = строка с севера
/// × столбцов + столбец с запада + 1 (у мира Comeback 1.4.6 — 8×11, номера 1…88).
/// <para>
/// Файл .bmd (CELBrushBuilding, загрузка в PW Classic client.exe 0x48E320 — модель 0x9BF370, меш 0x9BD4D0, кирпич 0xB8C0F0):
/// «MOXB»; int 0x80000001 и байт (иначе этих 5 байт нет); модель: версия 0x10000002, 4 вектора по 3 float (масштаб, оси,
/// место), int мешей; меш 0x10000005: имя 64 байта, текстура 256, int вершин, int треугольников, вершины по 24 байта,
/// индексы по 6, нормали по 12, два цвета по 4 байта на вершину, 0x30 байт, строка «MATERIAL: …» до 0 или перевода строки,
/// 69 байт материала (у меша 0x10000004 — Comeback 1.3.6 — материала нет, остальное то же; у 0x10000006 после числа треугольников байт, не 0 — ещё два цвета по 4 байта на вершину). Затем int N, N списков (int число и столько int), N кирпичей: 12 float (центр, полуразмер, min, max —
/// по x, высоте, y), int, int плоскостей, плоскость — нормаль 3 float, d, байт.
/// </para>
/// </summary>
public sealed class SceneObstacles
{
    private const string Archive = "litmodels.pck";
    private const int ModelVersion = 0x10000002;
    private const int MeshVersion = 0x10000005;
    // Comeback 1.3.6: как 0x10000005, но без материала в конце
    private const int MeshVersionOld = 0x10000004;
    // После числа треугольников байт: не 0 — у вершин ещё два цвета по 4 байта
    private const int MeshVersionLit = 0x10000006;
    private const int MeshVertexSize = 24;

    public SceneObstacles(IReadOnlyList<ConvexBrush> brushes) => Brushes = brushes;

    public static SceneObstacles Empty { get; } = new([]);

    public IReadOnlyList<ConvexBrush> Brushes { get; }

    /// <summary>Для деревьев (<see cref="SceneTrees"/>): сколько деревьев найдено и у скольких видов нет формы (кусты).</summary>
    public int TreeCount { get; init; }

    public int TreeKindsWithoutShape { get; init; }

    /// <summary>
    /// Есть ли тело на вертикали над (x, y) между высотами <paramref name="from"/> и <paramref name="to"/> — персонаж
    /// такого роста здесь не встанет.
    /// </summary>
    public bool Blocks(float x, float y, float from, float to)
    {
        foreach (var brush in Brushes)
            if (brush.VerticalSpan(x, y) is { } span && span.High > from && span.Low < to)
                return true;
        return false;
    }

    /// <summary>Номер квадрата карты (как папка в litmodels) с точкой (x, y); вне карты — null.</summary>
    public static int? TileNumber(float x, float y, int rows, int columns)
    {
        var column = (int)Math.Floor((x + columns * MoveMap.TileSize / 2f) / MoveMap.TileSize);
        var rowFromSouth = (int)Math.Floor((y + rows * MoveMap.TileSize / 2f) / MoveMap.TileSize);
        if (column < 0 || column >= columns || rowFromSouth < 0 || rowFromSouth >= rows)
            return null;
        return (rows - 1 - rowFromSouth) * columns + column + 1;
    }

    /// <summary>
    /// Формы моделей из квадратов <paramref name="tiles"/> карты <paramref name="map"/>. Не прочитанные файлы пропускаются —
    /// сколько и почему, в <paramref name="problem"/>.
    /// </summary>
    public static SceneObstacles LoadFromGameDirectory(string gameDirectory, string map, IEnumerable<int> tiles, PckFormat format,
        out string? problem)
    {
        var path = Path.Combine(gameDirectory, Archive);
        if (!File.Exists(path))
        {
            problem = $"Нет {path}";
            return Empty;
        }

        var prefixes = tiles.Distinct().Select(t => $"litmodels\\{map}\\{t}\\").ToList();
        var files = PckArchive.List(path, format)
            .Where(e => e.Path.EndsWith(".bmd", StringComparison.OrdinalIgnoreCase)
                        && prefixes.Any(p => e.Path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .Select(e => e.Path)
            .ToArray();

        var brushes = new List<ConvexBrush>();
        var failed = 0;
        string? firstError = null;
        var data = files.Length == 0 ? Array.Empty<byte[]?>() : PckArchive.ReadFiles(path, format, files);
        for (var i = 0; i < data.Length; i++)
        {
            try
            {
                brushes.AddRange(ReadModel(data[i] ?? throw new InvalidDataException("не прочитан из архива")));
            }
            catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
            {
                failed++;
                firstError ??= $"{files[i]}: {e.Message}";
            }
        }

        problem = files.Length == 0 ? $"В {Archive} нет моделей карты {map} в квадратах {string.Join(", ", prefixes)}"
            : failed > 0 ? $"Не разобрано моделей: {failed} из {files.Length} (первая — {firstError})"
            : null;
        return new SceneObstacles(brushes);
    }

    /// <summary>Кирпичи столкновений одного файла .bmd (у моделей без них — пусто).</summary>
    internal static IReadOnlyList<ConvexBrush> ReadModel(byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data), Encoding.ASCII);
        if (reader.ReadInt32() != 0x42584F4D)
            throw new InvalidDataException("нет «MOXB» в начале");

        if (reader.ReadInt32() == unchecked((int)0x80000001))
            reader.ReadByte();
        else
            reader.BaseStream.Position -= 4;

        var model = reader.ReadInt32();
        if (model != ModelVersion)
            throw new InvalidDataException($"версия модели 0x{model:X} (знаем 0x{ModelVersion:X})");
        reader.ReadBytes(4 * 12);
        var meshes = reader.ReadInt32();
        for (var m = 0; m < meshes; m++)
            SkipMesh(reader);

        var count = reader.ReadInt32();
        if (count < 0 || count > 100_000)
            throw new InvalidDataException($"кирпичей {count}");
        for (var i = 0; i < count; i++)
            Skip(reader, 4 * Checked(reader.ReadInt32(), "список"));

        var brushes = new List<ConvexBrush>(count);
        for (var i = 0; i < count; i++)
            brushes.Add(ReadBrush(reader));
        return brushes;
    }

    /// <summary>
    /// Один кирпич (CConvexHullData, загрузка 0xB8C0F0): 12 float (центр, полуразмер, min, max — по x, высоте, y), int, int
    /// плоскостей, плоскость — нормаль 3 float, d, байт.
    /// </summary>
    internal static ConvexBrush ReadBrush(BinaryReader reader)
    {
        var box = Floats(reader, 12);
        reader.ReadInt32();
        var planes = new ConvexBrush.Plane[Checked(reader.ReadInt32(), "плоскостей")];
        for (var p = 0; p < planes.Length; p++)
        {
            var f = Floats(reader, 4);
            reader.ReadByte();
            // В файле — (x, высота, y)
            planes[p] = new ConvexBrush.Plane(f[0], f[1], f[2], f[3]);
        }

        return new ConvexBrush(box[6], box[7], box[8], box[9], box[10], box[11], planes);
    }

    private static void SkipMesh(BinaryReader reader)
    {
        var version = reader.ReadInt32();
        if (version is not (MeshVersionOld or MeshVersion or MeshVersionLit))
            throw new InvalidDataException($"версия меша 0x{version:X} (знаем 0x{MeshVersionOld:X}…0x{MeshVersionLit:X})");

        Skip(reader, 64 + 256);
        var vertices = Checked(reader.ReadInt32(), "вершин");
        var triangles = Checked(reader.ReadInt32(), "треугольников");
        var extraColors = version == MeshVersionLit && reader.ReadByte() != 0;
        Skip(reader, vertices * MeshVertexSize + triangles * 6 + vertices * 12 + vertices * (extraColors ? 16 : 8) + 0x30);
        if (version == MeshVersionOld)
            return;

        // Строка «MATERIAL: имя» до нуля или перевода строки, за ней сам материал
        int b;
        while ((b = reader.ReadByte()) != 0 && b != '\n')
        {
        }

        Skip(reader, 69);
    }

    internal static int Checked(int value, string what)
        => value is >= 0 and <= 10_000_000 ? value : throw new InvalidDataException($"{what}: {value}");

    private static void Skip(BinaryReader reader, int bytes)
    {
        var stream = reader.BaseStream;
        if (stream.Position + bytes > stream.Length)
            throw new EndOfStreamException("файл обрывается");
        stream.Position += bytes;
    }

    private static float[] Floats(BinaryReader reader, int count)
    {
        var values = new float[count];
        for (var i = 0; i < count; i++)
            values[i] = reader.ReadSingle();
        return values;
    }
}
