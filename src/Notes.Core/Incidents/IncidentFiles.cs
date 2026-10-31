using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Notes.Core.Incidents;

/// <summary>
/// Случаи — файлами в папке: «2026-10-31 21-11-37.txt» по времени первого события (местному). Хранит последние
/// <paramref name="keep"/>, старые удаляет: папка не разрастается.
/// </summary>
public sealed class IncidentFiles(string directory, int keep = 50)
{
    /// <summary>Папка случаев внутри папки логов. Имя по-английски: путь без кириллицы.</summary>
    public const string FolderName = "deaths";

    public string Directory { get; } = directory;

    /// <returns>Путь к записанному файлу.</returns>
    public string Write(Incident incident)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var name = incident.Events[0].Time.ToLocalTime().ToString("yyyy-MM-dd HH-mm-ss");
        var path = Path.Combine(Directory, name + ".txt");
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(Directory, $"{name} ({n}).txt");

        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
        using (var writer = new StreamWriter(file, new UTF8Encoding(true)))
            writer.Write(IncidentReport.Text(incident));

        Prune();
        return path;
    }

    private void Prune()
    {
        var files = System.IO.Directory.GetFiles(Directory, "*.txt").Select(f => new FileInfo(f));
        foreach (var old in files.OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, StringComparer.Ordinal).Skip(keep).Select(f => f.FullName))
        {
            try
            {
                File.Delete(old);
            }
            catch (IOException)
            {
                // Открыт в блокноте — удалим в следующий раз
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
