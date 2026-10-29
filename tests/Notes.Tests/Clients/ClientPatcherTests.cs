using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Notes.Core.Clients;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Xunit;

namespace Notes.Tests.Clients;

public class ClientPatcherTests
{
    // Мини-PE с теми же частями, что клиент Comeback: .text (правки), .mgbase (заголовок контрольной суммы 0x24 +
    // digest 0x20 + пустая зона стабов), .reloc. Движку нужны только заголовки секций, содержимое произвольное.
    private const int TextRaw = 0x200, TextLen = 0x40;
    private const int MgRaw = 0x400, MgLen = 0x200;
    private const int RelocRaw = 0x600, RelocLen = 0x20;

    // ── Сборка изменённого файла: чистое преобразование, без диска ───────────

    [Fact]
    public void BuildAppliesEditsAndRecomputesDigest()
    {
        var (file, recipe, expected) = BuildClient();

        Assert.Equal(expected, ClientPatcher.Build(file, recipe));
    }

    [Fact]
    public void BuildRefusesUnknownBuild()
    {
        var (file, recipe, _) = BuildClient();
        file[TextRaw + 5] ^= 0xFF; // другой билд: байты правок на месте, sha другой

        var e = Assert.Throws<ClientPatchException>(() => ClientPatcher.Build(file, recipe));
        Assert.Contains("не тот билд", e.Message);
    }

    [Fact]
    public void BuildRefusesWrongExpectedBytes()
    {
        var (file, recipe, _) = BuildClient();
        file[0x202] = 0xEE; // байты под правкой не те — след чужой сборки
        Digest(file).CopyTo(file, MgRaw + 0x24); // у чужого билда своя самосогласованная сумма
        var pinned = new ClientPatchRecipe { OriginalSha256 = Sha(file), PatchedSha256 = recipe.PatchedSha256, Edits = recipe.Edits, Digest = recipe.Digest };

        var e = Assert.Throws<ClientPatchException>(() => ClientPatcher.Build(file, pinned));
        Assert.Contains("не те байты", e.Message);
    }

    [Fact]
    public void BuildRefusesWrongDigestFormula()
    {
        var (file, recipe, _) = BuildClient();
        // sha оригинала верный, а формула суммы неверна (хэшируется не та секция)
        var wrong = new ClientPatchRecipe
        {
            OriginalSha256 = Sha(file),
            PatchedSha256 = recipe.PatchedSha256,
            Edits = recipe.Edits,
            Digest = new ImageDigest { Section = ".mgbase", HeaderLength = 0x24, DigestOffset = 0x24, DigestLength = 0x20, HashSections = [".reloc"] },
        };

        var e = Assert.Throws<ClientPatchException>(() => ClientPatcher.Build(file, wrong));
        Assert.Contains("не сходится", e.Message);
    }

    [Fact]
    public void BuildRefusesWhenResultShaMismatches()
    {
        var (file, recipe, _) = BuildClient();
        var broken = new ClientPatchRecipe { OriginalSha256 = recipe.OriginalSha256, PatchedSha256 = new string('0', 64), Edits = recipe.Edits, Digest = recipe.Digest };

        var e = Assert.Throws<ClientPatchException>(() => ClientPatcher.Build(file, broken));
        Assert.Contains("не совпал с эталонным sha256", e.Message);
    }

    [Fact]
    public void BuildWithoutDigestJustChecksEdits()
    {
        // Клиент без контрольной суммы образа: правки применяются как есть, поле суммы в файле не пересчитывается
        var (file, recipe, expected) = BuildClient();

        // Ожидание: правки применены, но поле суммы осталось от исходника (его без digest никто не трогает)
        var noDigestExpected = (byte[])expected.Clone();
        Digest(file).CopyTo(noDigestExpected, MgRaw + 0x24);
        var noDigest = new ClientPatchRecipe { OriginalSha256 = recipe.OriginalSha256, PatchedSha256 = Sha(noDigestExpected), Edits = recipe.Edits };

        Assert.Equal(noDigestExpected, ClientPatcher.Build(file, noDigest));
    }

    // ── Файл: правка, бэкап, откат ────────────────────────────────────────────

    [Fact]
    public void PatchFileWritesBackupAndRestores()
    {
        var (file, recipe, expected) = BuildClient();
        var path = Write(file);
        try
        {
            Assert.Equal(ClientPatchResult.Patched, ClientPatcher.PatchFile(path, recipe, NullLogger.Instance));
            Assert.Equal(expected, File.ReadAllBytes(path));
            Assert.True(File.Exists(ClientPatcher.BackupPath(path)));
            Assert.Equal(file, File.ReadAllBytes(ClientPatcher.BackupPath(path)));
            Assert.Equal(ClientPatchState.Patched, ClientPatcher.Check(path, recipe));

            // Второй раз — не ошибка, а «уже стоит»
            Assert.Equal(ClientPatchResult.AlreadyPatched, ClientPatcher.PatchFile(path, recipe, NullLogger.Instance));

            ClientPatcher.Restore(path, recipe, NullLogger.Instance);
            Assert.Equal(file, File.ReadAllBytes(path));
            Assert.Equal(ClientPatchState.Original, ClientPatcher.Check(path, recipe));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void PatchFileRefusesSuspiciousBackup()
    {
        var (file, recipe, _) = BuildClient();
        var path = Write(file);
        try
        {
            ClientPatcher.PatchFile(path, recipe, NullLogger.Instance);
            ClientPatcher.Restore(path, recipe, NullLogger.Instance);

            // .orig кто-то подменил — молча затирать его своим бэкапом нельзя
            var backup = ClientPatcher.BackupPath(path);
            var bytes = File.ReadAllBytes(backup);
            bytes[0] ^= 1;
            File.WriteAllBytes(backup, bytes);

            Assert.Throws<ClientPatchException>(() => ClientPatcher.PatchFile(path, recipe, NullLogger.Instance));
            Assert.Equal(bytes, File.ReadAllBytes(backup));
            Assert.Equal(file, File.ReadAllBytes(path));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void PatchFileRefusesWhenFileIsBusy()
    {
        var (file, recipe, _) = BuildClient();
        var path = Write(file);
        try
        {
            // Читать можно, писать нет — как будто клиент запущен и держит exe
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var e = Assert.Throws<ClientPatchException>(() => ClientPatcher.PatchFile(path, recipe, NullLogger.Instance));
                Assert.Contains("Не удалось записать", e.Message);
            }

            Assert.Equal(file, File.ReadAllBytes(path));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void RestoreChecksCurrentFileBeforeOverwritingIt()
    {
        var (file, recipe, _) = BuildClient();
        var path = Write(file);
        try
        {
            ClientPatcher.PatchFile(path, recipe, NullLogger.Instance);

            // Клиент обновился поверх наших правок — старый оригинал поверх нового билда не кладём
            var updated = (byte[])file.Clone();
            updated[TextRaw + 9] ^= 0xFF;
            updated[MgRaw + 0x60] ^= 0xFF;
            File.WriteAllBytes(path, updated);
            var e = Assert.Throws<ClientPatchException>(() => ClientPatcher.Restore(path, recipe, NullLogger.Instance));
            Assert.Contains("не наши правки", e.Message);
            Assert.Equal(updated, File.ReadAllBytes(path));

            // Уже исходный — откатывать нечего
            File.WriteAllBytes(path, file);
            Assert.Throws<ClientPatchException>(() => ClientPatcher.Restore(path, recipe, NullLogger.Instance));

            // Файл удалили — оригинал возвращается из бэкапа
            File.Delete(path);
            ClientPatcher.Restore(path, recipe, NullLogger.Instance);
            Assert.Equal(file, File.ReadAllBytes(path));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void CheckTellsStatesApart()
    {
        var (file, recipe, _) = BuildClient();
        var path = Write(file);
        try
        {
            Assert.Equal(ClientPatchState.Missing, ClientPatcher.Check(null, recipe));
            Assert.Equal(ClientPatchState.Missing, ClientPatcher.Check(Path.Combine(path, "нет такого.exe"), recipe));
            Assert.Equal(ClientPatchState.Original, ClientPatcher.Check(path, recipe));

            ClientPatcher.PatchFile(path, recipe, NullLogger.Instance);
            Assert.Equal(ClientPatchState.Patched, ClientPatcher.Check(path, recipe));

            var updated = (byte[])file.Clone();
            updated[TextRaw + 3] ^= 0xFF;
            File.WriteAllBytes(path, updated);
            Assert.Equal(ClientPatchState.Unknown, ClientPatcher.Check(path, recipe));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void CheckTellsUnreadableFromUnknownBuild()
    {
        var (file, recipe, _) = BuildClient();
        var path = Write(file);
        try
        {
            ClientPatcher.PatchFile(path, recipe, NullLogger.Instance); // бэкап есть — откату есть из чего

            // Файл держат без общего доступа — не прочитали, это не «другой билд»
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Equal(ClientPatchState.Unreadable, ClientPatcher.Check(path, recipe));
                var e = Assert.Throws<ClientPatchException>(() => ClientPatcher.Restore(path, recipe, NullLogger.Instance));
                Assert.Contains("не читается", e.Message);
            }
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void CheckReadsFileHeldOpenForWriting()
    {
        var (file, recipe, _) = BuildClient();
        var path = Write(file);
        try
        {
            // Файл держит открытой другая копия бота — чтение всё равно работает (FileShare.ReadWrite, правило проекта)
            using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
                Assert.Equal(ClientPatchState.Original, ClientPatcher.Check(path, recipe));
            }
        }
        finally
        {
            Cleanup(path);
        }
    }

    // ── Профиль ──────────────────────────────────────────────────────────────

    [Fact]
    public void ComebackProfilesCarryRecipeClassicDoesNot()
    {
        var catalog = new ProfileCatalog();

        // Что именно в рецептах — проверяют сквозные тесты ниже; здесь только проводка профилей
        Assert.NotNull(catalog.Load("comeback146").Data.Patch);
        Assert.NotNull(catalog.Load("comeback136").Data.Patch);
        Assert.Null(catalog.Load("pwclassic136").Data.Patch);
    }

    /// <summary>
    /// Сквозная проверка на настоящем клиенте: патчер из вшитого профиля строит ровно эталонную сборку
    /// f5913175…. Запускается, только если задана папка element клиента (NOTES_COMEBACK146_DIR) с бэкапом
    /// оригинала ElementClient.exe.orig; на машинах без папки пропускается.
    /// </summary>
    [Fact]
    public void Comeback146RecipeBuildsReferencePatch()
    {
        Assert.Equal(ClientPatchState.Patched, BuildFromProfile("comeback146", "NOTES_COMEBACK146_DIR"));
    }

    /// <summary>То же для Comeback 1.3.6: рецепт из профиля собирает эталон 7c176f26….</summary>
    [Fact]
    public void Comeback136RecipeBuildsReferencePatch()
    {
        Assert.Equal(ClientPatchState.Patched, BuildFromProfile("comeback136", "NOTES_COMEBACK136_DIR"));
    }

    /// <summary>
    /// Копирует оригинал клиента (ElementClient.exe.orig из папки element по переменной окружения) в темп,
    /// применяет рецепт из профиля и проверяет статус. Без переменной тест пропускается.
    /// </summary>
    private static ClientPatchState BuildFromProfile(string profileId, string envVariable)
    {
        var directory = Environment.GetEnvironmentVariable(envVariable) ?? "";
        var source = Path.Combine(directory, "ElementClient.exe.orig");
        Assert.SkipUnless(File.Exists(source), $"Не задана {envVariable} с ElementClient.exe.orig");

        var recipe = new ProfileCatalog().Load(profileId).Data.Patch!;
        var path = Write(File.ReadAllBytes(source));
        try
        {
            ClientPatcher.PatchFile(path, recipe, NullLogger.Instance);
            return ClientPatcher.Check(path, recipe);
        }
        finally
        {
            Cleanup(path);
        }
    }

    // ── Синтетический клиент ────────────────────────────────────────────────

    /// <summary>
    /// Мини-PE с тремя секциями, правильной контрольной суммой и рецептом на две правки. Третий элемент —
    /// ожидаемый файл после правок.
    /// </summary>
    private static (byte[] File, ClientPatchRecipe Recipe, byte[] Expected) BuildClient()
    {
        var file = new byte[0x640];
        file[0] = 0x4D;
        file[1] = 0x5A;
        Put32(file, 0x3C, 0x40);                                    // e_lfanew
        Encoding.ASCII.GetBytes("PE\0\0").CopyTo(file, 0x40);
        Put16(file, 0x46, 3);                                       // NumberOfSections
        Put16(file, 0x54, 0xE0);                                    // SizeOfOptionalHeader
        PutSection(file, 0x138, ".text", TextLen, TextRaw);
        PutSection(file, 0x160, ".mgbase", MgLen, MgRaw);
        PutSection(file, 0x188, ".reloc", RelocLen, RelocRaw);

        for (var i = 0; i < TextLen; i++)
            file[TextRaw + i] = (byte)(i % 251);
        for (var i = 0; i < 0x24; i++)
            file[MgRaw + i] = 0x11;
        for (var i = 0; i < RelocLen; i++)
            file[RelocRaw + i] = 0x33;
        file[0x202] = 0xAA;                                         // маркер под правку .text
        file[0x203] = 0xBB;
        // зона стаба .mgbase+0x48 — нули, как в настоящем клиенте

        Digest(file).CopyTo(file, MgRaw + 0x24);                    // контрольная сумма исходника стоит в файле

        // Ожидание — руками: правки и пересчёт суммы
        var expected = (byte[])file.Clone();
        expected[0x202] = 0xCC;
        expected[0x203] = 0xDD;
        for (var i = 0; i < 8; i++)
            expected[MgRaw + 0x48 + i] = 0x90;
        Digest(expected).CopyTo(expected, MgRaw + 0x24);

        var recipe = new ClientPatchRecipe
        {
            OriginalSha256 = Sha(file),
            PatchedSha256 = Sha(expected),
            Edits =
            [
                new() { Offset = 0x202, Expected = [0xAA, 0xBB], New = [0xCC, 0xDD], Note = "правка .text" },
                new() { Offset = (uint)(MgRaw + 0x48), Expected = new byte[8], New = Bytes(0x90, 8), Note = "стаб" },
            ],
            Digest = new ImageDigest { Section = ".mgbase", HeaderLength = 0x24, DigestOffset = 0x24, DigestLength = 0x20, HashSections = [".text", ".reloc"] },
        };

        return (file, recipe, expected);
    }

    /// <summary>Формула контрольной суммы из кусков файла — независимо от движка.</summary>
    private static byte[] Digest(byte[] file)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(Slice(file, MgRaw, 0x24).Concat(Slice(file, TextRaw, TextLen)).Concat(Slice(file, RelocRaw, RelocLen)).ToArray());
    }

    private static byte[] Bytes(byte value, int count)
    {
        var bytes = new byte[count];
        for (var i = 0; i < count; i++)
            bytes[i] = value;
        return bytes;
    }

    private static byte[] Slice(byte[] file, int at, int length)
    {
        var part = new byte[length];
        Array.Copy(file, at, part, 0, length);
        return part;
    }

    private static void PutSection(byte[] file, int header, string name, int size, int raw)
    {
        Encoding.ASCII.GetBytes(name).CopyTo(file, header);
        Put32(file, header + 8, size);                              // VirtualSize
        Put32(file, header + 12, 0x1000);                           // VirtualAddress
        Put32(file, header + 16, size);                             // SizeOfRawData
        Put32(file, header + 20, raw);                              // PointerToRawData
    }

    private static void Put32(byte[] file, int at, int value)
    {
        file[at] = (byte)value;
        file[at + 1] = (byte)(value >> 8);
        file[at + 2] = (byte)(value >> 16);
        file[at + 3] = (byte)(value >> 24);
    }

    private static void Put16(byte[] file, int at, int value)
    {
        file[at] = (byte)value;
        file[at + 1] = (byte)(value >> 8);
    }

    private static string Sha(byte[] data)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data);
        var text = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            text.Append(b.ToString("x2"));
        return text.ToString();
    }

    private static string Write(byte[] data)
    {
        var path = Path.Combine(Path.GetTempPath(), "notes-patcher-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(path, data);
        return path;
    }

    private static void Cleanup(string path)
    {
        File.Delete(path);
        File.Delete(ClientPatcher.BackupPath(path));
        File.Delete(ClientPatcher.BackupPath(path) + ".tmp");
        File.Delete(path + ".patch.tmp");
    }
}
