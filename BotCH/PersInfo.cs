using BotCH.MemoryHelpers;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace BotCH
{
    class PersInfo
    {
        [DllImport("user32.dll")]
        static extern bool PostMessage(HandleRef hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        public static BotForm form;

        public static Thread PersInfoLabelsThread;

        public static void ShowPersInfoLabels()
        {
            while (Reader.statusConnection)
            {
                form.labelHpPers.Text = PersReader.GetPersHP().ToString();
                form.labelMpPers.Text = PersReader.GetPersMP().ToString();
                form.labelHpPet.Text = PersReader.GetPetHpPercent().ToString();
                form.targetIdTextBox.Text = PersReader.GetTargetId().ToString();
                PotEnergyBanks();

                if (PersReader.GetPersHP() == 0)
                {
                    ThreadHelper.StopAll();
                    form.StopButton_Click();
                }

                Thread.Sleep(1000);
            }
        }

        public static void PotEnergyBanks()
        {
            if (PersReader.GetPersMP() < Convert.ToInt32(form.textBoxMPusage.Text))
            {
                Action.PotMP();
            }

            if (GetPersHPPercent() < Convert.ToInt32(form.textBoxHPusage.Text))
            {
                if (PersReader.IsPotHPAvailable())
                {
                    Action.PotHP();
                }
            }
        }

        public static int GetPersHPPercent()
        {
            int currentHp = PersReader.GetPersHP();
            int maxHp = PersReader.GetPersMaxHP();
            double percent = (100 * currentHp) / maxHp;

            return (int)Math.Round(percent);
        }
    }
}
