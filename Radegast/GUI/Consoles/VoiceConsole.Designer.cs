//
// Radegast Metaverse Client
// Copyright (c) 2009-2014, Radegast Development Team
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
//
//     * Redistributions of source code must retain the above copyright notice,
//       this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above copyright
//       notice, this list of conditions and the following disclaimer in the
//       documentation and/or other materials provided with the distribution.
//     * Neither the name of the application "Radegast", nor the names of its
//       contributors may be used to endorse or promote products derived from
//       this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
// $Id: ChatConsole.Designer.cs 371 2009-10-26 10:26:04Z latifer $
//
using System.Windows.Forms;

namespace Radegast
{
    partial class VoiceConsole
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(VoiceConsole));
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.btnUnmuteAll = new System.Windows.Forms.Button();
            this.btnMuteAll = new System.Windows.Forms.Button();
            this.label5 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.button1 = new System.Windows.Forms.Button();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.spkrMute = new System.Windows.Forms.CheckBox();
            this.micMute = new System.Windows.Forms.CheckBox();
            this.pictureBox2 = new System.Windows.Forms.PictureBox();
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.btnReconnect = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.micLevel = new System.Windows.Forms.TrackBar();
            this.spkrLevel = new System.Windows.Forms.TrackBar();
            this.spkrDevice = new System.Windows.Forms.ComboBox();
            this.micDevice = new System.Windows.Forms.ComboBox();
            this.chkVoiceEnable = new System.Windows.Forms.CheckBox();
            this.chkAutoConnect = new System.Windows.Forms.CheckBox();
            this.chkPushToTalk = new System.Windows.Forms.CheckBox();
            this.labelPttKey = new System.Windows.Forms.Label();
            this.pttKeyCombo = new System.Windows.Forms.ComboBox();
            this.participants = new Radegast.ListViewNoFlicker();
            this.avatarContext = new Radegast.RadegastContextMenuStrip(this.components);
            this.ctxProfile = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxPay = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxStartIM = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxFollow = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxTextures = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxAttach = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxMaster = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxAnim = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxPoint = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxSource = new System.Windows.Forms.ToolStripMenuItem();
            this.TalkStates = new System.Windows.Forms.ImageList(this.components);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.micLevel)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.spkrLevel)).BeginInit();
            this.avatarContext.SuspendLayout();
            this.SuspendLayout();
            //
            // splitContainer1
            //
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 0);
            this.splitContainer1.Name = "splitContainer1";
            //
            // splitContainer1.Panel1
            //
            this.splitContainer1.Panel1.Controls.Add(this.pttKeyCombo);
            this.splitContainer1.Panel1.Controls.Add(this.labelPttKey);
            this.splitContainer1.Panel1.Controls.Add(this.chkPushToTalk);
            this.splitContainer1.Panel1.Controls.Add(this.chkAutoConnect);
            this.splitContainer1.Panel1.Controls.Add(this.btnUnmuteAll);
            this.splitContainer1.Panel1.Controls.Add(this.btnMuteAll);
            this.splitContainer1.Panel1.Controls.Add(this.label5);
            this.splitContainer1.Panel1.Controls.Add(this.label4);
            this.splitContainer1.Panel1.Controls.Add(this.label3);
            this.splitContainer1.Panel1.Controls.Add(this.label1);
            this.splitContainer1.Panel1.Controls.Add(this.button1);
            this.splitContainer1.Panel1.Controls.Add(this.pictureBox1);
            this.splitContainer1.Panel1.Controls.Add(this.spkrMute);
            this.splitContainer1.Panel1.Controls.Add(this.micMute);
            this.splitContainer1.Panel1.Controls.Add(this.pictureBox2);
            this.splitContainer1.Panel1.Controls.Add(this.progressBar1);
            this.splitContainer1.Panel1.Controls.Add(this.btnReconnect);
            this.splitContainer1.Panel1.Controls.Add(this.label2);
            this.splitContainer1.Panel1.Controls.Add(this.micLevel);
            this.splitContainer1.Panel1.Controls.Add(this.spkrLevel);
            this.splitContainer1.Panel1.Controls.Add(this.spkrDevice);
            this.splitContainer1.Panel1.Controls.Add(this.micDevice);
            this.splitContainer1.Panel1.Controls.Add(this.chkVoiceEnable);
            //
            // splitContainer1.Panel2
            //
            this.splitContainer1.Panel2.Controls.Add(this.participants);
            this.splitContainer1.Size = new System.Drawing.Size(654, 424);
            this.splitContainer1.SplitterDistance = 462;
            this.splitContainer1.TabIndex = 7;
            this.splitContainer1.TabStop = false;
            //
            // btnUnmuteAll
            //
            this.btnUnmuteAll.Location = new System.Drawing.Point(372, 211);
            this.btnUnmuteAll.Name = "btnUnmuteAll";
            this.btnUnmuteAll.Size = new System.Drawing.Size(75, 23);
            this.btnUnmuteAll.TabIndex = 30;
            this.btnUnmuteAll.Text = "Unmute All";
            this.btnUnmuteAll.UseVisualStyleBackColor = true;
            this.btnUnmuteAll.Click += new System.EventHandler(this.btnUnmuteAll_Click);
            //
            // btnMuteAll
            //
            this.btnMuteAll.Location = new System.Drawing.Point(372, 170);
            this.btnMuteAll.Name = "btnMuteAll";
            this.btnMuteAll.Size = new System.Drawing.Size(75, 23);
            this.btnMuteAll.TabIndex = 29;
            this.btnMuteAll.Text = "Mute All";
            this.btnMuteAll.UseVisualStyleBackColor = true;
            this.btnMuteAll.Click += new System.EventHandler(this.btnMuteAll_Click);
            //
            // label5
            //
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(245, 91);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(99, 13);
            this.label5.TabIndex = 28;
            this.label5.Text = "Audio device select";
            //
            // label4
            //
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(104, 385);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(225, 13);
            this.label4.TabIndex = 27;
            this.label4.Text = "( or hold the configured Push-To-Talk key )";
            //
            // label3
            //
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(230, 10);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(120, 13);
            this.label3.TabIndex = 26;
            this.label3.Text = "Voice connection status";
            //
            // label1
            //
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(395, 381);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(52, 26);
            this.label1.TabIndex = 25;
            this.label1.Text = "My Listen\r\nLevel";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // button1
            //
            this.button1.AccessibleName = "Push To Talk";
            this.button1.BackColor = System.Drawing.SystemColors.AppWorkspace;
            this.button1.Font = new System.Drawing.Font("Arial", 14.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.button1.Location = new System.Drawing.Point(81, 281);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(293, 97);
            this.button1.TabIndex = 24;
            this.button1.Text = "Push To Talk";
            this.button1.UseVisualStyleBackColor = false;
            this.button1.MouseDown += new System.Windows.Forms.MouseEventHandler(this.OnMouseDown);
            this.button1.MouseUp += new System.Windows.Forms.MouseEventHandler(this.OnMouseUp);
            //
            // pictureBox1
            //
            this.pictureBox1.Image = global::Radegast.Properties.Resources.mic;
            this.pictureBox1.Location = new System.Drawing.Point(22, 67);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(32, 32);
            this.pictureBox1.TabIndex = 23;
            this.pictureBox1.TabStop = false;
            //
            // spkrMute
            //
            this.spkrMute.AccessibleName = "Listen Mute";
            this.spkrMute.AutoSize = true;
            this.spkrMute.Location = new System.Drawing.Point(92, 112);
            this.spkrMute.Name = "spkrMute";
            this.spkrMute.Size = new System.Drawing.Size(50, 17);
            this.spkrMute.TabIndex = 21;
            this.spkrMute.Text = "Mute";
            this.spkrMute.UseVisualStyleBackColor = true;
            this.spkrMute.CheckedChanged += new System.EventHandler(this.spkrMute_CheckedChanged);
            //
            // micMute
            //
            this.micMute.AccessibleName = "Talk Mute";
            this.micMute.AutoSize = true;
            this.micMute.Checked = true;
            this.micMute.CheckState = System.Windows.Forms.CheckState.Checked;
            this.micMute.Location = new System.Drawing.Point(92, 69);
            this.micMute.Name = "micMute";
            this.micMute.Size = new System.Drawing.Size(50, 17);
            this.micMute.TabIndex = 20;
            this.micMute.Text = "Mute";
            this.micMute.UseVisualStyleBackColor = true;
            this.micMute.CheckedChanged += new System.EventHandler(this.micMute_CheckedChanged);
            //
            // pictureBox2
            //
            this.pictureBox2.Image = ((System.Drawing.Image)(resources.GetObject("pictureBox2.Image")));
            this.pictureBox2.InitialImage = ((System.Drawing.Image)(resources.GetObject("pictureBox2.InitialImage")));
            this.pictureBox2.Location = new System.Drawing.Point(22, 114);
            this.pictureBox2.Name = "pictureBox2";
            this.pictureBox2.Size = new System.Drawing.Size(31, 29);
            this.pictureBox2.TabIndex = 19;
            this.pictureBox2.TabStop = false;
            //
            // progressBar1
            //
            this.progressBar1.AccessibleName = "ConnectionStatus";
            this.progressBar1.Location = new System.Drawing.Point(212, 26);
            this.progressBar1.Maximum = 2;
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(147, 15);
            this.progressBar1.TabIndex = 17;
            //
            // btnReconnect
            //
            this.btnReconnect.AccessibleName = "Reconnect Voice";
            this.btnReconnect.Location = new System.Drawing.Point(212, 47);
            this.btnReconnect.Name = "btnReconnect";
            this.btnReconnect.Size = new System.Drawing.Size(90, 23);
            this.btnReconnect.TabIndex = 18;
            this.btnReconnect.Text = "Reconnect";
            this.btnReconnect.UseVisualStyleBackColor = true;
            this.btnReconnect.Click += new System.EventHandler(this.btnReconnect_Click);
            //
            // label2
            //
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(11, 381);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(43, 26);
            this.label2.TabIndex = 16;
            this.label2.Text = "My Talk\r\nLevel";
            this.label2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // micLevel
            //
            this.micLevel.AccessibleName = "Talk Level Meter";
            this.micLevel.BackColor = System.Drawing.SystemColors.Control;
            this.micLevel.Enabled = false;
            this.micLevel.LargeChange = 20;
            this.micLevel.Location = new System.Drawing.Point(14, 274);
            this.micLevel.Maximum = 100;
            this.micLevel.Minimum = 0;
            this.micLevel.Name = "micLevel";
            this.micLevel.Orientation = System.Windows.Forms.Orientation.Vertical;
            this.micLevel.Size = new System.Drawing.Size(45, 104);
            this.micLevel.TabIndex = 15;
            this.micLevel.TickFrequency = 20;
            //
            // spkrLevel
            //
            this.spkrLevel.AccessibleName = "Listen Level";
            this.spkrLevel.Location = new System.Drawing.Point(405, 274);
            this.spkrLevel.Maximum = 100;
            this.spkrLevel.Minimum = 0;
            this.spkrLevel.Name = "spkrLevel";
            this.spkrLevel.Orientation = System.Windows.Forms.Orientation.Vertical;
            this.spkrLevel.Size = new System.Drawing.Size(45, 104);
            this.spkrLevel.TabIndex = 14;
            this.spkrLevel.TickFrequency = 20;
            this.spkrLevel.Value = 80;
            this.spkrLevel.ValueChanged += new System.EventHandler(this.spkrLevel_ValueChanged);
            //
            // spkrDevice
            //
            this.spkrDevice.AccessibleName = "Listen Device Selector";
            this.spkrDevice.FormattingEnabled = true;
            this.spkrDevice.Location = new System.Drawing.Point(158, 112);
            this.spkrDevice.Name = "spkrDevice";
            this.spkrDevice.Size = new System.Drawing.Size(276, 21);
            this.spkrDevice.TabIndex = 12;
            this.spkrDevice.SelectedIndexChanged += new System.EventHandler(this.spkrDevice_SelectedIndexChanged);
            //
            // micDevice
            //
            this.micDevice.AccessibleName = "Talk Device Selector";
            this.micDevice.FormattingEnabled = true;
            this.micDevice.Location = new System.Drawing.Point(158, 67);
            this.micDevice.Name = "micDevice";
            this.micDevice.Size = new System.Drawing.Size(276, 21);
            this.micDevice.TabIndex = 11;
            this.micDevice.SelectedIndexChanged += new System.EventHandler(this.micDevice_SelectedIndexChanged);
            //
            // chkVoiceEnable
            //
            this.chkVoiceEnable.AutoSize = true;
            this.chkVoiceEnable.Location = new System.Drawing.Point(14, 10);
            this.chkVoiceEnable.Name = "chkVoiceEnable";
            this.chkVoiceEnable.Size = new System.Drawing.Size(86, 17);
            this.chkVoiceEnable.TabIndex = 0;
            this.chkVoiceEnable.Text = "Enable voice";
            this.chkVoiceEnable.UseVisualStyleBackColor = true;
            this.chkVoiceEnable.Click += new System.EventHandler(this.chkVoiceEnable_Click);
            //
            // chkAutoConnect
            //
            this.chkAutoConnect.AccessibleName = "Auto Connect On Login";
            this.chkAutoConnect.AutoSize = true;
            this.chkAutoConnect.Location = new System.Drawing.Point(14, 140);
            this.chkAutoConnect.Name = "chkAutoConnect";
            this.chkAutoConnect.Size = new System.Drawing.Size(133, 17);
            this.chkAutoConnect.TabIndex = 31;
            this.chkAutoConnect.Text = "Auto-connect on login";
            this.chkAutoConnect.UseVisualStyleBackColor = true;
            this.chkAutoConnect.CheckedChanged += new System.EventHandler(this.chkAutoConnect_CheckedChanged);
            //
            // chkPushToTalk
            //
            this.chkPushToTalk.AccessibleName = "Push To Talk Enable";
            this.chkPushToTalk.AutoSize = true;
            this.chkPushToTalk.Location = new System.Drawing.Point(14, 165);
            this.chkPushToTalk.Name = "chkPushToTalk";
            this.chkPushToTalk.Size = new System.Drawing.Size(190, 17);
            this.chkPushToTalk.TabIndex = 32;
            this.chkPushToTalk.Text = "Push-to-talk (hold key to talk)";
            this.chkPushToTalk.UseVisualStyleBackColor = true;
            this.chkPushToTalk.CheckedChanged += new System.EventHandler(this.chkPushToTalk_CheckedChanged);
            //
            // labelPttKey
            //
            this.labelPttKey.AutoSize = true;
            this.labelPttKey.Location = new System.Drawing.Point(14, 191);
            this.labelPttKey.Name = "labelPttKey";
            this.labelPttKey.Size = new System.Drawing.Size(50, 13);
            this.labelPttKey.TabIndex = 33;
            this.labelPttKey.Text = "PTT key:";
            //
            // pttKeyCombo
            //
            this.pttKeyCombo.AccessibleName = "Push To Talk Key Selector";
            this.pttKeyCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.pttKeyCombo.FormattingEnabled = true;
            this.pttKeyCombo.Location = new System.Drawing.Point(90, 188);
            this.pttKeyCombo.Name = "pttKeyCombo";
            this.pttKeyCombo.Size = new System.Drawing.Size(120, 21);
            this.pttKeyCombo.TabIndex = 34;
            this.pttKeyCombo.SelectedIndexChanged += new System.EventHandler(this.pttKeyCombo_SelectedIndexChanged);
            //
            // participants
            //
            this.participants.AllowDrop = true;
            this.participants.ContextMenuStrip = this.avatarContext;
            this.participants.Dock = System.Windows.Forms.DockStyle.Fill;
            this.participants.FullRowSelect = true;
            this.participants.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None;
            this.participants.HideSelection = false;
            this.participants.LabelWrap = false;
            this.participants.Location = new System.Drawing.Point(0, 0);
            this.participants.MultiSelect = false;
            this.participants.Name = "participants";
            this.participants.Size = new System.Drawing.Size(188, 424);
            this.participants.Sorting = System.Windows.Forms.SortOrder.Ascending;
            this.participants.StateImageList = this.TalkStates;
            this.participants.TabIndex = 8;
            this.participants.UseCompatibleStateImageBehavior = false;
            this.participants.View = System.Windows.Forms.View.List;
            this.participants.MouseUp += new System.Windows.Forms.MouseEventHandler(this.participants_MouseUp);
            //
            // avatarContext
            //
            this.avatarContext.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ctxProfile,
            this.ctxPay,
            this.ctxStartIM,
            this.ctxFollow,
            this.ctxTextures,
            this.ctxAttach,
            this.ctxMaster,
            this.ctxAnim,
            this.ctxPoint,
            this.ctxSource});
            this.avatarContext.Name = "avatarContext";
            this.avatarContext.Size = new System.Drawing.Size(68, 224);
            //
            // ctxProfile
            //
            this.ctxProfile.Name = "ctxProfile";
            this.ctxProfile.Size = new System.Drawing.Size(67, 22);
            //
            // ctxPay
            //
            this.ctxPay.Name = "ctxPay";
            this.ctxPay.Size = new System.Drawing.Size(67, 22);
            //
            // ctxStartIM
            //
            this.ctxStartIM.Name = "ctxStartIM";
            this.ctxStartIM.Size = new System.Drawing.Size(67, 22);
            //
            // ctxFollow
            //
            this.ctxFollow.Name = "ctxFollow";
            this.ctxFollow.Size = new System.Drawing.Size(67, 22);
            //
            // ctxTextures
            //
            this.ctxTextures.Name = "ctxTextures";
            this.ctxTextures.Size = new System.Drawing.Size(67, 22);
            //
            // ctxAttach
            //
            this.ctxAttach.Name = "ctxAttach";
            this.ctxAttach.Size = new System.Drawing.Size(67, 22);
            //
            // ctxMaster
            //
            this.ctxMaster.Name = "ctxMaster";
            this.ctxMaster.Size = new System.Drawing.Size(67, 22);
            //
            // ctxAnim
            //
            this.ctxAnim.Name = "ctxAnim";
            this.ctxAnim.Size = new System.Drawing.Size(67, 22);
            //
            // ctxPoint
            //
            this.ctxPoint.Name = "ctxPoint";
            this.ctxPoint.Size = new System.Drawing.Size(67, 22);
            //
            // ctxSource
            //
            this.ctxSource.Name = "ctxSource";
            this.ctxSource.Size = new System.Drawing.Size(67, 22);
            //
            // TalkStates
            //
            this.TalkStates.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("TalkStates.ImageStream")));
            this.TalkStates.TransparentColor = System.Drawing.Color.Transparent;
            this.TalkStates.Images.SetKeyName(0, "TalkIdle.png");
            this.TalkStates.Images.SetKeyName(1, "Talking.png");
            this.TalkStates.Images.SetKeyName(2, "TalkMute.png");
            //
            // VoiceConsole
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.splitContainer1);
            this.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Name = "VoiceConsole";
            this.Size = new System.Drawing.Size(654, 424);
            this.Load += new System.EventHandler(this.VoiceConsole_Load);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel1.PerformLayout();
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.micLevel)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.spkrLevel)).EndInit();
            this.avatarContext.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        public ListViewNoFlicker participants;
        public SplitContainer splitContainer1;
        public RadegastContextMenuStrip avatarContext;
        public ToolStripMenuItem ctxProfile;
        public ToolStripMenuItem ctxStartIM;
        public ToolStripMenuItem ctxFollow;
        public ToolStripMenuItem ctxTextures;
        public ToolStripMenuItem ctxAttach;
        public ToolStripMenuItem ctxMaster;
        public ToolStripMenuItem ctxAnim;
        public ToolStripMenuItem ctxPoint;
        public ToolStripMenuItem ctxSource;
        public ToolStripMenuItem ctxPay;
        private ImageList TalkStates;
        private ComboBox micDevice;
        private ComboBox spkrDevice;
        private TrackBar spkrLevel;
        private Label label2;
        private TrackBar micLevel;
        private ProgressBar progressBar1;
        private PictureBox pictureBox2;
        private CheckBox micMute;
        private CheckBox spkrMute;
        private PictureBox pictureBox1;
        private Button button1;
        public CheckBox chkVoiceEnable;
        public CheckBox chkAutoConnect;
        public CheckBox chkPushToTalk;
        private Label labelPttKey;
        private ComboBox pttKeyCombo;
        private Label label1;
        private Label label5;
        private Label label4;
        private Label label3;
        private Button btnUnmuteAll;
        private Button btnMuteAll;
        private Button btnReconnect;
    }
}
