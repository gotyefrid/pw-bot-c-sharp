using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using Memory;

namespace BotCH
{
    public partial class BotForm : Form
    {
        public BotForm()
        {
            InitializeComponent();
            CheckForIllegalCrossThreadCalls = false;
        }

        private void BotForm_Load(object sender, EventArgs e)
        {
            PersInfo.form = this;
        }

        private void ConnectButton_Click(object sender, EventArgs e)
        {
            if (textBoxPID.Text == String.Empty)
            {
                textBoxPID.Text = "0";
            }

            connectionPidLabel.Text = Reader.SetPID(int.Parse(textBoxPID.Text));

            if (Reader.statusConnection)
            {
                textBoxPID.BackColor = Color.LightGreen;
            }
            else
            {
                textBoxPID.BackColor = Color.Red;
            }
            PersInfo.ShowPersInfoLabels();
        }

        /// <summary>
        /// Запрет ввода не цифр.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TextBoxPID_KeyPress(object sender, KeyPressEventArgs e)
        {
            char number = e.KeyChar;

            if (!Char.IsDigit(number) && number != 8) // цифры и клавиша BackSpace
            {
                e.Handled = true;
            }
        }
        private void TextBoxPID_Click(object sender, EventArgs e)
        {
            textBoxPID.BackColor = Color.White;
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            Reader.statusConnection = false;
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            PersInfo.Test();
        }

        private void Button1_Click(object sender, EventArgs e)
        {
            VAMemory currentProcess = new VAMemory(Reader.processName);
            currentProcess.WriteUInt32((IntPtr)0x2415397C, 2148552024);


        }
    }


}
