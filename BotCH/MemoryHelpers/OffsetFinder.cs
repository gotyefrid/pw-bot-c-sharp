using System.Windows.Forms;

namespace BotCH.MemoryHelpers
{
    class OffsetFinder
    {
        public static void FindMobOffByHP(int hp, uint hpOffset)
        {
            for (uint mobOff1 = 0; mobOff1 <= 12; mobOff1++)
            {
                string mobOff1string = (mobOff1 * 4).ToString("X");
                uint mobOff1uint = uint.Parse(mobOff1string, System.Globalization.NumberStyles.HexNumber);

                for (uint mobOff2 = 0; mobOff2 <= 12; mobOff2++)
                {
                    string mobOff2string = (mobOff2 * 4).ToString("X");
                    uint mobOff2uint = uint.Parse(mobOff2string, System.Globalization.NumberStyles.HexNumber);

                    for (uint structMob = 0; structMob <= 12; structMob++)
                    {
                        string mobStructString = (structMob * 4).ToString("X");
                        uint mobStructUint = uint.Parse(mobStructString, System.Globalization.NumberStyles.HexNumber);

                        for (uint mobArray = 0; mobArray <= 768; mobArray++)
                        {
                            string mobArrayString = (mobArray * 4).ToString("X");
                            uint mobArrayStringUint = uint.Parse(mobArrayString, System.Globalization.NumberStyles.HexNumber);
                            uint memoryValue = Reader.ReadUint32(Reader.ReadGameAddress() + mobOff1uint);
                            memoryValue = Reader.ReadUint32(memoryValue + mobOff2uint);

                            memoryValue = Reader.ReadUint32(memoryValue + mobStructUint);
                            memoryValue = Reader.ReadUint32(memoryValue + mobArrayStringUint);
                            uint mobStruct = Reader.ReadUint32(memoryValue + 0x4);
                            memoryValue = Reader.ReadUint32(mobStruct + hpOffset);

                            if (memoryValue != 0)
                            {
                                if (memoryValue == hp)
                                {
                                    MessageBox.Show("Off1 = " + mobOff1string + "; off2 = " + mobOff2string + "; array = " + mobArrayString + "; struct = " + mobStructString);
                                }
                            }
                        }
                    }
                }
            }

            MessageBox.Show("Поиск закончен");
        }
    }
}
