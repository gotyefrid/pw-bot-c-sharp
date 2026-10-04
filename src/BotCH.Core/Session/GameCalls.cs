using System;
using BotCH.Core.Actions;
using BotCH.Core.Calls;
using BotCH.Core.Clients;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;

namespace BotCH.Core.Session;

/// <summary>Как бот вызывает функции игры.</summary>
public enum CallTransport
{
    /// <summary>В главном потоке игры, через свой обработчик её окна (<see cref="WindowCallRunner"/>) — основной способ.</summary>
    Window,

    /// <summary>Отдельным потоком в игре — запасной: клиент 1.4.6 от него падал на стыке «работ» персонажа.</summary>
    Thread,
}

/// <summary>
/// Транспорт вызовов в игру: дескриптор с правом вызова, обработчик окна (или поток) и <see cref="GameCaller"/>. Только
/// транспорт: о ждущих действиях не знает — их держит <see cref="ActionRunner"/> того, кто вызывает (бот, Probe).
/// Память читается через переданный дескриптор чтения: он живёт дольше этого объекта.
/// </summary>
public sealed class GameCalls : IDisposable
{
    private readonly GameProcess _exec;
    private readonly WindowCallRunner? _window;
    private bool _disposed;

    private GameCalls(GameProcess exec, WindowCallRunner? window, GameCaller caller, CallTransport transport)
    {
        _exec = exec;
        _window = window;
        Caller = caller;
        Actions = new DirectCallActions(caller);
        Transport = transport;
    }

    public CallTransport Transport { get; }

    /// <summary>Функции клиента: какие найдены, какие нет (для предупреждений в лог).</summary>
    public GameCaller Caller { get; }

    public IGameActions Actions { get; }

    public Capabilities Capabilities => Caller.Capabilities;

    /// <summary>Подключить вызовы. null — не вышло, причина для пользователя — в <paramref name="problem"/>.</summary>
    /// <param name="game">Дескриптор чтения: им же читается память при вызовах.</param>
    public static GameCalls? Open(GameProcess game, ProfileData profile, CallTransport transport, out string problem)
    {
        GameProcess exec;
        try
        {
            exec = GameProcess.Open(game.Pid, GameProcessRights.Execute);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            problem = "нет доступа к процессу игры: " + e.Message;
            return null;
        }

        WindowCallRunner? window = null;
        if (transport == CallTransport.Window)
        {
            window = WindowCallRunner.Install(exec, NativeWindows.FindMainWindow(game.Pid, includeHidden: true), out problem);
            if (window is null)
            {
                exec.Dispose();
                return null;
            }
        }

        problem = "";
        var caller = new GameCaller(game, window ?? (IRemoteRunner)exec, game.MainModuleBase, profile);
        return new GameCalls(exec, window, caller, transport);
    }

    /// <summary>Вернуть окну игры прежний обработчик и закрыть дескриптор с правом вызова. Повторный вызов ничего не делает.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _window?.Dispose();
        _exec.Dispose();
    }
}
