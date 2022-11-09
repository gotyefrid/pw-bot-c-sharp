using BotCH.Entity;
using BotCH.MemoryHelpers;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace BotCH
{
    public partial class BotForm : Form
    {
        public const string configName = "config.ini";
        public static INIManager IniManager = new INIManager(configName);
        public BotForm()
        {
            InitializeComponent();
            CheckForIllegalCrossThreadCalls = false;
        }

        private void BotForm_Load(object sender, EventArgs e)
        {
            Offset.ServerName = Offset.COMEBACK136;

            if (IniManager.ReadINI("settings", "renameWindows") == "1")
            {
                Reader.RenameGameWindows();
            }

            Auth.form = this;
            PersInfo.form = this;
            Reader.form = this;
            Bot.form = this;
            Logger.form = this;
            Pet.form = this;
            Action.form = this;
            this.cageSelect.SelectedIndex = 1;
            this.InitParamsFromIniConfig();
        }

        private void ConnectButton_Click(object sender, EventArgs e)
        {
            if (!Auth.CheckLicenseByHttp())
            {
                string str = "You shoud to buy license, contact me Telegram: @white_mel \r\nClick OK to open Telegram-link.";
                ;
                if (MessageBox.Show(str, "License error", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                {
                    Process.Start("https://t.me/white_mel");
                }

                return;
            }

            try
            {
                if (this.textBoxPID.Text == String.Empty)
                {
                    this.textBoxPID.Text = "0";
                }

                this.connectionPidLabel.Text = Reader.SetPID(int.Parse(this.textBoxPID.Text));

                if (Reader.statusConnection)
                {
                    this.textBoxPID.BackColor = Color.LightGreen;
                    this.textBoxPID.Text = Reader.process.Id.ToString();
                    this.textBoxPID.Enabled = false;
                    this.checkBoxUnfrezze.Visible = true;
                    ThreadHelper.StartPersInfoThreads();
                    Logger.setLog("----------------------");
                    Logger.setLog("Status connection TRUE");
                }
                else
                {
                    Logger.setLog("Status connection FALSE");
                    this.textBoxPID.BackColor = Color.Red;
                }
            }
            catch (Exception mainError)
            {
                MessageBox.Show(mainError.Message);
            }
        }
        private void TextBoxPID_Click(object sender, EventArgs e)
        {
            this.textBoxPID.BackColor = Color.White;
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            ThreadHelper.StopBotingThreads();
            this.startButton.Enabled = true;
            this.stopButton.Enabled = false;
            this.labelState.Text = "Bot stopped";
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            if (Reader.process == null)
            {
                MessageBox.Show("Connect to client first");

                return;
            }

            this.startButton.Enabled = false;
            this.stopButton.Enabled = true;

            ThreadHelper.StartBotingThreads();
            Logger.setLog("Start boting");
        }

        private void InitParamsFromIniConfig()
        {
            this.textBoxHPusage.Text = IniManager.ReadINI("settings", "useHp", "80");
            this.textBoxMPusage.Text = IniManager.ReadINI("settings", "useMp", "100");
            this.textBoxHealPetUsage.Text = IniManager.ReadINI("settings", "useHealPet", "70");
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

        private void BotForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            ThreadHelper.StopAll();
            Logger.InsertListToLogFile(Logger.logCache);
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
    }


}
