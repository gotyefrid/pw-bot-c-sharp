using Notes.Core.World;
using Xunit;

namespace Notes.Tests.World;

/// <summary>Текст сообщения чата так, как его видит игрок: без кодов цвета и ссылок.</summary>
public class ChatMessageTests
{
    [Theory]
    [InlineData("^BED293Itori. Запомните это имя. Так зовут вашего убийцу.", "Itori. Запомните это имя. Так зовут вашего убийцу.")]
    [InlineData("^C0C0C0Itori наносит вам 5676 ед. урона.", "Itori наносит вам 5676 ед. урона.")]
    [InlineData("^FFE400\ue000<4><-1><1><0><1.000000><> &Geranika&: 3гр ", "Geranika: 3гр")]
    [InlineData("^FF3600\ue000<4><-1><9><0><1.000000><> ^ffff00Поздравляем игрока", "Поздравляем игрока")]
    [InlineData("^BED293^00ff00Вы вошли в безопасную зону.", "Вы вошли в безопасную зону.")]
    public void PlainIsWhatThePlayerSees(string text, string plain)
    {
        Assert.Equal(plain, new ChatMessage(1, 8, text, uint.MaxValue).Plain);
    }
}
