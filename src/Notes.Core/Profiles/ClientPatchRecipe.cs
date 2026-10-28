using System.Collections.Generic;

namespace Notes.Core.Profiles;

/// <summary>
/// Рецепт правки exe клиента: фиксированный набор байтовых правок, привязанный строго к одному билду
/// (originalSha256), и пересчёт контрольной суммы образа после них. После обновления игры байты не сойдутся —
/// патчер откажется («не тот билд»), новому билду нужен новый рецепт.
/// </summary>
public sealed class ClientPatchRecipe
{
    /// <summary>sha256 исходного exe, к которому привязаны все правки (hex).</summary>
    public string OriginalSha256 { get; init; } = "";

    /// <summary>Ожидаемый sha256 после правок (hex): не сошёлся — рецепт неверен, файл не записывается.</summary>
    public string PatchedSha256 { get; init; } = "";

    /// <summary>Байтовые правки по смещениям в файле.</summary>
    public List<PatchEdit> Edits { get; init; } = new();

    /// <summary>Пересчёт контрольной суммы образа после правок; null — клиент образ не сверяет, пересчитывать нечего.</summary>
    public ImageDigest? Digest { get; init; }
}

/// <summary>Одна правка: по <see cref="Offset"/> лежат байты <see cref="Expected"/> («тот ли билд») — заменить на <see cref="New"/>.</summary>
public sealed class PatchEdit
{
    public uint Offset { get; init; }

    public byte[] Expected { get; init; } = [];

    public byte[] New { get; init; } = [];

    /// <summary>Что делает правка и как найти то же место в новом билде — для лога и сопровождения.</summary>
    public string Note { get; init; } = "";
}

/// <summary>
/// Контрольная сумма образа в секции клиента: digest = SHA-256(заголовок секции + куски других секций), поле
/// digest лежит в той же секции и сверяется байт в байт. Правки задели хэшируемые секции — digest надо
/// пересчитать, иначе клиент с изменённым файлом не запустится.
/// </summary>
public sealed class ImageDigest
{
    /// <summary>Секция с контрольной суммой.</summary>
    public string Section { get; init; } = "";

    /// <summary>Сколько байт начала секции входит в хэш (заголовок самой контрольной суммы).</summary>
    public uint HeaderLength { get; init; }

    /// <summary>Где в секции лежит поле digest (от начала сырых данных секции).</summary>
    public uint DigestOffset { get; init; }

    /// <summary>Длина поля digest в байтах.</summary>
    public uint DigestLength { get; init; }

    /// <summary>Какие секции хэшируются после заголовка, каждая куском min(VirtualSize, RawSize) от начала.</summary>
    public List<string> HashSections { get; init; } = new();
}
