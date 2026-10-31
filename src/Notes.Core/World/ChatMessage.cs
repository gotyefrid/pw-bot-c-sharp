using System.Text.RegularExpressions;

namespace Notes.Core.World;

/// <summary>Сообщение из окна чата игры.</summary>
/// <param name="Number">Порядковый номер: у каждого следующего сообщения на 1 больше — по нему видно новые.</param>
/// <param name="Channel">Канал, как в игре (1 мир, 5 бой, 8 системные, 9 объявления…).</param>
/// <param name="Text">Как в игре: с кодами цвета «^RRGGBB» и ссылками на предметы.</param>
/// <param name="SenderWid">Кто написал; у системных −1 (0xFFFFFFFF).</param>
public sealed record ChatMessage(int Number, int Channel, string Text, uint SenderWid)
{
    // Цвет «^FF3600»; ссылка «<4><-1><9><0><1.000000><>» перед ником/текстом; «&ник&» — ник, кликабельный в игре
    private static readonly Regex Color = new(@"\^[0-9A-Fa-f]{6}", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"[-](<[^>]*>)*\s?", RegexOptions.Compiled);

    /// <summary>Текст без кодов цвета, ссылок и «&amp;» вокруг ников — как его видит игрок.</summary>
    public string Plain => Link.Replace(Color.Replace(Text, ""), "").Replace("&", "").Trim();
}
