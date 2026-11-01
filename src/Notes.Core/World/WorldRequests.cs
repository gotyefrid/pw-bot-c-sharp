namespace Notes.Core.World;

/// <summary>
/// Что читать в снимок мира сверх обычного: просит мозг (через <see cref="Actions.GameControl"/>), читает поток снимков. Банк — не
/// четыре раза в секунду всегда, а только пока бот с ним работает.
/// </summary>
public sealed class WorldRequests
{
    private volatile bool _bank;

    /// <summary>Читать основную вкладку банка (<see cref="WorldState.Bank"/>).</summary>
    public bool Bank { get => _bank; set => _bank = value; }
}
