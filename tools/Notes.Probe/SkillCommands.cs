using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Notes.Core.GameFiles;
using Notes.Core.Memory;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Probe;

/// <summary>
/// Поля описания скилла (stub): объект скилла → ElementSkill → описание. Печатает int и float рядом по выбранным скиллам —
/// так ищутся стоимость в ярости и другие числа подсказки. Только чтение.
/// </summary>
internal static class SkillCommands
{
    public const string Usage = "  skillints ID... [0xОТ 0xДО]  числа описания скиллов рядом: стоимость ярости и другие поля подсказки";

    private static readonly Regex SkillString = new("^\\s*(\\d+)\\s+\"([^\"]*)\"", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly string[] Archives = ["configs.pck", "configs2.pck"];

    /// <summary>
    /// Текст скилла из skillstr.txt как есть: название (ID*10) и описание (ID*10+1). По нему видно, какие строки подсказки
    /// игра собирает сама. Только чтение файлов игры.
    /// </summary>
    public static int Text(ServerProfile profile, GameProcess game, string[] args)
    {
        var ids = args.Where(a => int.TryParse(a, out var n) && n != game.Pid).Select(int.Parse).ToList();
        if (ids.Count == 0)
        {
            Console.Error.WriteLine("Номера скиллов: skilltext 54 363");
            return 1;
        }

        var directory = Path.GetDirectoryName(game.MainModulePath)!;
        string? text = null;
        foreach (var archive in Archives)
        {
            var path = Path.Combine(directory, archive);
            if (!File.Exists(path))
                continue;
            try
            {
                if (PckArchive.ReadFile(path, "\\skillstr.txt", profile.Data.GameFiles.Pck) is { } data)
                {
                    text = Encoding.Unicode.GetString(data);
                    break;
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException)
            {
                Console.Error.WriteLine($"{archive}: {e.Message}");
            }
        }

        if (text is null)
        {
            Console.Error.WriteLine("❌ Нет skillstr.txt в configs.pck/configs2.pck");
            return 1;
        }

        var lines = new Dictionary<int, string>();
        foreach (Match match in SkillString.Matches(text))
            if (int.TryParse(match.Groups[1].Value, out var key))
                lines[key] = match.Groups[2].Value;

        foreach (var id in ids)
        {
            Console.WriteLine($"=== {id} ===");
            Console.WriteLine(lines.TryGetValue(id * 10, out var name) ? name : "(названия нет)");
            Console.WriteLine(lines.TryGetValue(id * 10 + 1, out var description) ? description : "(описания нет)");
            Console.WriteLine();
        }

        return 0;
    }


    public static int Ints(ServerProfile profile, GameProcess game, string[] args)
    {
        var range = args.Where(a => a.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            .Select(a => Convert.ToInt32(a.Substring(2), 16)).ToList();
        var from = range.Count > 0 ? range[0] : 0;
        var to = range.Count > 1 ? range[1] : 0xFF;
        if (from < 0 || to < from || to - from > 0x800)
        {
            Console.Error.WriteLine("❌ Диапазон — 0xОТ 0xДО, не больше 0x800 байт");
            return 1;
        }

        var ids = args.Where(a => int.TryParse(a, out var n) && n != game.Pid).Select(int.Parse).ToList();
        var names = SkillNames.LoadFromGameDirectory(Path.GetDirectoryName(game.MainModulePath)!, profile.Data.GameFiles.Pck, out _);
        var world = new WorldReader(game, GameRoots.Find(game, game.MainModuleBase, profile.Data), profile.Data, names.Get).Read();
        var host = world.Host.Address;
        var array = game.ReadUInt32(host + profile.Data.Host.Skills);
        var count = game.ReadInt32(host + profile.Data.Host.SkillsCount);
        var skillOffsets = profile.Data.Skill;

        var found = new List<(int Id, string? Name, int Level, uint Skill, uint Element, uint Stub)>();
        for (var i = 0; i < Math.Min(count, 256); i++)
        {
            var skill = game.ReadUInt32(array + (uint)i * 4);
            if (skill == 0)
                continue;
            var id = game.ReadInt32(skill + skillOffsets.Id);
            if (ids.Count > 0 && !ids.Contains(id))
                continue;
            var element = skillOffsets.Range.Element == 0 ? 0 : game.ReadUInt32(skill + skillOffsets.Range.Element);
            var stub = element == 0 ? 0 : game.ReadUInt32(element + skillOffsets.Range.Stub);
            found.Add((id, world.Skill(id)?.Name, game.ReadInt32(skill + skillOffsets.Level), skill, element, stub));
        }

        if (found.Count == 0)
        {
            Console.WriteLine("Скиллы не найдены — проверьте номера");
            return 1;
        }

        foreach (var s in found)
            Console.WriteLine($"Скилл {s.Id} «{s.Name ?? "?"}» ур. {s.Level}: объект 0x{s.Skill:X8}, ElementSkill 0x{s.Element:X8}, описание 0x{s.Stub:X8}");

        var blocks = found.Select(s => s.Stub == 0 ? null : Block(game, s.Stub + (uint)from, to - from + 4)).ToList();
        if (blocks.All(b => b is null))
        {
            Console.WriteLine("Описание не прочиталось — не задан skill.range?");
            return 1;
        }

        Console.WriteLine("смещение  | " + string.Join(" | ", found.Select(s => $"{s.Id,10} int/float")));
        for (var offset = from; offset <= to; offset += 4)
        {
            var at = offset - from;
            var cells = blocks.Select(b => b is null
                ? "—"
                : $"{BitConverter.ToInt32(b, at),10} {BitConverter.ToSingle(b, at),10:0.##}");
            Console.WriteLine($"+0x{offset:X3}    | " + string.Join(" | ", cells));
        }

        return 0;
    }

    private static byte[]? Block(GameProcess game, uint address, int size)
    {
        var data = new byte[size];
        return game.TryRead(address, data, size) ? data : null;
    }
}
