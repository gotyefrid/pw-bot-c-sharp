using Notes.Core.World;

namespace Notes.Core.Actions;

/// <summary>
/// Чем действие управляет игрой — передаётся в <see cref="GameAction.Send"/>: вызовы функций игры (<see cref="Calls"/>) и
/// нажатия клавиш в её окно (<see cref="Keys"/>, основа кликера: всё, что клиент делает клавишей). Новый способ — новым
/// свойством и новым действием, без правки исполнителя, мозга и остальных действий.
/// </summary>
public sealed class GameControl(IGameActions calls, IGameKeys? keys = null, WorldRequests? requests = null)
{
    /// <summary>Что читать в снимок сверх обычного (банк, пока с ним работаем). Не дали — просьбы никто не читает.</summary>
    public WorldRequests Requests { get; } = requests ?? new();

    /// <summary>Вызовы функций игры: пакеты и «работы» персонажа.</summary>
    public IGameActions Calls { get; } = calls;

    /// <summary>Клавиши в окно игры. Не дали — нажатие не отправляется («клавиши недоступны»).</summary>
    public IGameKeys Keys { get; } = keys ?? NoKeys.Instance;
}
