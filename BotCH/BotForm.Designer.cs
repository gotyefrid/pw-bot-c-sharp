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
            this.components = new System.ComponentModel.Container();
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
            this.labelTextPetHP = new System.Windows.Forms.Label();
            this.labelTextMP = new System.Windows.Forms.Label();
            this.labelTextHP = new System.Windows.Forms.Label();
            this.tabSettings = new System.Windows.Forms.TabPage();
            this.checkFindAgrMob = new System.Windows.Forms.CheckBox();
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
            this.checkBoxEnableLog = new System.Windows.Forms.CheckBox();
            this.richTextBoxLogBox = new System.Windows.Forms.RichTextBox();
            this.labelState = new System.Windows.Forms.Label();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.checkBoxComeCloser = new System.Windows.Forms.CheckBox();
            this.menuStrip1.SuspendLayout();
            this.tabControl1.SuspendLayout();
            this.tabMain.SuspendLayout();
            this.tabSettings.SuspendLayout();
            this.tabKeys.SuspendLayout();
            this.SuspendLayout();
            // 
            // startButton
            // 
            this.startButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.startButton.Location = new System.Drawing.Point(21, 233);
            this.startButton.Margin = new System.Windows.Forms.Padding(4);
            this.startButton.Name = "startButton";
            this.startButton.Size = new System.Drawing.Size(141, 59);
            this.startButton.TabIndex = 0;
            this.startButton.Text = "Start";
            this.startButton.UseVisualStyleBackColor = true;
            this.startButton.Click += new System.EventHandler(this.StartButton_Click);
            // 
            // labelHpPers
            // 
            this.labelHpPers.AutoSize = true;
            this.labelHpPers.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.labelHpPers.Location = new System.Drawing.Point(152, 64);
            this.labelHpPers.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelHpPers.Name = "labelHpPers";
            this.labelHpPers.Size = new System.Drawing.Size(73, 20);
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
            this.connectButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
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
            this.labelTargetId.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.labelTargetId.Location = new System.Drawing.Point(83, 181);
            this.labelTargetId.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelTargetId.Name = "labelTargetId";
            this.labelTargetId.Size = new System.Drawing.Size(79, 20);
            this.labelTargetId.TabIndex = 5;
            this.labelTargetId.Text = "Target ID";
            // 
            // stopButton
            // 
            this.stopButton.Enabled = false;
            this.stopButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.stopButton.Location = new System.Drawing.Point(204, 233);
            this.stopButton.Margin = new System.Windows.Forms.Padding(4);
            this.stopButton.Name = "stopButton";
            this.stopButton.Size = new System.Drawing.Size(141, 59);
            this.stopButton.TabIndex = 6;
            this.stopButton.Text = "Stop";
            this.stopButton.UseVisualStyleBackColor = true;
            this.stopButton.Click += new System.EventHandler(this.StopButton_Click);
            // 
            // labelMpPers
            // 
            this.labelMpPers.AutoSize = true;
            this.labelMpPers.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.labelMpPers.Location = new System.Drawing.Point(152, 90);
            this.labelMpPers.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelMpPers.Name = "labelMpPers";
            this.labelMpPers.Size = new System.Drawing.Size(74, 20);
            this.labelMpPers.TabIndex = 8;
            this.labelMpPers.Text = "MP Pers";
            // 
            // labelHpPet
            // 
            this.labelHpPet.AutoSize = true;
            this.labelHpPet.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.labelHpPet.Location = new System.Drawing.Point(185, 119);
            this.labelHpPet.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelHpPet.Name = "labelHpPet";
            this.labelHpPet.Size = new System.Drawing.Size(63, 20);
            this.labelHpPet.TabIndex = 9;
            this.labelHpPet.Text = "HP Pet";
            // 
            // cageSelect
            // 
            this.cageSelect.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cageSelect.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.cageSelect.FormattingEnabled = true;
            this.cageSelect.Items.AddRange(new object[] {
            "1",
            "2",
            "3",
            "4",
            "5"});
            this.cageSelect.Location = new System.Drawing.Point(168, 220);
            this.cageSelect.Name = "cageSelect";
            this.cageSelect.Size = new System.Drawing.Size(79, 28);
            this.cageSelect.TabIndex = 10;
            // 
            // targetIdTextBox
            // 
            this.targetIdTextBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.targetIdTextBox.Location = new System.Drawing.Point(169, 178);
            this.targetIdTextBox.Name = "targetIdTextBox";
            this.targetIdTextBox.ReadOnly = true;
            this.targetIdTextBox.Size = new System.Drawing.Size(119, 26);
            this.targetIdTextBox.TabIndex = 11;
            // 
            // menuStrip1
            // 
            this.menuStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.aboutToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(375, 28);
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
            this.tabControl1.Size = new System.Drawing.Size(375, 440);
            this.tabControl1.TabIndex = 13;
            // 
            // tabMain
            // 
            this.tabMain.Controls.Add(this.checkBoxUnfrezze);
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
            this.tabMain.Size = new System.Drawing.Size(367, 411);
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
            // labelTextPetHP
            // 
            this.labelTextPetHP.AutoSize = true;
            this.labelTextPetHP.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.labelTextPetHP.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.labelTextPetHP.Location = new System.Drawing.Point(114, 119);
            this.labelTextPetHP.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelTextPetHP.Name = "labelTextPetHP";
            this.labelTextPetHP.Size = new System.Drawing.Size(63, 20);
            this.labelTextPetHP.TabIndex = 14;
            this.labelTextPetHP.Text = "HP Pet";
            // 
            // labelTextMP
            // 
            this.labelTextMP.AutoSize = true;
            this.labelTextMP.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.labelTextMP.ForeColor = System.Drawing.Color.Blue;
            this.labelTextMP.Location = new System.Drawing.Point(113, 90);
            this.labelTextMP.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelTextMP.Name = "labelTextMP";
            this.labelTextMP.Size = new System.Drawing.Size(34, 20);
            this.labelTextMP.TabIndex = 13;
            this.labelTextMP.Text = "MP";
            // 
            // labelTextHP
            // 
            this.labelTextHP.AutoSize = true;
            this.labelTextHP.BackColor = System.Drawing.Color.Transparent;
            this.labelTextHP.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.labelTextHP.ForeColor = System.Drawing.Color.Red;
            this.labelTextHP.Location = new System.Drawing.Point(114, 64);
            this.labelTextHP.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.labelTextHP.Name = "labelTextHP";
            this.labelTextHP.Size = new System.Drawing.Size(33, 20);
            this.labelTextHP.TabIndex = 12;
            this.labelTextHP.Text = "HP";
            // 
            // tabSettings
            // 
            this.tabSettings.Controls.Add(this.checkBoxComeCloser);
            this.tabSettings.Controls.Add(this.checkFindAgrMob);
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
            this.tabSettings.Size = new System.Drawing.Size(367, 411);
            this.tabSettings.TabIndex = 1;
            this.tabSettings.Text = "Settings";
            this.tabSettings.UseVisualStyleBackColor = true;
            // 
            // checkFindAgrMob
            // 
            this.checkFindAgrMob.AutoSize = true;
            this.checkFindAgrMob.Checked = true;
            this.checkFindAgrMob.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkFindAgrMob.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.checkFindAgrMob.Location = new System.Drawing.Point(13, 144);
            this.checkFindAgrMob.Name = "checkFindAgrMob";
            this.checkFindAgrMob.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.checkFindAgrMob.Size = new System.Drawing.Size(129, 24);
            this.checkFindAgrMob.TabIndex = 25;
            this.checkFindAgrMob.Text = "Find agr mob";
            this.toolTip1.SetToolTip(this.checkFindAgrMob, "If checked = Char will generate near mob list once after start buttion, and then " +
        "before TAB click will looking for a AGR mob aroud, if founded - Char will not ch" +
        "ange he from target");
            this.checkFindAgrMob.UseVisualStyleBackColor = true;
            // 
            // textBoxHealPetUsage
            // 
            this.textBoxHealPetUsage.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.textBoxHealPetUsage.Location = new System.Drawing.Point(168, 188);
            this.textBoxHealPetUsage.Name = "textBoxHealPetUsage";
            this.textBoxHealPetUsage.Size = new System.Drawing.Size(100, 26);
            this.textBoxHealPetUsage.TabIndex = 24;
            this.textBoxHealPetUsage.Text = "90";
            // 
            // labelTextHealPet
            // 
            this.labelTextHealPet.AutoSize = true;
            this.labelTextHealPet.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.labelTextHealPet.Location = new System.Drawing.Point(9, 191);
            this.labelTextHealPet.Name = "labelTextHealPet";
            this.labelTextHealPet.Size = new System.Drawing.Size(113, 20);
            this.labelTextHealPet.TabIndex = 23;
            this.labelTextHealPet.Text = "Heal Pet <, %";
            this.toolTip1.SetToolTip(this.labelTextHealPet, "Press Heal Pet key when % will above");
            // 
            // checkBoxClickerMode
            // 
            this.checkBoxClickerMode.AutoSize = true;
            this.checkBoxClickerMode.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.checkBoxClickerMode.Location = new System.Drawing.Point(118, 369);
            this.checkBoxClickerMode.Name = "checkBoxClickerMode";
            this.checkBoxClickerMode.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.checkBoxClickerMode.Size = new System.Drawing.Size(129, 24);
            this.checkBoxClickerMode.TabIndex = 22;
            this.checkBoxClickerMode.Text = "Clicker Mode";
            this.checkBoxClickerMode.UseVisualStyleBackColor = true;
            this.checkBoxClickerMode.Visible = false;
            // 
            // checkBoxKillMobs
            // 
            this.checkBoxKillMobs.AutoSize = true;
            this.checkBoxKillMobs.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.checkBoxKillMobs.Location = new System.Drawing.Point(10, 369);
            this.checkBoxKillMobs.Name = "checkBoxKillMobs";
            this.checkBoxKillMobs.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.checkBoxKillMobs.Size = new System.Drawing.Size(100, 24);
            this.checkBoxKillMobs.TabIndex = 21;
            this.checkBoxKillMobs.Text = "Kill Mobs";
            this.checkBoxKillMobs.UseVisualStyleBackColor = true;
            this.checkBoxKillMobs.Visible = false;
            // 
            // checkBoxCheckId
            // 
            this.checkBoxCheckId.AutoSize = true;
            this.checkBoxCheckId.Checked = true;
            this.checkBoxCheckId.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBoxCheckId.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.checkBoxCheckId.Location = new System.Drawing.Point(168, 117);
            this.checkBoxCheckId.Name = "checkBoxCheckId";
            this.checkBoxCheckId.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.checkBoxCheckId.Size = new System.Drawing.Size(100, 24);
            this.checkBoxCheckId.TabIndex = 20;
            this.checkBoxCheckId.Text = "Check ID";
            this.toolTip1.SetToolTip(this.checkBoxCheckId, "If checked = Char will compare mob ID with MobID list in INI file");
            this.checkBoxCheckId.UseVisualStyleBackColor = true;
            // 
            // checkBoxLooting
            // 
            this.checkBoxLooting.AutoSize = true;
            this.checkBoxLooting.Checked = true;
            this.checkBoxLooting.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBoxLooting.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.checkBoxLooting.Location = new System.Drawing.Point(13, 117);
            this.checkBoxLooting.Name = "checkBoxLooting";
            this.checkBoxLooting.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.checkBoxLooting.Size = new System.Drawing.Size(86, 24);
            this.checkBoxLooting.TabIndex = 19;
            this.checkBoxLooting.Text = "Looting";
            this.toolTip1.SetToolTip(this.checkBoxLooting, "If checked = Char will come to mob first, and after mob died Char will get loot");
            this.checkBoxLooting.UseVisualStyleBackColor = true;
            // 
            // checkBoxUseSword
            // 
            this.checkBoxUseSword.AutoSize = true;
            this.checkBoxUseSword.Checked = true;
            this.checkBoxUseSword.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBoxUseSword.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.checkBoxUseSword.Location = new System.Drawing.Point(168, 91);
            this.checkBoxUseSword.Name = "checkBoxUseSword";
            this.checkBoxUseSword.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.checkBoxUseSword.Size = new System.Drawing.Size(113, 24);
            this.checkBoxUseSword.TabIndex = 18;
            this.checkBoxUseSword.Text = "Use Sword";
            this.toolTip1.SetToolTip(this.checkBoxUseSword, "If checked = Char will use Sword key when killing mob");
            this.checkBoxUseSword.UseVisualStyleBackColor = true;
            // 
            // checkBoxUseSkill
            // 
            this.checkBoxUseSkill.AutoSize = true;
            this.checkBoxUseSkill.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.checkBoxUseSkill.Location = new System.Drawing.Point(13, 91);
            this.checkBoxUseSkill.Name = "checkBoxUseSkill";
            this.checkBoxUseSkill.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.checkBoxUseSkill.Size = new System.Drawing.Size(97, 24);
            this.checkBoxUseSkill.TabIndex = 17;
            this.checkBoxUseSkill.Text = "Use Skill";
            this.toolTip1.SetToolTip(this.checkBoxUseSkill, "If checked = Char will use Skill key whe killing mob");
            this.checkBoxUseSkill.UseVisualStyleBackColor = true;
            // 
            // labelTextCage
            // 
            this.labelTextCage.AutoSize = true;
            this.labelTextCage.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.labelTextCage.Location = new System.Drawing.Point(9, 223);
            this.labelTextCage.Name = "labelTextCage";
            this.labelTextCage.Size = new System.Drawing.Size(48, 20);
            this.labelTextCage.TabIndex = 16;
            this.labelTextCage.Text = "Cage";
            this.toolTip1.SetToolTip(this.labelTextCage, "Set cage of pet");
            // 
            // textBoxMPusage
            // 
            this.textBoxMPusage.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.textBoxMPusage.Location = new System.Drawing.Point(168, 47);
            this.textBoxMPusage.Name = "textBoxMPusage";
            this.textBoxMPusage.Size = new System.Drawing.Size(100, 26);
            this.textBoxMPusage.TabIndex = 14;
            this.textBoxMPusage.Text = "100";
            // 
            // labelTextUseMP
            // 
            this.labelTextUseMP.AutoSize = true;
            this.labelTextUseMP.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.labelTextUseMP.Location = new System.Drawing.Point(8, 49);
            this.labelTextUseMP.Name = "labelTextUseMP";
            this.labelTextUseMP.Size = new System.Drawing.Size(148, 20);
            this.labelTextUseMP.TabIndex = 13;
            this.labelTextUseMP.Text = "Use MP key <, MP";
            this.toolTip1.SetToolTip(this.labelTextUseMP, "Use MP key whe MP above XX MP");
            // 
            // textBoxHPusage
            // 
            this.textBoxHPusage.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.textBoxHPusage.Location = new System.Drawing.Point(168, 15);
            this.textBoxHPusage.Name = "textBoxHPusage";
            this.textBoxHPusage.Size = new System.Drawing.Size(100, 26);
            this.textBoxHPusage.TabIndex = 12;
            this.textBoxHPusage.Text = "80";
            // 
            // labelTextUseHP
            // 
            this.labelTextUseHP.AutoSize = true;
            this.labelTextUseHP.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.labelTextUseHP.Location = new System.Drawing.Point(9, 18);
            this.labelTextUseHP.Name = "labelTextUseHP";
            this.labelTextUseHP.Size = new System.Drawing.Size(137, 20);
            this.labelTextUseHP.TabIndex = 11;
            this.labelTextUseHP.Text = "Use HP key <, %";
            this.toolTip1.SetToolTip(this.labelTextUseHP, "Use HP key when HP above XX %");
            // 
            // tabKeys
            // 
            this.tabKeys.Controls.Add(this.checkBoxEnableLog);
            this.tabKeys.Controls.Add(this.richTextBoxLogBox);
            this.tabKeys.Location = new System.Drawing.Point(4, 25);
            this.tabKeys.Name = "tabKeys";
            this.tabKeys.Size = new System.Drawing.Size(367, 411);
            this.tabKeys.TabIndex = 2;
            this.tabKeys.Text = "Log";
            this.tabKeys.UseVisualStyleBackColor = true;
            // 
            // checkBoxEnableLog
            // 
            this.checkBoxEnableLog.AutoSize = true;
            this.checkBoxEnableLog.Location = new System.Drawing.Point(9, 7);
            this.checkBoxEnableLog.Name = "checkBoxEnableLog";
            this.checkBoxEnableLog.Size = new System.Drawing.Size(98, 20);
            this.checkBoxEnableLog.TabIndex = 1;
            this.checkBoxEnableLog.Text = "Enable Log";
            this.checkBoxEnableLog.UseVisualStyleBackColor = true;
            // 
            // richTextBoxLogBox
            // 
            this.richTextBoxLogBox.Location = new System.Drawing.Point(8, 33);
            this.richTextBoxLogBox.Name = "richTextBoxLogBox";
            this.richTextBoxLogBox.ReadOnly = true;
            this.richTextBoxLogBox.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.ForcedBoth;
            this.richTextBoxLogBox.Size = new System.Drawing.Size(338, 360);
            this.richTextBoxLogBox.TabIndex = 0;
            this.richTextBoxLogBox.Text = "";
            // 
            // labelState
            // 
            this.labelState.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.labelState.Location = new System.Drawing.Point(0, 484);
            this.labelState.Name = "labelState";
            this.labelState.Size = new System.Drawing.Size(375, 16);
            this.labelState.TabIndex = 15;
            this.labelState.Text = "State";
            // 
            // checkBoxComeCloser
            // 
            this.checkBoxComeCloser.AutoSize = true;
            this.checkBoxComeCloser.Checked = true;
            this.checkBoxComeCloser.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBoxComeCloser.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.checkBoxComeCloser.Location = new System.Drawing.Point(168, 144);
            this.checkBoxComeCloser.Name = "checkBoxComeCloser";
            this.checkBoxComeCloser.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.checkBoxComeCloser.Size = new System.Drawing.Size(129, 24);
            this.checkBoxComeCloser.TabIndex = 26;
            this.checkBoxComeCloser.Text = "Come Closer";
            this.toolTip1.SetToolTip(this.checkBoxComeCloser, "If checked = Char will come to mob to get loot");
            this.checkBoxComeCloser.UseVisualStyleBackColor = true;
            // 
            // BotForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(375, 500);
            this.Controls.Add(this.tabControl1);
            this.Controls.Add(this.labelState);
            this.Controls.Add(this.menuStrip1);
            this.MainMenuStrip = this.menuStrip1;
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "BotForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Bot";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.BotForm_FormClosing);
            this.Load += new System.EventHandler(this.BotForm_Load);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.tabControl1.ResumeLayout(false);
            this.tabMain.ResumeLayout(false);
            this.tabMain.PerformLayout();
            this.tabSettings.ResumeLayout(false);
            this.tabSettings.PerformLayout();
            this.tabKeys.ResumeLayout(false);
            this.tabKeys.PerformLayout();
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
        public System.Windows.Forms.RichTextBox richTextBoxLogBox;
        public System.Windows.Forms.CheckBox checkBoxEnableLog;
        public System.Windows.Forms.CheckBox checkFindAgrMob;
        private System.Windows.Forms.ToolTip toolTip1;
        public System.Windows.Forms.CheckBox checkBoxComeCloser;
    }
}

