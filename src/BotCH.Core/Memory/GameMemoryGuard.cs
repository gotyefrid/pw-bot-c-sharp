using System;
using BotCH.Core.Logging;

namespace BotCH.Core.Memory;

/// <summary>Свободное адресное пространство игры, байты: всего и самый большой кусок подряд.</summary>
public readonly record struct FreeMemory(long Total, long Largest)
{
    public long TotalMb => Total / (1024 * 1024);
}

/// <summary>
/// Следит за памятью игры. Клиент 32-битный (2 ГБ адресов) и при долгом фарме только растёт; у края
/// любой вызов из бота падает с ошибкой 8, а скоро и сама игра. Бот замечает это заранее и останавливается
/// с понятной причиной, а не сыплет «не отправлено».
/// </summary>
public sealed class GameMemoryGuard(Func<FreeMemory?> query, ILogger log)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WarnEvery = TimeSpan.FromMinutes(10);

    /// <summary>Меньше — останавливаем бота. Кусок подряд нужен под стек нашего потока (256 КБ) и страницу вызова.</summary>
    public const long StopTotal = 48L * 1024 * 1024;
    public const long StopLargest = 512L * 1024;

    /// <summary>Меньше — предупреждаем, что скоро придётся перезапускать клиент.</summary>
    public const long WarnTotal = 150L * 1024 * 1024;

    private DateTime _nextCheck = DateTime.MinValue;
    private DateTime _nextWarning = DateTime.MinValue;

    /// <summary>Пора ли остановить бота. Меряет не чаще раза в 30 с; причину пишет в лог.</summary>
    public bool ShouldStop(DateTime now)
    {
        if (now < _nextCheck)
            return false;

        _nextCheck = now + Interval;
        if (query() is not FreeMemory free)
            return false;

        if (free.Total < StopTotal || free.Largest < StopLargest)
        {
            log.Error($"Игре не хватает памяти (свободно {free.TotalMb} МБ, кусок подряд {free.Largest / 1024} КБ) — бот остановлен. "
                      + "Перезапустите клиент игры");
            return true;
        }

        if (free.Total < WarnTotal && now >= _nextWarning)
        {
            _nextWarning = now + WarnEvery;
            log.Warning($"У игры осталось мало памяти ({free.TotalMb} МБ) — скоро придётся перезапустить клиент");
        }

        return false;
    }
}
