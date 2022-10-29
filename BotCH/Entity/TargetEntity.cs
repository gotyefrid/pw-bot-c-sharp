using BotCH.MemoryHelpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BotCH.Entity
{
    public static class TargetMobEntity
    {
        public const uint TYPE_MOB = 6;
        public const uint TYPE_NPC = 7;
        public const uint TYPE_PET = 9;

        public const uint ACTION_PASSIVE = 1;
        public const uint ACTION_PHYS_ATTACKS = 2;
        public const uint ACTION_MAG_ATTACKS = 3;
        public const uint ACTION_DIES = 4;
        public const uint ACTION_MOVES = 5;

        public static uint WID { 
            get { return PersReader.GetTargetId(); }
        }

        public static uint Type
        {
            get { return MobReader.GetMobType(WID); }
        }

        public static uint Action
        {
            get { return MobReader.GetMobAction(WID); }
        }
        public static float Distance
        {
            get { return MobReader.GetMobDistance(WID); }
        }
    }
}
