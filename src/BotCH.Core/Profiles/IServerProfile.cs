namespace BotCH.Core.Profiles;

/// <summary>
/// Всё, чем один сервер PW отличается от другого. Логика бота видит только этот интерфейс
/// и не знает, какой сейчас сервер. Новый сервер = новый профиль, логику бота не трогаем.
/// По мере готовности частей сюда добавятся «как прочитать мир» (часть 2) и «как сделать действие» (часть 4).
/// </summary>
public interface IServerProfile
{
    /// <summary>Короткий ключ: "pwclassic136".</summary>
    string Id { get; }

    /// <summary>Для людей: "PW Classic 1.3.6".</summary>
    string Name { get; }

    /// <summary>Смещения, адреса функций, ID скиллов.</summary>
    ProfileData Data { get; }

    ServerCapabilities Capabilities { get; }
}

/// <summary>Профиль, целиком описанный данными (JSON). Для серверов с особым поведением — наследник или своя реализация.</summary>
public class DataServerProfile(ProfileData data) : IServerProfile
{
    public string Id => Data.Id;
    public string Name => Data.Name;
    public ProfileData Data { get; } = data;
    public virtual ServerCapabilities Capabilities { get; } = ServerCapabilities.From(data);

    public override string ToString() => Name;
}
