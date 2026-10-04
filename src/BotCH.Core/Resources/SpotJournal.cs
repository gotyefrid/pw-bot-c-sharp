using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace BotCH.Core.Resources;

/// <summary>
/// Журнал наблюдений за ресурсами (<c>resource-events.csv</c>): строка на событие. Пишут все копии бота в один файл —
/// дописывание с общим доступом; потом по нему считаем, через сколько и где ресурсы появляются на каждом сервере.
/// </summary>
public sealed class SpotJournal(string path)
{
    private const string Header = "время;сервер;персонаж;событие;ресурс;номер;x;y;высота;центр_x;центр_y;от_центра_м;после_копки_мин;до_перса_м;кто;копка_с;в_сумку";
    private static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public string Path { get; } = path;

    private bool _headerChecked;

    public void Write(SpotEvent e, string server, string? character)
    {
        var c = CultureInfo.InvariantCulture;
        var line = string.Join(";",
            e.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", c), server, character ?? "", KindText(e.Kind), e.Name.Replace(';', ','),
            e.ResourceId == 0 ? "" : $"0x{e.ResourceId:X8}",
            e.At.X.ToString("0.0", c), e.At.Y.ToString("0.0", c), e.At.Height.ToString("0.0", c),
            e.Center.X.ToString("0.0", c), e.Center.Y.ToString("0.0", c),
            e.Kind is SpotEventKind.Respawned or SpotEventKind.InView ? e.FromCenter.ToString("0.0", c) : "",
            e.SinceDug is { } t ? t.TotalMinutes.ToString("0.00", c) : "",
            e.HostDistance.ToString("0.0", c),
            e.Who, e.DigSeconds is { } d ? d.ToString("0.0", c) : "", e.Loot);

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        if (!_headerChecked)
        {
            SetAsideOldFormat();
            _headerChecked = true;
        }

        // Excel открывает CSV с BOM как UTF-8; BOM — только в начале нового файла
        using var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        var text = line + "\r\n";
        if (stream.Length == 0)
        {
            var bom = Utf8Bom.GetPreamble();
            stream.Write(bom, 0, bom.Length);
            text = Header + "\r\n" + text;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    // Колонки поменялись (новая версия бота) — старый файл в сторону, новый начинается с нового заголовка
    private void SetAsideOldFormat()
    {
        if (!File.Exists(Path))
            return;

        string? first;
        using (var reader = new StreamReader(new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Encoding.UTF8))
            first = reader.ReadLine();
        if (first is null || first == Header)
            return;

        var old = System.IO.Path.ChangeExtension(Path, null) + $"-до-{DateTime.Now:yyyy-MM-dd-HHmmss}.csv";
        File.Move(Path, old);
    }

    private static string KindText(SpotEventKind kind) => kind switch
    {
        SpotEventKind.New => "новая точка",
        SpotEventKind.InView => "в поле зрения",
        SpotEventKind.Dug => "выкопан",
        SpotEventKind.Respawned => "появился",
        SpotEventKind.Empty => "пусто",
        _ => kind.ToString(),
    };
}
