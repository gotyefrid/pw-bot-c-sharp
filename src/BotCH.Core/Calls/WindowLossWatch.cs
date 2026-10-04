using System;

namespace BotCH.Core.Calls;

/// <summary>
/// Следит за вызовами через окно игры: несколько отказов «окно потеряно» подряд — связь потеряна (<see cref="Broken"/>).
/// Тогда бот должен остановиться, а не молча ничего не делать. Тихо переходить на вызовы потоком нельзя: на нём падал 1.4.6 —
/// новый «Старт» пройдёт обычный путь (основной способ, при неудаче — вопрос пользователю).
/// </summary>
public sealed class WindowLossWatch(IRemoteRunner window) : IRemoteRunner
{
    /// <summary>Сколько отказов подряд — уже не случайность.</summary>
    public const int LossesToBreak = 3;

    private int _losses;

    /// <summary>Причина для пользователя; null — связь есть.</summary>
    public string? Broken => _losses >= LossesToBreak ? "связь с окном игры потеряна" : null;

    public RemoteRunResult Run(byte[]? data, Func<uint, byte[]> buildStub)
    {
        var result = window.Run(data, buildStub);
        _losses = result.Status == RemoteRunStatus.WindowLost ? _losses + 1 : 0;
        return result;
    }
}
