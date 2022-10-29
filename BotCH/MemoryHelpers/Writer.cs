using System;
using System.Threading;
using System.Threading.Tasks;

namespace BotCH.MemoryHelpers
{
    class Writer : Reader
    {
        public static void TargetFirstMobFromArray(string[] idsArray)
        {
            foreach (string id in idsArray)
            {
                VAMemoryClass.WriteUInt32((IntPtr)PersReader.GetTargetIdAddres(), uint.Parse(id));
                Thread.Sleep(200);

                if (PersReader.GetTargetId() != 0)
                {
                    return;
                }
            }
        }
        public static void TargetMob(uint id)
        {
            VAMemoryClass.WriteUInt32((IntPtr)PersReader.GetTargetIdAddres(), id);
        }

        public static async void UnFreezeeWindow()
        {
            await Task.Run(() =>
            {
                while (form.checkBoxUnfrezze.Checked)
                {
                    uint baseAdrress = Read_uint32(Offset.GetBaseAddress());
                    VAMemoryClass.WriteInt32((IntPtr)(baseAdrress + Offset.UNZREEFE_OFFSET), 1);
                    Thread.Sleep(1000);
                }
            });
        }

    }
}
