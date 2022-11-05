using BotCH.MemoryHelpers;
using System;
using System.Threading;

namespace BotCH
{
    internal class Pet
    {
        public static BotForm form;

        public static Thread CheckStatusPetThread = new Thread(CheckingStatusPet);

        public static bool IsInvited()
        {
            return PersReader.IsPetInvited();
        }
        public static bool IsAlive()
        {
            return PersReader.GetPetHpPercent() != 0;
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
            if (PersReader.GetPetFeedStatus() > 0)
            {
                return true;
            }

            return false;
        }

        public static void CheckingStatusPet()
        {
            while (true)
            {
                if (!Pet.IsInvited())
                {
                    Pet.Invite();
                    Thread.Sleep(1000);
                }
                else
                {
                    if (PersReader.GetPetHpPercent() < Convert.ToInt32(form.textBoxHealPetUsage.Text))
                    {
                        Action.HealPet();
                    }

                    if (Pet.IsNeedToBeFeed())
                    {
                        if (PersReader.IsFeedPetAvailable())
                        {
                            Action.FeedPet();
                        }
                    }
                }

                Thread.Sleep(1000);
            }
        }

    }
}
