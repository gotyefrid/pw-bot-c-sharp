using BotCH.MemoryHelpers;

namespace BotCH.Entity
{
    public static class MyPersEntity
    {
        public const uint TYPE_MOB = 6;
        public const uint TYPE_NPC = 7;
        public const uint TYPE_PET = 9;

        public const uint ACTION_PASSIVE = 1;
        public const uint ACTION_PHYS_ATTACKS = 2;
        public const uint ACTION_MAG_ATTACKS = 3;
        public const uint ACTION_DIES = 4;
        public const uint ACTION_MOVES = 5;

        public static uint WID
        {
            get { return PersReader.GetMyPersWID(); }
        }

        public static bool IsUsingSkill
        {
            get { return PersReader.GetFlagUseSkill(); }
        }

        public static int MaxHP
        {
            get { return PersReader.GetPersMaxHP(); }
        }
    }
}
