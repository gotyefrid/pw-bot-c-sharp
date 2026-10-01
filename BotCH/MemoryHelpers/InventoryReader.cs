using System.Collections.Generic;

namespace BotCH.MemoryHelpers
{
    /*
     * Сумка персонажа: [перс + INVENTORY_OFFSET] — объект сумки, в нём +INV_ITEMS_OFFSET массив указателей на предметы
     * (ячейка = индекс, пустая ячейка = 0), +INV_SIZE_OFFSET число ячеек.
     */
    public class InventoryReader : Reader
    {
        public const uint CATEGORY_POTION = 9;
        public const uint CATEGORY_PET_FOOD = 27;

        /// <summary>
        /// Самая слабая банка HP (hp = true) или MP, которую персонаж может выпить по уровню.
        /// </summary>
        /// <returns>true, если банка найдена; slot и tid — параметры для использования, durationSec — сколько секунд действует.</returns>
        public static bool FindPotion(bool hp, out uint slot, out uint tid, out uint durationSec)
        {
            uint foundSlot = 0;
            uint foundTid = 0;
            uint foundDuration = 0;

            uint persLevel = ReadUint32(PersReader.GetPersStruct() + Offset.Get.PERS_LEVEL_OFFSET);
            uint weakest = uint.MaxValue;
            bool found = false;

            ForEachItem((i, item) =>
            {
                if (ReadUint32(item + Offset.Get.INV_ITEM_CATEGORY_OFFSET) != CATEGORY_POTION)
                {
                    return;
                }

                uint essence = ReadUint32(item + Offset.Get.INV_ITEM_ESSENCE_OFFSET);
                uint amount = ReadUint32(essence + (hp ? Offset.Get.ESSENCE_POTION_HP_OFFSET : Offset.Get.ESSENCE_POTION_MP_OFFSET));

                if (amount == 0 || ReadUint32(essence + Offset.Get.ESSENCE_LEVEL_OFFSET) > persLevel)
                {
                    return;
                }

                if (amount < weakest)
                {
                    weakest = amount;
                    foundSlot = i;
                    foundTid = ReadUint32(item + Offset.Get.INV_ITEM_TID_OFFSET);
                    foundDuration = ReadUint32(essence + (hp ? Offset.Get.ESSENCE_POTION_HP_TIME_OFFSET : Offset.Get.ESSENCE_POTION_MP_TIME_OFFSET));
                    found = true;
                }
            });

            slot = foundSlot;
            tid = foundTid;
            durationSec = foundDuration;

            return found;
        }

        /// <summary>
        /// Самый малый по верности корм для пета, кроме тех, что в списке skipTids (пет их не ест).
        /// </summary>
        public static bool FindPetFood(ICollection<uint> skipTids, out uint slot, out uint tid)
        {
            uint foundSlot = 0;
            uint foundTid = 0;
            uint weakest = uint.MaxValue;
            bool found = false;

            ForEachItem((i, item) =>
            {
                if (ReadUint32(item + Offset.Get.INV_ITEM_CATEGORY_OFFSET) != CATEGORY_PET_FOOD)
                {
                    return;
                }

                uint itemTid = ReadUint32(item + Offset.Get.INV_ITEM_TID_OFFSET);

                if (skipTids.Contains(itemTid))
                {
                    return;
                }

                uint essence = ReadUint32(item + Offset.Get.INV_FOOD_ESSENCE_OFFSET);
                uint loyalty = ReadUint32(essence + Offset.Get.ESSENCE_FOOD_LOYALTY_OFFSET);

                if (loyalty < weakest)
                {
                    weakest = loyalty;
                    foundSlot = i;
                    foundTid = itemTid;
                    found = true;
                }
            });

            slot = foundSlot;
            tid = foundTid;

            return found;
        }

        /// <summary>
        /// Сколько штук в ячейке slot, если там лежит предмет tid; иначе 0.
        /// </summary>
        public static uint GetCount(uint slot, uint tid)
        {
            uint inventory = ReadUint32(PersReader.GetPersStruct() + Offset.Get.INVENTORY_OFFSET);
            uint item = ReadUint32(ReadUint32(inventory + Offset.Get.INV_ITEMS_OFFSET) + slot * 4);

            if (item == 0 || ReadUint32(item + Offset.Get.INV_ITEM_TID_OFFSET) != tid)
            {
                return 0;
            }

            return ReadUint32(item + Offset.Get.INV_ITEM_COUNT_OFFSET);
        }

        private static void ForEachItem(System.Action<uint, uint> action)
        {
            uint inventory = ReadUint32(PersReader.GetPersStruct() + Offset.Get.INVENTORY_OFFSET);
            uint items = ReadUint32(inventory + Offset.Get.INV_ITEMS_OFFSET);
            uint size = ReadUint32(inventory + Offset.Get.INV_SIZE_OFFSET);

            for (uint i = 0; i < size && i < 256; i++)
            {
                uint item = ReadUint32(items + i * 4);

                if (item != 0)
                {
                    action(i, item);
                }
            }
        }
    }
}
