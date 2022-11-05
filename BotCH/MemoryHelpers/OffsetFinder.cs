using System;
using System.Threading;

namespace BotCH.MemoryHelpers
{
    class OffsetFinder
    {
        public const string secondPartOfBase = "70B0";
        public static bool GetBaseAddress()
        {
            INIManager IniManager = new INIManager(BotForm.configName);
            uint hpPersBefore = uint.Parse(IniManager.ReadINI("offset", "persHp"));

            for (uint i = 0; i <= 200; i++)
            {
                string firstPartOfBase = i.ToString();
                string fullBaseAddress = firstPartOfBase + secondPartOfBase;

                try
                {
                    uint baseAddr = Reader.VAMemoryClass.ReadUInt32((IntPtr)uint.Parse(fullBaseAddress, System.Globalization.NumberStyles.HexNumber));
                    uint gameAddr = Reader.VAMemoryClass.ReadUInt32((IntPtr)(baseAddr + Offset.GAMEADDR_OFFSET));
                    uint persStruct = Reader.VAMemoryClass.ReadUInt32((IntPtr)(gameAddr + Offset.PERS_STRUCT_OFFSET));
                    uint hpPers = Reader.VAMemoryClass.ReadUInt32((IntPtr)(persStruct + Offset.PERS_MAX_HP_OFFSET));

                    if (hpPers == hpPersBefore)
                    {
                        
                        Logger.setLog("Founded BaseAddres: " + fullBaseAddress);
                        IniManager.Write("offset", "baseAddr", fullBaseAddress);
                        IniManager.Write("offset", "persHp", hpPers.ToString());
                        return true;
                    }
                }
                catch
                {
                    continue;
                }
            }

            return false;
        }

    }
}
