using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Notes.Patcher;

/// <summary>
/// Патч «быстрый полёт». В клиенте есть шесть мест вида «скорость += dt × 3.0» перед ограничением максимальной
/// скоростью. Заменяем умножение на константу 5.0 — скорость набирается за один тик, разгона нет.
/// <para>
/// Места ищем по форме кода (mulss xmm, [3.0]; addss xmm, xmm), а не по адресам: пока форма та же, патч переживёт
/// пересборку клиента. Если мест не шесть — клиент обновился и его надо разобрать заново, патчер ничего не пишет.
/// </para>
/// </summary>
public static class FlightPatch
{
    public const int SiteCount = 6;
    private const float SlowValue = 3.0f;
    private const float FastValue = 5.0f;

    public enum State
    {
        Original,
        Patched,
        Unknown,
        NotClient,
        Unreadable,
    }

    public static State Detect(string path, out string details)
    {
        details = "";
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception e)
        {
            details = "файл не читается: " + e.Message;
            return State.Unreadable;
        }

        PeImage pe;
        try
        {
            pe = PeImage.Load(data);
        }
        catch (Exception e)
        {
            details = "это не exe клиента: " + e.Message;
            return State.NotClient;
        }

        var slow = FindSites(pe, SlowValue, 0x59);
        var fast = FindSites(pe, FastValue, 0x10);
        details = $"мест с разгоном 3.0: {slow.Count}, мест с патчем: {fast.Count}";
        if (slow.Count == SiteCount && fast.Count == 0)
            return State.Original;
        if (fast.Count == SiteCount && slow.Count == 0)
            return State.Patched;
        return State.Unknown;
    }

    public static void Apply(string path, Action<string> log)
    {
        var original = File.ReadAllBytes(path);
        var data = (byte[])original.Clone();
        var pe = PeImage.Load(data);
        if (FindSites(pe, FastValue, 0x10).Count == SiteCount)
        {
            log("Клиент уже пропатчен — ничего не делаю.");
            return;
        }

        var sites = FindSites(pe, SlowValue, 0x59);
        if (sites.Count != SiteCount)
            throw new PatchException($"Не нашёл {SiteCount} мест разгона (нашёл {sites.Count}). "
                                     + "Похоже, клиент обновился — нужен новый разбор, файл не изменён.");
        var fastVa = pe.FindFloat(FastValue)
                     ?? throw new PatchException("Не нашёл в клиенте константу 5.0 — файл не изменён.");

        foreach (var site in sites)
        {
            data[site + 2] = 0x10; // mulss → movss
            BitConverter.GetBytes(fastVa).CopyTo(data, site + 4);
        }

        var check = PeImage.Load(data);
        if (FindSites(check, FastValue, 0x10).Count != SiteCount || FindSites(check, SlowValue, 0x59).Count != 0)
            throw new PatchException("Проверка после правки не сошлась — файл не записан.");

        SaveBackup(path, original, log);
        WriteAtomically(path, data);
        log($"Готово: пропатчено {sites.Count} мест. Полёт будет сразу на максимальной скорости.");
    }

    public static void Revert(string path, Action<string> log)
    {
        var backup = path + ".orig";
        if (File.Exists(backup))
        {
            if (File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(backup)))
            {
                log("Клиент и так оригинальный — откатывать нечего.");
                return;
            }

            try
            {
                File.Copy(backup, path, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new PatchException("Не удалось записать файл: закрой клиент игры и попробуй снова. " + e.Message);
            }

            log($"Оригинал восстановлен из {Path.GetFileName(backup)}.");
            return;
        }

        var data = File.ReadAllBytes(path);
        var pe = PeImage.Load(data);
        var sites = FindSites(pe, FastValue, 0x10);
        if (sites.Count == 0)
        {
            log("Клиент и так оригинальный — откатывать нечего.");
            return;
        }

        if (sites.Count != SiteCount)
            throw new PatchException($"Нашёл {sites.Count} мест с патчем вместо {SiteCount}. "
                                     + "Похоже, файл изменён вручную — откат не делаю.");
        var slowVa = pe.FindFloat(SlowValue)
                     ?? throw new PatchException("Не нашёл в клиенте константу 3.0 — откат не сделан.");

        foreach (var site in sites)
        {
            data[site + 2] = 0x59; // movss → mulss
            BitConverter.GetBytes(slowVa).CopyTo(data, site + 4);
        }

        var check = PeImage.Load(data);
        if (FindSites(check, SlowValue, 0x59).Count != SiteCount || FindSites(check, FastValue, 0x10).Count != 0)
            throw new PatchException("Проверка после отката не сошлась — файл не записан.");

        WriteAtomically(path, data);
        log($"Откат сделан обратной правкой ({sites.Count} мест). Файла .orig не было.");
    }

    /// <summary>
    /// Шесть инструкций «mulss xmm, [константа]», сразу за которыми идёт «addss xmm, xmm» (регистр, не память).
    /// Именно так выглядит разгон в движении персонажа; другие умножения на 3.0 (эффекты, камера) не подходят.
    /// </summary>
    private static List<int> FindSites(PeImage pe, float value, byte opcode)
    {
        var text = pe.Sections.FirstOrDefault(s => s.Name == ".text")
                   ?? throw new InvalidDataException("в файле нет секции .text");
        var result = new List<int>();
        var data = pe.Data;
        var start = (int)text.Raw;
        var end = (int)(text.Raw + text.RawSize) - 16;
        for (var i = start; i < end; i++)
        {
            if (data[i] != 0xF3 || data[i + 1] != 0x0F || data[i + 2] != opcode)
                continue;
            if ((data[i + 3] & 0xC7) != 0x05) // xmmN, [абсолютный адрес]
                continue;
            var constantVa = BitConverter.ToUInt32(data, i + 4);
            if (pe.ReadFloat(constantVa) is not { } actual || Math.Abs(actual - value) > 1e-4f)
                continue;
            var next = i + 8;
            if (data[next] == 0xF3 && data[next + 1] == 0x0F && data[next + 2] == 0x58 && data[next + 3] >= 0xC0)
                result.Add(i);
        }

        return result;
    }

    /// <summary>
    /// Бэкап оригинала: один раз на билд. Если рядом уже лежит .orig от прежнего билда (игра обновилась), он
    /// откладывается под именем с отпечатком — как это делает патчер бота, чтобы откат не вернул старый клиент.
    /// </summary>
    private static void SaveBackup(string path, byte[] current, Action<string> log)
    {
        var backup = path + ".orig";
        try
        {
            if (File.Exists(backup))
            {
                var existing = File.ReadAllBytes(backup);
                if (existing.SequenceEqual(current))
                    return;

                var stale = path + "." + Sha256Hex(existing).Substring(0, 8) + ".orig";
                log($"Рядом лежал бэкап прежнего билда — отложен: {Path.GetFileName(stale)}");
                File.Delete(stale);
                File.Move(backup, stale);
            }

            var temp = backup + ".tmp";
            File.Copy(path, temp);
            File.Move(temp, backup);
            log($"Оригинал сохранён: {Path.GetFileName(backup)}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(backup + ".tmp");
            }
            catch
            {
                // временный файл мог не создаться
            }

            throw new PatchException("Не удалось сохранить оригинал: закрой клиент игры и проверь права на папку. "
                                     + e.Message);
        }
    }

    private static string Sha256Hex(byte[] data)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(data);
        var text = new System.Text.StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            text.Append(b.ToString("x2"));
        return text.ToString();
    }

    private static void WriteAtomically(string path, byte[] data)
    {
        var temp = path + ".new";
        try
        {
            File.WriteAllBytes(temp, data);
            try
            {
                File.Replace(temp, path, destinationBackupFileName: null);
            }
            catch (IOException)
            {
                File.Copy(temp, path, overwrite: true);
                File.Delete(temp);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // ничего: временный файл мог не создаться
            }

            throw new PatchException("Не удалось записать файл: закрой клиент игры и попробуй снова. " + e.Message);
        }
    }

    private sealed class PeImage
    {
        public byte[] Data { get; private init; } = [];

        public uint ImageBase { get; private init; }

        public List<Section> Sections { get; } = [];

        public static PeImage Load(byte[] data)
        {
            if (data.Length < 0x40 || data[0] != (byte)'M' || data[1] != (byte)'Z')
                throw new InvalidDataException("нет заголовка MZ");
            var peOffset = BitConverter.ToInt32(data, 0x3C);
            if (peOffset < 0 || peOffset + 0x18 > data.Length
                || data[peOffset] != (byte)'P' || data[peOffset + 1] != (byte)'E'
                || data[peOffset + 2] != 0 || data[peOffset + 3] != 0)
                throw new InvalidDataException("нет заголовка PE");

            var machine = BitConverter.ToUInt16(data, peOffset + 4);
            if (machine != 0x014C)
                throw new InvalidDataException("это не 32-битный exe");

            var sectionCount = BitConverter.ToUInt16(data, peOffset + 6);
            var optionalSize = BitConverter.ToUInt16(data, peOffset + 20);
            var optional = peOffset + 24;
            if (optional + optionalSize > data.Length || optionalSize < 32)
                throw new InvalidDataException("битый заголовок PE");
            if (BitConverter.ToUInt16(data, optional) != 0x10B)
                throw new InvalidDataException("это не PE32");

            var image = new PeImage
            {
                Data = data,
                ImageBase = BitConverter.ToUInt32(data, optional + 28),
            };

            var table = optional + optionalSize;
            if (table + sectionCount * 40 > data.Length)
                throw new InvalidDataException("битая таблица секций");
            for (var i = 0; i < sectionCount; i++)
            {
                var at = table + i * 40;
                var section = new Section
                {
                    Name = System.Text.Encoding.ASCII.GetString(data, at, 8).TrimEnd('\0'),
                    VirtualSize = BitConverter.ToUInt32(data, at + 8),
                    Rva = BitConverter.ToUInt32(data, at + 12),
                    RawSize = BitConverter.ToUInt32(data, at + 16),
                    Raw = BitConverter.ToUInt32(data, at + 20),
                    Characteristics = BitConverter.ToUInt32(data, at + 36),
                };
                image.Sections.Add(section);
            }

            return image;
        }

        public long? VaToFile(uint va)
        {
            foreach (var section in Sections)
            {
                var start = ImageBase + section.Rva;
                var size = Math.Max(section.RawSize, section.VirtualSize);
                if (va < start || va - start >= size)
                    continue;
                var offset = section.Raw + (va - start);
                return offset + 4 <= Data.Length ? offset : null;
            }

            return null;
        }

        public float? ReadFloat(uint va)
            => VaToFile(va) is { } offset ? BitConverter.ToSingle(Data, (int)offset) : null;

        /// <summary>Адрес любой константы нужного значения: .rdata в первую очередь, затем прочие секции.</summary>
        public uint? FindFloat(float value)
        {
            var bytes = BitConverter.GetBytes(value);
            foreach (var section in Sections
                         .Where(s => s.RawSize >= 4)
                         .OrderBy(s => s.Name.StartsWith(".rdata", StringComparison.OrdinalIgnoreCase) ? 0
                             : (s.Characteristics & 0x20000000) == 0 ? 1 : 2)
                         .ThenBy(s => s.Raw))
            {
                var end = section.Raw + section.RawSize - 4;
                for (var offset = section.Raw; offset <= end; offset += 4)
                {
                    if (Data[offset] != bytes[0] || Data[offset + 1] != bytes[1]
                        || Data[offset + 2] != bytes[2] || Data[offset + 3] != bytes[3])
                        continue;
                    return ImageBase + section.Rva + (offset - section.Raw);
                }
            }

            return null;
        }
    }

    private sealed class Section
    {
        public string Name { get; init; } = "";

        public uint Rva { get; init; }

        public uint Raw { get; init; }

        public uint RawSize { get; init; }

        public uint VirtualSize { get; init; }

        public uint Characteristics { get; init; }
    }
}

public sealed class PatchException : Exception
{
    public PatchException(string message)
        : base(message)
    {
    }
}
