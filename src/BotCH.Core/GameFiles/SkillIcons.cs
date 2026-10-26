using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;

namespace BotCH.Core.GameFiles;

/// <summary>
/// Иконки скиллов. Картинки — набор iconlist_skill из surfaces.pck (<see cref="IconAtlas"/>). Какая картинка у какого скилла,
/// знает только сама игра: у каждого скилла есть описание в библиотеке скиллов клиента (elementskill*.dll), в нём номер
/// скилла и имя файла иконки (профиль: <see cref="SkillStubOffsets"/>). Описания — объекты в данных библиотеки, игра заполняет
/// их, загружая библиотеку, поэтому читаем память процесса: сразу после запуска клиента, ещё до входа в мир.
/// </summary>
public sealed class SkillIcons
{
    /// <summary>Начало имени библиотеки скиллов: elementskill.dll, ElementSkill_test.dll, ElementSkill2.dll.</summary>
    public const string Library = "elementskill";

    private static readonly Encoding Gbk = Encoding.GetEncoding(936);
    private const int MaxSkillId = 0xFFFF;
    private const int MaxFileName = 128;

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
    /// <paramref name="problem"/>; на сервере без <see cref="ProfileData.SkillStub"/> — <see cref="Empty"/> без причины.
    /// </summary>
    public static SkillIcons Load(GameProcess game, ProfileData profile, out string? problem)
    {
        problem = null;
        if (profile.SkillStub.Icon == 0)
            return Empty;

        var files = new Dictionary<int, string>();
        foreach (var library in game.ModuleBasesStartingWith(Library))
        {
            foreach (var file in ReadFiles(game, library, profile.SkillStub))
            {
                if (!files.ContainsKey(file.Key))
                    files[file.Key] = file.Value;
            }
        }

        if (files.Count == 0)
        {
            problem = $"нет описаний скиллов в {Library}*.dll";
            return Empty;
        }

        var atlas = IconAtlas.LoadFromGameDirectory(Path.GetDirectoryName(game.MainModulePath)!, "skill", profile.GameFiles.Pck, out problem);
        return atlas.Count == 0 ? Empty : new SkillIcons(files, atlas);
    }

    /// <summary>
    /// Номер скилла → имя файла иконки по описаниям в данных библиотеки. Описание узнаём по форме: +0 — указатель в постоянные
    /// данные библиотеки (таблица методов), номер скилла 1..65535, указатель на строку «….dds» там же.
    /// </summary>
    internal static Dictionary<int, string> ReadFiles(IMemory memory, uint library, SkillStubOffsets stub)
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

        var files = new Dictionary<int, string>();
        var size = (int)Math.Max(stub.Id, stub.Icon) + 4;
        foreach (var (_, _, bytes) in sections.Where(s => s.IsWritable))
        {
            for (var i = 0; i + size <= bytes.Length; i += 4)
            {
                var id = BitConverter.ToInt32(bytes, i + (int)stub.Id);
                if (id is <= 0 or > MaxSkillId || files.ContainsKey(id) || Constant(BitConverter.ToUInt32(bytes, i), out _) is null)
                    continue;

                if (Constant(BitConverter.ToUInt32(bytes, i + (int)stub.Icon), out var at) is not { } text)
                    continue;

                var end = Array.IndexOf(text, (byte)0, at, Math.Min(MaxFileName, text.Length - at));
                var file = end > at ? Gbk.GetString(text, at, end - at) : "";
                if (file.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                    files[id] = file;
            }
        }

        return files;
    }
}
