using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BotCH
{
    public class Action
    {
        [DllImport("User32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;

        private static void ClickKey(Keys key)
        {
            PostMessage(Reader.process.MainWindowHandle, WM_KEYDOWN, (IntPtr)key, IntPtr.Zero);
            PostMessage(Reader.process.MainWindowHandle, WM_KEYUP, (IntPtr)key, IntPtr.Zero);
            Thread.Sleep(200);
        }

        private static void ClickCombineKeys(Keys key1, Keys key2)
        {
            PostMessage(Reader.process.MainWindowHandle, WM_KEYDOWN, (IntPtr)key1, IntPtr.Zero);
            PostMessage(Reader.process.MainWindowHandle, WM_KEYDOWN, (IntPtr)key2, IntPtr.Zero);
            PostMessage(Reader.process.MainWindowHandle, WM_KEYUP, (IntPtr)key1, IntPtr.Zero);
            PostMessage(Reader.process.MainWindowHandle, WM_KEYUP, (IntPtr)key2, IntPtr.Zero);
            Thread.Sleep(500);
        }
        public static async void EscapeClicks()
        {
            await Task.Run(() =>
            {
                for (int i = 0; i < 3; i++)
                {
                    Action.ClickKey(Keys.Escape);
                    Thread.Sleep(600);
                }
            });
        }

        public static void ChangeTarget()
        {
            Action.ClickKey(Keys.Tab);
        }

        public static void AttackBySword()
        {
            Action.ClickKey(Keys.F1);
        }

        public static void AttackByPet()
        {
            Action.ClickCombineKeys(Keys.Menu, Keys.D1);
        }

        public static void AttackBySkill()
        {
            Action.ClickKey(Keys.F2);
        }

        public static void PickUpLoot()
        {
            Action.ClickKey(Keys.F4);
        }

        public static void PotHP()
        {
            Action.ClickKey(Keys.F6);
        }

        public static void PotMP()
        {
            Action.ClickKey(Keys.F3);
        }

        public static void FeedPet()
        {
            Action.ClickKey(Keys.F5);
        }

        public static void HealPet()
        {
            Action.ClickKey(Keys.F7);
        }

        public static void BringPetToLife()
        {
            Action.ClickKey(Keys.F8);
        }

        public static void InvitePet()
        {
            Action.ClickKey(Keys.D9);
        }
    }
}
