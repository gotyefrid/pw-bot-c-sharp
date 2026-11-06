using System;

namespace Notes.Core.Stats;

/// <summary>Что случилось — одна строка файла статистики (<see cref="StatsBook"/>). Имена пишутся в файл: не переименовывать.</summary>
public enum StatKind
{
    /// <summary>Бот запущен: началась сессия.</summary>
    SessionStart,
    /// <summary>Бот остановлен: сессия кончилась.</summary>
    SessionEnd,
    /// <summary>Бот работает — раз в несколько минут: программа закрылась без «Стоп» — сессия кончилась не раньше этого.</summary>
    Alive,
    /// <summary>Убит моб (<see cref="StatEvent.Name"/>).</summary>
    Kill,
    /// <summary>Подобран или выкопан предмет: название и сколько штук.</summary>
    Item,
    /// <summary>Подобраны деньги: сколько.</summary>
    Money,
    /// <summary>Персонаж погиб (не во время слива опыта).</summary>
    Death,
    /// <summary>Начался слив опыта.</summary>
    DrainStart,
    /// <summary>Слив опыта кончился.</summary>
    DrainEnd,
    /// <summary>Получен опыт: сколько.</summary>
    Exp,
    /// <summary>Новый уровень: какой.</summary>
    Level,
    /// <summary>Снаряжение починено.</summary>
    Repair,
}

/// <param name="Time">Когда — всемирное время, как у снимка мира.</param>
/// <param name="Name">Кто или что: моб, предмет; у остальных пусто.</param>
/// <param name="Amount">Сколько: штук, монет, опыта; у уровня — какой стал; у остальных 1.</param>
public sealed record StatEvent(DateTime Time, StatKind Kind, string Name = "", long Amount = 1);

/// <summary>Куда мозг отдаёт события статистики (<see cref="StatsBook"/>). Зовётся из потока снимков.</summary>
public interface IStatsSink
{
    void Add(StatEvent stat);
}
