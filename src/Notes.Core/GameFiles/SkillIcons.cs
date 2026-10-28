using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Notes.Core.Memory;
using Notes.Core.Profiles;

namespace Notes.Core.GameFiles;

/// <summary>
/// Иконки скиллов. Картинки — набор iconlist_skill из surfaces.pck (<see cref="IconAtlas"/>). Какая картинка у какого скилла,
/// знает только сама игра: у каждого скилла есть описание в библиотеке скиллов клиента (elementskill*.dll) — таблица методов,
/// номер скилла, …, указатель на имя файла иконки «….dds». Описания — объекты в данных библиотеки, игра заполняет их, загружая
/// библиотеку, поэтому читаем память процесса: сразу после запуска клиента, ещё до входа в мир.
/// <para>
/// Где в описании номер, бот находит сам, как базу (<see cref="BasePointerSearch"/>): берёт все указатели на «.dds» в данных
/// библиотеки и смотрит, на каком расстоянии от них лежит номер — сразу за указателем в постоянные данные (таблицей методов),
/// 1..65535. Верное расстояние даёт больше всех разных номеров: у Comeback 1.3.6 и 1.4.6 и PW Classic это −0x10 (номер +4,
/// иконка +0x14), в 3–6 раз больше следующего.
/// </para>
/// </summary>
public sealed class SkillIcons
{
    /// <summary>Начало имени библиотеки скиллов: elementskill.dll, ElementSkill_test.dll, ElementSkill2.dll.</summary>
    public const string Library = "elementskill";

    private static readonly Encoding Gbk = Encoding.GetEncoding(936);
    private const int MaxSkillId = 0xFFFF;
    private const int MaxFileName = 128;
    // Насколько далеко от указателя на иконку искать номер
    private const int Reach = 0x40;
    // Второе по числу скиллов расстояние должно отставать хотя бы вдвое — иначе неясно, где номер
    private const int MinLead = 2;

    private readonly Dictionary<int, string> _files;
    private readonly IconAtlas _atlas;

    private SkillIcons(Dictionary<int, string> files, IconAtlas atlas)
    {
        _files = files;
        _atlas = atlas;
    }

    public static SkillIcons Empty { get; } = new(new Dictionary<int, string>(), IconAtlas.Empty);

    /// <summary>У скольких скиллов нашлась картинка.</summary>
    public int Count => _files.Count(f => _atlas.Contains(f.Value));

    /// <summary>Имя файла иконки скилла из его описания («治疗宠物.dds»); null — описания нет.</summary>
    public string? FileOf(int skillId) => _files.TryGetValue(skillId, out var file) ? file : null;

    public IconImage? Get(int skillId) => FileOf(skillId) is { } file ? _atlas.Get(file) : null;

    /// <summary>
    /// Описания скиллов из памяти клиента и картинки из его папки. Не получилось — <see cref="Empty"/> и причина в
    /// <paramref name="problem"/>.
    /// </summary>
    public static SkillIcons Load(GameProcess game, ProfileData profile, out string? problem)
    {
        var files = new Dictionary<int, string>();
        foreach (var library in game.ModuleBasesStartingWith(Library))
        {
            foreach (var file in ReadFiles(game, library))
            {
                if (!files.ContainsKey(file.Key))
                    files[file.Key] = file.Value;
            }
        }

        if (files.Count == 0)
        {
            problem = $"описания скиллов в {Library}*.dll не узнаны";
            return Empty;
        }

        var atlas = IconAtlas.LoadFromGameDirectory(Path.GetDirectoryName(game.MainModulePath)!, "skill", profile.GameFiles.Pck, out problem);
        return atlas.Count == 0 ? Empty : new SkillIcons(files, atlas);
    }

    /// <summary>Номер скилла → имя файла иконки по описаниям в данных библиотеки; не узнали, где номер, — пусто.</summary>
    internal static Dictionary<int, string> ReadFiles(IMemory memory, uint library)
    {
        var sections = ModuleSections.Read(memory, library)
            .Where(s => !s.IsCode && s.Size > 0)
            .Select(s => (s.Address, s.IsWritable, Bytes: ModuleSections.ReadRegion(memory, s.Address, s.Size)))
            .ToList();
        var constants = sections.Where(s => !s.IsWritable).ToList();

        byte[]? Constant(uint address, out int at)
        {
            foreach (var s in constants)
            {
                if (address >= s.Address && address - s.Address < (uint)s.Bytes.Length)
                {
                    at = (int)(address - s.Address);
                    return s.Bytes;
                }
            }

            at = 0;
            return null;
        }

        string? IconFile(uint pointer)
        {
            if (Constant(pointer, out var at) is not { } text)
                return null;
            var end = Array.IndexOf(text, (byte)0, at, Math.Min(MaxFileName, text.Length - at));
            var file = end > at ? Gbk.GetString(text, at, end - at) : "";
            return file.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? file : null;
        }

        var icons = new List<(byte[] Bytes, int At, string File)>();
        foreach (var (_, _, bytes) in sections.Where(s => s.IsWritable))
        {
            for (var i = 0; i + 4 <= bytes.Length; i += 4)
            {
                if (IconFile(BitConverter.ToUInt32(bytes, i)) is { } file)
                    icons.Add((bytes, i, file));
            }
        }

        // Номер на расстоянии shift от указателя на иконку; у одного скилла бывает несколько описаний — берём первое
        Dictionary<int, string> FilesAt(int shift)
        {
            var files = new Dictionary<int, string>();
            foreach (var (bytes, at, file) in icons)
            {
                var id = at + shift;
                if (id < 4 || id + 4 > bytes.Length)
                    continue;
                var skill = BitConverter.ToInt32(bytes, id);
                if (skill is > 0 and <= MaxSkillId && !files.ContainsKey(skill) && Constant(BitConverter.ToUInt32(bytes, id - 4), out _) is not null)
                    files[skill] = file;
            }

            return files;
        }

        var ranked = Enumerable.Range(-Reach / 4, Reach / 2 + 1).Select(k => k * 4).Where(shift => shift != 0)
            .Select(FilesAt).OrderByDescending(f => f.Count).Take(2).ToList();
        return ranked.Count > 0 && ranked[0].Count > 0 && (ranked.Count < 2 || ranked[1].Count * MinLead <= ranked[0].Count)
            ? ranked[0]
            : new Dictionary<int, string>();
    }
}
