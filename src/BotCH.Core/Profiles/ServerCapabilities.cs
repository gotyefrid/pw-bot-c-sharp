namespace BotCH.Core.Profiles;

/// <summary>
/// Что вообще умеет сервер/клиент. Бот выключает или заменяет функцию, если возможности нет.
/// Здесь — что заявлено в профиле; найдена ли функция в конкретном клиенте, проверяет <see cref="FunctionResolver"/>.
/// </summary>
public sealed record ServerCapabilities(
    bool DirectCalls,
    bool ApproachLikeMouse,
    bool MoveToPoint,
    bool GroundItems,
    bool SkillNamesFromGame)
{
    public static ServerCapabilities From(ProfileData data)
    {
        bool Has(string name) => data.Functions.TryGetValue(name, out var f) && f.Rva != 0;

        return new ServerCapabilities(
            DirectCalls: data.Functions.Count > 0,
            ApproachLikeMouse: Has(GameFunctions.HostApplySkill) && Has(GameFunctions.HostPickupObject),
            MoveToPoint: data.Host.WorkMan != 0 && Has(GameFunctions.WorkCreate) && Has(GameFunctions.WorkMoveSetDestination) && Has(GameFunctions.WorkStart),
            GroundItems: data.World.GroundItems.Manager != 0,
            // configs.pck → skillstr.txt есть у всех известных клиентов; если у какого-то нет — профиль это переопределит
            SkillNamesFromGame: true);
    }
}
