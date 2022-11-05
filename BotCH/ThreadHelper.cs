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
        public static void Start(Thread thread)
        {
            if (thread.ThreadState == ThreadState.Suspended)
            {
                thread.Resume();
            }
            else
            {
                thread.Start();
            }
        }
        public static void Stop(Thread thread)
        {
            if (thread.ThreadState == ThreadState.Running)
            {
                thread.Suspend();
            }
        }
    }
}
