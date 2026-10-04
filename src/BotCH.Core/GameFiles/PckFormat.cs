namespace BotCH.Core.GameFiles;

/// <summary>
/// Ключи и размер хвоста архива .pck — у каждого клиента свои, задаются в профиле сервера (gameFiles.pck).
/// По умолчанию — стандартные ключи Angelica.
/// </summary>
public sealed class PckFormat
{
    public static PckFormat Standard { get; } = new();

    /// <summary>Размер записи оглавления ^ Key1.</summary>
    public uint Key1 { get; init; } = 0xA8937462;
    /// <summary>Тот же размер ^ Key2 — для проверки.</summary>
    public uint Key2 { get; init; } = 0xF1A43653;
    /// <summary>Смещение оглавления в хвосте ^ OffsetKey (у стандартного клиента совпадает с Key1).</summary>
    public uint OffsetKey { get; init; } = 0xA8937462;
    /// <summary>За сколько байт до конца архива начинается хвост.</summary>
    public uint TailSize { get; init; } = 0x118;
}
