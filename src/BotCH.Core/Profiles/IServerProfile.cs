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

    /// <summary>Что заявлено в профиле — пока клиент не подключён (после подключения — <see cref="Calls.GameCaller.Capabilities"/>).</summary>
    Capabilities Capabilities { get; }
}

/// <summary>Профиль, целиком описанный данными (JSON). Для серверов с особым поведением — наследник или своя реализация.</summary>
public class DataServerProfile(ProfileData data) : IServerProfile
{
    public string Id => Data.Id;
    public string Name => Data.Name;
    public ProfileData Data { get; } = data;
    public Capabilities Capabilities { get; } = Capabilities.FromProfile(data);

    public override string ToString() => Name;
}
