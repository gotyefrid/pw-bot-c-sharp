using System.Threading;
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
            this.thread = new System.Windows.Forms.Label();
            this.stopButton = new System.Windows.Forms.Button();
            this.button1 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // startButton
            // 
            this.startButton.Location = new System.Drawing.Point(141, 192);
            this.startButton.Name = "startButton";
            this.startButton.Size = new System.Drawing.Size(75, 23);
            this.startButton.TabIndex = 0;
            this.startButton.Text = "Start";
            this.startButton.UseVisualStyleBackColor = true;
            this.startButton.Click += new System.EventHandler(this.StartButton_Click);
            // 
            // labelHpPers
            // 
            this.labelHpPers.AutoSize = true;
            this.labelHpPers.Location = new System.Drawing.Point(138, 54);
            this.labelHpPers.Name = "labelHpPers";
            this.labelHpPers.Size = new System.Drawing.Size(46, 13);
            this.labelHpPers.TabIndex = 1;
            this.labelHpPers.Text = "HP Pers";
            // 
            // textBoxPID
            // 
            this.textBoxPID.Location = new System.Drawing.Point(235, 15);
            this.textBoxPID.Name = "textBoxPID";
            this.textBoxPID.Size = new System.Drawing.Size(100, 20);
            this.textBoxPID.TabIndex = 2;
            this.textBoxPID.Text = "0";
            this.textBoxPID.Click += new System.EventHandler(this.TextBoxPID_Click);
            this.textBoxPID.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.TextBoxPID_KeyPress);
            // 
            // connectButton
            // 
            this.connectButton.Location = new System.Drawing.Point(121, 13);
            this.connectButton.Name = "connectButton";
            this.connectButton.Size = new System.Drawing.Size(108, 23);
            this.connectButton.TabIndex = 3;
            this.connectButton.Text = "Connect to PID";
            this.connectButton.UseVisualStyleBackColor = true;
            this.connectButton.Click += new System.EventHandler(this.ConnectButton_Click);
            // 
            // connectionPidLabel
            // 
            this.connectionPidLabel.AutoSize = true;
            this.connectionPidLabel.Location = new System.Drawing.Point(341, 18);
            this.connectionPidLabel.Name = "connectionPidLabel";
            this.connectionPidLabel.Size = new System.Drawing.Size(78, 13);
            this.connectionPidLabel.TabIndex = 4;
            this.connectionPidLabel.Text = "Not connected";
            // 
            // thread
            // 
            this.thread.AutoSize = true;
            this.thread.Location = new System.Drawing.Point(149, 142);
            this.thread.Name = "thread";
            this.thread.Size = new System.Drawing.Size(35, 13);
            this.thread.TabIndex = 5;
            this.thread.Text = "label1";
            // 
            // stopButton
            // 
            this.stopButton.Location = new System.Drawing.Point(235, 192);
            this.stopButton.Name = "stopButton";
            this.stopButton.Size = new System.Drawing.Size(75, 23);
            this.stopButton.TabIndex = 6;
            this.stopButton.Text = "Stop";
            this.stopButton.UseVisualStyleBackColor = true;
            this.stopButton.Click += new System.EventHandler(this.StopButton_Click);
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(200, 344);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(184, 58);
            this.button1.TabIndex = 7;
            this.button1.Text = "button1";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.Button1_Click);
            // 
            // BotForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(593, 495);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.stopButton);
            this.Controls.Add(this.thread);
            this.Controls.Add(this.connectionPidLabel);
            this.Controls.Add(this.connectButton);
            this.Controls.Add(this.textBoxPID);
            this.Controls.Add(this.labelHpPers);
            this.Controls.Add(this.startButton);
            this.Name = "BotForm";
            this.Text = "Bot";
            this.Load += new System.EventHandler(this.BotForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button startButton;
        private System.Windows.Forms.Button connectButton;
        private System.Windows.Forms.TextBox textBoxPID;
        private System.Windows.Forms.Label connectionPidLabel;
        public System.Windows.Forms.Label labelHpPers;
        private System.Windows.Forms.Label thread;
        private System.Windows.Forms.Button stopButton;
        private System.Windows.Forms.Button button1;
    }
}

