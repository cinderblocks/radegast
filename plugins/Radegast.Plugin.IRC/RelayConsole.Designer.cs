namespace Radegast.Plugin.IRC
{
    partial class RelayConsole
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.pnlChatLine = new System.Windows.Forms.Panel();
            this.btnSend = new System.Windows.Forms.Button();
            this.cbxInput = new Radegast.ChatInputBox();
            this.pnlConnectionSettings = new System.Windows.Forms.Panel();
            this.cbSource = new System.Windows.Forms.ComboBox();
            this.label5 = new System.Windows.Forms.Label();
            this.btnConnect = new System.Windows.Forms.Button();
            this.txtChan = new System.Windows.Forms.TextBox();
            this.txtNick = new System.Windows.Forms.TextBox();
            this.txtPort = new System.Windows.Forms.TextBox();
            this.txtServer = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.btnDisconnect = new System.Windows.Forms.Button();
            this.scChat = new System.Windows.Forms.SplitContainer();
            this.rtbChatText = new Radegast.RRichTextBox();
            this.Participants = new Radegast.ListViewNoFlicker();
            this.autoScrollCB = new System.Windows.Forms.CheckBox();
            this.pnlChatLine.SuspendLayout();
            this.pnlConnectionSettings.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.scChat)).BeginInit();
            this.scChat.Panel1.SuspendLayout();
            this.scChat.Panel2.SuspendLayout();
            this.scChat.SuspendLayout();
            this.SuspendLayout();
            // 
            // pnlChatLine
            // 
            this.pnlChatLine.Controls.Add(this.autoScrollCB);
            this.pnlChatLine.Controls.Add(this.btnSend);
            this.pnlChatLine.Controls.Add(this.cbxInput);
            this.pnlChatLine.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlChatLine.Location = new System.Drawing.Point(0, 397);
            this.pnlChatLine.Name = "pnlChatLine";
            this.pnlChatLine.Size = new System.Drawing.Size(754, 27);
            this.pnlChatLine.TabIndex = 0;
            // 
            // btnSend
            // 
            this.btnSend.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSend.Enabled = false;
            this.btnSend.Location = new System.Drawing.Point(620, 3);
            this.btnSend.Name = "btnSend";
            this.btnSend.Size = new System.Drawing.Size(55, 23);
            this.btnSend.TabIndex = 2;
            this.btnSend.Text = "Send";
            this.btnSend.UseVisualStyleBackColor = true;
            this.btnSend.Click += new System.EventHandler(this.btnSend_Click);
            // 
            // cbxInput
            // 
            this.cbxInput.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.cbxInput.Location = new System.Drawing.Point(3, 3);
            this.cbxInput.Name = "cbxInput";
            this.cbxInput.Size = new System.Drawing.Size(611, 20);
            this.cbxInput.TabIndex = 1;
            this.cbxInput.KeyDown += new System.Windows.Forms.KeyEventHandler(this.cbxInput_KeyDown);
            // 
            // pnlConnectionSettings
            // 
            this.pnlConnectionSettings.Controls.Add(this.cbSource);
            this.pnlConnectionSettings.Controls.Add(this.label5);
            this.pnlConnectionSettings.Controls.Add(this.btnConnect);
            this.pnlConnectionSettings.Controls.Add(this.txtChan);
            this.pnlConnectionSettings.Controls.Add(this.txtNick);
            this.pnlConnectionSettings.Controls.Add(this.txtPort);
            this.pnlConnectionSettings.Controls.Add(this.txtServer);
            this.pnlConnectionSettings.Controls.Add(this.label4);
            this.pnlConnectionSettings.Controls.Add(this.label3);
            this.pnlConnectionSettings.Controls.Add(this.label2);
            this.pnlConnectionSettings.Controls.Add(this.label1);
            this.pnlConnectionSettings.Controls.Add(this.btnDisconnect);
            this.pnlConnectionSettings.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlConnectionSettings.Location = new System.Drawing.Point(0, 0);
            this.pnlConnectionSettings.Name = "pnlConnectionSettings";
            this.pnlConnectionSettings.Size = new System.Drawing.Size(754, 32);
            this.pnlConnectionSettings.TabIndex = 1;
            // 
            // cbSource
            // 
            this.cbSource.FormattingEnabled = true;
            this.cbSource.Location = new System.Drawing.Point(620, 6);
            this.cbSource.Name = "cbSource";
            this.cbSource.Size = new System.Drawing.Size(121, 21);
            this.cbSource.TabIndex = 14;
            this.cbSource.SelectionChangeCommitted += new System.EventHandler(this.cbSource_SelectionChangeCommitted);
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(573, 9);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(41, 13);
            this.label5.TabIndex = 13;
            this.label5.Text = "Source";
            // 
            // btnConnect
            // 
            this.btnConnect.Location = new System.Drawing.Point(9, 4);
            this.btnConnect.Name = "btnConnect";
            this.btnConnect.Size = new System.Drawing.Size(75, 23);
            this.btnConnect.TabIndex = 10;
            this.btnConnect.Text = "Connect";
            this.btnConnect.UseVisualStyleBackColor = true;
            this.btnConnect.Click += new System.EventHandler(this.btnConnect_Click);
            // 
            // txtChan
            // 
            this.txtChan.Location = new System.Drawing.Point(487, 6);
            this.txtChan.Name = "txtChan";
            this.txtChan.Size = new System.Drawing.Size(80, 20);
            this.txtChan.TabIndex = 4;
            this.txtChan.Text = "#";
            this.txtChan.Validated += new System.EventHandler(this.txtChan_Validated);
            // 
            // txtNick
            // 
            this.txtNick.Location = new System.Drawing.Point(382, 6);
            this.txtNick.Name = "txtNick";
            this.txtNick.Size = new System.Drawing.Size(61, 20);
            this.txtNick.TabIndex = 3;
            this.txtNick.Validated += new System.EventHandler(this.txtNick_Validated);
            // 
            // txtPort
            // 
            this.txtPort.Location = new System.Drawing.Point(301, 6);
            this.txtPort.Name = "txtPort";
            this.txtPort.Size = new System.Drawing.Size(40, 20);
            this.txtPort.TabIndex = 2;
            this.txtPort.Text = "66677";
            this.txtPort.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.txtPort_KeyPress);
            this.txtPort.Validated += new System.EventHandler(this.txtPort_Validated);
            // 
            // txtServer
            // 
            this.txtServer.Location = new System.Drawing.Point(155, 6);
            this.txtServer.Name = "txtServer";
            this.txtServer.Size = new System.Drawing.Size(108, 20);
            this.txtServer.TabIndex = 1;
            this.txtServer.Text = "irc.freenode.net";
            this.txtServer.Validated += new System.EventHandler(this.txtServer_Validated);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(449, 9);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(32, 13);
            this.label4.TabIndex = 0;
            this.label4.Text = "Chan";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(347, 9);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(29, 13);
            this.label3.TabIndex = 0;
            this.label3.Text = "Nick";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(269, 9);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(26, 13);
            this.label2.TabIndex = 0;
            this.label2.Text = "Port";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(90, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(59, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "IRC Server";
            // 
            // btnDisconnect
            // 
            this.btnDisconnect.Enabled = false;
            this.btnDisconnect.Location = new System.Drawing.Point(9, 4);
            this.btnDisconnect.Name = "btnDisconnect";
            this.btnDisconnect.Size = new System.Drawing.Size(75, 23);
            this.btnDisconnect.TabIndex = 11;
            this.btnDisconnect.Text = "Disconnect";
            this.btnDisconnect.UseVisualStyleBackColor = true;
            this.btnDisconnect.Visible = false;
            this.btnDisconnect.Click += new System.EventHandler(this.btnDisconnect_Click);
            // 
            // scChat
            // 
            this.scChat.Dock = System.Windows.Forms.DockStyle.Fill;
            this.scChat.Location = new System.Drawing.Point(0, 32);
            this.scChat.Name = "scChat";
            // 
            // scChat.Panel1
            // 
            this.scChat.Panel1.Controls.Add(this.rtbChatText);
            // 
            // scChat.Panel2
            // 
            this.scChat.Panel2.Controls.Add(this.Participants);
            this.scChat.Panel2.Enabled = false;
            this.scChat.Size = new System.Drawing.Size(754, 365);
            this.scChat.SplitterDistance = 569;
            this.scChat.TabIndex = 2;
            // 
            // rtbChatText
            // 
            this.rtbChatText.BackColor = System.Drawing.Color.White;
            this.rtbChatText.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rtbChatText.HideSelection = false;
            this.rtbChatText.Location = new System.Drawing.Point(0, 0);
            this.rtbChatText.Name = "rtbChatText";
            this.rtbChatText.ReadOnly = true;
            this.rtbChatText.Size = new System.Drawing.Size(569, 365);
            this.rtbChatText.TabIndex = 4;
            this.rtbChatText.Text = "";
            // 
            // Participants
            // 
            this.Participants.Activation = System.Windows.Forms.ItemActivation.OneClick;
            this.Participants.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Participants.HideSelection = false;
            this.Participants.Location = new System.Drawing.Point(0, 0);
            this.Participants.MultiSelect = false;
            this.Participants.Name = "Participants";
            this.Participants.ShowGroups = false;
            this.Participants.Size = new System.Drawing.Size(181, 365);
            this.Participants.Sorting = System.Windows.Forms.SortOrder.Ascending;
            this.Participants.TabIndex = 5;
            this.Participants.UseCompatibleStateImageBehavior = false;
            this.Participants.View = System.Windows.Forms.View.List;
            // 
            // autoScrollCB
            // 
            this.autoScrollCB.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.autoScrollCB.AutoSize = true;
            this.autoScrollCB.Checked = true;
            this.autoScrollCB.CheckState = System.Windows.Forms.CheckState.Checked;
            this.autoScrollCB.Location = new System.Drawing.Point(679, 7);
            this.autoScrollCB.Name = "autoScrollCB";
            this.autoScrollCB.Size = new System.Drawing.Size(72, 17);
            this.autoScrollCB.TabIndex = 14;
            this.autoScrollCB.Text = "Autoscroll";
            this.autoScrollCB.UseVisualStyleBackColor = true;
            // 
            // RelayConsole
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.scChat);
            this.Controls.Add(this.pnlConnectionSettings);
            this.Controls.Add(this.pnlChatLine);
            this.Name = "RelayConsole";
            this.Size = new System.Drawing.Size(754, 424);
            this.pnlChatLine.ResumeLayout(false);
            this.pnlChatLine.PerformLayout();
            this.pnlConnectionSettings.ResumeLayout(false);
            this.pnlConnectionSettings.PerformLayout();
            this.scChat.Panel1.ResumeLayout(false);
            this.scChat.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.scChat)).EndInit();
            this.scChat.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel pnlChatLine;
        private System.Windows.Forms.Panel pnlConnectionSettings;
        private System.Windows.Forms.SplitContainer scChat;
        public RRichTextBox rtbChatText;
        public ListViewNoFlicker Participants;
        public ChatInputBox cbxInput;
        public System.Windows.Forms.Button btnSend;
        private System.Windows.Forms.TextBox txtNick;
        private System.Windows.Forms.TextBox txtPort;
        private System.Windows.Forms.TextBox txtServer;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button btnConnect;
        private System.Windows.Forms.TextBox txtChan;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Button btnDisconnect;
        private System.Windows.Forms.ComboBox cbSource;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.CheckBox autoScrollCB;
    }
}
