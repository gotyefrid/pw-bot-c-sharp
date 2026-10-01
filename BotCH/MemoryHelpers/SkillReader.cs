using System.Collections.Generic;

namespace BotCH.MemoryHelpers
{
    /*
     * Изученные скиллы персонажа: [перс + SKILLS_OFFSET] — массив указателей на объекты скиллов, [перс + SKILLS_COUNT_OFFSET] — их число.
     */
    public class SkillReader : Reader
    {
        /// <summary>
        /// Объект изученного скилла или 0, если скилл не изучен.
        /// </summary>
        public static uint GetSkill(uint skillId)
        {
            uint pers = PersReader.GetPersStruct();
            uint skills = ReadUint32(pers + Offset.Get.SKILLS_OFFSET);
            uint count = ReadUint32(pers + Offset.Get.SKILLS_COUNT_OFFSET);

            for (uint i = 0; i < count && i < 256; i++)
            {
                uint skill = ReadUint32(skills + i * 4);

                if (skill != 0 && ReadUint32(skill + Offset.Get.SKILL_ID_OFFSET) == skillId)
                {
                    return skill;
                }
            }

            return 0;
        }

        /// <summary>
        /// ID всех изученных скиллов в порядке списка игры.
        /// </summary>
        public static List<uint> GetLearnedSkillIds()
        {
            var ids = new List<uint>();
            uint pers = PersReader.GetPersStruct();
            uint skills = ReadUint32(pers + Offset.Get.SKILLS_OFFSET);
            uint count = ReadUint32(pers + Offset.Get.SKILLS_COUNT_OFFSET);

            for (uint i = 0; i < count && i < 256; i++)
            {
                uint skill = ReadUint32(skills + i * 4);

                if (skill != 0)
                {
                    ids.Add(ReadUint32(skill + Offset.Get.SKILL_ID_OFFSET));
                }
            }

            return ids;
        }

        /// <summary>
        /// Скилл изучен и не на перезарядке.
        /// </summary>
        public static bool IsReady(uint skillId)
        {
            uint skill = GetSkill(skillId);

            return skill != 0 && ReadUint32(skill + Offset.Get.SKILL_COOLDOWN_OFFSET) == 0;
        }
    }
}
