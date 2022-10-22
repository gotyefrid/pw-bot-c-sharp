using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

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
                    PersInfo.form.labelHpPers.Text = Reader.GetPersHP().ToString();
                    PersInfo.form.labelMpPers.Text = Reader.GetPersMP().ToString();
                    PersInfo.form.labelHpPet.Text = Reader.GetPetHpPercent().ToString();
                    PersInfo.form.targetIdTextBox.Text = Reader.GetTargetId().ToString();
                    PersInfo.PotEnergyBanks();

                    Thread.Sleep(1000);
                }
            });
        }

        public static void PotEnergyBanks()
        {
            if (Reader.GetPersMP() < Convert.ToInt32(form.textBoxMPusage.Text))
            {
                Action.PotMP();
            }

            if (PersInfo.GetPersHPPercent() < Convert.ToInt32(form.textBoxHPusage.Text))
            {
                Action.PotHP();
            }
        }

        public static int GetPersHPPercent()
        {
            int currentHp = Reader.GetPersHP();
            int maxHp = Reader.GetPersMaxHP();
            double percent = (100 * currentHp) / maxHp;

            return (int)Math.Round(percent);
        }
    }
}
