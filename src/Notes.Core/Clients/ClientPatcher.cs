using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Notes.Core.Logging;
using Notes.Core.Memory;
using Notes.Core.Profiles;

namespace Notes.Core.Clients;

/// <summary>Какой exe лежит по пути относительно рецепта правки.</summary>
public enum ClientPatchState
{
    /// <summary>Правки стоят.</summary>
    Patched,

    /// <summary>Исходный exe — можно применять правки (клиент должен быть закрыт: файл занят).</summary>
    Original,

    /// <summary>Ни исходник, ни изменённый — другой билд (обновление игры?): рецепт не подходит.</summary>
    Unknown,

    /// <summary>Файла нет по пути.</summary>
    Missing,

    /// <summary>Файл есть, но не читается (нет прав, занят без общего доступа): что в нём — неизвестно.</summary>
    Unreadable,
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

    /// <summary>
    /// Куда откладывается бэкап прежнего билда, когда игра обновилась: рядом с exe, с отпечатком файла в имени —
    /// ничего не теряется, а имя <see cref="BackupPath"/> освобождается под оригинал нового билда.
    /// </summary>
    public static string OldBuildBackupPath(string clientPath, string sha256Hex) => clientPath + "." + sha256Hex.Substring(0, 8) + ".orig";

    /// <summary>Состояние файла: правки, исходник, чужой билд, файла нет или он не читается.</summary>
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
            return ClientPatchState.Unreadable;
        }
    }

    /// <summary>
    /// Собрать изменённый файл из исходного — все проверки, без диска (тестам файлы не нужны). Не тот билд,
    /// неверный рецепт — <see cref="ClientPatchException"/>, файл-источник не используется дальше.
    /// </summary>
    public static byte[] Build(byte[] original, ClientPatchRecipe recipe) => Build(original, Sha256Hex(original), recipe);

    /// <param name="sha">sha256 <paramref name="original"/> — уже посчитан вызывающим (файл на десятки мегабайт).</param>
    private static byte[] Build(byte[] original, string sha, ClientPatchRecipe recipe)
    {
        switch (State(sha, recipe))
        {
            case ClientPatchState.Patched:
                throw new ClientPatchException("Правки уже стоят — делать нечего");
            case ClientPatchState.Unknown:
                throw new ClientPatchException("Это не тот билд клиента (обновление игры?) — рецепт не подходит");
        }

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

        var sha = Sha256Hex(original);
        if (State(sha, recipe) == ClientPatchState.Patched)
        {
            log.Info("Правки уже стоят — делать нечего");
            return ClientPatchResult.AlreadyPatched;
        }

        var patched = Build(original, sha, recipe);

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

        switch (Check(clientPath, recipe))
        {
            case ClientPatchState.Original:
                throw new ClientPatchException("Файл уже исходный — откатывать нечего");
            case ClientPatchState.Unreadable:
                throw new ClientPatchException("Файл клиента не читается (запущен? нет прав?) — откат не выполнен");
            case ClientPatchState.Unknown:
                throw new ClientPatchException($"В exe не наши правки (обновление клиента?) — откат не выполнен. Оригинал лежит рядом: {backup}");
        }

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
                var existing = Sha256Hex(ReadAll(backup));
                if (Same(existing, recipe.OriginalSha256))
                    return;

                // Бэкап от прежнего билда (игра обновилась): откладываем под именем с отпечатком и кладём свежий.
                // Чужой файл при этом не пропадает — его имя видно в сообщении об ошибке, если что-то помешает.
                var stale = OldBuildBackupPath(clientPath, existing);
                log.Info($"рядом лежит бэкап прежнего билда (sha256 {existing.Substring(0, 8)}…) — отложен: {stale}");
                File.Delete(stale); // тот же билд уже откладывали раньше — заменяем (содержимое одинаковое)
                File.Move(backup, stale);
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
        IReadOnlyList<PeSection> table;
        try
        {
            table = PeHeaders.Sections((offset, count) => Slice(data, offset, count));
        }
        catch (FormatException e)
        {
            throw new ClientPatchException($"Файл не похож на exe ({e.Message}) — файл битый или рецепт неверен");
        }

        var sections = new Dictionary<string, Section>(table.Count);
        foreach (var section in table)
            sections[section.Name] = new Section(section.VirtualSize, section.RawSize, (int)section.RawOffset);
        return sections;
    }

    /// <summary>Кусок заголовка файла для <see cref="PeHeaders"/>; за концом файла — «не exe».</summary>
    private static byte[] Slice(byte[] data, uint offset, int count)
    {
        if ((long)offset + count > data.Length)
            throw new FormatException("заголовок за концом файла");

        var part = new byte[count];
        Array.Copy(data, offset, part, 0, count);
        return part;
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
