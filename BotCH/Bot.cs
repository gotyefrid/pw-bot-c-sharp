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

                if (GameCall.CanPickupWithApproach)
                {
                    GoToKillPlace();
                    PickUpLootWithApproach(n);
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

        // Дальше этого от места смерти — сначала идём туда, м
        private const float KillPlaceNearDistance = 3f;
        // Сколько ждать, пока дойдём до места смерти, мс
        private const int KillPlaceWaitMs = 10000;

        // Перед лутом подходим туда, где умер моб: лут падает вокруг него (важно, если моба убил пет вдалеке)
        private static void GoToKillPlace()
        {
            float[] target = _killPos;
            bool dead = _killPosDead;
            _killPos = null;

            if (target == null || !dead || !GameCall.CanMove)
            {
                return;
            }

            float dist = DistanceToMe(target);

            if (dist <= KillPlaceNearDistance)
            {
                return;
            }

            Logger.setLog("Go to kill place, " + dist.ToString("0.0") + " m");

            if (!GameCall.MoveTo(target[0], target[1], target[2]))
            {
                return;
            }

            var start = DateTime.Now;

            while (DistanceToMe(target) > KillPlaceNearDistance && (DateTime.Now - start).TotalMilliseconds < KillPlaceWaitMs)
            {
                Thread.Sleep(200);
            }
        }

        private static float DistanceToMe(float[] p)
        {
            uint pers = PersReader.GetPersStruct();
            float dx = Reader.ReadFloat(pers + Offset.Get.PERS_LOC_X) - p[0];
            float dh = Reader.ReadFloat(pers + Offset.Get.PERS_LOC_Z) - p[1];
            float dy = Reader.ReadFloat(pers + Offset.Get.PERS_LOC_Y) - p[2];

            return (float)Math.Sqrt(dx * dx + dh * dh + dy * dy);
        }

        // Как далеко бот ходит за лутом, м
        private const float PickupMaxDistance = 20f;
        // Сколько ждать, пока персонаж дойдёт и поднимет предмет, мс
        private const int PickupWaitMs = 10000;
        // Пауза между подборами, мс: случайная, чтобы не выглядело как бот
        private const int PickupDelayMin = 700;
        private const int PickupDelayMax = 1300;
        private static readonly Random _random = new Random();

        // Подбор как кликом мышью: до n раз ближайший предмет в пределах PickupMaxDistance,
        // персонаж сам подходит к нему. Прямой пакет подбора (GameCall.Pickup) берёт с 10 м — человек так не может
        private static void PickUpLootWithApproach(int n)
        {
            int picked = 0;
            var skipped = new HashSet<uint>();

            for (int i = 0; i < n; i++)
            {
                if (!ItemReader.FindNearestItem(PickupMaxDistance, skipped, out uint id, out _))
                {
                    break;
                }

                if (!GameCall.PickupWithApproach(id))
                {
                    break;
                }

                // Ждём, пока предмет исчезнет с земли: иначе следующая команда собьёт подход к этому
                var start = DateTime.Now;

                while (ItemReader.IsOnGround(id) && (DateTime.Now - start).TotalMilliseconds < PickupWaitMs)
                {
                    Thread.Sleep(200);
                }

                if (ItemReader.IsOnGround(id))
                {
                    Logger.setLog("Pick up loot: item " + id.ToString("X") + " not picked in " + PickupWaitMs / 1000 + " s, skip it");
                    skipped.Add(id);
                    continue;
                }

                picked++;
                Thread.Sleep(_random.Next(PickupDelayMin, PickupDelayMax));
            }

            Logger.setLog("Pick up loot: " + picked + " item(s) (with approach)");
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

            // Место моба для подхода к луту: структуру ищем один раз, дальше читаем из неё только координаты
            _killPos = null;
            _killPosDead = false;
            uint mobStruct = GameCall.CanMove ? MobReader.GetMobStruct(mobId) : 0;

            try
            {
                KillMobLoop(mobId, mobStruct);
            }
            finally
            {
                // После смерти труп ещё ~5 с лежит в списке — читаем точное место смерти.
                // Цель у персонажа сбрасывается чуть раньше, чем у моба ставится флаг смерти, поэтому ждём его до 1 с
                for (int k = 0; k < 5; k++)
                {
                    RememberKillPos(mobStruct, mobId);

                    if (_killPosDead || _killPos == null)
                    {
                        break;
                    }

                    Thread.Sleep(200);
                }
            }
        }

        // Последние координаты моба, которого били, и умер ли он
        private static float[] _killPos;
        private static bool _killPosDead;

        private static void RememberKillPos(uint mobStruct, uint mobId)
        {
            if (MobReader.ReadMobPosition(mobStruct, mobId, out float[] pos, out bool dead))
            {
                _killPos = pos;
                _killPosDead = dead;
            }
        }

        private static void KillMobLoop(uint mobId, uint mobStruct)
        {
            int i = 0;
            var timeStart = DateTime.Now;

            while (TargetMobEntity.WID == mobId)
            {
                RememberKillPos(mobStruct, mobId);

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
