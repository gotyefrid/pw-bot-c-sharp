using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;

namespace Notes.Patcher;

/// <summary>Что сейчас с патчем на выбранном клиенте.</summary>
public enum PatchState
{
    /// <summary>Патч можно применить.</summary>
    NotApplied,

    /// <summary>Патч уже стоит — можно откатить.</summary>
    Applied,

    /// <summary>Применить нельзя: клиент не узнан, не читается или не выбран.</summary>
    Unavailable,
}

/// <summary>
/// Строка списка патчей: имя, описание, состояние и кнопка «Пропатчить»/«Откатить».
/// Новый патч — это ещё один такой объект в списке окна.
/// </summary>
public sealed class PatchItem : INotifyPropertyChanged
{
    private static readonly Brush NeutralBackground = Freeze(Color.FromRgb(0xF1, 0xF3, 0xF5));
    private static readonly Brush NeutralForeground = Freeze(Color.FromRgb(0x6B, 0x72, 0x80));
    private static readonly Brush AppliedBackground = Freeze(Color.FromRgb(0xE7, 0xF6, 0xEC));
    private static readonly Brush AppliedForeground = Freeze(Color.FromRgb(0x18, 0x79, 0x4E));
    private static readonly Brush UnavailableBackground = Freeze(Color.FromRgb(0xFD, 0xEC, 0xEC));
    private static readonly Brush UnavailableForeground = Freeze(Color.FromRgb(0xB4, 0x23, 0x18));

    private readonly Action _apply;
    private readonly Action _revert;
    private PatchState _state;
    private string _stateText = "";
    private string _actionText = "";
    private bool _canAct;
    private bool _busy;
    private Brush _badgeBackground = NeutralBackground;
    private Brush _badgeForeground = NeutralForeground;

    public PatchItem(string name, string description, Action apply, Action revert)
    {
        Name = name;
        Description = description;
        _apply = apply;
        _revert = revert;
        ActCommand = new RelayCommand(Act, () => CanAct && !Busy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public string Description { get; }

    public ICommand ActCommand { get; }

    public PatchState State
    {
        get => _state;
        private set
        {
            if (_state == value)
                return;
            _state = value;
            OnPropertyChanged();
        }
    }

    public string StateText
    {
        get => _stateText;
        private set
        {
            if (_stateText == value)
                return;
            _stateText = value;
            OnPropertyChanged();
        }
    }

    public string ActionText
    {
        get => _actionText;
        private set
        {
            if (_actionText == value)
                return;
            _actionText = value;
            OnPropertyChanged();
        }
    }

    public bool CanAct
    {
        get => _canAct;
        private set
        {
            if (_canAct == value)
                return;
            _canAct = value;
            OnPropertyChanged();
            ((RelayCommand)ActCommand).RaiseCanExecuteChanged();
        }
    }

    /// <summary>Пока идёт патч/откат, кнопку не нажимаем повторно.</summary>
    public bool Busy
    {
        get => _busy;
        set
        {
            if (_busy == value)
                return;
            _busy = value;
            OnPropertyChanged();
            ((RelayCommand)ActCommand).RaiseCanExecuteChanged();
        }
    }

    public Brush BadgeBackground
    {
        get => _badgeBackground;
        private set
        {
            if (ReferenceEquals(_badgeBackground, value))
                return;
            _badgeBackground = value;
            OnPropertyChanged();
        }
    }

    public Brush BadgeForeground
    {
        get => _badgeForeground;
        private set
        {
            if (ReferenceEquals(_badgeForeground, value))
                return;
            _badgeForeground = value;
            OnPropertyChanged();
        }
    }

    public void SetState(PatchState state, string stateText, string actionText, bool canAct)
    {
        State = state;
        StateText = stateText;
        ActionText = actionText;
        CanAct = canAct;

        switch (state)
        {
            case PatchState.Applied:
                BadgeBackground = AppliedBackground;
                BadgeForeground = AppliedForeground;
                break;
            case PatchState.Unavailable:
                BadgeBackground = UnavailableBackground;
                BadgeForeground = UnavailableForeground;
                break;
            default:
                BadgeBackground = NeutralBackground;
                BadgeForeground = NeutralForeground;
                break;
        }
    }

    private void Act()
    {
        if (Busy)
            return;

        switch (State)
        {
            case PatchState.Applied:
                _revert();
                break;
            case PatchState.NotApplied:
                _apply();
                break;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
