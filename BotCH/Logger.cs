using System;
using System.IO;
using System.Windows.Forms;

namespace BotCH
{
    public class Logger
    {
        public static BotForm form;
        public static StreamWriter log = new StreamWriter("log.txt", true);
        public static bool KeyLogger = false;

        public static void setLog(string text)
        {
            form.labelState.Text = text;

            InsertToLogFile(text);
            form.richTextBoxLogBox.AppendText("\r\n" + text);
            form.richTextBoxLogBox.ScrollToCaret();
        }


        private static void InsertToLogFile(string text)
        {
            string str = DateTime.Now.ToString("G");
            log.WriteLine(str + ": " + text);
        }
    }
}
