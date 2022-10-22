using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace BotCH
{
    public partial class BotForm : Form
    {
        public static bool teee()
        {
            return true;
        }
        public BotForm()
        {
            InitializeComponent();
            CheckForIllegalCrossThreadCalls = false;
        }

        private void BotForm_Load(object sender, EventArgs e)
        {
            Resolver.RegisterDependencyResolver();
            PersInfo.form = this;
            Reader.form = this;
            Bot.form = this;
            Logger.form = this;
            Pet.form = this;
            this.cageSelect.SelectedIndex = 0;
            this.stopButton.Enabled = false;
        }

        private void ConnectButton_Click(object sender, EventArgs e)
        {
            if (this.textBoxPID.Text == String.Empty)
            {
                this.textBoxPID.Text = "0";
            }

            this.connectionPidLabel.Text = Reader.SetPID(int.Parse(this.textBoxPID.Text));

            if (Reader.statusConnection)
            {
                this.textBoxPID.BackColor = Color.LightGreen;
                this.textBoxPID.Text = Reader.currentPID.ToString();
                this.textBoxPID.Enabled = false;
            }
            else
            {
                this.textBoxPID.BackColor = Color.Red;
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
            this.textBoxPID.BackColor = Color.White;
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            Bot.active = false;
            this.startButton.Enabled = true;
            this.stopButton.Enabled = false;
            Action.EscapeClicks();
            this.labelState.Text = "Bot stopped";
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            if (Reader.currentPID == 0)
            {
                MessageBox.Show("Connect to client first");

                return;
            }


            this.startButton.Enabled = false;
            this.stopButton.Enabled = true;

            Bot.active = true;
            Pet.CheckingStatusPet();
            Bot.Run();
        }

        private void Button1_Click(object sender, EventArgs e)
        {
            VAMemory currentProcess = new VAMemory(Reader.processName);
            textBoxPID.Text = currentProcess.ReadUInt32((IntPtr)0x1235A304).ToString();


        }

        private void donationsToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }
    }


}
