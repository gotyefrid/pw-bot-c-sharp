using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Notes.Core.GameFiles;

/// <summary>
/// Деревья сцены и их формы столкновений. Где какое дерево — файл сцены element\maps\&lt;карта&gt;\&lt;карта&gt;.ecwld, форма вида —
/// файл .tbh в trees.pck рядом с его .spt (CELTree::InitCDObjects, PW Classic client.exe 0x49E930).
/// <para>
/// .ecwld (CECScene::Load 0x89FA70): заголовок 0x74 байт — «DWCE», версия (≤ 12), float ширина и высота карты, float сторона
/// блока (64 м), +0x18 блоков, +0x1C строк, +0x20 столбцов, +0x24 где список видов деревьев, +0x2C сколько видов. Вид — две
/// строки (int длина и байты GBK): путь .spt и текстура. Сразу за заголовком — смещения блоков (int на блок), блоки — строками
/// с севера, в строке с запада. Блок (CECSceneBlock::Load 0x8A6940): int номер, затем числа объектов (у версии ≥ 9 — 10 int,
/// &lt; 9 — 9, &lt; 7 — 8, &lt; 4 — 5), первое — деревья; дальше деревья (0x8A6E60) по 16 байт у версии ≥ 5: int вид, float x,
/// высота земли, y — координаты мира.
/// </para>
/// <para>
/// .tbh: «MOXB», int версия (9), int кирпичей; на каждый — запись CConvexHullData (пропускаем) и кирпич как у моделей
/// (<see cref="SceneObstacles.ReadBrush"/>) — относительно места дерева, без поворота и масштаба.
/// </para>
/// </summary>
public static class SceneTrees
{
    private const string Archive = "trees.pck";
    private const int SceneMagic = 0x45435744;
    private const int MaxSceneVersion = 12;
    private const int HeaderSize = 0x74;
    private const int ShapeVersion = 9;
    private const int MoxbMagic = 0x42584F4D;
    // Из формы дерева — только то, что начинается ниже этой высоты над землёй под деревом (ствол, низкие ветки): крона выше
    // роста не мешает, а «мешает ли» по ногам перса её захватывало, где земля под деревом ниже ног
    private const float TrunkHeight = 2f;
    private const string HullTag = "V0.2======Convex_Hull_Data_Start=========";

    /// <summary>Дерево на карте: вид (номер в списке сцены) и место у земли.</summary>
    public readonly record struct Tree(int Kind, float X, float Height, float Y);

    /// <summary>Файл сцены: виды деревьев (путь .spt) и деревья в прямоугольнике.</summary>
    public sealed class Scene
    {
        private readonly byte[] _data;
        private readonly int _version;
        private readonly int _rows;
        private readonly int _columns;
        private readonly float _block;
        private readonly float _width;
        private readonly float _height;

        internal Scene(byte[] data)
        {
            _data = data;
            if (data.Length < HeaderSize || BitConverter.ToInt32(data, 0) != SceneMagic)
                throw new InvalidDataException("не файл сцены .ecwld (нет «DWCE»)");
            _version = BitConverter.ToInt32(data, 4);
            if (_version is < 5 or > MaxSceneVersion)
                throw new InvalidDataException($"версия сцены {_version} (знаем 5…{MaxSceneVersion})");

            _width = BitConverter.ToSingle(data, 8);
            _height = BitConverter.ToSingle(data, 0xC);
            _block = BitConverter.ToSingle(data, 0x10);
            _rows = BitConverter.ToInt32(data, 0x1C);
            _columns = BitConverter.ToInt32(data, 0x20);
            if (BitConverter.ToInt32(data, 0x18) != _rows * _columns || _block <= 0 || HeaderSize + _rows * _columns * 4 > data.Length)
                throw new InvalidDataException("сцена .ecwld: не сходится число блоков");

            var kinds = new List<string>();
            var at = BitConverter.ToInt32(data, 0x24);
            var count = SceneObstacles.Checked(BitConverter.ToInt32(data, 0x2C), "видов деревьев");
            for (var i = 0; i < count; i++)
            {
                kinds.Add(String(ref at));
                String(ref at);
            }

            Kinds = kinds;
        }

        /// <summary>Пути .spt видов деревьев (как в архиве, GBK).</summary>
        public IReadOnlyList<string> Kinds { get; }

        /// <summary>Деревья в блоках, которые задевает прямоугольник мира.</summary>
        public IReadOnlyList<Tree> TreesIn(float minX, float minY, float maxX, float maxY)
        {
            var trees = new List<Tree>();
            int Column(float x) => Math.Max(0, Math.Min(_columns - 1, (int)Math.Floor((x + _width / 2) / _block)));
            int Row(float y) => Math.Max(0, Math.Min(_rows - 1, (int)Math.Floor((_height / 2 - y) / _block)));
            for (var row = Row(maxY); row <= Row(minY); row++)
            {
                for (var column = Column(minX); column <= Column(maxX); column++)
                {
                    var at = BitConverter.ToInt32(_data, HeaderSize + (row * _columns + column) * 4);
                    if (at <= 0 || at + 8 > _data.Length)
                        continue;
                    var count = SceneObstacles.Checked(BitConverter.ToInt32(_data, at + 4), "деревьев в блоке");
                    var first = at + 4 * (1 + Counts);
                    if (first + count * 16 > _data.Length)
                        throw new InvalidDataException($"сцена .ecwld: блок {row}×{column} обрывается");
                    for (var i = 0; i < count; i++)
                    {
                        var p = first + i * 16;
                        trees.Add(new Tree(BitConverter.ToInt32(_data, p), BitConverter.ToSingle(_data, p + 4),
                            BitConverter.ToSingle(_data, p + 8), BitConverter.ToSingle(_data, p + 12)));
                    }
                }
            }

            return trees;
        }

        // Сколько чисел объектов в заголовке блока
        private int Counts => _version >= 9 ? 10 : _version >= 7 ? 9 : 8;

        private string String(ref int at)
        {
            var length = BitConverter.ToInt32(_data, at);
            if (length < 0 || at + 4 + length > _data.Length)
                throw new InvalidDataException("сцена .ecwld: строка вида дерева обрывается");
            var value = PckArchive.Gbk.GetString(_data, at + 4, length);
            at += 4 + length;
            return value;
        }
    }

    internal static Scene ReadScene(byte[] data) => new(data);

    /// <summary>Кирпичи формы вида дерева (.tbh) — относительно места дерева.</summary>
    internal static IReadOnlyList<ConvexBrush> ReadShape(byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data));
        // «MOXB» в начале — метка двоичного файла движка, её может и не быть
        if (reader.ReadInt32() != MoxbMagic)
            reader.BaseStream.Position -= 4;
        var version = reader.ReadInt32();
        if (version != ShapeVersion)
            throw new InvalidDataException($"версия формы дерева {version} (знаем {ShapeVersion})");
        var count = SceneObstacles.Checked(reader.ReadInt32(), "кирпичей дерева");
        var brushes = new List<ConvexBrush>(count);
        for (var i = 0; i < count; i++)
        {
            SkipHull(reader);
            brushes.Add(SceneObstacles.ReadBrush(reader));
        }

        return brushes;
    }

    // Запись CConvexHullData перед каждым кирпичом (загрузка 0xB8DE00): метка 41 байт «V0.2======Convex_Hull_Data_Start=========»
    // (бывает V0.1 и «==========…» — версия 0); у V0.1 и V0.2 ещё int, затем int размер и столько байт
    private static void SkipHull(BinaryReader reader)
    {
        var tag = Encoding.ASCII.GetString(reader.ReadBytes(HullTag.Length));
        if (!tag.EndsWith(HullTag.Substring(4), StringComparison.Ordinal))
            throw new InvalidDataException("форма дерева: нет метки Convex_Hull_Data_Start");
        if (tag.StartsWith("V0.", StringComparison.Ordinal))
            reader.ReadInt32();
        var size = SceneObstacles.Checked(reader.ReadInt32(), "размер записи");
        if (reader.BaseStream.Position + size > reader.BaseStream.Length)
            throw new EndOfStreamException("форма дерева обрывается");
        reader.BaseStream.Position += size;
    }

    /// <summary>Путь формы .tbh для вида: путь .spt в нижнем регистре с другим расширением (так делает клиент).</summary>
    public static string ShapePath(string spt)
        => spt.EndsWith(".spt", StringComparison.OrdinalIgnoreCase) ? spt.Substring(0, spt.Length - 4).ToLowerInvariant() + ".tbh" : "";

    /// <summary>
    /// Формы деревьев в прямоугольнике мира, уже на своих местах. Виды без формы (у кустов её нет) пропускаются; что не
    /// прочиталось — в <paramref name="problem"/>.
    /// </summary>
    public static SceneObstacles LoadFromGameDirectory(string gameDirectory, string map, float minX, float minY, float maxX, float maxY,
        PckFormat format, out string? problem)
    {
        var scenePath = Path.Combine(gameDirectory, "maps", map, map + ".ecwld");
        var archive = Path.Combine(gameDirectory, Archive);
        if (!File.Exists(scenePath) || !File.Exists(archive))
        {
            problem = $"Нет {(File.Exists(scenePath) ? archive : scenePath)}";
            return SceneObstacles.Empty;
        }

        try
        {
            Scene scene;
            using (var stream = new FileStream(scenePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var data = new byte[stream.Length];
                var read = 0;
                while (read < data.Length && stream.Read(data, read, data.Length - read) is var n and > 0)
                    read += n;
                scene = ReadScene(data);
            }

            var trees = scene.TreesIn(minX, minY, maxX, maxY);
            var kinds = trees.Select(t => t.Kind).Where(k => k >= 0 && k < scene.Kinds.Count).Distinct().ToArray();
            var paths = kinds.Select(k => ShapePath(scene.Kinds[k])).ToArray();
            var files = paths.Length == 0 ? Array.Empty<byte[]?>() : PckArchive.ReadFiles(archive, format, paths);
            var shapes = new Dictionary<int, IReadOnlyList<ConvexBrush>>();
            var missing = 0;
            for (var i = 0; i < kinds.Length; i++)
            {
                if (files[i] is { } shape && paths[i].Length > 0)
                    shapes[kinds[i]] = ReadShape(shape).Where(b => b.MinHeight < TrunkHeight).ToList();
                else
                    missing++;
            }

            var brushes = trees.Where(t => shapes.ContainsKey(t.Kind))
                .SelectMany(t => shapes[t.Kind].Select(b => b.Moved(t.X, t.Height, t.Y)))
                .ToList();
            problem = null;
            return new SceneObstacles(brushes) { TreeCount = trees.Count, TreeKindsWithoutShape = missing };
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or IOException)
        {
            problem = e.Message;
            return SceneObstacles.Empty;
        }
    }
}
