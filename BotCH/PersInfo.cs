using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using System.Runtime.InteropServices;

namespace BotCH
{
    class PersInfo
    {
        [DllImport("user32.dll")]
        static extern bool PostMessage(HandleRef hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        public static BotForm form;
        
        public async static void ShowPersInfoLabels()
        {
            await Task.Run(() =>
            {
                while (Reader.statusConnection)
                {
                    form.labelHpPers.Text = Reader.GetPersHP().ToString();
                    Thread.Sleep(500);
                }
            });
        }
        public async static void Test()
        {
            await Task.Run(() =>
            {
                while (Reader.statusConnection)
                {
                    SendKeys.SendWait("{TAB}");
                    Thread.Sleep(1000);
                }
            });
        }

    }
}
