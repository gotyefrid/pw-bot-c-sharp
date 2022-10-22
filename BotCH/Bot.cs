using System.Threading;
using System.Threading.Tasks;

namespace BotCH
{
    internal class Bot
    {
        public static bool active = false;
        public static BotForm form;

        public async static void Run()
        {
            await Task.Run(() =>
            {
                while (Bot.active)
                {
                    Reader.IsPetInvited();
                    //form.label8.Text = Reader.IsPetInvited().ToString();
                    if (form.checkBoxCheckId.Checked == true)
                    {
                        if (Bot.SearchMobIdInList())
                        {
                            Bot.KillMobActions();
                        }
                        else
                        {
                            Action.ChangeTarget();
                        }
                    }
                    else
                    {
                        Bot.KillMobActions();
                    }

                    Logger.setLog("Waiting");
                }
            });
        }

        private static void GetLoot()
        {
            if (form.checkBoxLooting.Checked == true)
            {
                for (int i = 0; i < 3; i++)
                {
                    Logger.setLog("Pick up loot " + (i+1));
                    Action.PickUpLoot();
                    Thread.Sleep(600);
                }
            }     
        }

        private static bool SearchMobIdInList()
        {
            return true;
        }

        private static void KillMobActions()
        {
            Logger.setLog("Killing mob");

            if (Reader.GetTargetId() != "0")
            {
                Bot.Attack();
                Bot.GetLoot();
            }
            else
            {
                Action.ChangeTarget();
            }
        }

        private static void Attack()
        {
            while (Reader.GetTargetId() != "0")
            {
                Action.AttackByPet();

                if (form.checkBoxUseSkill.Checked == true)
                {
                    Action.AttackBySkill();
                }

                if (form.checkBoxUseSword.Checked == true)
                {
                    Action.AttackBySword();
                }
            }

        }
    }
}
