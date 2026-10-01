using BotCH.HotKeys;
using BotCH.MemoryHelpers;
using System;
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
            //Offset.ServerName = Offset.COMEBACK136;
            Offset.ServerName = Offset.PWCLASSIC136;

            if (IsRenameWindowsEnabled())
            {
                Reader.RenameGameWindows();
            }

            PersInfo.form = this;
            Reader.form = this;
            Bot.form = this;
            Logger.form = this;
            Pet.form = this;
            Action.form = this;
            HotKeysService.form = this;
            this.cageSelect.SelectedIndex = 0;
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

                string connectionString = Reader.SetPID(int.Parse(this.textBoxPID.Text));

                if (Reader.statusConnection)
                {
                    this.connectionPidLabel.Text = connectionString;
                    this.textBoxPID.BackColor = Color.LightGreen;
                    this.textBoxPID.Text = Reader.process.Id.ToString();
                    this.textBoxPID.Enabled = false;
                    this.startButton.Enabled = true;
                    this.checkBoxUnfrezze.Visible = true;
                    ThreadHelper.StartPersInfoThreads();
                    RefreshSkillList();

                    // Повторно при подключении: игру могли запустить после бота или сменить персонажа
                    if (IsRenameWindowsEnabled())
                    {
                        Reader.RenameGameWindows();
                    }
                    Logger.setLog("----------------------");
                    Logger.setLog("Status connection TRUE");
                    HotKeysService.RegisterHotKeysToApp();
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

        public void StopButton_Click(object sender = null, EventArgs e = null)
        {
            ThreadHelper.StopBotingThreads();
            Writer.RestoreDefaultsInjections();
            this.startButton.Enabled = true;
            this.stopButton.Enabled = false;
            this.labelState.Text = "Bot stopped";
        }

        public void StartButton_Click(object sender = null, EventArgs e = null)
        {
            if (Reader.process == null || !Reader.statusConnection)
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
            this.textBoxComeCloserDist.Text = IniManager.ReadINI("settings", "comeCloserDist", "8");
            this.checkBoxUseSkill.Checked = IniManager.ReadINI("settings", "checkBoxUseSkill") == "1";
            this.checkBoxUseSkill.Checked = IniManager.ReadINI("settings", "checkBoxUseSkill") == "1";
            this.checkBoxUseSword.Checked = IniManager.ReadINI("settings", "checkBoxUseSword") == "1";
            this.checkBoxLooting.Checked = IniManager.ReadINI("settings", "checkBoxLooting") == "1";
            this.checkBoxCheckId.Checked = IniManager.ReadINI("settings", "checkBoxCheckId") == "1";
            this.cageSelect.SelectedIndex = int.Parse(IniManager.ReadINI("settings", "selectCage", "1")) - 1;
        }

        // Переименование окон игры в «Ник PID». Включено по умолчанию, выключить: [settings] renameWindows=0
        private static bool IsRenameWindowsEnabled()
        {
            return IniManager.ReadINI("settings", "renameWindows", "1") == "1";
        }

        // Пункт списка «Attack skill»: показывается название из файла игры, а если не прочиталось — ID
        private class SkillItem
        {
            public uint Id;
            public string Name;

            public override string ToString()
            {
                return Name ?? Id.ToString();
            }
        }

        private void checkBoxUseSkill_CheckedChanged(object sender, EventArgs e)
        {
            RefreshSkillList();
        }

        private void comboBoxSkill_DropDown(object sender, EventArgs e)
        {
            RefreshSkillList();
        }

        private void comboBoxSkill_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Во время пересборки списка выбор меняется сам — это не выбор пользователя
            if (_refreshingSkills)
            {
                return;
            }

            SetAttackSkill(comboBoxSkill.SelectedItem as SkillItem);
        }

        private bool _refreshingSkills;

        // Запоминаем выбранный скилл; в лог — только когда он действительно сменился
        private void SetAttackSkill(SkillItem item)
        {
            uint id = item == null ? 0 : item.Id;

            if (id != Action.AttackSkillId)
            {
                Logger.setLog(item == null
                    ? "Attack skill: none, Use Skill will press F2"
                    : "Attack skill: " + item + " (id " + id + ")");
            }

            Action.AttackSkillId = id;
        }

        // Список изученных скиллов без точно не атакующих. Виден, только если стоит Use Skill и есть прямые вызовы.
        // Выбор: прежний, если он ещё есть; иначе скилл по умолчанию; иначе первый в списке
        private void RefreshSkillList()
        {
            bool show = checkBoxUseSkill.Checked && Reader.statusConnection && Offset.Get != null && Offset.Get.SKILLS_OFFSET != 0 && GameCall.Enabled;
            labelTextSkill.Visible = show;
            comboBoxSkill.Visible = show;

            if (!show)
            {
                _refreshingSkills = true;
                comboBoxSkill.Items.Clear();
                _refreshingSkills = false;
                Action.AttackSkillId = 0;
                return;
            }

            uint selected = Action.AttackSkillId != 0 ? Action.AttackSkillId : Offset.Get.SKILL_DEFAULT_ATTACK;
            var notAttack = Offset.Get.SKILLS_NOT_ATTACK;

            _refreshingSkills = true;
            comboBoxSkill.BeginUpdate();
            comboBoxSkill.Items.Clear();

            try
            {
                foreach (uint id in SkillReader.GetLearnedSkillIds())
                {
                    if (Array.IndexOf(notAttack, id) < 0)
                    {
                        comboBoxSkill.Items.Add(new SkillItem { Id = id, Name = SkillNames.Get(id) });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.setLog("Read skills failed: " + ex.Message);
            }

            int index = 0;

            for (int i = 0; i < comboBoxSkill.Items.Count; i++)
            {
                if (((SkillItem)comboBoxSkill.Items[i]).Id == selected)
                {
                    index = i;
                    break;
                }
            }

            comboBoxSkill.EndUpdate();
            comboBoxSkill.SelectedIndex = comboBoxSkill.Items.Count > 0 ? index : -1;
            _refreshingSkills = false;
            SetAttackSkill(comboBoxSkill.SelectedItem as SkillItem);
        }

        private void checkBoxUnfrezze_CheckedChanged(object sender, EventArgs e)
        {
            Writer.UnFreezeeWindow();
        }

        private void BotForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            HotKeysService.UnregisterHotKeysFromApp();
            ThreadHelper.StopAll();

            if (Reader.statusConnection == true)
            {
                Writer.RestoreDefaultsInjections();
            }

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

        protected override void WndProc(ref Message m)
        {
            HotKeysService.HotKeysHandler(ref m);

            base.WndProc(ref m);
        }
    }
}
