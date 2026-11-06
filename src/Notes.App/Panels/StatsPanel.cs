using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Threading;
using Notes.App.Mvvm;
using Notes.Core.Stats;

namespace Notes.App.Panels;

/// <summary>
/// Карточка «Статистика» на вкладке «Бот»: итоги персонажа (<see cref="StatsBook"/>) за сессию, день, неделю, месяц. Книга
/// меняется из потока снимков — здесь только отметка «пересчитать»; пересчёт и показ — по таймеру окна, пока бот работает,
/// время сессии идёт и без событий.
/// </summary>
public sealed class StatsPanel : ObservableObject
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private StatsBook? _book;
    private bool _moneyKnown;
    private volatile bool _dirty;

    public StatsPanel()
    {
        _timer.Tick += (_, _) =>
        {
            if (_dirty || _book?.Live == true)
                Refresh();
        };
        _timer.Start();
    }

    /// <summary>Книга текущего персонажа — бот пишет в неё; null — персонаж не выбран.</summary>
    public StatsBook? Book => _book;

    /// <summary>Персонаж сменился: его книга; <paramref name="moneyKnown"/> — деньги читаются на этом сервере.</summary>
    public void SetBook(StatsBook? book, bool moneyKnown)
    {
        if (_book is not null)
            _book.Changed -= MarkDirty;
        _book = book;
        _moneyKnown = moneyKnown;
        if (book is not null)
            book.Changed += MarkDirty;
        Refresh();
    }

    private void MarkDirty() => _dirty = true;

    private int _period;

    /// <summary>Период: 0 — сессия, 1 — день, 2 — неделя, 3 — месяц.</summary>
    public int Period
    {
        get => _period;
        set
        {
            if (SetProperty(ref _period, value))
                Refresh();
        }
    }

    private string _worked = "—";
    public string Worked { get => _worked; private set => SetProperty(ref _worked, value); }

    private string _kills = "—";
    public string Kills { get => _kills; private set => SetProperty(ref _kills, value); }

    private string _deaths = "—";
    public string Deaths { get => _deaths; private set => SetProperty(ref _deaths, value); }

    private string _exp = "—";
    public string Exp { get => _exp; private set => SetProperty(ref _exp, value); }

    private string _money = "—";
    public string Money { get => _money; private set => SetProperty(ref _money, value); }

    private string _repairs = "—";
    public string Repairs { get => _repairs; private set => SetProperty(ref _repairs, value); }

    private string _drains = "—";
    public string Drains { get => _drains; private set => SetProperty(ref _drains, value); }

    private IReadOnlyList<ItemCount> _items = [];

    /// <summary>Подобранное и выкопанное: больше всего — сверху.</summary>
    public IReadOnlyList<ItemCount> Items { get => _items; private set => SetProperty(ref _items, value); }

    private bool _hasItems;
    public bool HasItems { get => _hasItems; private set => SetProperty(ref _hasItems, value); }

    public void Refresh()
    {
        _dirty = false;
        var now = DateTime.UtcNow;
        var s = _book?.Summary((StatsPeriod)Period, now) ?? StatsSummary.Empty;

        Worked = s.Sessions switch
        {
            0 => "—",
            1 => Duration(s.Worked),
            var n => $"{Duration(s.Worked)} · {n} {Plural(n, "сессия", "сессии", "сессий")}",
        };
        Kills = WithRate(s.Kills, s);
        Deaths = s.Deaths.ToString(Ru);
        Exp = WithRate(s.Exp, s) + (s.Levels > 0 ? $" · +{s.Levels} {Plural(s.Levels, "уровень", "уровня", "уровней")}" : "");
        Money = _moneyKnown ? Number(s.Money) : "не читается на этом сервере";
        Repairs = s.Repairs.ToString(Ru);
        Drains = s.Drains.Count == 0 ? "0"
            : $"{s.Drains.Count}: " + string.Join(", ", s.Drains.Select(d => $"{When(d.Start, now)} ({(d.Running ? "идёт " : "")}{Duration(d.Duration)})"));
        // Список заново — только если поменялся: иначе прокрутка прыгает наверх каждые 2 с
        if (!s.Items.SequenceEqual(Items))
            Items = s.Items;
        HasItems = Items.Count > 0;
    }

    private static string WithRate(long count, StatsSummary s)
        => s.PerHour(count) is { } rate && count > 0 ? $"{Number(count)} · {Number((long)Math.Round(rate))} в час" : Number(count);

    private static string Number(long n) => n.ToString("#,0", Ru);

    private static string Duration(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} ч {t.Minutes} мин" : $"{Math.Max(0, (int)t.TotalMinutes)} мин";

    // Сегодня — только время, раньше — с датой
    private static string When(DateTime utc, DateTime nowUtc)
    {
        var local = utc.ToLocalTime();
        return local.Date == nowUtc.ToLocalTime().Date ? local.ToString("HH:mm", Ru) : local.ToString("dd.MM HH:mm", Ru);
    }

    private static string Plural(long n, string one, string few, string many)
    {
        var tens = n % 100;
        var last = n % 10;
        return tens is >= 11 and <= 14 ? many : last == 1 ? one : last is >= 2 and <= 4 ? few : many;
    }
}
