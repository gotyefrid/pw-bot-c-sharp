namespace BotCH.MemoryHelpers
{
    /*
     * Предметы на земле (дроп, монеты, ресурсы для сбора).
     * Список устроен как список мобов: [мир + GROUND_ITEMS_OFFSET] + MOB_STRUCT_OFFSET, 769 ячеек, в ячейке +0x4 — объект.
     */
    public class ItemReader : Reader
    {
        public const uint KIND_ITEM = 1;
        public const uint KIND_RESOURCE = 2; // корни, шахты — их копают, а не подбирают
        public const uint KIND_MONEY = 3;

        /// <summary>
        /// Ближайший предмет или монета не дальше maxDist. Ресурсы для сбора пропускаются.
        /// </summary>
        /// <returns>true, если предмет найден; id и tid — параметры для подбора.</returns>
        public static bool FindNearestItem(float maxDist, out uint id, out uint tid)
        {
            id = 0;
            tid = 0;

            uint array = ReadUint32(ReadGameAddress() + Offset.Get.MOB_OFFSET_1);
            array = ReadUint32(array + Offset.Get.GROUND_ITEMS_OFFSET);
            array = ReadUint32(array + Offset.Get.MOB_STRUCT_OFFSET);

            float nearestDist = maxDist;
            bool found = false;

            for (uint i = 0; i <= 768; i++)
            {
                uint entry = ReadUint32(array + i * 4);

                if (entry == 0)
                {
                    continue;
                }

                uint item = ReadUint32(entry + 0x4);
                uint kind = ReadUint32(item + Offset.Get.ITEM_KIND_OFFSET);

                if (kind != KIND_ITEM && kind != KIND_MONEY)
                {
                    continue;
                }

                float dist = ReadFloat(item + Offset.Get.ITEM_DIST_OFFSET);

                if (dist <= nearestDist)
                {
                    nearestDist = dist;
                    id = ReadUint32(item + Offset.Get.ITEM_ID_OFFSET);
                    tid = ReadUint32(item + Offset.Get.ITEM_TID_OFFSET);
                    found = true;
                }
            }

            return found;
        }
    }
}
