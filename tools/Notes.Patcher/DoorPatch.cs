using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Notes.Patcher;

/// <summary>
/// Патч «двери подземелья». У двух моделей дверей подземелья (覆霜城门 и 覆霜宫门) клиент держит
/// файлы столкновений прямо в client.exe и берёт их из памяти. В каждом таком файле одну грань формы
/// уносим на 1000 м вверх — форма становится пустой: дверь выглядит закрытой, но пройти сквозь неё можно.
/// <para>
/// Стены, пол, деревья и обычные NPC не затрагиваются: правка только в двух встроенных файлах дверей.
/// Места ищем по форме данных, а не по адресам: после обновления игры патчер откажется писать,
/// пока клиент не разберут заново.
/// </para>
/// </summary>
public static class DoorPatch
{
    public const int SiteCount = 2;

    /// <summary>Заголовок встроенного файла столкновений двери.</summary>
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MOXBpbce");

    /// <summary>Заголовок выпуклых форм внутри файла двери.</summary>
    private const string HullHeader = "V0.2======Convex_Hull_Data_Start=========";

    private const int VersionOffset = 8;
    private const int HullHeaderOffset = 0x118;
    private const int HullSizeOffset = 0x145;
    private const int PointCountOffset = 0x149;
    private const uint OriginalHullSize = 0x578;
    private const uint OriginalPointCount = 16;

    /// <summary>
    /// Что правим в каждом файле двери. Форма двери — одна выпуклая фигура: 16 точек (её коробка) и 10 граней
    /// (число по смещению 0x20D; запись грани — нормаль и d, n·p ≤ d). Первая грань — «не ниже пола»:
    /// n = (0,−1,0), d = 0.279. Ставим d = −1000: «не ниже 1000 м над дверью» — фигура становится пустой,
    /// сквозь дверь можно пройти. Число граней не трогаем: с 0 граней ломаются столкновения на всей карте,
    /// с 1 гранью дверь остаётся сплошной коробкой по точкам. Смещения — от начала «MOXBpbce».
    /// </summary>
    private const int FaceCountOffset = 0x20D;
    private const uint FaceCount = 10;

    private static readonly (int Offset, uint Original, uint Patched)[] Edits =
    {
        (0x21D, F(0.27901f, 0x3E8EDA65), F(-1000f)), // d первой грани: 0.279 → −1000
    };

    private static uint F(float value) => BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);

    /// <summary>Точное значение из файла; число рядом — только для чтения человеком.</summary>
    private static uint F(float _, uint exact) => exact;

    public enum State
    {
        Original,
        Patched,
        Unknown,
        NotClient,
        Unreadable,
    }

    private sealed record Site(int Offset, bool Patched);

    private static void WriteEdits(byte[] data, int site, bool patched)
    {
        foreach (var e in Edits)
            BitConverter.GetBytes(patched ? e.Patched : e.Original).CopyTo(data, site + e.Offset);
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

        return DetectData(data, out details);
    }

    public static void Apply(string path, Action<string> log)
    {
        var original = File.ReadAllBytes(path);
        var state = DetectData(original, out var details);
        if (state == State.Patched)
        {
            log("Клиент уже пропатчен — ничего не делаю.");
            return;
        }

        if (state != State.Original)
            throw new PatchException("Не нашёл двери подземелья в этом клиенте (" + details + "). "
                                     + "Похоже, клиент обновился — нужен новый разбор, файл не изменён.");

        var sites = FindSites(original);
        var data = (byte[])original.Clone();
        foreach (var site in sites)
            WriteEdits(data, site.Offset, patched: true);

        if (DetectData(data, out var after) != State.Patched)
            throw new PatchException("Проверка после правки не сошлась — файл не записан. " + after);

        SaveBackup(path, original, log);
        WriteAtomically(path, data);
        log($"Готово: у {sites.Count} дверей убрано столкновение. Двери останутся закрытыми на вид.");
    }

    public static void Revert(string path, Action<string> log)
    {
        var backup = path + ".door.orig";
        if (File.Exists(backup))
        {
            var original = File.ReadAllBytes(backup);
            if (DetectData(original, out var backupDetails) != State.Original)
                throw new PatchException("Резервная копия дверей не подходит к этому клиенту (" + backupDetails + "). "
                                         + "Похоже, игра обновилась — откат не делаю.");
            if (File.ReadAllBytes(path).SequenceEqual(original))
            {
                log("Клиент и так без этого патча — откатывать нечего.");
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
        if (DetectData(data, out var details) != State.Patched)
        {
            log("Клиент и так без этого патча — откатывать нечего.");
            return;
        }

        var sites = FindSites(data);
        var restored = (byte[])data.Clone();
        foreach (var site in sites)
            WriteEdits(restored, site.Offset, patched: false);

        if (DetectData(restored, out var after) != State.Original)
            throw new PatchException("Проверка после отката не сошлась — файл не записан. " + after);

        WriteAtomically(path, restored);
        log($"Откат сделан обратной правкой ({sites.Count} дверей). Файла .door.orig не было.");
    }

    private static State DetectData(byte[] data, out string details)
    {
        details = "";
        if (!LooksLikeClient(data))
        {
            details = "это не 32-битный client.exe";
            return State.NotClient;
        }

        var sites = FindSites(data);
        if (sites.Count != SiteCount)
        {
            details = $"дверей с нужным устройством: {sites.Count}, ожидалось {SiteCount}";
            return State.Unknown;
        }

        var original = sites.Count(s => !s.Patched);
        var patched = sites.Count(s => s.Patched);
        if (original == sites.Count)
        {
            details = $"дверей: {sites.Count}, столкновение включено";
            return State.Original;
        }

        if (patched == sites.Count)
        {
            details = $"дверей: {sites.Count}, столкновение убрано";
            return State.Patched;
        }

        details = "у дверей смешанное состояние — файл правили вручную";
        return State.Unknown;
    }

    /// <summary>
    /// Ищет встроенные файлы дверей по заголовку «MOXBpbce» и проверяет, что внутри лежит
    /// ожидаемый набор выпуклых форм. Возвращает только полностью подходящие места.
    /// </summary>
    private static List<Site> FindSites(byte[] data)
    {
        var result = new List<Site>();
        var start = 0;
        while (start <= data.Length - Magic.Length)
        {
            var at = IndexOf(data, Magic, start);
            if (at < 0)
                break;
            start = at + 1;

            if (at + Edits.Max(e => e.Offset) + 4 > data.Length)
                continue;
            if (BitConverter.ToUInt32(data, at + VersionOffset) != 1)
                continue;
            if (Encoding.ASCII.GetString(data, at + HullHeaderOffset, HullHeader.Length) != HullHeader)
                continue;
            if (BitConverter.ToUInt32(data, at + HullSizeOffset) != OriginalHullSize)
                continue;
            if (BitConverter.ToUInt32(data, at + PointCountOffset) != OriginalPointCount)
                continue;

            // число граней формы двери — 10; иное значит, что файл правили вручную
            if (BitConverter.ToUInt32(data, at + FaceCountOffset) != FaceCount)
                continue;
            var isOriginal = Edits.All(e => BitConverter.ToUInt32(data, at + e.Offset) == e.Original);
            var isPatched = Edits.All(e => BitConverter.ToUInt32(data, at + e.Offset) == e.Patched);
            if (!isOriginal && !isPatched)
                continue;
            result.Add(new Site(at, isPatched));
        }

        return result;
    }

    private static int IndexOf(byte[] data, byte[] pattern, int start)
    {
        for (var i = start; i <= data.Length - pattern.Length; i++)
        {
            var j = 0;
            while (j < pattern.Length && data[i + j] == pattern[j])
                j++;
            if (j == pattern.Length)
                return i;
        }

        return -1;
    }

    private static bool LooksLikeClient(byte[] data)
    {
        if (data.Length < 0x40 || data[0] != (byte)'M' || data[1] != (byte)'Z')
            return false;
        var pe = BitConverter.ToInt32(data, 0x3C);
        if (pe < 0 || pe + 0x18 > data.Length)
            return false;
        if (data[pe] != (byte)'P' || data[pe + 1] != (byte)'E' || data[pe + 2] != 0 || data[pe + 3] != 0)
            return false;
        return BitConverter.ToUInt16(data, pe + 4) == 0x014C;
    }

    /// <summary>
    /// Резервная копия — своя, рядом с клиентом: client.exe.door.orig. Общий client.exe.orig
    /// принадлежит патчу полёта, его не трогаем. Если рядом лежит копия другого билда — откладываем.
    /// </summary>
    private static void SaveBackup(string path, byte[] current, Action<string> log)
    {
        var backup = path + ".door.orig";
        try
        {
            if (File.Exists(backup))
            {
                var existing = File.ReadAllBytes(backup);
                if (existing.SequenceEqual(current))
                    return;

                var stale = path + "." + Sha256Hex(existing).Substring(0, 8) + ".door.orig";
                log($"Рядом лежала копия прежнего билда — отложена: {Path.GetFileName(stale)}");
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
        var text = new StringBuilder(hash.Length * 2);
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
}
