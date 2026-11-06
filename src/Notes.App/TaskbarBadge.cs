using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Notes.Core.Clients;

namespace Notes.App;

/// <summary>
/// Кнопка окна на панели задач: на значке — буквы ника на цвете персонажа (выбран в окне или по нику, см. <see cref="IconPalette"/>),
/// справа внизу — кружок: зелёный — бот запущен, красный — подключён, но стоит. Не подключены — обычный значок без кружка.
/// Кружок рисуем на самом значке, а не значком поверх (overlay): тот Windows ставит сама и крупно — с полкнопки.
/// <para>
/// Окну игры, к которому подключены, — его родной значок (у каждого клиента свой) с кружком цвета бота справа внизу: видно,
/// какое окно чьё. Отключились — родной значок обратно (наш значок живёт, пока жив бот: без этого после выхода окно осталось бы без значка).
/// </para>
/// </summary>
public sealed class TaskbarBadge
{
    // Рисуем крупнее, чем на панели (24…32 пикселя): Windows уменьшит сама, буквы остаются чёткими
    private const int IconSize = 64;
    // Сколько букв ника на значке — в две строки
    private const int Letters = 6;
    // Кружок — треть значка, вплотную к правому нижнему углу
    private const double DotSize = IconSize / 3.0;

    private static readonly Color Running = Color.FromRgb(0x2E, 0xA0, 0x43);
    private static readonly Color Stopped = Color.FromRgb(0xE5, 0x48, 0x4D);

    private readonly Window _window;
    private readonly MainViewModel _model;
    private readonly ImageSource? _appIcon;
    private (string? Nick, string Color, Color? Dot) _drawn;
    // Окно игры с нашим значком: какое, какого цвета кружок, сам значок (освободить, когда снимем)
    private (IntPtr Window, string Color, IntPtr Icon)? _game;

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
        if (e.PropertyName is nameof(MainViewModel.Nick) or nameof(MainViewModel.IsRunning) or nameof(MainViewModel.IsConnected)
            or nameof(MainViewModel.Client) or nameof(MainViewModel.IconColor))
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
        UpdateGame(_model.Client is { } client && nick is not null ? (client, _model.IconColor) : null);

        Color? dot = !_model.IsConnected ? null : _model.IsRunning ? Running : Stopped;
        var color = _model.IconColor;
        if ((nick, color, dot) == _drawn)
            return;
        _drawn = (nick, color, dot);
        _window.Icon = nick is null && dot is null ? _appIcon : Icon(nick, (Color)ColorConverter.ConvertFromString(color), dot, _appIcon);
    }

    private void UpdateGame((GameClient Client, string Color)? want)
    {
        if (_game is { } had && want is { } same && had.Window == same.Client.Window && had.Color == same.Color)
            return;

        // Снять прежний: окно снова со значком класса (родным)
        if (_game is { } old)
        {
            NativeWindows.SetIcon(old.Window, IntPtr.Zero);
            NativeWindows.DestroyIcon(old.Icon);
            _game = null;
        }
        if (want is not { } now || now.Client.Window == IntPtr.Zero)
            return;

        var icon = GameIcon(now.Client, (Color)ColorConverter.ConvertFromString(now.Color));
        if (icon == IntPtr.Zero)
            return;
        if (NativeWindows.SetIcon(now.Client.Window, icon))
            _game = (now.Client.Window, now.Color, icon);
        else
            NativeWindows.DestroyIcon(icon);
    }

    // Родной значок окна игры и кружок цвета бота справа внизу — крупнее кружка «запущен / стоит»: по нему ищут пару окну бота
    private static IntPtr GameIcon(GameClient client, Color color)
    {
        var (original, owned) = NativeWindows.OriginalIcon(client.Window, client.ExePath);
        if (original == IntPtr.Zero)
            return IntPtr.Zero;
        try
        {
            var picture = Imaging.CreateBitmapSourceFromHIcon(original, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(picture, new Rect(0, 0, IconSize, IconSize));
                DrawDot(dc, color, IconSize * 0.45);
            }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create((BitmapSource)Render(visual, IconSize)));
            using var png = new MemoryStream();
            encoder.Save(png);
            return NativeWindows.IconFromPng(png.ToArray(), IconSize);
        }
        finally
        {
            if (owned)
                NativeWindows.DestroyIcon(original);
        }
    }

    /// <summary>Шесть первых букв (или цифр) ника строчными, по три в строку: «MoonLight» → «moo» и «nli». Букв нет — первые знаки.</summary>
    public static string[] Initials(string nick)
    {
        var letters = nick.Where(char.IsLetterOrDigit).Take(Letters).ToArray();
        var text = new string(letters.Length > 0 ? letters : nick.Take(Letters).ToArray()).ToLower(CultureInfo.CurrentCulture);
        return text.Length <= Letters / 2 ? [text] : [text.Substring(0, Letters / 2), text.Substring(Letters / 2)];
    }

    /// <summary>Значок: буквы ника (ника ещё нет — обычный значок) и кружок справа внизу.</summary>
    public static ImageSource Icon(string? nick, Color background, Color? dot, ImageSource? appIcon)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (nick is not null)
                DrawLetters(dc, nick, background);
            else if (appIcon is not null)
                dc.DrawImage(appIcon, new Rect(0, 0, IconSize, IconSize));

            if (dot is { } color)
                DrawDot(dc, color, DotSize);
        }
        return Render(visual, IconSize);
    }

    // Кружок вплотную к правому нижнему углу; белая обводка — виден на любом значке и на тёмной или светлой панели
    private static void DrawDot(DrawingContext dc, Color color, double size)
    {
        var r = size / 2;
        var center = new Point(IconSize - r, IconSize - r);
        dc.DrawEllipse(Brushes.White, null, center, r, r);
        dc.DrawEllipse(new SolidColorBrush(color), null, center, r - 3, r - 3);
    }

    private static void DrawLetters(DrawingContext dc, string nick, Color background)
    {
        dc.DrawRoundedRectangle(new SolidColorBrush(background), null, new Rect(0, 0, IconSize, IconSize), 12, 12);
        dc.DrawGeometry(Brushes.White, null, LettersGeometry(nick));
    }

    // Буквы ника в две строки по центру значка
    private static Geometry LettersGeometry(string nick)
    {
        var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var em = IconSize * 0.42;
        var text = new FormattedText(string.Join("\n", Initials(nick)), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, em,
            Brushes.White, 1.0) { TextAlignment = TextAlignment.Center, LineHeight = em * 0.95 };
        // Центрируем по самим буквам, а не по строкам шрифта (у строчных пустое место сверху)
        var ink = text.BuildGeometry(new Point(0, 0));
        var box = ink.Bounds;
        // Широкие буквы («ww») — уменьшить, чтобы влезли с полями (кружок может задеть угол буквы — так и задумано)
        var scale = Math.Min(1, IconSize * 0.84 / Math.Max(box.Width, box.Height));
        var group = new TransformGroup();
        group.Children.Add(new TranslateTransform(-(box.Left + box.Width / 2), -(box.Top + box.Height / 2)));
        group.Children.Add(new ScaleTransform(scale, scale));
        group.Children.Add(new TranslateTransform(IconSize / 2.0, IconSize / 2.0));
        ink.Transform = group;
        return ink;
    }

    private static ImageSource Render(Visual visual, int size)
    {
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
