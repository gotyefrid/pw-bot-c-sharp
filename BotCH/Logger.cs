using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotCH
{
    public class Logger
    {
        public static BotForm form;

        public static void setLog(string text)
        {
            form.labelState.Text = text;
        }
    }
}
