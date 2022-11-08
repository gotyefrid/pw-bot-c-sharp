using System;
using System.Threading;
using System.Threading.Tasks;

namespace BotCH.MemoryHelpers
{
    class Writer : Reader
    {
        public static async void UnFreezeeWindow()
        {
            await Task.Run(() =>
            {
                while (form.checkBoxUnfrezze.Checked)
                {
                    uint baseAdrress = ReadUint32(Offset.Get.GetBaseAddress());
                    memory.WriteMemory((baseAdrress + Offset.Get.UNZREEFE_OFFSET).ToString("X"), "int", "1");
                    Thread.Sleep(1000);
                }
            });
        }

    }
}
