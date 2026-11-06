using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace Notes.App;

/// <summary>
/// Кнопка окна на панели задач: на значке — две первые буквы ника на своём цвете (у каждого ника свой, между запусками тот же),
/// в углу — кружок: зелёный — бот запущен, красный — подключён, но стоит. Не подключены — обычный значок без кружка.
/// </summary>
public sealed class TaskbarBadge
{
    // Рисуем крупнее, чем на панели (24…32 пикселя): Windows уменьшит сама, буквы остаются чёткими
    private const int IconSize = 64;
    private const int DotSize = 32;

    private static readonly Color Running = Color.FromRgb(0x2E, 0xA0, 0x43);
    private static readonly Color Stopped = Color.FromRgb(0xE5, 0x48, 0x4D);

    private readonly Window _window;
    private readonly MainViewModel _model;
    private readonly ImageSource? _appIcon;
    private readonly ImageSource _runningDot = Dot(Running);
    private readonly ImageSource _stoppedDot = Dot(Stopped);
    private string? _drawnNick;

    public TaskbarBadge(Window window, MainViewModel model)
    {
        _window = window;
        _model = model;
        _appIcon = window.Icon;
        window.TaskbarItemInfo ??= new TaskbarItemInfo();
        model.PropertyChanged += Changed;
        Update();
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Nick) or nameof(MainViewModel.IsRunning) or nameof(MainViewModel.IsConnected))
        {
            if (_window.Dispatcher.CheckAccess())
                Update();
            else
                _window.Dispatcher.BeginInvoke(new Action(Update));
        }
    }

    private void Update()
    {
        var nick = _model.IsConnected ? _model.Nick : null;
        if (nick != _drawnNick)
        {
            _drawnNick = nick;
            _window.Icon = nick is null ? _appIcon : Letters(nick);
        }

        _window.TaskbarItemInfo.Overlay = !_model.IsConnected ? null : _model.IsRunning ? _runningDot : _stoppedDot;
    }

    /// <summary>Две первые буквы (или цифры) ника: «StarFox» → «Sa». Букв нет — два первых знака.</summary>
    public static string Initials(string nick)
    {
        var letters = nick.Where(char.IsLetterOrDigit).Take(2).ToArray();
        var text = letters.Length > 0 ? new string(letters) : new string(nick.Take(2).ToArray());
        return text.Length == 0 ? "" : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text.Substring(1);
    }

    /// <summary>Цвет ника: тон по хешу имени (свой, а не string.GetHashCode — тот может меняться между версиями .NET).</summary>
    public static Color Hue(string nick)
    {
        uint hash = 2166136261;
        foreach (var ch in nick)
            hash = (hash ^ ch) * 16777619;
        return FromHsl(hash % 360, 0.55, 0.40);
    }

    private static ImageSource Letters(string nick)
    {
        var text = Initials(nick);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Hue(nick)), null, new Rect(0, 0, IconSize, IconSize), 12, 12);
            var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var size = IconSize * 0.62;
            FormattedText Format(double em) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, em, Brushes.White, 1.0);
            var formatted = Format(size);
            // Широкие буквы («Ww») — уменьшить, чтобы влезли с полями
            if (formatted.Width > IconSize * 0.9)
                formatted = Format(size * IconSize * 0.9 / formatted.Width);
            dc.DrawText(formatted, new Point((IconSize - formatted.Width) / 2, (IconSize - formatted.Height) / 2));
        }
        return Render(visual, IconSize);
    }

    private static ImageSource Dot(Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var r = DotSize / 2.0;
            // Белая обводка: кружок виден и на тёмной, и на светлой панели, и на цвете значка
            dc.DrawEllipse(new SolidColorBrush(color), new Pen(Brushes.White, 3), new Point(r, r), r - 2, r - 2);
        }
        return Render(visual, DotSize);
    }

    private static ImageSource Render(Visual visual, int size)
    {
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = lightness - c / 2;
        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
