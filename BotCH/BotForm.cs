using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BotCH
{
    public partial class BotForm : Form
    {
        public static INIManager IniManager = new INIManager("config.ini");
        public BotForm()
        {
            InitializeComponent();
            CheckForIllegalCrossThreadCalls = false;
        }

        private void BotForm_Load(object sender, EventArgs e)
        {
            PersInfo.form = this;
            Reader.form = this;
            Bot.form = this;
            Logger.form = this;
            Pet.form = this;
            Action.form = this;
            this.cageSelect.SelectedIndex = 0;
            this.stopButton.Enabled = false;
            this.InitParamsFromIniConfig();
        }

        private void ConnectButton_Click(object sender, EventArgs e)
        {
            this.labelState.Text = IniManager.ReadINI("main", "test");
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
                PersInfo.ShowPersInfoLabels();
                this.checkBoxUnfrezze.Visible = true;

            }
            else
            {
                this.textBoxPID.BackColor = Color.Red;
            }
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

        private void InitParamsFromIniConfig()
        {
            this.textBoxHPusage.Text = IniManager.ReadINI("settings", "useHp");
            this.textBoxMPusage.Text = IniManager.ReadINI("settings", "useMp");
            this.textBoxHealPetUsage.Text = IniManager.ReadINI("settings", "useHealPet");
            this.checkBoxUseSkill.Checked = IniManager.ReadINI("settings", "checkBoxUseSkill") == "1";
            this.checkBoxUseSkill.Checked = IniManager.ReadINI("settings", "checkBoxUseSkill") == "1";
            this.checkBoxUseSword.Checked = IniManager.ReadINI("settings", "checkBoxUseSword") == "1";
            this.checkBoxLooting.Checked = IniManager.ReadINI("settings", "checkBoxLooting") == "1";
            this.checkBoxCheckId.Checked = IniManager.ReadINI("settings", "checkBoxCheckId") == "1";
            this.cageSelect.SelectedIndex = int.Parse(IniManager.ReadINI("settings", "selectCage")) - 1;
        }

        private void checkBoxUnfrezze_CheckedChanged(object sender, EventArgs e)
        {
            Writer.UnFreezeeWindow();
        }
    }


}
