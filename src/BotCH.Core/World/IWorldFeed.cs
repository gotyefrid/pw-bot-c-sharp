using System;

namespace BotCH.Core.World;

/// <summary>Лента снимков мира: <see cref="WorldMonitor"/> или подставная в тестах. События приходят из потока снимков.</summary>
public interface IWorldFeed
{
    event Action<WorldState>? Updated;

    /// <summary>Чтение не удалось: текст для пользователя.</summary>
    event Action<string>? Failed;
}
