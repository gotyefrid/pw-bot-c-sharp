namespace BotCH.Core.Brain;

/// <summary>
/// Возможность режима для окна: он идёт по маршруту и знает, какая точка сейчас текущая (окно подсвечивает её строку).
/// Окно получает её через <see cref="IBotRunner.Part{T}"/>, не зная, какой класс у режима.
/// </summary>
public interface IRouteProgress
{
    /// <summary>Номер текущей точки маршрута (с 0).</summary>
    int Index { get; }
}
