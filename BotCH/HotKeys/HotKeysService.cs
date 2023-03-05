

using System.Runtime.InteropServices;
using System.Windows.Forms;
using System;

namespace BotCH.HotKeys
{
    public class HotKeysService
    {
        public static BotForm form;

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public const int WM_HOTKEY = 0x0312;
        public static void HotKeysHandler(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    int id = m.WParam.ToInt32();
                    switch (id)
                    {
                        case 1:
                            form.StartButton_Click();
                            break;
                        case 2:
                            form.StopButton_Click();
                            break;
                    }
                }
            }
        }

        public static void RegisterHotKeysToApp()
        {
            // регистрация глобальной горячей клавиши
            RegisterHotKey(form.Handle, 1, (uint)0x0002 | (uint)0x0001, (uint)Keys.NumPad1);
            RegisterHotKey(form.Handle, 2, (uint)0x0002 | (uint)0x0001, (uint)Keys.NumPad0);
        }

        public static void UnregisterHotKeysFromApp()
        {
            // отмена регистрации глобальной горячей клавиши
            UnregisterHotKey(form.Handle, 1);
            UnregisterHotKey(form.Handle, 2);
        }

    }
}
