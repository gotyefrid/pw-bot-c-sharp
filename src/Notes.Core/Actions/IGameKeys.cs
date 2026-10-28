using Notes.Core.Calls;

namespace Notes.Core.Actions;

/// <summary>
/// Нажатия клавиш в окно игры (кликер): как игрок жмёт клавишу, только сообщением в окно своего клиента — клавиатура,
/// активное окно и соседние клиенты их не получают. Окно игры может быть в фоне.
/// </summary>
public interface IGameKeys
{
    /// <summary>Нажать и отпустить. Сработало ли в игре, не узнать — только отправлено или нет.</summary>
    CallResult Press(KeyStroke key);
}

/// <summary>Клавиш не дали (Probe act, тесты без кликера) — нажатие не отправляется.</summary>
internal sealed class NoKeys : IGameKeys
{
    public static readonly NoKeys Instance = new();

    public CallResult Press(KeyStroke key) => CallResult.Refused("клавиши в этом запуске недоступны");
}
