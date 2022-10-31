using System;
using System.Collections.Generic;
using System.IO;

namespace BotCH
{
    public class Logger
    {
        public static BotForm form;
        public static List<string> logCache = new List<string>();
        public static bool KeyLogger = false;

        public static void setLog(string text)
        {
            form.labelState.Text = text;

            if (form.checkBoxEnableLog.Checked)
            {
                form.richTextBoxLogBox.AppendText("\r\n" + text);
                form.richTextBoxLogBox.ScrollToCaret();

                logCache.Add(text);

                if (logCache.Count > 100)
                {
                    InsertToLogFile(text);
                    logCache.Clear();
                }
            }
        }

        private static void InsertToLogFile(string text)
        {
            StreamWriter logFile = new StreamWriter("logFile.txt", true);
            string str = DateTime.Now.ToString("G");
            logFile.WriteLine(str + ": " + text);
        }
    }
}
