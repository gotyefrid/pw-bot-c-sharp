using System;
using System.Threading;
using System.Threading.Tasks;

namespace BotCH
{
    internal class Pet
    {
        public static BotForm form;

        public static bool IsInvited()
        {
            return Reader.IsPetInvited();
        }
        public static bool IsAlive()
        {
            return Reader.GetPetHpPercent() != 0;
        }

        public static void Invite()
        {
            if (IsAlive())
            {
                Action.InvitePet();
            }
            else
            {
                Action.BringPetToLife();
            }
        }

        public static bool IsNeedToBeFeed()
        {
            if (Reader.GetPetFeedStatus() > 0)
            {
                return true;
            }

            return false;
        }

        public async static void CheckingStatusPet()
        {
            await Task.Run(() =>
            {
                while (Bot.active)
                {
                    if (!Pet.IsInvited())
                    {
                        Pet.Invite();
                        Thread.Sleep(3000);
                    }
                    else
                    {
                        if (Reader.GetPetHpPercent() < Convert.ToInt32(form.textBoxHealPetUsage.Text))
                        {
                            Action.HealPet();
                        }

                        if (Pet.IsNeedToBeFeed())
                        {
                            if (Reader.IsFeedPetAvailable())
                            {
                                Action.FeedPet();
                            }
                        }
                    }

                    Thread.Sleep(1000);
                }
            });
        }

    }
}
