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
        public static int KillMobTimerSec = 120;
        public static Thread BotingThread;
        private static uint AgressiveMob = 0;
        private static Dictionary<uint, string> MobsAround;
        private static string[] AllowMobsIds;
        private static int _currentIndexMobIdFromList;

        public static void Run()
        {
            AllowMobsIds = BotForm.IniManager.ReadINI("bot", "mobIDs").Split(',');

            if (form.checkFindAgrMob.Checked)
            {
                Logger.setLog("Making list of alive mobs");
                MobsAround = MobReader.GetActualListMobsOffsetsInArray();
                Logger.setLog("Around us " + MobsAround.Count() + " mobs");
            }

            try
            {
                while (true)
                {
                    Logger.setLog("New Loop");

                    if (PersReader.IsExistTarget())
                    {
                        if (MobsAround == null || !MobsAround.ContainsKey(TargetMobEntity.WID))
                        {
                            Logger.setLog("Add new mod in ID lsit");
                            AddMobToList(TargetMobEntity.WID, MobReader.GetMobArrayHexNumber(TargetMobEntity.WID));
                        }

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
                            //if (MobReader.GetMobName(TargetMobEntity.WID, MobsAround) == "Кровожадная росянка")
                            //{
                            KillMobActions(TargetMobEntity.WID);
                            GetLoot();
                            //}
                        }
                    }

                    ChangeTarget();
                }
            }
            catch (Exception ex)
            {
                if (ex.Message != "Поток находился в процессе прерывания.")
                {
                    Logger.setLog(ex.Message);
                    Logger.setLog(ex.StackTrace);
                }
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


            if (TargetMobEntity.WID != 0)
            {
                if (MobReader.IsMobAttakingMeNow(TargetMobEntity.WID))
                {
                    AgressiveMob = TargetMobEntity.WID;

                    return;
                }
            }

            if (GameCall.Enabled)
            {
                // Сначала тот, кто бьёт нас или пета (белый список для него не важен), потом ближайший
                uint aggressor = MobReader.FindMobAttackingUs();

                if (aggressor != 0)
                {
                    Logger.setLog("Mob " + aggressor + " is attacking me or my pet");
                    AgressiveMob = aggressor;
                    Action.SelectMob(aggressor);
                    return;
                }

                Action.SelectNearestMob(form.checkBoxCheckId.Checked ? AllowMobsIds : null);
                return;
            }

            Logger.setLog("Change mob by click TAB");

            if (form.checkBoxCheckId.Checked == false)
            {
                Action.ChangeTargetByTab();
                return;
            }

            if (form.useInjectToTargetCheckBox.Checked)
            {
                // Этот блок кода должен работать только если заполнен адрес фукнции SET_TARGET_FUNC_OFFSET а так же ORIG_BYTES_FUNC_OFFSE                               
                // Я не вспомнил как это работает после долгой паузы, и как найти этот адрес.
                while (true)
                {
                    string id = GetNextMobIdFromList();

                    Logger.setLog(AllowMobsIds.Length.ToString());
                    Logger.setLog("Inject " + id);
                    Writer.ChangeGetTargetAssembly(uint.Parse(id));
                    Action.ChangeTargetByTab();
                    Thread.Sleep(500);

                    if (TargetMobEntity.WID == 0)
                    {
                        Logger.setLog("Inject mob not found, try another ID");
                        Writer.ChangeGetTargetAssembly(0);
                    }
                    else
                    {
                        Logger.setLog("Mob targeted!");
                        Writer.ChangeGetTargetAssembly(0);
                        return;
                    }
                }
            } else
            {
                Action.ChangeTargetByTab();
            }
        }

        public static string GetNextMobIdFromList()
        {
            string result = AllowMobsIds[_currentIndexMobIdFromList];
            _currentIndexMobIdFromList++;

            if (_currentIndexMobIdFromList >= AllowMobsIds.Length)
            {
                _currentIndexMobIdFromList = 0;
            }

            return result;
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
                int n = int.Parse(form.textBoxLootingClicks.Text);

                if (GameCall.CanPickup)
                {
                    PickUpLootDirect(n);
                    return;
                }

                Logger.setLog("Pick up loot " + n + " times");

                for (int i = 0; i < n; i++)
                {
                    Action.PickUpLoot();
                    Thread.Sleep(600);
                }
            }
        }

        // Радиус подбора на сервере ~10 м (проверено), берём с запасом на задержку координат
        private const float PickupRadius = 9.5f;
        // Пауза между подборами, мс: случайная, чтобы не выглядело как бот
        private const int PickupDelayMin = 700;
        private const int PickupDelayMax = 1300;
        private static readonly Random _random = new Random();

        // Подбор прямым вызовом: до n раз ближайший предмет в радиусе, без подхода к дальним
        private static void PickUpLootDirect(int n)
        {
            int picked = 0;

            for (int i = 0; i < n; i++)
            {
                if (!ItemReader.FindNearestItem(PickupRadius, out uint id, out uint tid))
                {
                    break;
                }

                if (!GameCall.Pickup(id, tid))
                {
                    break;
                }

                picked++;
                Thread.Sleep(_random.Next(PickupDelayMin, PickupDelayMax));
            }

            Logger.setLog("Pick up loot: " + picked + " item(s) (direct call)");
        }

        private static bool SearchCurrentMobIdInList()
        {
            string[] mobsIds = AllowMobsIds;

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
            if (!form.checkBoxKillMobs.Checked)
            {
                Logger.setLog("Killing mob disable");
                return;
            }

            Logger.setLog("Killing mob");
            int i = 0;
            var timeStart = DateTime.Now;

            while (TargetMobEntity.WID == mobId)
            {
                if (TargetMobEntity.WID == 0)
                {
                    return;
                }

                if ((DateTime.Now - timeStart).TotalSeconds > KillMobTimerSec)
                {
                    Logger.setLog("Change trarget. Time-out 120 sec. to killing mob passed");
                    return;
                }

                if (!form.checkBoxUseSword.Checked && form.checkBoxComeCloser.Checked)
                {
                    Action.AttackByPet();
                    if (!int.TryParse(form.textBoxComeCloserDist.Text, out int comeCloserDist))
                    {
                        comeCloserDist = 8;
                    }

                    ComeCloser(TargetMobEntity.WID, comeCloserDist);
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

        public static void AddMobToList(uint wid, string arrayNumber)
        {
            if (MobsAround != null)
            {
                MobsAround.Add(wid, arrayNumber);
            }
            else
            {
                MobsAround = new Dictionary<uint, string> { { wid, arrayNumber } };
            }
        }

        private static void ComeCloser(uint mobId, float dist)
        {
            if (MobReader.GetMobDistance(mobId, MobsAround) > dist)
            {
                Logger.setLog("Distance to mob > " + dist);
                Logger.setLog("Come closer to mob");
                int i = 0;

                float beforeDist;

                while ((beforeDist = MobReader.GetMobDistance(mobId, MobsAround)) > dist)
                {
                    if (TargetMobEntity.WID == 0)
                    {
                        break;
                    }

                    Thread.Sleep(300);

                    // не приблизились за паузу — повторяем команду атаки, чтобы чар шёл к мобу
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
