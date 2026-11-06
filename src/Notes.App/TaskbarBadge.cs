using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Notes.App;

/// <summary>
/// Кнопка окна на панели задач: на значке — две первые буквы ника на своём цвете (у каждого ника свой, между запусками тот же),
/// справа внизу — кружок: зелёный — бот запущен, красный — подключён, но стоит. Не подключены — обычный значок без кружка.
/// Кружок рисуем на самом значке, а не значком поверх (overlay): тот Windows ставит сама и крупно — с полкнопки.
/// </summary>
public sealed class TaskbarBadge
{
    // Рисуем крупнее, чем на панели (24…32 пикселя): Windows уменьшит сама, буквы остаются чёткими
    private const int IconSize = 64;
    // Кружок — треть значка, вплотную к правому нижнему углу
    private const double DotSize = IconSize / 3.0;

    private static readonly Color Running = Color.FromRgb(0x2E, 0xA0, 0x43);
    private static readonly Color Stopped = Color.FromRgb(0xE5, 0x48, 0x4D);

    private readonly Window _window;
    private readonly MainViewModel _model;
    private readonly ImageSource? _appIcon;
    private (string? Nick, Color? Dot) _drawn;

    public TaskbarBadge(Window window, MainViewModel model)
    {
        _window = window;
        _model = model;
        _appIcon = window.Icon;
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
        Color? dot = !_model.IsConnected ? null : _model.IsRunning ? Running : Stopped;
        if ((nick, dot) == _drawn)
            return;
        _drawn = (nick, dot);
        _window.Icon = nick is null && dot is null ? _appIcon : Icon(nick, dot, _appIcon);
    }

    /// <summary>Четыре первые буквы (или цифры) ника строчными, по две в строку: «MoonLight» → «mo» и «on». Букв нет — первые знаки.</summary>
    public static string[] Initials(string nick)
    {
        var letters = nick.Where(char.IsLetterOrDigit).Take(4).ToArray();
        var text = new string(letters.Length > 0 ? letters : nick.Take(4).ToArray()).ToLower(CultureInfo.CurrentCulture);
        return text.Length <= 2 ? [text] : [text.Substring(0, 2), text.Substring(2)];
    }

    /// <summary>Цвет ника: тон по хешу имени (свой, а не string.GetHashCode — тот может меняться между версиями .NET).</summary>
    public static Color Hue(string nick)
    {
        uint hash = 2166136261;
        foreach (var ch in nick)
            hash = (hash ^ ch) * 16777619;
        return FromHsl(hash % 360, 0.55, 0.40);
    }

    /// <summary>Значок: буквы ника (ника ещё нет — обычный значок) и кружок справа внизу.</summary>
    public static ImageSource Icon(string? nick, Color? dot, ImageSource? appIcon)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (nick is not null)
                DrawLetters(dc, nick);
            else if (appIcon is not null)
                dc.DrawImage(appIcon, new Rect(0, 0, IconSize, IconSize));

            if (dot is { } color)
            {
                // Белая обводка: кружок виден и на цвете значка, и на тёмной или светлой панели
                var r = DotSize / 2;
                var center = new Point(IconSize - r, IconSize - r);
                dc.DrawEllipse(Brushes.White, null, center, r, r);
                dc.DrawEllipse(new SolidColorBrush(color), null, center, r - 3, r - 3);
            }
        }
        return Render(visual, IconSize);
    }

    private static void DrawLetters(DrawingContext dc, string nick)
    {
        dc.DrawRoundedRectangle(new SolidColorBrush(Hue(nick)), null, new Rect(0, 0, IconSize, IconSize), 12, 12);
        var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var em = IconSize * 0.42;
        var text = new FormattedText(string.Join("\n", Initials(nick)), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, em,
            Brushes.White, 1.0) { TextAlignment = TextAlignment.Center, LineHeight = em * 0.95 };
        // Центрируем по самим буквам, а не по строкам шрифта (у строчных пустое место сверху)
        var ink = text.BuildGeometry(new Point(0, 0));
        var box = ink.Bounds;
        // Буквы занимают не больше 72 % значка: место под кружок
        var scale = Math.Min(1, IconSize * 0.72 / Math.Max(box.Width, box.Height));
        var group = new TransformGroup();
        group.Children.Add(new TranslateTransform(-(box.Left + box.Width / 2), -(box.Top + box.Height / 2)));
        group.Children.Add(new ScaleTransform(scale, scale));
        // Чуть левее и выше середины — дальше от кружка в правом нижнем углу
        group.Children.Add(new TranslateTransform(IconSize / 2.0 - 3, IconSize / 2.0 - 3));
        ink.Transform = group;
        dc.DrawGeometry(Brushes.White, null, ink);
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
