using System.Threading;

namespace BotCH
{
    public class ThreadHelper
    {
        public static void StartBotingThreads()
        {
            Start(Pet.CheckStatusPetThread = new Thread(Pet.CheckingStatusPet));
            StartPersInfoThreads();
            Start(Bot.BotingThread = new Thread(Bot.Run));
        }

        public static void StopBotingThreads()
        {
            Stop(Bot.BotingThread);
            Stop(Pet.CheckStatusPetThread);
        }

        // Поток надписей и банок запускается при Connect; при Start второй не создаём — иначе банки пьются дважды
        public static void StartPersInfoThreads()
        {
            if (PersInfo.PersInfoLabelsThread != null && PersInfo.PersInfoLabelsThread.IsAlive)
            {
                return;
            }

            Start(PersInfo.PersInfoLabelsThread = new Thread(PersInfo.ShowPersInfoLabels));
        }

        public static void StopPersInfoThreads()
        {
            Stop(PersInfo.PersInfoLabelsThread);
        }

        private static void Start(Thread thread)
        {
            thread.IsBackground = true;

            if (thread != null && (thread.ThreadState & (ThreadState.Stopped | ThreadState.Unstarted)) != 0)
            {
                thread.Start();
            }
        }
        private static void Stop(Thread thread)
        {
            if (thread != null && thread.ThreadState != ThreadState.Aborted && thread.ThreadState != ThreadState.Unstarted)
            {
                thread.Abort();
            }
        }

        public static void StopAll()
        {
            try
            {
                Stop(PersInfo.PersInfoLabelsThread);
                Stop(Bot.BotingThread);
                Stop(Pet.CheckStatusPetThread);
            }
            catch { }
        }
    }
}
