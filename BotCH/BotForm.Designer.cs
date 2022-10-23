namespace BotCH
{
    partial class BotForm
    {
        /// <summary>
        /// Обязательная переменная конструктора.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Освободить все используемые ресурсы.
        /// </summary>
        /// <param name="disposing">истинно, если управляемый ресурс должен быть удален; иначе ложно.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Код, автоматически созданный конструктором форм Windows

        /// <summary>
        /// Требуемый метод для поддержки конструктора — не изменяйте 
        /// содержимое этого метода с помощью редактора кода.
        /// </summary>
        private void InitializeComponent()
        {
            this.startButton = new System.Windows.Forms.Button();
            this.labelHpPers = new System.Windows.Forms.Label();
            this.textBoxPID = new System.Windows.Forms.TextBox();
            this.connectButton = new System.Windows.Forms.Button();
            this.connectionPidLabel = new System.Windows.Forms.Label();
            this.labelTargetId = new System.Windows.Forms.Label();
            this.stopButton = new System.Windows.Forms.Button();
            this.labelMpPers = new System.Windows.Forms.Label();
            this.labelHpPet = new System.Windows.Forms.Label();
            this.cageSelect = new System.Windows.Forms.ComboBox();
            this.targetIdTextBox = new System.Windows.Forms.TextBox();
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.aboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.donationsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabMain = new System.Windows.Forms.TabPage();
            this.checkBoxUnfrezze = new System.Windows.Forms.CheckBox();
            this.labelState = new System.Windows.Forms.Label();
            this.labelTextPetHP = new System.Windows.Forms.Label();
            this.labelTextMP = new System.Windows.Forms.Label();
            this.labelTextHP = new System.Windows.Forms.Label();
            this.tabSettings = new System.Windows.Forms.TabPage();
            this.textBoxHealPetUsage = new System.Windows.Forms.TextBox();
            this.labelTextHealPet = new System.Windows.Forms.Label();
            this.checkBoxClickerMode = new System.Windows.Forms.CheckBox();
            this.checkBoxKillMobs = new System.Windows.Forms.CheckBox();
            this.checkBoxCheckId = new System.Windows.Forms.CheckBox();
            this.checkBoxLooting = new System.Windows.Forms.CheckBox();
            this.checkBoxUseSword = new System.Windows.Forms.CheckBox();
            this.checkBoxUseSkill = new System.Windows.Forms.CheckBox();
            this.labelTextCage = new System.Windows.Forms.Label();
            this.textBoxMPusage = new System.Windows.Forms.TextBox();
            this.labelTextUseMP = new System.Windows.Forms.Label();
            this.textBoxHPusage = new System.Windows.Forms.TextBox();
            this.labelTextUseHP = new System.Windows.Forms.Label();
            this.tabKeys = new System.Windows.Forms.TabPage();
            this.checkBoxTargetByTab = new System.Windows.Forms.CheckBox();
            this.menuStrip1.SuspendLayout();
            this.tabControl1.SuspendLayout();
            this.tabMain.SuspendLayout();
            this.tabSettings.SuspendLayout();
            this.SuspendLayout();
            // 
            // startButton
            // 
            this.startButton.Location = new System.Drawing.Point(7, 141);
            this.startButton.Margin = new System.Windows.Forms.Padding(4);
            this.startButton.Name = "startButton";
            this.startButton.Size = new System.Drawing.Size(100, 28);
            this.startButton.TabIndex = 0;
            this.startButton.Text = "Start";
            this.startButton.UseVisualStyleBackColor = true;
            this.startButton.Click += new System.EventHandler(this.StartButton_Click);
            // 
            // labelHpPers
            // 
            this.labelHpPers.AutoSize = true;
            this.labelHpPers.Location = new System.Drawing.Point(49, 58);
            this.labelHpPers.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelHpPers.Name = "labelHpPers";
            this.labelHpPers.Size = new System.Drawing.Size(57, 16);
            this.labelHpPers.TabIndex = 1;
            this.labelHpPers.Text = "HP Pers";
            // 
            // textBoxPID
            // 
            this.textBoxPID.Location = new System.Drawing.Point(156, 7);
            this.textBoxPID.Margin = new System.Windows.Forms.Padding(4);
            this.textBoxPID.Name = "textBoxPID";
            this.textBoxPID.Size = new System.Drawing.Size(132, 22);
            this.textBoxPID.TabIndex = 2;
            this.textBoxPID.Text = "0";
            this.textBoxPID.Click += new System.EventHandler(this.TextBoxPID_Click);
            this.textBoxPID.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.TextBoxPID_KeyPress);
            // 
            // connectButton
            // 
            this.connectButton.Location = new System.Drawing.Point(4, 4);
            this.connectButton.Margin = new System.Windows.Forms.Padding(4);
            this.connectButton.Name = "connectButton";
            this.connectButton.Size = new System.Drawing.Size(144, 28);
            this.connectButton.TabIndex = 3;
            this.connectButton.Text = "Connect to PID";
            this.connectButton.UseVisualStyleBackColor = true;
            this.connectButton.Click += new System.EventHandler(this.ConnectButton_Click);
            // 
            // connectionPidLabel
            // 
            this.connectionPidLabel.AutoSize = true;
            this.connectionPidLabel.Location = new System.Drawing.Point(7, 36);
            this.connectionPidLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.connectionPidLabel.Name = "connectionPidLabel";
            this.connectionPidLabel.Size = new System.Drawing.Size(94, 16);
            this.connectionPidLabel.TabIndex = 4;
            this.connectionPidLabel.Text = "Not connected";
            // 
            // labelTargetId
            // 
            this.labelTargetId.AutoSize = true;
            this.labelTargetId.Location = new System.Drawing.Point(16, 106);
            this.labelTargetId.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelTargetId.Name = "labelTargetId";
            this.labelTargetId.Size = new System.Drawing.Size(63, 16);
            this.labelTargetId.TabIndex = 5;
            this.labelTargetId.Text = "Target ID";
            // 
            // stopButton
            // 
            this.stopButton.Location = new System.Drawing.Point(115, 141);
            this.stopButton.Margin = new System.Windows.Forms.Padding(4);
            this.stopButton.Name = "stopButton";
            this.stopButton.Size = new System.Drawing.Size(100, 28);
            this.stopButton.TabIndex = 6;
            this.stopButton.Text = "Stop";
            this.stopButton.UseVisualStyleBackColor = true;
            this.stopButton.Click += new System.EventHandler(this.StopButton_Click);
            // 
            // labelMpPers
            // 
            this.labelMpPers.AutoSize = true;
            this.labelMpPers.Location = new System.Drawing.Point(49, 74);
            this.labelMpPers.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelMpPers.Name = "labelMpPers";
            this.labelMpPers.Size = new System.Drawing.Size(58, 16);
            this.labelMpPers.TabIndex = 8;
            this.labelMpPers.Text = "MP Pers";
            // 
            // labelHpPet
            // 
            this.labelHpPet.AutoSize = true;
            this.labelHpPet.Location = new System.Drawing.Point(210, 58);
            this.labelHpPet.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelHpPet.Name = "labelHpPet";
            this.labelHpPet.Size = new System.Drawing.Size(49, 16);
            this.labelHpPet.TabIndex = 9;
            this.labelHpPet.Text = "HP Pet";
            // 
            // cageSelect
            // 
            this.cageSelect.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cageSelect.FormattingEnabled = true;
            this.cageSelect.Items.AddRange(new object[] {
            "1",
            "2",
            "3",
            "4",
            "5"});
            this.cageSelect.Location = new System.Drawing.Point(267, 185);
            this.cageSelect.Name = "cageSelect";
            this.cageSelect.Size = new System.Drawing.Size(79, 24);
            this.cageSelect.TabIndex = 10;
            // 
            // targetIdTextBox
            // 
            this.targetIdTextBox.Location = new System.Drawing.Point(86, 103);
            this.targetIdTextBox.Name = "targetIdTextBox";
            this.targetIdTextBox.ReadOnly = true;
            this.targetIdTextBox.Size = new System.Drawing.Size(100, 22);
            this.targetIdTextBox.TabIndex = 11;
            // 
            // menuStrip1
            // 
            this.menuStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.aboutToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(377, 28);
            this.menuStrip1.TabIndex = 12;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // aboutToolStripMenuItem
            // 
            this.aboutToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.donationsToolStripMenuItem});
            this.aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            this.aboutToolStripMenuItem.Size = new System.Drawing.Size(64, 24);
            this.aboutToolStripMenuItem.Text = "About";
            // 
            // donationsToolStripMenuItem
            // 
            this.donationsToolStripMenuItem.Name = "donationsToolStripMenuItem";
            this.donationsToolStripMenuItem.Size = new System.Drawing.Size(160, 26);
            this.donationsToolStripMenuItem.Text = "Donations";
            // 
            // tabControl1
            // 
            this.tabControl1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tabControl1.Controls.Add(this.tabMain);
            this.tabControl1.Controls.Add(this.tabSettings);
            this.tabControl1.Controls.Add(this.tabKeys);
            this.tabControl1.Location = new System.Drawing.Point(0, 33);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(377, 459);
            this.tabControl1.TabIndex = 13;
            // 
            // tabMain
            // 
            this.tabMain.Controls.Add(this.checkBoxUnfrezze);
            this.tabMain.Controls.Add(this.labelState);
            this.tabMain.Controls.Add(this.labelTextPetHP);
            this.tabMain.Controls.Add(this.labelTextMP);
            this.tabMain.Controls.Add(this.labelTextHP);
            this.tabMain.Controls.Add(this.connectButton);
            this.tabMain.Controls.Add(this.targetIdTextBox);
            this.tabMain.Controls.Add(this.textBoxPID);
            this.tabMain.Controls.Add(this.startButton);
            this.tabMain.Controls.Add(this.stopButton);
            this.tabMain.Controls.Add(this.connectionPidLabel);
            this.tabMain.Controls.Add(this.labelHpPet);
            this.tabMain.Controls.Add(this.labelHpPers);
            this.tabMain.Controls.Add(this.labelTargetId);
            this.tabMain.Controls.Add(this.labelMpPers);
            this.tabMain.Location = new System.Drawing.Point(4, 25);
            this.tabMain.Name = "tabMain";
            this.tabMain.Padding = new System.Windows.Forms.Padding(3);
            this.tabMain.Size = new System.Drawing.Size(369, 430);
            this.tabMain.TabIndex = 0;
            this.tabMain.Text = "Main";
            this.tabMain.UseVisualStyleBackColor = true;
            // 
            // checkBoxUnfrezze
            // 
            this.checkBoxUnfrezze.AutoSize = true;
            this.checkBoxUnfrezze.Font = new System.Drawing.Font("Microsoft Sans Serif", 5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.checkBoxUnfrezze.Location = new System.Drawing.Point(295, 9);
            this.checkBoxUnfrezze.Name = "checkBoxUnfrezze";
            this.checkBoxUnfrezze.Size = new System.Drawing.Size(68, 17);
            this.checkBoxUnfrezze.TabIndex = 16;
            this.checkBoxUnfrezze.Text = "UnFreeze";
            this.checkBoxUnfrezze.UseVisualStyleBackColor = true;
            this.checkBoxUnfrezze.Visible = false;
            this.checkBoxUnfrezze.CheckedChanged += new System.EventHandler(this.checkBoxUnfrezze_CheckedChanged);
            // 
            // labelState
            // 
            this.labelState.AutoSize = true;
            this.labelState.Location = new System.Drawing.Point(7, 411);
            this.labelState.Name = "labelState";
            this.labelState.Size = new System.Drawing.Size(38, 16);
            this.labelState.TabIndex = 15;
            this.labelState.Text = "State";
            // 
            // labelTextPetHP
            // 
            this.labelTextPetHP.AutoSize = true;
            this.labelTextPetHP.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.labelTextPetHP.Location = new System.Drawing.Point(153, 58);
            this.labelTextPetHP.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelTextPetHP.Name = "labelTextPetHP";
            this.labelTextPetHP.Size = new System.Drawing.Size(49, 16);
            this.labelTextPetHP.TabIndex = 14;
            this.labelTextPetHP.Text = "HP Pet";
            // 
            // labelTextMP
            // 
            this.labelTextMP.AutoSize = true;
            this.labelTextMP.ForeColor = System.Drawing.Color.Blue;
            this.labelTextMP.Location = new System.Drawing.Point(16, 74);
            this.labelTextMP.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelTextMP.Name = "labelTextMP";
            this.labelTextMP.Size = new System.Drawing.Size(27, 16);
            this.labelTextMP.TabIndex = 13;
            this.labelTextMP.Text = "MP";
            // 
            // labelTextHP
            // 
            this.labelTextHP.AutoSize = true;
            this.labelTextHP.BackColor = System.Drawing.Color.Transparent;
            this.labelTextHP.ForeColor = System.Drawing.Color.Red;
            this.labelTextHP.Location = new System.Drawing.Point(16, 58);
            this.labelTextHP.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelTextHP.Name = "labelTextHP";
            this.labelTextHP.Size = new System.Drawing.Size(26, 16);
            this.labelTextHP.TabIndex = 12;
            this.labelTextHP.Text = "HP";
            // 
            // tabSettings
            // 
            this.tabSettings.Controls.Add(this.checkBoxTargetByTab);
            this.tabSettings.Controls.Add(this.textBoxHealPetUsage);
            this.tabSettings.Controls.Add(this.labelTextHealPet);
            this.tabSettings.Controls.Add(this.checkBoxClickerMode);
            this.tabSettings.Controls.Add(this.checkBoxKillMobs);
            this.tabSettings.Controls.Add(this.checkBoxCheckId);
            this.tabSettings.Controls.Add(this.checkBoxLooting);
            this.tabSettings.Controls.Add(this.checkBoxUseSword);
            this.tabSettings.Controls.Add(this.checkBoxUseSkill);
            this.tabSettings.Controls.Add(this.labelTextCage);
            this.tabSettings.Controls.Add(this.textBoxMPusage);
            this.tabSettings.Controls.Add(this.labelTextUseMP);
            this.tabSettings.Controls.Add(this.textBoxHPusage);
            this.tabSettings.Controls.Add(this.labelTextUseHP);
            this.tabSettings.Controls.Add(this.cageSelect);
            this.tabSettings.Location = new System.Drawing.Point(4, 25);
            this.tabSettings.Name = "tabSettings";
            this.tabSettings.Padding = new System.Windows.Forms.Padding(3);
            this.tabSettings.Size = new System.Drawing.Size(369, 430);
            this.tabSettings.TabIndex = 1;
            this.tabSettings.Text = "Settings";
            this.tabSettings.UseVisualStyleBackColor = true;
            // 
            // textBoxHealPetUsage
            // 
            this.textBoxHealPetUsage.Location = new System.Drawing.Point(106, 187);
            this.textBoxHealPetUsage.Name = "textBoxHealPetUsage";
            this.textBoxHealPetUsage.Size = new System.Drawing.Size(100, 22);
            this.textBoxHealPetUsage.TabIndex = 24;
            this.textBoxHealPetUsage.Text = "90";
            // 
            // labelTextHealPet
            // 
            this.labelTextHealPet.AutoSize = true;
            this.labelTextHealPet.Location = new System.Drawing.Point(29, 190);
            this.labelTextHealPet.Name = "labelTextHealPet";
            this.labelTextHealPet.Size = new System.Drawing.Size(72, 16);
            this.labelTextHealPet.TabIndex = 23;
            this.labelTextHealPet.Text = "Heal Pet < ";
            // 
            // checkBoxClickerMode
            // 
            this.checkBoxClickerMode.AutoSize = true;
            this.checkBoxClickerMode.Location = new System.Drawing.Point(225, 119);
            this.checkBoxClickerMode.Name = "checkBoxClickerMode";
            this.checkBoxClickerMode.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.checkBoxClickerMode.Size = new System.Drawing.Size(108, 20);
            this.checkBoxClickerMode.TabIndex = 22;
            this.checkBoxClickerMode.Text = "Clicker Mode";
            this.checkBoxClickerMode.UseVisualStyleBackColor = true;
            // 
            // checkBoxKillMobs
            // 
            this.checkBoxKillMobs.AutoSize = true;
            this.checkBoxKillMobs.Location = new System.Drawing.Point(27, 145);
            this.checkBoxKillMobs.Name = "checkBoxKillMobs";
            this.checkBoxKillMobs.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.checkBoxKillMobs.Size = new System.Drawing.Size(83, 20);
            this.checkBoxKillMobs.TabIndex = 21;
            this.checkBoxKillMobs.Text = "Kill Mobs";
            this.checkBoxKillMobs.UseVisualStyleBackColor = true;
            // 
            // checkBoxCheckId
            // 
            this.checkBoxCheckId.AutoSize = true;
            this.checkBoxCheckId.Location = new System.Drawing.Point(115, 119);
            this.checkBoxCheckId.Name = "checkBoxCheckId";
            this.checkBoxCheckId.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.checkBoxCheckId.Size = new System.Drawing.Size(83, 20);
            this.checkBoxCheckId.TabIndex = 20;
            this.checkBoxCheckId.Text = "Check ID";
            this.checkBoxCheckId.UseVisualStyleBackColor = true;
            // 
            // checkBoxLooting
            // 
            this.checkBoxLooting.AutoSize = true;
            this.checkBoxLooting.Checked = true;
            this.checkBoxLooting.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBoxLooting.Location = new System.Drawing.Point(27, 119);
            this.checkBoxLooting.Name = "checkBoxLooting";
            this.checkBoxLooting.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.checkBoxLooting.Size = new System.Drawing.Size(73, 20);
            this.checkBoxLooting.TabIndex = 19;
            this.checkBoxLooting.Text = "Looting";
            this.checkBoxLooting.UseVisualStyleBackColor = true;
            // 
            // checkBoxUseSword
            // 
            this.checkBoxUseSword.AutoSize = true;
            this.checkBoxUseSword.Checked = true;
            this.checkBoxUseSword.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBoxUseSword.Location = new System.Drawing.Point(115, 93);
            this.checkBoxUseSword.Name = "checkBoxUseSword";
            this.checkBoxUseSword.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.checkBoxUseSword.Size = new System.Drawing.Size(95, 20);
            this.checkBoxUseSword.TabIndex = 18;
            this.checkBoxUseSword.Text = "Use Sword";
            this.checkBoxUseSword.UseVisualStyleBackColor = true;
            // 
            // checkBoxUseSkill
            // 
            this.checkBoxUseSkill.AutoSize = true;
            this.checkBoxUseSkill.Location = new System.Drawing.Point(27, 93);
            this.checkBoxUseSkill.Name = "checkBoxUseSkill";
            this.checkBoxUseSkill.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.checkBoxUseSkill.Size = new System.Drawing.Size(82, 20);
            this.checkBoxUseSkill.TabIndex = 17;
            this.checkBoxUseSkill.Text = "Use Skill";
            this.checkBoxUseSkill.UseVisualStyleBackColor = true;
            // 
            // labelTextCage
            // 
            this.labelTextCage.AutoSize = true;
            this.labelTextCage.Location = new System.Drawing.Point(221, 190);
            this.labelTextCage.Name = "labelTextCage";
            this.labelTextCage.Size = new System.Drawing.Size(40, 16);
            this.labelTextCage.TabIndex = 16;
            this.labelTextCage.Text = "Cage";
            // 
            // textBoxMPusage
            // 
            this.textBoxMPusage.Location = new System.Drawing.Point(128, 49);
            this.textBoxMPusage.Name = "textBoxMPusage";
            this.textBoxMPusage.Size = new System.Drawing.Size(100, 22);
            this.textBoxMPusage.TabIndex = 14;
            this.textBoxMPusage.Text = "100";
            // 
            // labelTextUseMP
            // 
            this.labelTextUseMP.AutoSize = true;
            this.labelTextUseMP.Location = new System.Drawing.Point(30, 52);
            this.labelTextUseMP.Name = "labelTextUseMP";
            this.labelTextUseMP.Size = new System.Drawing.Size(93, 16);
            this.labelTextUseMP.TabIndex = 13;
            this.labelTextUseMP.Text = "Use MP key < ";
            // 
            // textBoxHPusage
            // 
            this.textBoxHPusage.Location = new System.Drawing.Point(128, 15);
            this.textBoxHPusage.Name = "textBoxHPusage";
            this.textBoxHPusage.Size = new System.Drawing.Size(100, 22);
            this.textBoxHPusage.TabIndex = 12;
            this.textBoxHPusage.Text = "80";
            // 
            // labelTextUseHP
            // 
            this.labelTextUseHP.AutoSize = true;
            this.labelTextUseHP.Location = new System.Drawing.Point(30, 18);
            this.labelTextUseHP.Name = "labelTextUseHP";
            this.labelTextUseHP.Size = new System.Drawing.Size(92, 16);
            this.labelTextUseHP.TabIndex = 11;
            this.labelTextUseHP.Text = "Use HP key < ";
            // 
            // tabKeys
            // 
            this.tabKeys.Location = new System.Drawing.Point(4, 25);
            this.tabKeys.Name = "tabKeys";
            this.tabKeys.Size = new System.Drawing.Size(369, 430);
            this.tabKeys.TabIndex = 2;
            this.tabKeys.Text = "Keys";
            this.tabKeys.UseVisualStyleBackColor = true;
            // 
            // checkBoxTargetByTab
            // 
            this.checkBoxTargetByTab.AutoSize = true;
            this.checkBoxTargetByTab.Location = new System.Drawing.Point(27, 254);
            this.checkBoxTargetByTab.Name = "checkBoxTargetByTab";
            this.checkBoxTargetByTab.Size = new System.Drawing.Size(117, 20);
            this.checkBoxTargetByTab.TabIndex = 25;
            this.checkBoxTargetByTab.Text = "Target by TAB";
            this.checkBoxTargetByTab.UseVisualStyleBackColor = true;
            // 
            // BotForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(377, 491);
            this.Controls.Add(this.tabControl1);
            this.Controls.Add(this.menuStrip1);
            this.MainMenuStrip = this.menuStrip1;
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "BotForm";
            this.Text = "Bot";
            this.Load += new System.EventHandler(this.BotForm_Load);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.tabControl1.ResumeLayout(false);
            this.tabMain.ResumeLayout(false);
            this.tabMain.PerformLayout();
            this.tabSettings.ResumeLayout(false);
            this.tabSettings.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public System.Windows.Forms.Button startButton;
        public System.Windows.Forms.Button connectButton;
        public System.Windows.Forms.TextBox textBoxPID;
        public System.Windows.Forms.Label connectionPidLabel;
        public System.Windows.Forms.Label labelHpPers;
        public System.Windows.Forms.Label labelTargetId;
        public System.Windows.Forms.Button stopButton;
        public System.Windows.Forms.Label labelMpPers;
        public System.Windows.Forms.Label labelHpPet;
        public System.Windows.Forms.ComboBox cageSelect;
        public System.Windows.Forms.TextBox targetIdTextBox;
        public System.Windows.Forms.MenuStrip menuStrip1;
        public System.Windows.Forms.ToolStripMenuItem aboutToolStripMenuItem;
        public System.Windows.Forms.ToolStripMenuItem donationsToolStripMenuItem;
        public System.Windows.Forms.TabControl tabControl1;
        public System.Windows.Forms.TabPage tabMain;
        public System.Windows.Forms.TabPage tabSettings;
        public System.Windows.Forms.TabPage tabKeys;
        public System.Windows.Forms.Label labelTextPetHP;
        public System.Windows.Forms.Label labelTextMP;
        public System.Windows.Forms.Label labelTextHP;
        public System.Windows.Forms.Label labelTextCage;
        public System.Windows.Forms.TextBox textBoxMPusage;
        public System.Windows.Forms.Label labelTextUseMP;
        public System.Windows.Forms.TextBox textBoxHPusage;
        public System.Windows.Forms.Label labelTextUseHP;
        public System.Windows.Forms.Label labelTextHealPet;
        public System.Windows.Forms.CheckBox checkBoxUseSkill;
        public System.Windows.Forms.CheckBox checkBoxClickerMode;
        public System.Windows.Forms.CheckBox checkBoxCheckId;
        public System.Windows.Forms.CheckBox checkBoxKillMobs;
        public System.Windows.Forms.CheckBox checkBoxLooting;
        public System.Windows.Forms.CheckBox checkBoxUseSword;
        public System.Windows.Forms.Label labelState;
        public System.Windows.Forms.TextBox textBoxHealPetUsage;
        public System.Windows.Forms.CheckBox checkBoxUnfrezze;
        public System.Windows.Forms.CheckBox checkBoxTargetByTab;
    }
}

