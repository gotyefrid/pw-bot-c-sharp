using System.Collections.Generic;

namespace Notes.Core.Logging;

/// <summary>
/// Источники записей лога — кто пишет; по ним окно фильтрует лог (в файл пишется всё). Новый источник — строка здесь, и он
/// появится в окне фильтров.
/// </summary>
public static class LogSources
{
    public const string Brain = "мозг";
    public const string Actions = "действия";
    public const string Damage = "урон";
    public const string Connection = "подключение";
    public const string Resources = "ресурсы";
    public const string Memory = "память";
    public const string Window = "окно";

    /// <summary>Все по порядку, как в окне фильтров: источник и что он пишет.</summary>
    public static IReadOnlyList<(string Name, string Hint)> All { get; } =
    [
        (Brain, "Что бот решает: кого бить, что подбирать и копать, куда идти"),
        (Actions, "Что ушло в игру и чем кончилось"),
        (Damage, "Кто кого и сколько: ваши удары и удары пета, удары по вам и пету с HP после удара"),
        (Connection, "Подключение к клиенту"),
        (Resources, "Блокнот ресурсов: где и когда появляются"),
        (Memory, "Сколько памяти осталось у игры"),
        (Window, "Настройки и сообщения окна"),
    ];
}
