using System;
using System.Threading;
using System.Threading.Tasks;

namespace BotCH
{
    class Writer : Reader
    {
        public static void TargetFirstMobFromArray(string[] idsArray)
        {
            foreach (string id in idsArray)
            {
                Reader.VAM.WriteUInt32((IntPtr)Reader.GetTargetIdAddres(), uint.Parse(id));
                Thread.Sleep(200);

                if (uint.Parse(Reader.GetTargetId()) != 0)
                {
                    return;
                }
            }
        }
        public static void TargetMob(uint id)
        {
            Reader.VAM.WriteUInt32((IntPtr)Reader.GetTargetIdAddres(), id);
        }

        public static async void UnFreezeeWindow()
        {
            await Task.Run(() =>
            {
                while (form.checkBoxUnfrezze.Checked)
                {
                    Reader.VAM.WriteInt32((IntPtr)Reader.UNZREEFE_ADDR, 1);
                    Thread.Sleep(1000);
                }
            });
        }

    }
}
