using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BotCH
{
    public class Logger
    {
        public static BotForm form;
        public static List<string> logCache = new List<string>();
        public static bool KeyLogger = true;
        private const int MaxLogBoxLines = 1000;

        public static void setLog(string text)
        {
            // Форма ещё не создана (например, переименование окон при запуске) — писать некуда
            if (form == null)
            {
                return;
            }

            form.labelState.Text = text;

            if (form.checkBoxEnableLog.Checked)
            {
                form.richTextBoxLogBox.AppendText("\r\n" + text);

                // Лог включён по умолчанию — держим в окне только последние строки, иначе за часы работы он съест память
                // Номер последней строки считается быстро, без копирования всего текста
                if (form.richTextBoxLogBox.GetLineFromCharIndex(form.richTextBoxLogBox.TextLength) > MaxLogBoxLines)
                {
                    form.richTextBoxLogBox.Lines = form.richTextBoxLogBox.Lines.Skip(form.richTextBoxLogBox.Lines.Length - MaxLogBoxLines / 2).ToArray();
                    form.richTextBoxLogBox.SelectionStart = form.richTextBoxLogBox.TextLength;
                }

                form.richTextBoxLogBox.ScrollToCaret();

                AddToLogCache(text);

                if (logCache.Count > 100)
                {
                    InsertListToLogFile(logCache);
                    logCache.Clear();
                }
            }
        }

        public static void AddToLogCache(string text)
        {
            string str = DateTime.Now.ToString("G");
            logCache.Add(str + ": " + text);
        }

        public static void InsertListToLogFile(IList<string> list)
        {
            StreamWriter logFile = new StreamWriter("logFile.txt", true);

            foreach (string item in list)
            {
                logFile.WriteLine(item);
            }

            logFile.Close();
        }
    }
}
