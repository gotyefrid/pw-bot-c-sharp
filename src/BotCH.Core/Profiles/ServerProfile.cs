namespace BotCH.Core.Profiles;

/// <summary>
/// Всё, чем один сервер PW отличается от другого, — данные профиля (<c>.jsonc</c>). Логика бота не знает, какой сейчас
/// сервер: новый сервер — новый профиль, без «если сервер такой-то» в коде.
/// </summary>
public sealed class ServerProfile(ProfileData data)
{
    /// <summary>Короткий ключ: "pwclassic136".</summary>
    public string Id => Data.Id;

    /// <summary>Для людей: "PW Classic 1.3.6".</summary>
    public string Name => Data.Name;

    /// <summary>Смещения, адреса функций, ID скиллов.</summary>
    public ProfileData Data { get; } = data;

    /// <summary>Что заявлено в профиле — пока клиент не подключён (после подключения — <see cref="Calls.GameCaller.Capabilities"/>).</summary>
    public Capabilities Capabilities { get; } = Capabilities.FromProfile(data);

    public override string ToString() => Name;
}
