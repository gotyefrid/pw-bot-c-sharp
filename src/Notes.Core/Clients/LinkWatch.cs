using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Notes.Core.Clients;

/// <summary>
/// Сторож связи работающего бота: персонаж выпал из игры. Признаки — окно разрыва в мире (тексты
/// <see cref="Profiles.LoginScreenOffsets.Disconnected"/>) или экран входа вместо мира, если держится <see cref="Hold"/>
/// (короткое подвисание — не разрыв), и процесс клиента пропал — сразу. Сообщает один раз (<see cref="Lost"/>), дальше молчит.
/// Память только читается; в окне ничего не нажимает.
/// </summary>
public sealed class LinkWatch : IDisposable
{
    public static readonly TimeSpan DefaultHold = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(2);

    private readonly Func<bool> _alive;
    private readonly Func<LoginView> _read;
    private readonly IReadOnlyList<string> _texts;
    private Timer? _timer;
    private int _busy;
    private DateTime? _since;
    private volatile bool _lost;

    /// <param name="alive">Процесс клиента жив.</param>
    /// <param name="read">Экран клиента (<see cref="LoginScreen"/>).</param>
    /// <param name="texts">Начала текстов окна разрыва.</param>
    public LinkWatch(Func<bool> alive, Func<LoginView> read, IReadOnlyList<string> texts)
    {
        _alive = alive;
        _read = read;
        _texts = texts;
    }

    public TimeSpan Hold { get; init; } = DefaultHold;

    /// <summary>Персонаж выпал из игры — причина для лога; из потока сторожа, один раз.</summary>
    public event Action<string>? Lost;

    /// <summary>Смотреть раз в <paramref name="period"/> в фоне.</summary>
    public void Start(TimeSpan? period = null)
    {
        var every = period ?? DefaultPeriod;
        _timer = new Timer(_ => Tick(), null, every, every);
    }

    private void Tick()
    {
        // Чтение затянулось — следующий раз пропускаем, а не копим
        if (Interlocked.Exchange(ref _busy, 1) != 0)
            return;
        try
        {
            Check(DateTime.UtcNow);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Не прочиталось — посмотрим в следующий раз
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <summary>Посмотреть сейчас. Выпал — причина (и <see cref="Lost"/>); всё в порядке или уже сообщили — null.</summary>
    public string? Check(DateTime now)
    {
        if (_lost)
            return null;
        if (!_alive())
            return Fire("клиент игры закрылся");

        var view = _read();
        var problem = Problem(view);
        if (problem is null)
        {
            _since = null;
            return null;
        }

        _since ??= now;
        return now - _since.Value >= Hold ? Fire(problem) : null;
    }

    private string? Problem(LoginView view)
    {
        if (view.Stage == LoginStage.World && view.Message is { } message
            && _texts.Any(t => t.Length > 0 && message.StartsWith(t, StringComparison.OrdinalIgnoreCase)))
            return $"связь с сервером потеряна: «{message}»";
        return view.Stage is LoginStage.Login or LoginStage.Select ? "клиент вышел на экран входа" : null;
    }

    private string Fire(string reason)
    {
        _lost = true;
        Lost?.Invoke(reason);
        return reason;
    }

    public void Dispose()
    {
        _lost = true;
        _timer?.Dispose();
        _timer = null;
    }
}
