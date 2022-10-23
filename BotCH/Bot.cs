using System.Linq;
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
                    if (Reader.IsExistTarget())
                    {
                        if (form.checkBoxCheckId.Checked == true)
                        {
                            uint isExistMobAttackingMe = Reader.IsExistMobAttackingMe();

                            if (Bot.SearchCurrentMobIdInList() || isExistMobAttackingMe != 0)
                            {
                                Bot.KillMobActions();
                                Bot.GetLoot();
                            }
                        }
                        else
                        {
                            Bot.KillMobActions();
                            Bot.GetLoot();
                        }
                    }
                    else
                    {
                        Action.ChangeTarget();
                        Thread.Sleep(600);
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
                    Logger.setLog("Pick up loot " + (i + 1));
                    Action.PickUpLoot();
                    Thread.Sleep(600);
                }
            }
        }

        private static bool SearchCurrentMobIdInList()
        {
            string[] mobsIds = BotForm.IniManager.ReadINI("bot", "mobIDs").Split(',');

            if (mobsIds.Contains(Reader.GetTargetId()))
            {
                return true;
            }

            return false;
        }

        private static void KillMobActions()
        {
            Logger.setLog("Killing mob");

            if (Reader.GetTargetId() != "0")
            {
                Bot.Attack();
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
                    if (Reader.GetCurrentMobDistance() > 3.5)
                    {
                        while (Reader.GetCurrentMobDistance() > 3.5)
                        {
                            Action.AttackBySword();
                        }
                    }

                }
            }

        }
    }
}
