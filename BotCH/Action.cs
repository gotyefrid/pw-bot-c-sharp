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
            if (GameCall.Unselect())
            {
                return;
            }

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

        // Атакующий скилл, выбранный в списке «Attack skill» на форме. 0 — не выбран (жмём F2)
        public static volatile uint AttackSkillId = 0;

        // Нажатый скилл ждёт подтверждения от игры — начала перезарядки. Пока его нет (подход, каст), повторно не жмём.
        // Ключ — ID скилла, значение — когда и по какой цели нажали
        private static readonly Dictionary<uint, KeyValuePair<DateTime, uint>> _skillPressed = new Dictionary<uint, KeyValuePair<DateTime, uint>>();
        // Сколько ждать подтверждения: подход к цели + каст
        private const int SkillConfirmTimeoutMs = 8000;

        private static bool CanPressSkill(uint skill, uint target)
        {
            lock (_skillPressed)
            {
                if (!SkillReader.IsReady(skill))
                {
                    // Перезарядка идёт — подтверждение получено (или скилл просто откатывается)
                    _skillPressed.Remove(skill);
                    return false;
                }

                if (_skillPressed.TryGetValue(skill, out var pressed)
                    && pressed.Value == target
                    && (DateTime.Now - pressed.Key).TotalMilliseconds < SkillConfirmTimeoutMs)
                {
                    return false;
                }

                return true;
            }
        }

        private static void SkillPressed(uint skill, uint target)
        {
            lock (_skillPressed)
            {
                _skillPressed[skill] = new KeyValuePair<DateTime, uint>(DateTime.Now, target);
            }
        }

        public static void AttackBySkill()
        {
            uint skill = AttackSkillId;

            if (skill != 0 && GameCall.CanCastSkill(skill))
            {
                uint target = TargetMobEntity.WID;

                if (target == 0)
                {
                    return;
                }

                WaitForCasting(Keys.F2);

                // На перезарядке или ещё не подтверждён прошлый нажим — пропускаем, бот попробует на следующем круге атаки.
                // Как нажатие кнопки: если моб дальше дальности скилла, персонаж сам подойдёт и применит
                if (!CanPressSkill(skill, target))
                {
                    return;
                }

                bool cast = GameCall.CanApplySkill ? GameCall.ApplySkill(skill) : GameCall.CastSkill(skill, target);

                if (cast)
                {
                    SkillPressed(skill, target);

                    if (Logger.KeyLogger)
                    {
                        Logger.setLog("Skill " + skill + " on " + target + " (direct call)");
                    }

                    Thread.Sleep(500);
                }

                return;
            }

            Action.ClickKey(Keys.F2);
        }

        public static void PickUpLoot()
        {
            Action.ClickKey(Keys.F4);
        }

        public static void PotHP()
        {
            if (UsePotion(true))
            {
                return;
            }

            Action.ClickKey(Keys.F6);
        }

        public static void PotMP()
        {
            if (UsePotion(false))
            {
                return;
            }

            Action.ClickKey(Keys.F3);
        }

        public static void FeedPet()
        {
            if (GameCall.CanUseItem)
            {
                // Пробуем корма от самого малого; если стопка не уменьшилась — пет этот корм не ест, берём следующий
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    if (!InventoryReader.FindPetFood(_foodPetDoesNotEat, out uint slot, out uint tid))
                    {
                        Logger.setLog("No pet food in inventory");
                        break;
                    }

                    uint countBefore = InventoryReader.GetCount(slot, tid);
                    WaitForCasting(Keys.F5);

                    if (!GameCall.UseItem(slot, tid))
                    {
                        break;
                    }

                    Thread.Sleep(700);

                    if (InventoryReader.GetCount(slot, tid) < countBefore)
                    {
                        Logger.setLog("Feed pet with " + tid + " from slot " + slot + " (direct call)");
                        return;
                    }

                    Logger.setLog("Pet does not eat food " + tid + ", try another");
                    _foodPetDoesNotEat.Add(tid);
                }
            }

            Action.ClickKey(Keys.F5);
        }

        // Корм, который пет не съел (стопка не уменьшилась). Сбрасывается при перезапуске бота
        private static readonly HashSet<uint> _foodPetDoesNotEat = new HashSet<uint>();

        // Самая слабая подходящая по уровню банка HP или MP из сумки прямым вызовом. false — не получилось, нужен запасной путь
        // До какого момента действует выпитая банка HP / MP: раньше пить банку того же вида не нужно
        private static DateTime _hpPotionActiveUntil = DateTime.MinValue;
        private static DateTime _mpPotionActiveUntil = DateTime.MinValue;

        // Самая слабая подходящая по уровню банка HP или MP из сумки прямым вызовом. false — не получилось, нужен запасной путь
        private static bool UsePotion(bool hp)
        {
            if (!GameCall.CanUseItem)
            {
                return false;
            }

            // Прошлая банка ещё действует — пить не нужно, и клавишу жать тоже не нужно
            if (DateTime.Now < (hp ? _hpPotionActiveUntil : _mpPotionActiveUntil))
            {
                return true;
            }

            if (!InventoryReader.FindPotion(hp, out uint slot, out uint tid, out uint durationSec))
            {
                Logger.setLog("No " + (hp ? "HP" : "MP") + " potions for my level in inventory");
                return false;
            }

            WaitForCasting(hp ? Keys.F6 : Keys.F3);

            uint countBefore = InventoryReader.GetCount(slot, tid);

            if (!GameCall.UseItem(slot, tid))
            {
                return false;
            }

            Thread.Sleep(500);

            // Стопка не уменьшилась — игра не дала выпить (перезарядка); попробуем на следующем круге
            if (InventoryReader.GetCount(slot, tid) >= countBefore)
            {
                return true;
            }

            var activeUntil = DateTime.Now.AddSeconds(durationSec > 0 ? durationSec : 10);

            if (hp)
            {
                _hpPotionActiveUntil = activeUntil;
            }
            else
            {
                _mpPotionActiveUntil = activeUntil;
            }

            Logger.setLog("Use " + (hp ? "HP" : "MP") + " potion " + tid + " from slot " + slot + " for " + durationSec + " s (direct call)");

            return true;
        }

        public static void HealPet(bool waitCasting = true)
        {
            uint skill = Offset.Get.SKILL_HEAL_PET;
            uint pet = PersReader.GetCurrentPetId();

            if (pet != 0 && GameCall.CanCastSkill(skill))
            {
                if (waitCasting)
                {
                    WaitForCasting(Keys.F7);
                }

                if (!CanPressSkill(skill, pet))
                {
                    return;
                }

                // Как нажатие кнопки, целью — пет: если он дальше дальности лечения, персонаж подойдёт.
                // Прямая команда (запасной путь) сервер принимает только с петом в качестве цели и без подхода
                if (GameCall.CanApplySkill ? GameCall.ApplySkill(skill, pet) : GameCall.CastSkill(skill, pet))
                {
                    SkillPressed(skill, pet);
                    Logger.setLog("Heal pet (direct call)");
                    Thread.Sleep(500);
                    return;
                }
            }

            Action.ClickKey(Keys.F7, waitCasting);
        }

        public static void BringPetToLife()
        {
            uint skill = Offset.Get.SKILL_REVIVE_PET;

            if (GameCall.CanCastSkill(skill))
            {
                WaitForCasting(Keys.F8);

                // Воскрешение — без цели. Каст долгий (~12 с): ждём его конца, чтобы не сбить повторной командой
                if (SkillReader.IsReady(skill) && GameCall.CastSkill(skill, 0))
                {
                    Logger.setLog("Revive pet (direct call)");
                    Thread.Sleep(1000);

                    for (int i = 0; i < 20 && PersReader.GetFlagUseSkill(); i++)
                    {
                        Thread.Sleep(1000);
                    }
                }

                return;
            }

            Action.ClickKey(Keys.F8);
        }

        public static void InvitePet()
        {
            if (GameCall.CanSummonPet)
            {
                int cage = Convert.ToInt32(form.cageSelect.Text);
                WaitForCasting(Keys.D9);

                if (GameCall.SummonPet(cage))
                {
                    Logger.setLog("Summon pet from cage " + cage + " (direct call)");

                    // Призыв идёт ~3.5 с: ждём, чтобы не отправить команду повторно и не сбить его
                    for (int i = 0; i < 12 && !PersReader.IsPetInvited(); i++)
                    {
                        Thread.Sleep(500);
                    }

                    return;
                }
            }

            Action.ClickKey(Keys.D9);
        }
    }
}
