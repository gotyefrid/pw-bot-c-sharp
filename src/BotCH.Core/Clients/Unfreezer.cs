using System;
using System.Threading;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;

namespace BotCH.Core.Clients;

/// <summary>
/// Unfreeze — клиент не «засыпает», когда окно не в фокусе: раз в секунду пишет 1 в [база + Unfreeze] (как старый бот).
/// Пишет в данные игры, не в код. Отдельный дескриптор с правом записи открывается только на время работы.
/// </summary>
public sealed class Unfreezer : IDisposable
{
    private readonly int _pid;
    private readonly ProfileData _profile;
    private readonly Action<string> _log;
    private Timer? _timer;
    private GameProcess? _game;
    private bool _warned;

    public Unfreezer(int pid, ProfileData profile, Action<string> log)
    {
        _pid = pid;
        _profile = profile;
        _log = log;
    }

    public bool IsSupported => _profile.Base.Unfreeze != 0;

    public void Start()
    {
        if (_timer is not null || !IsSupported)
            return;

        _game = GameProcess.Open(_pid, GameProcessRights.Write);
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _game?.Dispose();
        _game = null;
    }

    public void Dispose() => Stop();

    private void Tick()
    {
        var game = _game;
        if (game is null)
            return;

        try
        {
            var baseAddress = game.ReadUInt32(game.MainModuleBase + _profile.Base.BasePointer);
            game.WriteUInt32(baseAddress + _profile.Base.Unfreeze, 1);
            _warned = false;
        }
        catch (Exception e) when (e is MemoryAccessException or InvalidOperationException)
        {
            if (!_warned)
                _log("Unfreeze: не удалось записать — " + e.Message);
            _warned = true;
        }
    }
}
