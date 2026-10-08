using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BotCH.Core.GameFiles;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using BotCH.Core.World;
using Newtonsoft.Json;

namespace BotCH.Probe.Port;

/// <summary>
/// Мастер переноса профиля на новый клиент: шаг за шагом проверяет адреса профиля на живом клиенте, неверные ищет и
/// сразу вписывает в файл профиля. Где нужен опыт «до/после» — просит владельца сделать действие в игре.
/// Только чтение памяти игры. Прогресс — в port-&lt;сервер&gt;.json рядом с Probe (только на этой машине).
/// </summary>
internal static class PortCommand
{
    public const string Usage = """
          port [--profile файл.jsonc] [--again шаг,шаг|all]  мастер переноса профиля на новый клиент: проверяет адреса,
                неверные ищет и вписывает в профиль; где нужно — просит сделать действие в игре (Enter — готово, с — пропустить).
                Без --profile правит profiles\<сервер>.jsonc рядом с Probe. Шаги:
        """;

    public static int Run(ServerProfile profile, string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        var path = Option(args, "--profile") ?? DefaultProfilePath(profile.Id);
        using var game = Program.OpenClient(args);
        var progress = PortProgress.Load(profile.Id);
        var again = (Option(args, "--again") ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToHashSet();

        var ctx = new PortContext(game, path);
        Console.WriteLine($"Профиль: {Path.GetFullPath(path)}");
        Console.WriteLine($"Клиент:  {game.ProcessName} PID {game.Pid}, модуль 0x{game.MainModuleBase:X8}");
        Console.WriteLine(ctx.Interactive
            ? "На вопросы: Enter — готово/да, «н» — нет, «с» — пропустить шаг."
            : "Без владельца: шаги, где нужно действие в игре, пропускаются.");

        foreach (var step in PortSteps.All)
        {
            if (progress.IsDone(step.Id) && !again.Contains(step.Id) && !again.Contains("all"))
            {
                Console.WriteLine($"\n— {step.Title}: уже {progress.Text(step.Id)} (повторить: --again {step.Id})");
                continue;
            }

            Console.WriteLine($"\n═══ {step.Title} ═══");
            PortResult result;
            if (step.NeedsPlayer && !ctx.Interactive)
            {
                result = PortResult.Skip("нужен владелец у клиента");
            }
            else
            {
                ctx.Changes.Clear();
                try
                {
                    result = step.Run(ctx);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    result = PortResult.Fail("ошибка: " + e.Message);
                }

                if (ctx.Changes.Count > 0 && result.Status == PortStatus.Verified)
                    result = result with { Status = PortStatus.Fixed };
                result = result with { Text = string.Join("; ", ctx.Changes.Append(result.Text).Where(t => t.Length > 0)) };
            }

            Console.WriteLine($"{Mark(result.Status)} {result.Text}");
            progress.Set(step.Id, result);
        }

        Console.WriteLine("\n═══ Итог ═══");
        foreach (var step in PortSteps.All)
            Console.WriteLine($"{Mark(progress.Status(step.Id)),-2} {step.Id,-9} {progress.Text(step.Id)}");
        Console.WriteLine($"Прогресс: {PortProgress.PathFor(profile.Id)}");
        return 0;
    }

    public static string StepList => string.Join(Environment.NewLine, PortSteps.All.Select(s => $"          {s.Id,-9} {s.Title}{(s.NeedsPlayer ? " (с владельцем)" : "")}"));

    private static string Mark(PortStatus? status) => status switch
    {
        PortStatus.Verified => "✅",
        PortStatus.Fixed => "🔧",
        PortStatus.Likely => "🟡",
        PortStatus.Skipped => "⏭",
        PortStatus.Failed => "❌",
        _ => "·",
    };

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // Файл-переопределение рядом с exe: его же подхватывают все команды Probe и бот (ProfileCatalog.Default)
    private static string DefaultProfilePath(string id)
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles", id + ".jsonc");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, new ProfileCatalog().ReadJson(id), new UTF8Encoding(false));
        }

        return path;
    }
}

internal enum PortStatus
{
    /// <summary>Значение из профиля подтверждено.</summary>
    Verified,
    /// <summary>Найдено новое значение и записано в профиль.</summary>
    Fixed,
    /// <summary>Правдоподобно, но без подтверждения в игре.</summary>
    Likely,
    Skipped,
    Failed,
}

internal sealed record PortResult(PortStatus Status, string Text)
{
    public static PortResult Ok(string text) => new(PortStatus.Verified, text);
    public static PortResult Likely(string text) => new(PortStatus.Likely, text);
    public static PortResult Skip(string text) => new(PortStatus.Skipped, text);
    public static PortResult Fail(string text) => new(PortStatus.Failed, text);
}

internal sealed record PortStep(string Id, string Title, bool NeedsPlayer, Func<PortContext, PortResult> Run);

/// <summary>Что уже пройдено: port-&lt;сервер&gt;.json рядом с Probe. Только на этой машине, в репозиторий не идёт.</summary>
internal sealed class PortProgress
{
    private readonly string _path;
    private readonly Dictionary<string, Entry> _entries;

    private PortProgress(string path, Dictionary<string, Entry> entries)
    {
        _path = path;
        _entries = entries;
    }

    public static string PathFor(string id) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"port-{id}.json");

    public static PortProgress Load(string id)
    {
        var path = PathFor(id);
        var entries = File.Exists(path)
            ? JsonConvert.DeserializeObject<Dictionary<string, Entry>>(ReadShared(path)) ?? new()
            : new Dictionary<string, Entry>();
        return new PortProgress(path, entries);
    }

    public bool IsDone(string step) => Status(step) is PortStatus.Verified or PortStatus.Fixed;
    public PortStatus? Status(string step) => _entries.TryGetValue(step, out var e) ? e.Status : null;
    public string Text(string step) => _entries.TryGetValue(step, out var e) ? $"{e.Text} ({e.When:dd.MM HH:mm})" : "не запускался";

    public void Set(string step, PortResult result)
    {
        _entries[step] = new Entry { Status = result.Status, Text = result.Text, When = DateTime.Now };
        WriteShared(_path, JsonConvert.SerializeObject(_entries, Formatting.Indented, new Newtonsoft.Json.Converters.StringEnumConverter()));
    }

    internal static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    internal static void WriteShared(string path, string text, bool bom = false)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream, new UTF8Encoding(bom));
        writer.Write(text);
    }

    private sealed class Entry
    {
        public PortStatus Status { get; set; }
        public string Text { get; set; } = "";
        public DateTime When { get; set; }
    }
}

/// <summary>Всё, что нужно шагу: клиент, текущий профиль (перечитывается после каждой правки), вопросы владельцу.</summary>
internal sealed class PortContext
{
    private readonly string _path;
    private SkillNames? _names;

    public PortContext(GameProcess game, string path)
    {
        Game = game;
        _path = path;
        Data = ProfileJson.Parse(PortProgress.ReadShared(path));
    }

    public GameProcess Game { get; }
    public ProfileData Data { get; private set; }
    public bool Interactive => !Console.IsInputRedirected;
    /// <summary>Что записано в профиль за текущий шаг.</summary>
    public List<string> Changes { get; } = new();

    public SkillNames Names => _names ??= SkillNames.LoadFromGameDirectory(Path.GetDirectoryName(Game.MainModulePath)!, Data.GameFiles.Pck, out _);

    public WorldReader Reader() => new(Game, Game.MainModuleBase, Data, Names.Get);

    public uint Module => Game.MainModuleBase;
    public bool InModule(uint address) => address >= Module && address < Module + (uint)Game.MainModuleSize;

    /// <summary>[[модуль + база] + game]; 0 — не читается.</summary>
    public uint GameObject()
        => Game.TryReadUInt32(Module + Data.Base.BasePointer, out var b) && b != 0 && Game.TryReadUInt32(b + Data.Base.Game, out var g) ? g : 0;

    public uint HostAddress() => GameObject() is var g and not 0 && Game.TryReadUInt32(g + Data.Host.Struct, out var h) ? h : 0;

    public const int HostSize = 0x2000;

    public byte[] HostBlock() => Game.ReadBytes(HostAddress(), HostSize);

    /// <summary>Записывает значение в профиль и перечитывает его.</summary>
    public void Set(string path, uint value, string why)
    {
        var raw = File.ReadAllBytes(_path);
        var bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
        var text = PortProgress.ReadShared(_path);
        var patched = JsoncPatch.Set(text, path, $"\"0x{value:X}\"")
                      ?? throw new InvalidOperationException($"в профиле нет поля {path} — впишите его (0x{value:X}) руками");
        var data = ProfileJson.Parse(patched);
        PortProgress.WriteShared(_path, patched, bom);
        Data = data;
        Changes.Add($"{path} = 0x{value:X} ({why})");
        Console.WriteLine($"  ✏️ {path} = 0x{value:X} — {why}");
    }

    /// <summary>Ответ владельца; null — пропустить (или владельца нет).</summary>
    public string? Ask(string question)
    {
        if (!Interactive)
            return null;
        Console.Write($"  ❓ {question} > ");
        var answer = Console.ReadLine()?.Trim();
        return answer is null || answer.Equals("с", StringComparison.OrdinalIgnoreCase) || answer.Equals("s", StringComparison.OrdinalIgnoreCase)
            ? null
            : answer;
    }

    /// <summary>Да (Enter, «д»), нет («н»), пропустить — null.</summary>
    public bool? Confirm(string question)
        => Ask(question + " (Enter — да, н — нет)") is { } a ? a.Length == 0 || a.StartsWith("д", StringComparison.OrdinalIgnoreCase) || a.StartsWith("y", StringComparison.OrdinalIgnoreCase) : null;

    /// <summary>Действия в игре по шагам; последний — «Нажмите Enter». false — владелец пропустил.</summary>
    public bool Do(params string[] steps)
    {
        if (!Interactive)
            return false;
        Console.WriteLine("  В игре:");
        for (var i = 0; i < steps.Length; i++)
            Console.WriteLine($"    {i + 1}. {steps[i]}");
        return Ask($"{steps.Length + 1}. Нажмите Enter") is not null;
    }

    /// <summary>Записывает блок памяти раз в <paramref name="periodMs"/> мс, <paramref name="seconds"/> секунд.</summary>
    public List<(double Ms, byte[] Data)> Record(Func<byte[]> read, double seconds, int periodMs = 100)
    {
        var samples = new List<(double, byte[])>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Console.Write($"  ⏺ запись {seconds:0} с ");
        var dots = 0;
        while (watch.Elapsed.TotalSeconds < seconds)
        {
            samples.Add((watch.Elapsed.TotalMilliseconds, read()));
            if (watch.Elapsed.TotalSeconds > dots + 1)
            {
                dots++;
                Console.Write('.');
            }

            Thread.Sleep(periodMs);
        }

        Console.WriteLine(" готово");
        return samples;
    }

    /// <summary>Строка UTF-16 по адресу, если похожа на текст (ник, название); иначе null.</summary>
    public string? Text(uint address) => ScanCommands.TryString(Game, address);
}
