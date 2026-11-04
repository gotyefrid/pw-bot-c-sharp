using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using Notes.App.Mvvm;
using Notes.Core.Logging;
using Notes.Core.Notify;
using Notes.Core.Profiles;
using Notes.Core.Settings;

namespace Notes.App.Panels;

/// <summary>
/// Вкладка «Чат», карточка «Уведомления»: куда писать в Telegram и о каких сообщениях. Настройки общие на всех персонажей
/// (settings.json, как подключение) — правка сразу сохраняется и сразу действует у работающего бота.
/// </summary>
public sealed class ChatPanel : ObservableObject
{
    private readonly Func<ChatNotifySettings> _settings;
    private readonly Func<ServerProfile> _profile;
    private readonly Action _save;
    private readonly ILogger _log;
    private string _testResult = "";
    private bool _testing;

    public ChatPanel(Func<ChatNotifySettings> settings, Func<ServerProfile> profile, Action save, ILogger log)
    {
        _settings = settings;
        _profile = profile;
        _save = save;
        _log = log;
        TestCommand = new RelayCommand(Test, () => !_testing && S.CanSend);
    }

    private ChatNotifySettings S => _settings();

    public string Token
    {
        get => S.Token;
        set => Edit(S.Token, value?.Trim() ?? "", v => S.Token = v);
    }

    public string ChatId
    {
        get => S.ChatId;
        set => Edit(S.ChatId, value?.Trim() ?? "", v => S.ChatId = v);
    }

    public bool Private
    {
        get => S.Private;
        set => Edit(S.Private, value, v => S.Private = v);
    }

    public bool Group
    {
        get => S.Group;
        set => Edit(S.Group, value, v => S.Group = v);
    }

    public bool Clan
    {
        get => S.Clan;
        set => Edit(S.Clan, value, v => S.Clan = v);
    }

    public bool Common
    {
        get => S.Common;
        set => Edit(S.Common, value, v => S.Common = v);
    }

    /// <summary>Каналы, которые на сервере ещё не проверены в игре: о них бот не напишет. Пусто — все проверены.</summary>
    public string Unverified
    {
        get
        {
            var c = _profile().Data.Chat.Channels;
            var missing = new List<string>();
            if (c.Private is null)
                missing.Add("личные");
            if (c.Group is null)
                missing.Add("группа");
            if (c.Clan is null)
                missing.Add("клан");
            if (c.Common is null)
                missing.Add("общий");
            return missing.Count == 0 ? "" : $"На сервере «{_profile().Name}» бот пока не умеет: {string.Join(", ", missing)}";
        }
    }

    public ICommand TestCommand { get; }

    /// <summary>Чем кончилась проверка: «Отправлено…» или почему не дошло.</summary>
    public string TestResult
    {
        get => _testResult;
        private set
        {
            if (SetProperty(ref _testResult, value))
                OnPropertyChanged(nameof(HasTestResult));
        }
    }

    public bool HasTestResult => TestResult.Length > 0;

    public bool HasUnverified => Unverified.Length > 0;

    /// <summary>Сменился сервер — другие проверенные каналы.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Unverified));
        OnPropertyChanged(nameof(HasUnverified));
    }

    private void Edit<T>(T old, T value, Action<T> set, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(old, value))
            return;
        set(value);
        OnPropertyChanged(name);
        _save();
    }

    // Проверка — в фоне: Telegram отвечает до 15 с, окно не ждёт
    private void Test()
    {
        _testing = true;
        TestResult = "Отправляю…";
        var (token, chatId) = (S.Token, S.ChatId);
        Task.Run(() => TelegramSender.Send(token, chatId, "Проверка: бот на связи")).ContinueWith(t => UiThread.Run(() =>
        {
            _testing = false;
            var problem = t.Exception is { } e ? e.GetBaseException().Message : t.Result;
            TestResult = problem is null ? "Отправлено — проверьте Telegram" : $"Не дошло: {problem}";
            if (problem is not null)
                _log.Warning($"Проверка Telegram: {problem}");
            CommandManager.InvalidateRequerySuggested();
        }));
    }
}
