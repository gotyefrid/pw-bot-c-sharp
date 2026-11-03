namespace Notes.Core.World;

/// <summary>
/// Координаты как на миникарте игры: X и Y — <c>(X + 4000) / 10</c> и <c>(Y + 5500) / 10</c> от координат в памяти, высота — в
/// памяти в 10 раз больше. Проверено в игре (PW Classic): в памяти (-61,9; -1033,9; h 224,9) — на миникарте 394 447, высота 22.
/// Как проверить на другом клиенте: Probe snapshot показывает координаты перса в памяти — сравнить с миникартой.
/// </summary>
public readonly record struct MapCoords(double X, double Y, double Height)
{
    private const double ShiftX = 4000;
    private const double ShiftY = 5500;
    private const double Scale = 10;

    public static MapCoords From(Position p) => new((p.X + ShiftX) / Scale, (p.Y + ShiftY) / Scale, p.Height / Scale);

    public Position ToPosition() => new((float)(X * Scale - ShiftX), (float)(Height * Scale), (float)(Y * Scale - ShiftY));
}
