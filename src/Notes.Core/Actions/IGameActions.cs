using System.Collections.Generic;
using Notes.Core.Calls;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Core.Actions;

/// <summary>
/// Всё, что бот умеет делать в игре вызовами функций клиента (<see cref="GameCaller"/>; в тестах — подставные). Клавиши — потом,
/// отдельным способом в <see cref="GameControl"/>. Методы только отправляют команду; сработала ли она, решает
/// <see cref="ActionRunner"/> по снимкам.
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

    /// <summary>Запустить макрос персонажа 1..N, как кнопкой на панели.</summary>
    CallResult ApplyMacro(int macro);

    /// <summary>Отменить текущее действие (каст, копание) — как Esc — и идущий макрос персонажа.</summary>
    CallResult CancelAction();

    /// <summary>Остановить идущий макрос персонажа; каст не прерывает.</summary>
    CallResult StopMacro();

    /// <summary>Открыть банк (хранилище) из сумки — где угодно, в том числе в бою.</summary>
    CallResult OpenBank();

    /// <summary>Открыть лавку (торговля и ремонт) из сумки, как кнопкой в сумке.</summary>
    CallResult OpenShop();

    /// <summary>Продать в открытой лавке: ячейка сумки — сколько штук.</summary>
    CallResult NpcSell(IReadOnlyList<(InventoryItem Item, int Count)> items);

    /// <summary>Починить всё в открытой лавке.</summary>
    CallResult RepairAll();

    /// <summary>Закрыть окно разговора с NPC, как кнопкой «Назад».</summary>
    CallResult NpcDialogClose();

    /// <summary>Закрыть лавку, как крестиком.</summary>
    CallResult ShopDialogClose();

    /// <summary>Встать в ближайшем городе после смерти, как кнопкой окна смерти.</summary>
    CallResult ReviveInTown();

    /// <summary>Начать разговор с NPC (как кликом): откроется окно со списком услуг.</summary>
    CallResult NpcHello(uint wid);

    /// <summary>Открыть банк у банкира (пункт «Услуги хранения»): стоя рядом с ним, разговор не нужен.</summary>
    CallResult NpcOpenBank();

    /// <summary>Сдать задание NPC, с которым начат разговор.</summary>
    CallResult NpcReturnTask(int taskId);

    /// <summary>Взять задание у NPC, с которым начат разговор.</summary>
    CallResult NpcAcceptTask(int taskId);

    /// <summary>Можно ли взять задания сейчас (проверка игры): ответы в <see cref="CallResult.Values"/>, 0 — можно.</summary>
    CallResult QuestChecks(IReadOnlyList<int> taskIds);

    /// <summary>Выучить скилл у NPC, с которым начат разговор.</summary>
    CallResult NpcLearnSkill(int skillId);

    /// <summary>«Ассист»: взять в цель то же, что у члена пати.</summary>
    CallResult TeamAssist(uint wid);

    /// <summary>«Следовать» за игроком: ведёт сам клиент.</summary>
    CallResult Follow(uint wid);

    /// <summary>Вывести питомца из яйца (ячейка сумки) у NPC, с которым начат разговор.</summary>
    CallResult NpcHatchPet(int slot, uint eggTid);

    /// <summary>Подойти к NPC и заговорить, как вторым кликом по нему (игра сама ведёт перса).</summary>
    CallResult TalkTo(uint wid);

    /// <summary>Перенести N штук из ячейки сумки в другую ячейку сумки; в пустую — разделить стопку.</summary>
    CallResult BagMove(int fromSlot, int toSlot, int count);

    /// <summary>Поменять местами ячейку сумки и ячейку банка (основная вкладка): в пустую ячейку — перенос стопки целиком.</summary>
    CallResult BankExchange(int bagSlot, int bankSlot);

    /// <summary>Доложить N штук из ячейки сумки на стопку того же предмета в банке (основная вкладка): стопки сливаются.</summary>
    CallResult BankPutCount(int bagSlot, int bankSlot, int count);


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
