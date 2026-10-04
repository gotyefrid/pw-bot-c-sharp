using System;

namespace BotCH.Core.Calls;

public enum RemoteRunStatus
{
    Done,
    /// <summary>Не удалось выделить память / записать / создать поток — в игре ничего не выполнено.</summary>
    Failed,
    /// <summary>Поток не закончился вовремя. Память оставлена (освободить её под работающим потоком — уронить игру).</summary>
    Timeout,
    /// <summary>
    /// Окно игры наших вызовов не принимает: закрыто, пересоздано (например, Alt+Enter) или поверх нашего обработчика стоит
    /// чужой. В игре ничего не выполнено.
    /// </summary>
    WindowLost,
}

public sealed record RemoteRunResult(RemoteRunStatus Status, string Details = "")
{
    public bool IsDone => Status == RemoteRunStatus.Done;
}

/// <summary>
/// Выполняет заглушку в процессе игры: данные и код кладутся в выделенную страницу, поток запускается и ожидается.
/// <paramref name="buildStub"/> получает адрес, куда легли <c>data</c> (указатель для аргументов).
/// Настоящие — <see cref="WindowCallRunner"/> (главный поток игры) и <see cref="ThreadCallRunner"/> (свой поток); в тестах подменяется.
/// </summary>
public interface IRemoteRunner
{
    RemoteRunResult Run(byte[]? data, Func<uint, byte[]> buildStub);
}
