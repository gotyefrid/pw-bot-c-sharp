using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Notes.Core.Logging;
using Notes.Core.Profiles;

namespace Notes.Core.Clients;

/// <summary>Какой exe лежит по пути относительно рецепта правки.</summary>
public enum ClientPatchState
{
    /// <summary>Правки стоят.</summary>
    Patched,

    /// <summary>Исходный exe — можно применять правки (клиент должен быть закрыт: файл занят).</summary>
    Original,

    /// <summary>Ни исходник, ни изменённый — другой билд (обновление игры?), либо файл не прочитался: рецепт не подходит.</summary>
    Unknown,

    /// <summary>Файла нет по пути.</summary>
    Missing,
}

/// <summary>Правка не вышла и почему — показывается пользователю как есть.</summary>
public sealed class ClientPatchException(string message) : Exception(message);

/// <summary>Чем кончилась правка файла.</summary>
public enum ClientPatchResult
{
    /// <summary>Правки применены и записаны.</summary>
    Patched,

    /// <summary>Правки уже стояли — делать нечего.</summary>
    AlreadyPatched,
}

/// <summary>
/// Патчер exe клиента по рецепту профиля. <see cref="Build"/> — чистое преобразование: все проверки в памяти
/// (sha исходника, контрольная сумма до правок, ожидаемые байты, сумма после, sha результата), файл не тронут,
/// пока всё не сошлось. <see cref="PatchFile"/> и <see cref="Restore"/> — работа с диском: оригинал хранится
/// в .orig рядом с exe (создаётся один раз, не перезаписывается), запись — атомарной заменой через временный
/// файл: на диске оказывается либо старый файл целиком, либо новый. Наружу вылетает только
/// <see cref="ClientPatchException"/> — окно ничего другого не ждёт. Клиент при записи должен быть закрыт.
/// </summary>
public static class ClientPatcher
{
    public static string BackupPath(string clientPath) => clientPath + ".orig";

    /// <summary>Состояние файла: правки, исходник, чужой билд или файла нет.</summary>
    public static ClientPatchState Check(string? clientPath, ClientPatchRecipe recipe)
    {
        if (clientPath is null || !File.Exists(clientPath))
            return ClientPatchState.Missing;

        try
        {
            return State(Sha256Hex(ReadAll(clientPath)), recipe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ClientPatchState.Unknown; // не прочитали — не проверили
        }
    }

    /// <summary>
    /// Собрать изменённый файл из исходного — все проверки, без диска (тестам файлы не нужны). Не тот билд,
    /// неверный рецепт — <see cref="ClientPatchException"/>, файл-источник не используется дальше.
    /// </summary>
    public static byte[] Build(byte[] original, ClientPatchRecipe recipe)
    {
        var sha = Sha256Hex(original);
        if (Same(sha, recipe.PatchedSha256))
            throw new ClientPatchException("Правки уже стоят — делать нечего");
        if (!Same(sha, recipe.OriginalSha256))
            throw new ClientPatchException("Это не тот билд клиента (обновление игры?) — рецепт не подходит");

        if (recipe.Digest is not { } digest)
            return ApplyEdits(original, recipe, null);

        if (digest.DigestLength == 0 || digest.HashSections.Count == 0)
            throw new ClientPatchException("Спека контрольной суммы пуста — рецепт неверен");
        if (!DigestMatches(original, digest))
            throw new ClientPatchException("Контрольная сумма исходника не сходится — рецепт неверен");

        return ApplyEdits(original, recipe, digest);
    }

    /// <summary>
    /// Применить правки к файлу: собрать в памяти (<see cref="Build"/>), сохранить оригинал в .orig, записать
    /// атомарной заменой. Лог — по ходу: строка про бэкап видна, даже если запись потом не удалась.
    /// </summary>
    public static ClientPatchResult PatchFile(string clientPath, ClientPatchRecipe recipe, ILogger log)
    {
        byte[] original;
        try
        {
            original = ReadAll(clientPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ClientPatchException($"Файл не читается: {e.Message}");
        }

        if (Same(Sha256Hex(original), recipe.PatchedSha256))
        {
            log.Info("Правки уже стоят — делать нечего");
            return ClientPatchResult.AlreadyPatched;
        }

        var patched = Build(original, recipe);

        foreach (var edit in recipe.Edits)
            log.Info($"правка 0x{edit.Offset:X}: {Describe(edit.Expected)} → {Describe(edit.New)}  ({edit.Note})");
        if (recipe.Digest is { } spec)
            log.Info($"контрольная сумма секции {spec.Section} пересчитана");

        WriteBackup(clientPath, recipe, log);
        WriteFile(clientPath, patched);
        log.Info($"правки записаны: {clientPath} (sha256 {Sha256Hex(patched).Substring(0, 16)}…)");
        return ClientPatchResult.Patched;
    }

    /// <summary>
    /// Вернуть оригинал из бэкапа .orig. Сверяются оба файла: бэкап — что это оригинал рецепта, текущий — что
    /// откат ведёт из нашего изменённого файла (или файла нет). Поверх чужого билда (обновление игры) старый
    /// оригинал не кладём.
    /// </summary>
    public static void Restore(string clientPath, ClientPatchRecipe recipe, ILogger log)
    {
        var backup = BackupPath(clientPath);
        if (!File.Exists(backup))
            throw new ClientPatchException($"Бэкапа нет: {backup} — откатывать нечего");

        var state = Check(clientPath, recipe);
        if (state == ClientPatchState.Original)
            throw new ClientPatchException("Файл уже исходный — откатывать нечего");
        if (state is not (ClientPatchState.Patched or ClientPatchState.Missing))
            throw new ClientPatchException($"В exe не наши правки (обновление клиента?) — откат не выполнен. Оригинал лежит рядом: {backup}");

        byte[] original;
        try
        {
            original = ReadAll(backup);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ClientPatchException($"Бэкап не читается: {e.Message}");
        }

        if (!Same(Sha256Hex(original), recipe.OriginalSha256))
            throw new ClientPatchException($"{backup} — не оригинал этого рецепта, откат не выполнен");

        WriteFile(clientPath, original);
        log.Info($"возвращён исходный файл: {clientPath}");
    }

    // ── Внутреннее ──────────────────────────────────────────────────────────

    private static byte[] ApplyEdits(byte[] original, ClientPatchRecipe recipe, ImageDigest? digest)
    {
        var data = (byte[])original.Clone();
        foreach (var edit in recipe.Edits)
        {
            if (edit.Expected.Length == 0 || (long)edit.Offset + edit.Expected.Length > data.Length)
                throw new ClientPatchException($"Правка 0x{edit.Offset:X} («{edit.Note}») вне файла — рецепт неверен");

            var offset = (int)edit.Offset;
            for (var i = 0; i < edit.Expected.Length; i++)
                if (data[offset + i] != edit.Expected[i])
                    throw new ClientPatchException($"По смещению 0x{edit.Offset:X} не те байты («{edit.Note}») — это не тот билд, файл не изменён");

            edit.New.CopyTo(data, offset);
        }

        if (digest is not null)
            ComputeDigest(data, digest).CopyTo(data, SectionRaw(data, digest.Section).RawOffset + (int)digest.DigestOffset);

        var patched = Sha256Hex(data);
        if (!Same(patched, recipe.PatchedSha256))
            throw new ClientPatchException("Собранный файл не совпал с эталонным sha256 — рецепт неверен");
        return data;
    }

    private static ClientPatchState State(string sha, ClientPatchRecipe recipe)
        => Same(sha, recipe.PatchedSha256) ? ClientPatchState.Patched
         : Same(sha, recipe.OriginalSha256) ? ClientPatchState.Original
         : ClientPatchState.Unknown;

    /// <summary>Бэкап: один раз и навсегда. Через временный файл — прервётся, останется .tmp, но не битый .orig.</summary>
    private static void WriteBackup(string clientPath, ClientPatchRecipe recipe, ILogger log)
    {
        var backup = BackupPath(clientPath);
        try
        {
            if (File.Exists(backup))
            {
                if (!Same(Sha256Hex(ReadAll(backup)), recipe.OriginalSha256))
                    throw new ClientPatchException($"{backup} — не оригинал этого рецепта. Разберитесь с файлом вручную, правки не применены");
                return;
            }

            var temp = backup + ".tmp";
            File.Copy(clientPath, temp);
            File.Move(temp, backup); // существует уже — откажет: бэкап не перезаписываем даже в гонке
            log.Info($"оригинал сохранён: {backup}");
        }
        catch (ClientPatchException)
        {
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ClientPatchException($"Не удалось сохранить оригинал ({e.Message}) — прав на папку не хватает? Файл не изменён");
        }
    }

    /// <summary>Запись атомарной заменой (ReplaceFile): «наполовину записанный» файл невозможен.</summary>
    private static void WriteFile(string path, byte[] data)
    {
        var temp = path + ".patch.tmp";
        try
        {
            File.WriteAllBytes(temp, data);
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path); // файла нет (восстановление после удаления) — создать атомарным переносом
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            throw new ClientPatchException($"Не удалось записать файл ({e.Message}) — клиент запущен? Закройте игру и повторите");
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // Временный файл мог остаться — следующий прогон его перезапишет
            }
        }
    }

    /// <summary>Чтение с FileShare.ReadWrite: файл могут держать клиент и другие копии бота (правило проекта).</summary>
    private static byte[] ReadAll(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(stream);
        return reader.ReadBytes((int)stream.Length);
    }

    private static bool DigestMatches(byte[] data, ImageDigest spec)
    {
        var section = SectionRaw(data, spec.Section);
        var stored = new byte[(int)spec.DigestLength];
        Array.Copy(data, section.RawOffset + spec.DigestOffset, stored, 0, spec.DigestLength);
        return ComputeDigest(data, spec).SequenceEqual(stored);
    }

    /// <summary>Формула контрольной суммы: SHA-256(секция[0..HeaderLength] + каждая из HashSections куском min(VirtualSize, RawSize)).</summary>
    private static byte[] ComputeDigest(byte[] data, ImageDigest spec)
    {
        var section = SectionRaw(data, spec.Section);
        if (spec.HeaderLength > section.RawSize)
            throw new ClientPatchException($"Секция {spec.Section} короче заголовка контрольной суммы — рецепт неверен");

        using var sha = SHA256.Create();
        sha.TransformBlock(data, section.RawOffset, (int)spec.HeaderLength, null, 0);
        foreach (var name in spec.HashSections)
        {
            var part = SectionRaw(data, name);
            sha.TransformBlock(data, part.RawOffset, (int)Math.Min(part.VirtualSize, part.RawSize), null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return sha.Hash!;
    }

    private sealed record Section(uint VirtualSize, uint RawSize, int RawOffset);

    private static Section SectionRaw(byte[] data, string name)
    {
        if (Sections(data).TryGetValue(name, out var section))
            return section;

        throw new ClientPatchException($"В файле нет секции {name} — рецепт не подходит к этому билду");
    }

    /// <summary>Секции PE: имя → (VirtualSize, SizeOfRawData, файл-смещение сырых данных).</summary>
    private static Dictionary<string, Section> Sections(byte[] data)
    {
        if (data.Length < 0x40 || data[0] != 0x4D || data[1] != 0x5A)
            throw new ClientPatchException("Нет заголовка MZ — это не exe");
        var pe = BitConverter.ToUInt32(data, 0x3C);
        if (pe + 44 >= data.Length || BitConverter.ToUInt32(data, (int)pe) != 0x00004550)
            throw new ClientPatchException("Нет заголовка PE — это не exe");

        var count = BitConverter.ToUInt16(data, (int)pe + 6);
        var options = BitConverter.ToUInt16(data, (int)pe + 20);
        var sections = new Dictionary<string, Section>(count);
        for (var i = 0; i < count; i++)
        {
            var header = (int)pe + 24 + options + i * 40;
            if (header + 40 > data.Length)
                throw new ClientPatchException("Таблица секций вне файла — файл битый или рецепт неверен");

            var name = Encoding.ASCII.GetString(data, header, 8).TrimEnd('\0');
            sections[name] = new Section(BitConverter.ToUInt32(data, header + 8),
                BitConverter.ToUInt32(data, header + 16), BitConverter.ToInt32(data, header + 20));
        }

        return sections;
    }

    private static string Sha256Hex(byte[] data)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data);
        var text = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            text.Append(b.ToString("x2"));
        return text.ToString();
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Байты для лога: «74 4F».</summary>
    private static string Describe(byte[] bytes) => BitConverter.ToString(bytes).Replace('-', ' ');
}
