﻿using BotCH.Entity;
using BotCH.MemoryHelpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BotCH
{
    internal class Bot
    {
        public static bool active = false;
        public static BotForm form;
        private static uint AgressiveMob = 0;
        private static Dictionary<uint, string> MobsAround;

        public async static void Run()
        {
            await Task.Run(() =>
            {
                try
                {
                    Logger.setLog("Making list of alive mobs");
                    MobsAround = MobReader.GetActualListMobsOffsetsInArray();
                    Logger.setLog("Around us " + MobsAround.Count() + " mobs");

                    while (active)
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
                catch (Exception ex)
                {
                    active = false;
                    MessageBox.Show(ex.Message);

                    throw ex;
                }
            });
        }

        public static void ChangeTarget()
        {
            FindAgressiveMobAroud();

            Logger.setLog("Change mob by click TAB");
            Action.ChangeTargetByTab();
        }

        public static void FindAgressiveMobAroud()
        {
            Logger.setLog("Find agressive mob who beat me");
            uint id = MobReader.IsExistMobAttackingMe(MobsAround);

            if (id != 0)
            {
                AgressiveMob = id;
                Logger.setLog("Found the mob who is attacking me now: " + id);
                Logger.setLog("Choose mob in target who attaking me");
                Writer.TargetMob(id);
                return;
            }
            else
            {
                Logger.setLog("Not Found agressive mob");
                AgressiveMob = 0;
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

            if (MobReader.GetMobDistance(mobId) > 3.5)
            {
                int i = 0;

                while (MobReader.GetMobDistance(mobId) > 4)
                {
                    if (TargetMobEntity.WID == 0)
                    {
                        break;
                    }

                    float beforeDist = MobReader.GetMobDistance(mobId);

                    if (beforeDist <= MobReader.GetMobDistance(mobId))
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
                    //Thread.Sleep(500);
                    //Action.EscapeClick();
                    //Logger.setLog("Clicked ESC");
                }

            }
            else
            {
                Logger.setLog("I already near the mob");
            }

        }
    }
}
