using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BotCH
{
    public class ThreadHelper
    {
        public static void StartBotingThreads()
        {
            Start(Pet.CheckStatusPetThread);
            Start(Bot.BotingThread);
        }

        public static void StopBotingThreads()
        {
            Stop(Bot.BotingThread);
            Stop(Pet.CheckStatusPetThread);
        }

        public static void StartPersInfoThreads()
        {
            Start(PersInfo.PersInfoLabelsThread);
        }

        public static void StopPersInfoThreads()
        {
            Stop(PersInfo.PersInfoLabelsThread);
        }


        private static void Start(Thread thread)
        {
            thread.IsBackground = true;

            if ((thread.ThreadState & (ThreadState.Stopped | ThreadState.Unstarted)) != 0)
            {
                thread.Start();
            }
            else
            {
                thread.Resume();
            }
        }
        private static void Stop(Thread thread)
        {
            if (thread.ThreadState != ThreadState.Aborted && thread.ThreadState != ThreadState.Unstarted)
            {
                thread.Suspend();
            }
        }
    }
}
