using System;
using System.Globalization;
using Notes.Core.World;

namespace Notes.App.Panels;

/// <summary>Координаты точки в окне: как на миникарте игры (X, Y, высота); дробь — и через запятую, и через точку.</summary>
internal static class PointText
{
    public static (string X, string Y, string Height) Format(Position p)
    {
        var map = MapCoords.From(p);
        return (Show(map.X), Show(map.Y), Show(map.Height));
    }

    public static string Show(double v) => v.ToString("0.#", CultureInfo.CurrentCulture);

    public static double? Parse(string text)
    {
        var t = (text ?? "").Trim().Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !double.IsNaN(v) && !double.IsInfinity(v)
            ? v
            : null;
    }
}
