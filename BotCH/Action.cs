using BotCH.Entity;
using BotCH.MemoryHelpers;
using System;
using System.Collections.Generic;
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
                WaitForCasting(key);
            }

            PostMessage(Reader.process.MainWindowHandle, WM_KEYDOWN, (IntPtr)key, IntPtr.Zero);
            PostMessage(Reader.process.MainWindowHandle, WM_KEYUP, (IntPtr)key, IntPtr.Zero);

            if (Logger.KeyLogger)
            {
                Logger.setLog("Press " + key);
            }

            Thread.Sleep(200);
        }

        private static void ClickCombineKeys(Keys key1, Keys key2)
        {
            PostMessage(Reader.process.MainWindowHandle, WM_SYSKEYDOWN, (IntPtr)key1, IntPtr.Zero);
            PostMessage(Reader.process.MainWindowHandle, WM_KEYDOWN, (IntPtr)key2, IntPtr.Zero);
            PostMessage(Reader.process.MainWindowHandle, WM_SYSKEYUP, (IntPtr)key1, IntPtr.Zero);
            //PostMessage(Reader.process.MainWindowHandle, WM_KEYUP, (IntPtr)key2, IntPtr.Zero);

            if (Logger.KeyLogger)
            {
                Logger.setLog("Press " + key1 + " + " + key2);
            }

            Thread.Sleep(500);
        }

        public static void WaitForCasting(Keys key)
        {
            int i = 0;

            while (PersReader.GetFlagUseSkill())
            {
                if (i == 0)
                {
                    Logger.setLog("Waiting for skill is active before click " + key.ToString());
                }

                Thread.Sleep(1000);
                i++;
            }

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

        public static void EscapeClick(bool needCastingCheck = false)
        {
            Action.ClickKey(Keys.Escape, needCastingCheck);
        }

        public static void ChangeTargetByTab()
        {
            Action.ClickKey(Keys.Tab);
        }

        // Выбор ближайшего моба прямым вызовом функции игры (работает при неактивном окне).
        // allowedWids — белый список WID или null. Возвращает WID выбранного моба или 0.
        public static uint SelectNearestMob(ICollection<string> allowedWids)
        {
            uint wid = MobReader.FindNearestMob(allowedWids);

            if (wid == 0)
            {
                Logger.setLog("No mobs around to select");
                Thread.Sleep(1000);
                return 0;
            }

            return SelectMob(wid) ? wid : 0;
        }

        // Выбор конкретного моба прямым вызовом функции игры
        public static bool SelectMob(uint wid)
        {
            if (!GameCall.SelectTarget(wid))
            {
                return false;
            }

            Logger.setLog("Select target " + wid + " (direct call)");
            Thread.Sleep(300);

            return true;
        }

        public static void AttackBySword()
        {
            if (GameCall.Enabled)
            {
                WaitForCasting(Keys.F1);

                if (GameCall.NormalAttack())
                {
                    if (Logger.KeyLogger)
                    {
                        Logger.setLog("Normal attack (direct call)");
                    }

                    Thread.Sleep(200);
                    return;
                }
            }

            Action.ClickKey(Keys.F1);
        }

        public static void AttackByPet()
        {
            if (GameCall.CanPetAttack)
            {
                uint target = TargetMobEntity.WID;

                if (target != 0 && GameCall.PetAttack(target))
                {
                    if (Logger.KeyLogger)
                    {
                        Logger.setLog("Pet attack " + target + " (direct call)");
                    }

                    Thread.Sleep(500);
                    return;
                }
            }

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

        public static void HealPet(bool waitCasting = true)
        {
            Action.ClickKey(Keys.F7, waitCasting);
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
