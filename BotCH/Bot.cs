using BotCH.Entity;
using BotCH.MemoryHelpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BotCH
{
    internal class Bot
    {
        public static BotForm form;
        public static Thread BotingThread;
        private static uint AgressiveMob = 0;
        private static Dictionary<uint, string> MobsAround;

        public static void Run()
        {
            if (form.checkFindAgrMob.Checked)
            {
                Logger.setLog("Making list of alive mobs");
                MobsAround = MobReader.GetActualListMobsOffsetsInArray();
                Logger.setLog("Around us " + MobsAround.Count() + " mobs");
            }

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

                            KillMobActions(TargetMobEntity.WID);
                            GetLoot();
                        }
                    }
                    else
                    {
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
            if (form.checkFindAgrMob.Checked)
            {
                FindAgressiveMobAroud();
            }

            /* Если после убийства моба автоматически взят таргет и этот таргет нас бьёт */
            if (AgressiveMob != 0 && AgressiveMob == TargetMobEntity.WID)
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
            var timeStart = DateTime.Now;

            while (TargetMobEntity.WID == mobId)
            {
                if (TargetMobEntity.WID == 0)
                {
                    return;
                }

                if ((DateTime.Now - timeStart).Duration().Seconds > 120)
                {
                    Logger.setLog("Change trarget. Time-out 120 sec. to killing mob passed");
                    return;
                }

                if (!form.checkBoxUseSword.Checked && form.checkBoxComeCloser.Checked)
                {
                    Action.AttackByPet();
                    ComeCloser(TargetMobEntity.WID);
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
                    if (i == 0 || i % 5 == 0)
                    {
                        Action.AttackBySword();
                    }
                }

                i++;

                Thread.Sleep(1000);
            }
        }

        private static void ComeCloser(uint mobId)
        {
            if (MobReader.GetMobDistance(mobId, MobsAround) > 3.5)
            {
                Logger.setLog("Distance to mob > 3.5");
                Logger.setLog("Come closer to mob");
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
                    Logger.setLog("Clicked heal pet to prevent SwordAttak");
                }

            }
            else
            {
                Logger.setLog("I already near the mob");
            }
        }
    }
}
