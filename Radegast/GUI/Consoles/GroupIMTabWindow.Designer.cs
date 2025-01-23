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
// $Id$
//
namespace Radegast
{
    partial class GroupIMTabWindow
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
            this.rtbIMText = new Radegast.RRichTextBox();
            this.btnSend = new System.Windows.Forms.Button();
            this.chatSplit = new System.Windows.Forms.SplitContainer();
            this.Participants = new Radegast.ListViewNoFlicker();
            this.avatarContext = new Radegast.RadegastContextMenuStrip(this.components);
            this.ctxProfile = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxPay = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxStartIM = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxOfferTP = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxReqestLure = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxEject = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxBan = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxMute = new System.Windows.Forms.ToolStripMenuItem();
            this.btnShow = new System.Windows.Forms.Button();
            this.pnlChatInput = new System.Windows.Forms.Panel();
            this.cbxInput = new Radegast.ChatInputBox();
            this.autoScrollCB = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.chatSplit)).BeginInit();
            this.chatSplit.Panel1.SuspendLayout();
            this.chatSplit.Panel2.SuspendLayout();
            this.chatSplit.SuspendLayout();
            this.avatarContext.SuspendLayout();
            this.pnlChatInput.SuspendLayout();
            this.SuspendLayout();
            // 
            // rtbIMText
            // 
            this.rtbIMText.BackColor = System.Drawing.SystemColors.Window;
            this.rtbIMText.DetectUrls = false;
            this.rtbIMText.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rtbIMText.HideSelection = false;
            this.rtbIMText.Location = new System.Drawing.Point(0, 0);
            this.rtbIMText.Name = "rtbIMText";
            this.rtbIMText.ReadOnly = true;
            this.rtbIMText.Size = new System.Drawing.Size(377, 302);
            this.rtbIMText.TabIndex = 3;
            this.rtbIMText.Text = "";
            this.rtbIMText.LinkClicked += new System.Windows.Forms.LinkClickedEventHandler(this.rtbIMText_LinkClicked);
            // 
            // btnSend
            // 
            this.btnSend.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSend.Enabled = false;
            this.btnSend.Location = new System.Drawing.Point(328, 2);
            this.btnSend.Name = "btnSend";
            this.btnSend.Size = new System.Drawing.Size(45, 23);
            this.btnSend.TabIndex = 1;
            this.btnSend.Text = "Send";
            this.btnSend.UseVisualStyleBackColor = true;
            this.btnSend.Click += new System.EventHandler(this.btnSend_Click);
            // 
            // chatSplit
            // 
            this.chatSplit.Dock = System.Windows.Forms.DockStyle.Fill;
            this.chatSplit.Location = new System.Drawing.Point(0, 0);
            this.chatSplit.Name = "chatSplit";
            // 
            // chatSplit.Panel1
            // 
            this.chatSplit.Panel1.Controls.Add(this.rtbIMText);
            // 
            // chatSplit.Panel2
            // 
            this.chatSplit.Panel2.Controls.Add(this.Participants);
            this.chatSplit.Size = new System.Drawing.Size(500, 302);
            this.chatSplit.SplitterDistance = 377;
            this.chatSplit.TabIndex = 3;
            // 
            // Participants
            // 
            this.Participants.Activation = System.Windows.Forms.ItemActivation.OneClick;
            this.Participants.ContextMenuStrip = this.avatarContext;
            this.Participants.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Participants.HideSelection = false;
            this.Participants.Location = new System.Drawing.Point(0, 0);
            this.Participants.MultiSelect = false;
            this.Participants.Name = "Participants";
            this.Participants.ShowGroups = false;
            this.Participants.Size = new System.Drawing.Size(119, 302);
            this.Participants.Sorting = System.Windows.Forms.SortOrder.Ascending;
            this.Participants.TabIndex = 4;
            this.Participants.UseCompatibleStateImageBehavior = false;
            this.Participants.View = System.Windows.Forms.View.List;
            this.Participants.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.Participants_MouseDoubleClick);
            // 
            // avatarContext
            // 
            this.avatarContext.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ctxProfile,
            this.ctxPay,
            this.ctxStartIM,
            this.ctxOfferTP,
            this.ctxReqestLure,
            this.ctxEject,
            this.ctxBan,
            this.ctxMute});
            this.avatarContext.Name = "avatarContext";
            this.avatarContext.Size = new System.Drawing.Size(162, 180);
            this.avatarContext.Opening += new System.ComponentModel.CancelEventHandler(this.avatarContext_Opening);
            // 
            // ctxProfile
            // 
            this.ctxProfile.Name = "ctxProfile";
            this.ctxProfile.Size = new System.Drawing.Size(161, 22);
            this.ctxProfile.Text = "Profile";
            this.ctxProfile.ToolTipText = " Profile ";
            this.ctxProfile.Click += new System.EventHandler(this.ctxProfile_Click);
            // 
            // ctxPay
            // 
            this.ctxPay.Enabled = false;
            this.ctxPay.Name = "ctxPay";
            this.ctxPay.Size = new System.Drawing.Size(161, 22);
            this.ctxPay.Text = "Pay";
            this.ctxPay.ToolTipText = " Pay ";
            this.ctxPay.Click += new System.EventHandler(this.ctxPay_Click);
            // 
            // ctxStartIM
            // 
            this.ctxStartIM.Name = "ctxStartIM";
            this.ctxStartIM.Size = new System.Drawing.Size(161, 22);
            this.ctxStartIM.Text = "Start IM";
            this.ctxStartIM.ToolTipText = " Start IM ";
            this.ctxStartIM.Click += new System.EventHandler(this.ctxStartIM_Click);
            // 
            // ctxOfferTP
            // 
            this.ctxOfferTP.Name = "ctxOfferTP";
            this.ctxOfferTP.Size = new System.Drawing.Size(161, 22);
            this.ctxOfferTP.Text = "Offer Teleport";
            this.ctxOfferTP.ToolTipText = " Offer Teleport ";
            this.ctxOfferTP.Click += new System.EventHandler(this.ctxOfferTP_Click);
            // 
            // ctxReqestLure
            // 
            this.ctxReqestLure.Name = "ctxReqestLure";
            this.ctxReqestLure.Size = new System.Drawing.Size(161, 22);
            this.ctxReqestLure.Text = "Request Teleport";
            this.ctxReqestLure.ToolTipText = " Request Teleport ";
            this.ctxReqestLure.Click += new System.EventHandler(this.ctxReqestLure_Click);
            // 
            // ctxEject
            // 
            this.ctxEject.Name = "ctxEject";
            this.ctxEject.Size = new System.Drawing.Size(161, 22);
            this.ctxEject.Text = "Eject";
            this.ctxEject.ToolTipText = " Eject ";
            this.ctxEject.Click += new System.EventHandler(this.ctxEject_Click);
            // 
            // ctxBan
            // 
            this.ctxBan.Name = "ctxBan";
            this.ctxBan.Size = new System.Drawing.Size(161, 22);
            this.ctxBan.Text = "Ban";
            this.ctxBan.ToolTipText = " Ban ";
            this.ctxBan.Visible = false;
            this.ctxBan.Click += new System.EventHandler(this.ctxBan_Click);
            // 
            // ctxMute
            // 
            this.ctxMute.Name = "ctxMute";
            this.ctxMute.Size = new System.Drawing.Size(161, 22);
            this.ctxMute.Text = "Mute";
            this.ctxMute.ToolTipText = " Mute ";
            this.ctxMute.Click += new System.EventHandler(this.ctxMute_Click);
            // 
            // btnShow
            // 
            this.btnShow.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnShow.Location = new System.Drawing.Point(379, 2);
            this.btnShow.Name = "btnShow";
            this.btnShow.Size = new System.Drawing.Size(45, 23);
            this.btnShow.TabIndex = 2;
            this.btnShow.Text = "Show";
            this.btnShow.UseVisualStyleBackColor = true;
            this.btnShow.Click += new System.EventHandler(this.btnShow_Click);
            // 
            // pnlChatInput
            // 
            this.pnlChatInput.Controls.Add(this.autoScrollCB);
            this.pnlChatInput.Controls.Add(this.cbxInput);
            this.pnlChatInput.Controls.Add(this.btnSend);
            this.pnlChatInput.Controls.Add(this.btnShow);
            this.pnlChatInput.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlChatInput.Location = new System.Drawing.Point(0, 302);
            this.pnlChatInput.Name = "pnlChatInput";
            this.pnlChatInput.Size = new System.Drawing.Size(500, 28);
            this.pnlChatInput.TabIndex = 4;
            // 
            // cbxInput
            // 
            this.cbxInput.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.cbxInput.Location = new System.Drawing.Point(0, 4);
            this.cbxInput.Name = "cbxInput";
            this.cbxInput.Size = new System.Drawing.Size(322, 21);
            this.cbxInput.TabIndex = 0;
            this.cbxInput.SizeChanged += new System.EventHandler(this.cbxInput_SizeChanged);
            this.cbxInput.TextChanged += new System.EventHandler(this.cbxInput_TextChanged);
            this.cbxInput.KeyDown += new System.Windows.Forms.KeyEventHandler(this.cbxInput_KeyDown);
            // 
            // autoScrollCB
            // 
            this.autoScrollCB.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.autoScrollCB.AutoSize = true;
            this.autoScrollCB.Checked = true;
            this.autoScrollCB.CheckState = System.Windows.Forms.CheckState.Checked;
            this.autoScrollCB.Location = new System.Drawing.Point(430, 5);
            this.autoScrollCB.Name = "autoScrollCB";
            this.autoScrollCB.Size = new System.Drawing.Size(73, 17);
            this.autoScrollCB.TabIndex = 13;
            this.autoScrollCB.Text = "Autoscroll";
            this.autoScrollCB.UseVisualStyleBackColor = true;
            // 
            // GroupIMTabWindow
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.chatSplit);
            this.Controls.Add(this.pnlChatInput);
            this.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Name = "GroupIMTabWindow";
            this.Size = new System.Drawing.Size(500, 330);
            this.VisibleChanged += new System.EventHandler(this.cbxInput_VisibleChanged);
            this.chatSplit.Panel1.ResumeLayout(false);
            this.chatSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.chatSplit)).EndInit();
            this.chatSplit.ResumeLayout(false);
            this.avatarContext.ResumeLayout(false);
            this.pnlChatInput.ResumeLayout(false);
            this.pnlChatInput.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        public Radegast.RRichTextBox rtbIMText;
        public ChatInputBox cbxInput;
        public System.Windows.Forms.Button btnSend;
        public System.Windows.Forms.SplitContainer chatSplit;
        public ListViewNoFlicker Participants;
        public System.Windows.Forms.Button btnShow;
        private System.Windows.Forms.Panel pnlChatInput;
        public RadegastContextMenuStrip avatarContext;
        public System.Windows.Forms.ToolStripMenuItem ctxProfile;
        public System.Windows.Forms.ToolStripMenuItem ctxPay;
        public System.Windows.Forms.ToolStripMenuItem ctxStartIM;
        private System.Windows.Forms.ToolStripMenuItem ctxOfferTP;
        private System.Windows.Forms.ToolStripMenuItem ctxReqestLure;
        private System.Windows.Forms.ToolStripMenuItem ctxEject;
        private System.Windows.Forms.ToolStripMenuItem ctxBan;
        private System.Windows.Forms.ToolStripMenuItem ctxMute;
        private System.Windows.Forms.CheckBox autoScrollCB;
    }
}
