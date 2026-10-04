using System;
using System.Linq;
using System.Runtime.InteropServices;
using BotCH.Core.Memory;

namespace BotCH.Core.Calls;

/// <summary>
/// Вызовы в главном потоке игры. Поток бота (CreateRemoteThread) работал параллельно игре — на стыке «работ» персонажа
/// (бег закончился → новый скилл) клиент 1.4.6 падал с повреждением кучи. Здесь окну игры ставится свой обработчик
/// (<see cref="WindowStubs.WindowProc"/>): на наше сообщение он выполняет заглушку вызова, остальное отдаёт прежнему.
/// Игра разбирает сообщения между кадрами — вызов идёт в безопасный момент, в её же потоке. Код игры не меняется.
/// <para>
/// Обработчик у окна один на всех и остаётся после «Стоп»: следующий «Старт» (и Probe) находит свой и берёт его же
/// (<see cref="Attach"/>) — раньше каждый Старт/Стоп оставлял в игре новую страницу 64 КБ. Снимает его
/// <see cref="Remove"/> — при отключении бота от клиента.
/// </para>
/// <para>
/// «Свой» — по коду: по адресу обработчика окна лежат ровно наши байты <see cref="WindowStubs.WindowProc"/> с тем же
/// номером сообщения (<c>RegisterWindowMessage("BotCH.Call")</c> в одном сеансе Windows даёт одно число всем). Новая
/// версия бота с другим кодом обработчика старый своим не признает и поставит новый поверх: одна страница на обновление
/// бота. Поменялся смысл сообщения (что в wParam, что возвращаем) — поменяйте и его имя, иначе старый обработчик
/// под новым примет чужой вызов за свой.
/// </para>
/// </summary>
public sealed class WindowCallRunner : IRemoteRunner, IDisposable
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);
    private const int OriginalSlotOffset = 0x100;
    private const string MessageName = "BotCH.Call";

    private readonly RemotePages _pages;
    private readonly IntPtr _window;
    private readonly uint _message;
    private bool _disposed;

    private WindowCallRunner(GameProcess game, IntPtr window, uint message, bool reused)
    {
        _pages = new RemotePages(game);
        _window = window;
        _message = message;
        Reused = reused;
    }

    /// <summary>Наш обработчик уже стоял (от прошлого «Старт» или Probe) — новый не ставился.</summary>
    public bool Reused { get; }

    /// <summary>
    /// Подключиться к окну игры: сверху уже стоит наш обработчик — взять его, нет — поставить новый.
    /// null — не вышло, причина в <paramref name="problem"/>.
    /// </summary>
    public static WindowCallRunner? Attach(GameProcess game, IntPtr window, out string problem)
    {
        if (User32.Find(game, window, out problem) is not { } user32)
            return null;

        var reused = FindOurs(game, user32) is not null;
        if (!reused && Install(game, user32, out problem) is null)
            return null;

        problem = "";
        return new WindowCallRunner(game, window, user32.Message, reused);
    }

    /// <summary>
    /// Вернуть окну прежний обработчик, если сверху стоит наш (чужой поверх — не трогаем, иначе снимем и его). Страница
    /// остаётся: игра может быть внутри неё (окно тащат мышью — прежний обработчик крутит свой цикл, вызванный из нашего).
    /// true — снят; false — нет, почему — в <paramref name="details"/>.
    /// </summary>
    public static bool Remove(GameProcess game, IntPtr window, out string details)
    {
        if (User32.Find(game, window, out details) is not { } user32)
            return false;

        if (FindOurs(game, user32) is not { } ours)
        {
            details = "сверху не наш обработчик";
            return false;
        }

        var restore = new ThreadCallRunner(game).Run(null, _ => WindowStubs.Restore(user32.GetWindowLong, user32.SetWindowLong, user32.Window, ours,
            ours + OriginalSlotOffset));
        details = restore.Details;
        return restore.IsDone;
    }

    /// <summary>
    /// Адрес нашего обработчика, если сверху у окна стоит он; null — чужой (или прочитать не вышло). Адрес спрашиваем
    /// потоком в игре (<see cref="WindowStubs.ReadWindowProc"/>), байты читаем сами.
    /// </summary>
    private static uint? FindOurs(GameProcess game, User32 user32)
    {
        var read = new ThreadCallRunner(game).Run(null, _ => WindowStubs.ReadWindowProc(user32.GetWindowLong, user32.Window), out var proc);
        if (!read.IsDone || proc == 0)
            return null;

        var expected = user32.WindowProc(proc);
        var actual = new byte[expected.Length];
        // Прежний обработчик ещё не записан (0) — обработчик поставлен не до конца: снимать его нельзя, брать — тоже
        return game.TryRead(proc, actual, actual.Length) && actual.SequenceEqual(expected)
            && game.TryReadUInt32(proc + OriginalSlotOffset, out var original) && original != 0
                ? proc
                : null;
    }

    /// <summary>
    /// Новый обработчик поверх того, что стоит. Страница: обработчик в начале, слот «прежний обработчик» на 0x100 (пока 0 —
    /// обработчик отдаёт DefWindowProc). null — не вышло.
    /// </summary>
    private static uint? Install(GameProcess game, User32 user32, out string problem)
    {
        if (new RemotePages(game).AllocateResident(new byte[OriginalSlotOffset + 4]) is not { } windowProc)
        {
            problem = "не удалось выделить память под обработчик";
            return null;
        }

        if (!game.TryWrite(windowProc, user32.WindowProc(windowProc)))
        {
            problem = "не удалось записать обработчик";
            return null;
        }

        var install = new ThreadCallRunner(game).Run(null, _ => WindowStubs.Install(user32.SetWindowLong, user32.Window, windowProc, windowProc + OriginalSlotOffset));
        if (!install.IsDone || !game.TryReadUInt32(windowProc + OriginalSlotOffset, out var original) || original == 0)
        {
            problem = $"обработчик не поставился{(install.IsDone ? "" : ": " + install.Details)}";
            return null;
        }

        problem = "";
        return windowProc;
    }

    public RemoteRunResult Run(byte[]? data, Func<uint, byte[]> buildStub)
    {
        if (_disposed)
            return new RemoteRunResult(RemoteRunStatus.Failed, "вызовы через окно игры закрыты");
        // Окна больше нет (игра его пересоздала) — сообщение не дойдёт: не тратим страницу и 5 с ожидания
        if (!IsWindow(_window))
            return new RemoteRunResult(RemoteRunStatus.WindowLost, "окно игры закрыто или пересоздано");
        if (_pages.Prepare(data, buildStub, out var page) is { } failed)
            return failed;

        var sent = SendMessageTimeout(_window, _message, new IntPtr(unchecked((int)page)), IntPtr.Zero, SmtoAbortIfHung,
            (uint)CallTimeout.TotalMilliseconds, out var result);
        if (sent == IntPtr.Zero)
        {
            // Сообщение могло остаться в очереди и выполниться позже — память не освобождаем
            return new RemoteRunResult(RemoteRunStatus.Timeout, $"игра не обработала вызов за {CallTimeout.TotalSeconds:0} с");
        }

        _pages.Free(page);
        return unchecked((uint)result.ToInt32()) == WindowStubs.Handled
            ? new RemoteRunResult(RemoteRunStatus.Done)
            : new RemoteRunResult(RemoteRunStatus.WindowLost, "обработчик окна снят — вызов не выполнен");
    }

    /// <summary>Больше не вызывать. Обработчик остаётся у окна — следующий <see cref="Attach"/> возьмёт его же.</summary>
    public void Dispose() => _disposed = true;

    /// <summary>Что нужно от user32 для обработчика этого окна: функции в A- или W-версии — как у окна, и номер сообщения.</summary>
    private sealed record User32(uint Window, uint Message, uint CallWindowProc, uint DefWindowProc, uint GetWindowLong, uint SetWindowLong)
    {
        public static User32? Find(GameProcess game, IntPtr window, out string problem)
        {
            if (window == IntPtr.Zero || !IsWindow(window))
            {
                problem = "окно игры не найдено";
                return null;
            }

            // Адреса функций user32 берём у себя: системные библиотеки грузятся по одному адресу во всех процессах — проверяем
            var user32 = GetModuleHandle("user32.dll");
            var ours = unchecked((uint)user32.ToInt32());
            if (game.ModuleBase("user32.dll") is not { } theirs || theirs != ours)
            {
                problem = "user32.dll у игры по другому адресу";
                return null;
            }

            var unicode = IsWindowUnicode(window);
            uint Function(string name) => unchecked((uint)GetProcAddress(user32, name + (unicode ? "W" : "A")).ToInt32());
            var found = new User32(unchecked((uint)window.ToInt32()), RegisterWindowMessage(MessageName), Function("CallWindowProc"),
                Function("DefWindowProc"), Function("GetWindowLong"), Function("SetWindowLong"));
            if (found.Message == 0 || found.CallWindowProc == 0 || found.DefWindowProc == 0 || found.GetWindowLong == 0
                || found.SetWindowLong == 0)
            {
                problem = "нет функций user32";
                return null;
            }

            problem = "";
            return found;
        }

        /// <summary>Код нашего обработчика, если он лежит по адресу <paramref name="proc"/> (слот прежнего — на 0x100 от него).</summary>
        public byte[] WindowProc(uint proc) => WindowStubs.WindowProc(Message, proc + OriginalSlotOffset, CallWindowProc, DefWindowProc);
    }

    private const uint SmtoAbortIfHung = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout,
        out IntPtr result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowUnicode(IntPtr window);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
