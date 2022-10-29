using BotCH.MemoryHelpers;
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

        public static BotForm form;

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private static void ClickKey(Keys key, bool needCastingCheck = true)
        {
            if (needCastingCheck)
            {
                WaitForCasting();
            }

            PostMessage(Reader.process.MainWindowHandle, WM_KEYDOWN, (IntPtr)key, IntPtr.Zero);
            PostMessage(Reader.process.MainWindowHandle, WM_KEYUP, (IntPtr)key, IntPtr.Zero);
            Logger.setLog("Press " + key);
            Thread.Sleep(200);
        }

        private static void ClickCombineKeys(Keys key1, Keys key2, bool needCastingCheck = true)
        {
            //if (needCastingCheck)
            //{
            //    WaitForCasting();
            //}

            PostMessage(Reader.process.MainWindowHandle, WM_SYSKEYDOWN, (IntPtr)key1, IntPtr.Zero);
            PostMessage(Reader.process.MainWindowHandle, WM_KEYDOWN, (IntPtr)key2, IntPtr.Zero);
            PostMessage(Reader.process.MainWindowHandle, WM_SYSKEYUP, (IntPtr)key1, IntPtr.Zero);
            //PostMessage(Reader.process.MainWindowHandle, WM_KEYUP, (IntPtr)key2, IntPtr.Zero);
            Logger.setLog("Press " + key1 + " + " + key2);
            Thread.Sleep(500);
        }

        public static void WaitForCasting()
        {
            int i = 0;

            while (PersReader.GetFlagUseSkill())
            {
                if (i == 0)
                {
                    Logger.setLog("Waiting for skill is active");
                }

                Thread.Sleep(1000);
                i++;
            }
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

        public static void EscapeClick(bool needCastingCheck = false)
        {
            Action.ClickKey(Keys.Escape, needCastingCheck);
        }

        public static void ChangeTargetByTab()
        {
            Action.ClickKey(Keys.Tab);
        }
        public static void ChangeTargetByInject()
        {
            string[] mobsIds = BotForm.IniManager.ReadINI("bot", "mobIDs").Split(',');

            Writer.TargetFirstMobFromArray(mobsIds);
        }

        public static void AttackBySword()
        {
            Action.ClickKey(Keys.F1);
        }

        public static void AttackByPet()
        {
            Action.ClickCombineKeys(Keys.Menu, Keys.D1, false);
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
