using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Clients;
using Xunit;

namespace Notes.Tests.Clients;

/// <summary>
/// Нажатие в окно игры: клавиша идёт между «ты активна» и «ты снова в фоне» (WM_ACTIVATEAPP), иначе игра в фоне её
/// выкидывает. Окно и Windows — подставные: тест видит, какие сообщения ушли и в каком порядке.
/// </summary>
public class WindowKeysTests
{
    private const int Pid = 100, OtherPid = 200;
    private const uint ActivateApp = 0x1C, KeyDown = 0x100, KeyUp = 0x101, SysKeyDown = 0x104;

    [Fact]
    public void KeyGoesBetweenActiveAndBackground()
    {
        var window = new FakeKeyWindow { ForegroundNow = (Thread: 7, Process: OtherPid) };

        Assert.True(new WindowKeys(Pid, window).Press(new KeyStroke(0x75)).Ok);

        Assert.Equal(["активна", "↓ 75", "↑ 75", "в фоне, поток 7"], window.Sent);
    }

    [Fact]
    public void ModifiersAreInsideActivation()
    {
        var window = new FakeKeyWindow { ForegroundNow = (Thread: 7, Process: OtherPid) };

        Assert.True(new WindowKeys(Pid, window).Press(new KeyStroke(0x31, Ctrl: true)).Ok);
        Assert.True(new WindowKeys(Pid, window).Press(new KeyStroke(0x31, Alt: true)).Ok);

        Assert.Equal([
            "активна", "↓ 11", "↓ 31", "↑ 31", "↑ 11", "в фоне, поток 7",
            "активна", "сист↓ 12", "сист↓ 31", "↑ 31", "↑ 12", "в фоне, поток 7",
        ], window.Sent);
    }

    [Fact]
    public void BackgroundThreadIsTakenAfterThePress()
    {
        // Пока клавиша зажата, игрок переключился в другое окно — «в фоне» несёт поток того окна, а не прежнего
        var window = new FakeKeyWindow { ForegroundNow = (Thread: 7, Process: OtherPid) };
        window.OnWait = () => window.ForegroundNow = (Thread: 8, Process: OtherPid + 1);

        Assert.True(new WindowKeys(Pid, window).Press(new KeyStroke(0x75)).Ok);

        Assert.Equal("в фоне, поток 8", window.Sent.Last());
    }

    [Fact]
    public void PlayerClickedIntoGameKeepsItActive()
    {
        // Игрок щёлкнул в игру, пока шло нажатие: после «в фоне» — снова «активна», иначе его клавиши пропадали бы
        var window = new FakeKeyWindow { ForegroundNow = (Thread: 7, Process: OtherPid) };
        window.OnWait = () => window.ForegroundNow = (Thread: 9, Process: Pid);

        Assert.True(new WindowKeys(Pid, window).Press(new KeyStroke(0x75)).Ok);

        Assert.Equal(["активна", "↓ 75", "↑ 75", "в фоне, поток 9", "активна"], window.Sent);
    }

    [Fact]
    public void RejectedKeyStillReturnsToBackground()
    {
        var window = new FakeKeyWindow { Rejects = KeyDown };

        Assert.False(new WindowKeys(Pid, window).Press(new KeyStroke(0x31, Ctrl: true)).Ok);

        // Ctrl не ушёл — отпускание всё равно, и игра снова «в фоне»; ждать кадр незачем
        Assert.Equal(["активна", "↑ 31", "↑ 11", "в фоне, поток 0"], window.Sent);
        Assert.Empty(window.Waits);
    }

    [Fact]
    public void RejectedActivationSendsNothingElse()
    {
        var window = new FakeKeyWindow { Rejects = ActivateApp };

        Assert.False(new WindowKeys(Pid, window).Press(new KeyStroke(0x75)).Ok);

        Assert.Empty(window.Sent);
    }

    [Fact]
    public void ForbiddenOrNoWindowSendsNothing()
    {
        var window = new FakeKeyWindow();
        Assert.False(new WindowKeys(Pid, window).Press(new KeyStroke(0x73, Alt: true)).Ok);
        Assert.Empty(window.Sent);

        var none = new FakeKeyWindow { Window = IntPtr.Zero };
        Assert.False(new WindowKeys(Pid, none).Press(new KeyStroke(0x75)).Ok);
        Assert.Empty(none.Sent);
    }

    [Fact]
    public void PressTakesAboutOneTenthOfSecond()
    {
        // Удержание и кадр после отпускания; после «активна» не ждём — сообщения в очереди окна идут по порядку
        var window = new FakeKeyWindow();

        new WindowKeys(Pid, window).Press(new KeyStroke(0x75));

        Assert.Equal([50, 50], window.Waits.Select(w => (int)w.TotalMilliseconds));
    }

    private sealed class FakeKeyWindow : IKeyWindow
    {
        public IntPtr Window = new(0x1234);
        public (int Thread, int Process) ForegroundNow;
        public uint? Rejects;
        public Action? OnWait;
        public List<string> Sent { get; } = [];
        public List<TimeSpan> Waits { get; } = [];

        public IntPtr Find(int pid) => pid == Pid ? Window : IntPtr.Zero;

        public bool Exists(IntPtr window) => window == Window;

        public bool Post(IntPtr window, uint message, int wParam, int lParam)
        {
            Assert.Equal(Window, window);
            if (message == Rejects)
                return false;
            Sent.Add(message switch
            {
                ActivateApp when wParam == 1 => "активна",
                ActivateApp => $"в фоне, поток {lParam}",
                KeyDown => $"↓ {wParam:X2}",
                SysKeyDown => $"сист↓ {wParam:X2}",
                _ => $"↑ {wParam:X2}",
            });
            return true;
        }

        public (int Thread, int Process) Foreground() => ForegroundNow;

        public void Wait(TimeSpan time)
        {
            Waits.Add(time);
            OnWait?.Invoke();
        }
    }
}
