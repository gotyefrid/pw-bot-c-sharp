using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Notes.Core.Clients;
using Notes.Core.Profiles;
using Xunit;

namespace Notes.Tests.Clients;

/// <summary>Проверка сторожа клиента: тот ли pwprotector.exe лежит рядом с exe игры.</summary>
public class ProtectorCheckTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "notes-protector-" + Guid.NewGuid().ToString("N"));

    public ProtectorCheckTests() => Directory.CreateDirectory(Path.Combine(_dir, "element", "reportbugs"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string GameExe => Path.Combine(_dir, "element", "ElementClient.exe");

    private string ProtectorExe => Path.Combine(_dir, "element", "reportbugs", "pwprotector.exe");

    /// <summary>Положить файл сторожа с заданным содержимым и вернуть его sha256.</summary>
    private string PutProtector(byte[] content)
    {
        File.WriteAllBytes(ProtectorExe, content);
        using var sha = SHA256.Create();
        var text = new StringBuilder();
        foreach (var b in sha.ComputeHash(content))
            text.Append(b.ToString("x2"));
        return text.ToString();
    }

    private static ProtectorSpec Spec(string sha) => new() { Sha256 = sha };

    [Fact]
    public void ResolvePutsProtectorNextToElementFolder()
        => Assert.Equal(Path.GetFullPath(Path.Combine(_dir, "element", "reportbugs", "pwprotector.exe")),
            Path.GetFullPath(ProtectorCheck.Resolve(GameExe)));

    [Fact]
    public void SameFileIsKnown()
    {
        var sha = PutProtector([1, 2, 3, 4]);

        var (state, file) = ProtectorCheck.Check(GameExe, Spec(sha));

        Assert.Equal(ProtectorState.Known, state);
        Assert.NotNull(file);
        Assert.Equal(4, file!.Length);
        Assert.Equal(sha, file.Sha256);
    }

    [Fact]
    public void DifferentFileIsChanged()
    {
        PutProtector([1, 2, 3, 4]);

        var (state, file) = ProtectorCheck.Check(GameExe, Spec(new string('a', 64)));

        Assert.Equal(ProtectorState.Changed, state);
        Assert.NotNull(file);
        Assert.NotEqual(new string('a', 64), file!.Sha256);
    }

    [Fact]
    public void MissingFileIsMissing()
        => Assert.Equal(ProtectorState.Missing, ProtectorCheck.Check(GameExe, Spec("abc")).State);

    [Fact]
    public void EmptySpecIsNotConfigured()
    {
        PutProtector([1, 2, 3]);

        Assert.Equal(ProtectorState.NotConfigured, ProtectorCheck.Check(GameExe, Spec("")).State);
        Assert.Equal(ProtectorState.NotConfigured, ProtectorCheck.Check(GameExe, null).State);
        Assert.Equal(ProtectorState.NotConfigured, ProtectorCheck.Check(null, Spec("abc")).State);
    }

    [Fact]
    public void ComparisonIgnoresCase()
        => Assert.Equal(ProtectorState.Known, ProtectorCheck.Check(GameExe, Spec(PutProtector([9, 9]).ToUpperInvariant())).State);

    /// <summary>Файл занят другой программой на запись — читать всё равно можно (так его держит клиент).</summary>
    [Fact]
    public void FileHeldOpenForWritingIsStillReadable()
    {
        var sha = PutProtector([5, 5, 5]);
        using (var holder = new FileStream(ProtectorExe, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            var (state, _) = ProtectorCheck.Check(GameExe, Spec(sha));
            Assert.Equal(ProtectorState.Known, state);
        }
    }
}
