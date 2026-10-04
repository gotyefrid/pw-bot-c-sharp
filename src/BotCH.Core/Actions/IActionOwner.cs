namespace BotCH.Core.Actions;

/// <summary>
/// Хозяин действия — кто его отправил: поведение мозга, команда Probe, потом — шаг кликера. Итог действия получает только
/// хозяин. В слоте (<see cref="ActionKey"/>) его же ждущее действие — «уже ждёт», чужое — уступает только более важному
/// (решает <see cref="ActionRunner"/>).
/// </summary>
public interface IActionOwner
{
    /// <summary>Для лога.</summary>
    string Name { get; }
}

/// <summary>Хозяин, которому итог нужен только в ответе исполнителя: команда Probe, тест.</summary>
public sealed class ActionOwner(string name) : IActionOwner
{
    public string Name { get; } = name;

    public override string ToString() => Name;
}
