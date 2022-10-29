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
            ToolTip t = new ToolTip();
            t.SetToolTip(this.buttonFindBaseAddr, "Open game, enter to Character and set your MAX HP to " + configName);
            PersInfo.form = this;
            Reader.form = this;
            Bot.form = this;
            Logger.form = this;
            Pet.form = this;
            Action.form = this;
            this.cageSelect.SelectedIndex = 1;
            this.stopButton.Enabled = false;
            this.InitParamsFromIniConfig();
        }

        private void ConnectButton_Click(object sender, EventArgs e)
        {
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
                    this.textBoxPID.Text = Reader.currentPID.ToString();
                    this.textBoxPID.Enabled = false;
                    PersInfo.ShowPersInfoLabels();
                    IniManager.Write("offset", "persHp", MyPersEntity.MaxHP.ToString());
                    this.checkBoxUnfrezze.Visible = true;
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
            Logger.setLog("Start boting");
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

        private void buttonFindBaseAddr_Click(object sender, EventArgs e)
        {
            Reader.SetPID(int.Parse(this.textBoxPID.Text));

            if (OffsetFinder.GetBaseAddress())
            {
                MessageBox.Show("Succsesfully founded and writed!");
            }
            else
            {
                MessageBox.Show("Not found, try yourself, and write it to " + configName);
            }
        }
    }


}
