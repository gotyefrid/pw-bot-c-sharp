using BotCH.Core.Calls;
using BotCH.Core.Profiles;
using BotCH.Core.World;

namespace BotCH.Core.Actions;

/// <summary>
/// Всё, что бот умеет делать в игре. Реализации: прямые вызовы функций клиента (<see cref="DirectCallActions"/>),
/// позже — клавиши. Методы только отправляют команду; сработала ли она, решает <see cref="ActionRunner"/> по снимкам.
/// </summary>
public interface IGameActions
{
    /// <summary>Как выполняются действия: «вызовы» / «клавиши».</summary>
    string Mode { get; }

    /// <summary>
    /// Что доступно на этом клиенте. Нет <see cref="Capability.Move"/> — мозг не ходит сам (подбор «как мышкой» всё равно подводит
    /// к предмету); нет <see cref="Capability.SmartMove"/> — <see cref="MoveTo"/> бежит по прямой; нет <see cref="Capability.Cancel"/> —
    /// пет ждёт, пока персонаж освободится.
    /// </summary>
    Capabilities Capabilities { get; }

    CallResult SelectTarget(uint wid);
    CallResult Unselect();
    CallResult NormalAttack();

    /// <summary>Скилл пакетом, без подхода. target 0 — без цели.</summary>
    CallResult CastSkill(int skillId, uint targetWid);

    /// <summary>Скилл как нажатием кнопки: клиент сам подходит на дальность. target 0 — текущая цель.</summary>
    CallResult ApplySkill(int skillId, uint targetWid);

    CallResult PetAttack(uint targetWid);

    /// <summary>Отменить текущее действие (каст, копание) — как Esc.</summary>
    CallResult CancelAction();

    /// <summary>Подобрать пакетом (только в радиусе ~10 м).</summary>
    CallResult Pickup(GroundItem item);

    /// <summary>Подобрать как кликом мыши: клиент подходит сам.</summary>
    CallResult PickupObject(GroundItem item);

    CallResult UseItem(InventoryItem item);
    CallResult SummonPet(int cage);

    /// <summary>Умеет ли отзывать пета (<see cref="RecallPet"/>). Нет — пет остаётся призванным.</summary>

    /// <summary>Отозвать призванного пета в клетку.</summary>
    CallResult RecallPet();

    /// <summary>Бежать в точку: по прямой или <paramref name="smart"/> — с автопутём (если умеет, иначе по прямой).</summary>
    CallResult MoveTo(Position point, bool smart);

    /// <summary>Собрать ресурс (трава, руда) как кликом мыши: PickupObject с gather — клиент подходит и копает сам.</summary>
    CallResult Gather(GroundItem resource);

    /// <summary>Кнопка «Полёт»: на земле — взлететь, в воздухе — сесть.</summary>
    CallResult ToggleFly();

    /// <summary>Лететь в точку вместе с её высотой (только в воздухе).</summary>
    CallResult FlyTo(Position point);
}

/// <summary>Действия прямыми вызовами функций клиента — работают и при неактивном окне игры.</summary>
public sealed class DirectCallActions(GameCaller caller) : IGameActions
{
    public string Mode => "вызовы";

    public Capabilities Capabilities => caller.Capabilities;

    public CallResult SelectTarget(uint wid) => caller.SelectTarget(wid);
    public CallResult Unselect() => caller.Unselect();
    public CallResult NormalAttack() => caller.NormalAttack();
    public CallResult CastSkill(int skillId, uint targetWid) => caller.CastSkill(skillId, targetWid);
    public CallResult ApplySkill(int skillId, uint targetWid) => caller.ApplySkill(skillId, targetWid);
    public CallResult PetAttack(uint targetWid) => caller.PetAttack(targetWid);
    public CallResult CancelAction() => caller.CancelAction();
    public CallResult Pickup(GroundItem item) => caller.Pickup(item.Id, item.Tid);
    public CallResult PickupObject(GroundItem item) => caller.PickupObject(item.Id);
    public CallResult UseItem(InventoryItem item) => caller.UseItem(item.Slot, item.Tid);
    public CallResult SummonPet(int cage) => caller.SummonPet(cage);
    public CallResult RecallPet() => caller.RecallPet();
    public CallResult MoveTo(Position point, bool smart) => caller.MoveTo(point.X, point.Height, point.Y, smart);
    public CallResult Gather(GroundItem resource) => caller.PickupObject(resource.Id, gather: true);
    public CallResult ToggleFly() => caller.ToggleFly();
    public CallResult FlyTo(Position point) => caller.FlyTo(point.X, point.Height, point.Y);
}
