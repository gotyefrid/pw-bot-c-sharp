using System;
using Notes.Core.Logging;

namespace Notes.Core.Memory;

/// <summary>Свободное адресное пространство игры, байты: всего и самый большой кусок подряд.</summary>
public readonly record struct FreeMemory(long Total, long Largest)
{
    public long TotalMb => Total / (1024 * 1024);
}

/// <summary>Участок адресного пространства игры (как его отдаёт VirtualQueryEx).</summary>
public sealed record MemoryRegion(uint Start, uint AllocationBase, long Size, uint State, uint Type, uint Protect)
{
    public const uint Commit = 0x1000;
    public const uint Reserve = 0x2000;
    public const uint Free = 0x10000;

    public const uint Private = 0x20000;
    public const uint Mapped = 0x40000;
    public const uint Image = 0x1000000;
}

/// <summary>Что делать с клиентом по его памяти.</summary>
public enum GameMemoryState
{
    /// <summary>Памяти хватает.</summary>
    Ok,

    /// <summary>Памяти мало — перезапустить клиент, как только перс ничем не занят.</summary>
    Low,

    /// <summary>Памяти нет или ждали свободного момента слишком долго — перезапустить клиент сейчас, чем бы перс ни был занят.</summary>
    Critical,
}

/// <summary>
/// Следит за памятью игры. Клиент 32-битный (2 ГБ адресов) и при долгом фарме только растёт; у края любой вызов из бота
/// падает с ошибкой 8, а скоро и сама игра — лечится только перезапуском клиента. Сторож говорит заранее: сначала «мало»
/// (перезапуск в свободный момент), через <see cref="LowWait"/> или у самого края — «критично» (перезапуск сразу).
/// Однажды сказав «мало», назад не возвращается: память клиента сама не освобождается.
/// </summary>
public sealed class GameMemoryGuard(Func<FreeMemory?> query, ILogger log)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>Столько после «мало» ждём свободного момента; не дождались — перезапуск как есть.</summary>
    public static readonly TimeSpan LowWait = TimeSpan.FromMinutes(5);

    /// <summary>Меньше — критично. Кусок подряд нужен под стек нашего потока (256 КБ) и страницу вызова.</summary>
    public const long CriticalTotal = 48L * 1024 * 1024;
    public const long CriticalLargest = 512L * 1024;

    /// <summary>Меньше — мало: пора перезапускать клиент.</summary>
    public const long LowTotal = 150L * 1024 * 1024;

    private DateTime _nextCheck = DateTime.MinValue;
    private DateTime? _lowSince;
    private GameMemoryState _state = GameMemoryState.Ok;

    /// <summary>Что с памятью. Меряет не чаще раза в 30 с, между замерами отдаёт прошлый ответ; причину пишет в лог один раз.</summary>
    public GameMemoryState Check(DateTime now)
    {
        if (_state == GameMemoryState.Critical)
            return _state;

        if (_lowSince is { } since && now - since >= LowWait)
        {
            log.Warning($"Свободного момента за {LowWait.TotalMinutes:0} мин не было — перезапускаю клиент как есть");
            return _state = GameMemoryState.Critical;
        }

        if (now < _nextCheck)
            return _state;

        _nextCheck = now + Interval;
        if (query() is not FreeMemory free)
            return _state;

        if (free.Total < CriticalTotal || free.Largest < CriticalLargest)
        {
            log.Error($"Игре не хватает памяти (свободно {free.TotalMb} МБ, кусок подряд {free.Largest / 1024} КБ) — перезапускаю клиент сейчас");
            return _state = GameMemoryState.Critical;
        }

        if (free.Total < LowTotal && _state == GameMemoryState.Ok)
        {
            _lowSince = now;
            log.Warning($"У игры осталось мало памяти ({free.TotalMb} МБ) — перезапущу клиент, как только перс освободится "
                        + $"(не дольше {LowWait.TotalMinutes:0} мин)");
            return _state = GameMemoryState.Low;
        }

        return _state;
    }
}
