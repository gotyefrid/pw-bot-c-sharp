namespace Notes.Core.World;

/// <summary>Классы персонажей — номера, как их хранит игра (одинаковы у серверов PW).</summary>
public static class PlayerClasses
{
    public const int Assassin = 5;

    // 3 (друид) и 7 (жрец) сверены в игре; 8 и 9 — по обычной нумерации PW
    private static readonly string[] Names = ["воин", "маг", "шаман", "друид", "оборотень", "убийца", "лучник", "жрец", "страж", "мистик"];

    /// <summary>Название; null — номер неизвестен.</summary>
    public static string? Name(int? number) => number is >= 0 and < 10 ? Names[number.Value] : null;
}
