using BotCH.Entity;
using BotCH.MemoryHelpers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BotCH
{
    internal class Bot
    {
        public static BotForm form;
        public static Thread BotingThread = new Thread(Run);
        private static uint AgressiveMob = 0;
        private static Dictionary<uint, string> MobsAround;

        public static void Run()
        {
            Logger.setLog("Making list of alive mobs");
            MobsAround = MobReader.GetActualListMobsOffsetsInArray();
            Logger.setLog("Around us " + MobsAround.Count() + " mobs");

            while (true)
            {
                Logger.setLog("New Loop");

                if (PersReader.IsExistTarget())
                {
                    if (form.checkBoxCheckId.Checked == true)
                    {
                        bool isAgressiveMobAttackMeNow = AgressiveMob == TargetMobEntity.WID;

                        bool isMobInList = SearchCurrentMobIdInList();

                        if (isMobInList || isAgressiveMobAttackMeNow)
                        {
                            if (!isMobInList && isAgressiveMobAttackMeNow)
                            {
                                Logger.setLog("Killing mob because it attaking me!");
                            }

                            if (form.checkBoxLooting.Checked == true)
                            {
                                Action.AttackByPet();
                                ComeCloser(TargetMobEntity.WID);
                            }

                            KillMobActions(TargetMobEntity.WID);
                            GetLoot();
                        }
                    }
                    else
                    {
                        if (form.checkBoxLooting.Checked == true)
                        {
                            ComeCloser(TargetMobEntity.WID);
                        }

                        KillMobActions(TargetMobEntity.WID);
                        GetLoot();
                    }
                }

                ChangeTarget();
                Thread.Sleep(300);
            }
        }

        public static void ChangeTarget()
        {
            if (FindAgressiveMobAroud())
            {
                return;
            }


            Logger.setLog("Change mob by click TAB");
            Action.ChangeTargetByTab();
        }

        public static bool FindAgressiveMobAroud()
        {
            Logger.setLog("Find agressive mob who beat me");
            uint id = MobReader.IsExistMobAttackingMe(MobsAround);

            if (id != 0)
            {
                AgressiveMob = id;
                Logger.setLog("Found the mob who is attacking me now: " + id);
                Logger.setLog("Choose mob in target who attaking me");
                Writer.TargetMob(id);
                return true;
            }
            else
            {
                Logger.setLog("Not Found agressive mob");
                AgressiveMob = 0;
                return false;
            }
        }

        private static void GetLoot()
        {
            if (form.checkBoxLooting.Checked == true)
            {
                int n = 4;

                Logger.setLog("Pick up loot " + n + " times");

                for (int i = 0; i < n; i++)
                {
                    Action.WaitForCasting();
                    Action.PickUpLoot();
                    Thread.Sleep(600);
                }
            }
        }

        private static bool SearchCurrentMobIdInList()
        {
            string[] mobsIds = BotForm.IniManager.ReadINI("bot", "mobIDs").Split(',');

            uint currTargetId = TargetMobEntity.WID;

            if (mobsIds.Contains(currTargetId.ToString()))
            {
                Logger.setLog("Current mob in target exist in my INI file");

                return true;
            }
            else
            {
                Logger.setLog("Current mob in target not exist in my INI file");

                return false;
            }
        }

        private static void KillMobActions(uint mobId)
        {
            Logger.setLog("Killing mob");
            int i = 0;

            while (TargetMobEntity.WID == mobId)
            {
                if (TargetMobEntity.WID == 0)
                {
                    return;
                }

                if (i == 0)
                {
                    Action.AttackByPet();
                }

                if (form.checkBoxUseSkill.Checked == true)
                {
                    Action.AttackBySkill();
                }

                if (form.checkBoxUseSword.Checked == true)
                {
                    Action.AttackBySword();
                }

                i++;
            }
        }

        private static void ComeCloser(uint mobId)
        {
            Logger.setLog("Come closer to mob");

            if (MobReader.GetMobDistance(mobId, MobsAround) > 3.5)
            {
                int i = 0;

                float beforeDist;

                while ((beforeDist = MobReader.GetMobDistance(mobId, MobsAround)) > 4)
                {
                    if (TargetMobEntity.WID == 0)
                    {
                        break;
                    }

                    if (beforeDist <= MobReader.GetMobDistance(mobId, MobsAround))
                    {
                        Action.AttackBySword();
                    }

                    if (i == 0)
                    {
                        Logger.setLog("Going to mob");
                    }

                    i++;
                }

                Logger.setLog("Came!");

                if (form.checkBoxUseSkill.Checked == true)
                {
                    Action.AttackBySkill();
                    Logger.setLog("Clicked skill");
                    Thread.Sleep(500);
                }
                else
                {
                    Action.HealPet();
                    Logger.setLog("Clicked heal pet");
                }

            }
            else
            {
                Logger.setLog("I already near the mob");
            }
        }
    }
}
