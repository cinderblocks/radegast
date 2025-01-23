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
using System.Windows.Forms;

namespace Radegast
{
    partial class ChatConsole
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
            this.btnSay = new System.Windows.Forms.Button();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.pnlMovement = new System.Windows.Forms.Panel();
            this.btnRun = new System.Windows.Forms.Button();
            this.btnFly = new System.Windows.Forms.Button();
            this.btnJump = new System.Windows.Forms.Button();
            this.btnCrouch = new System.Windows.Forms.Button();
            this.btnMoveBack = new System.Windows.Forms.Button();
            this.btnFwd = new System.Windows.Forms.Button();
            this.btnTurnRight = new System.Windows.Forms.Button();
            this.btnTurnLeft = new System.Windows.Forms.Button();
            this.pnlChatInput = new System.Windows.Forms.Panel();
            this.autoScrollCB = new System.Windows.Forms.CheckBox();
            this.cbChatType = new System.Windows.Forms.ComboBox();
            this.rtbChat = new Radegast.RRichTextBox();
            this.lvwObjects = new Radegast.ListViewNoFlicker();
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
            this.ctxOfferTP = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxTeleportTo = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxReqestLure = new System.Windows.Forms.ToolStripMenuItem();
            this.goToToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.faceToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxEject = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxBan = new System.Windows.Forms.ToolStripMenuItem();
            this.ctxEstateEject = new System.Windows.Forms.ToolStripMenuItem();
            this.muteToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.cbxInput = new Radegast.ChatInputBox();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.pnlMovement.SuspendLayout();
            this.pnlChatInput.SuspendLayout();
            this.avatarContext.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnSay
            // 
            this.btnSay.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSay.Enabled = false;
            this.btnSay.Location = new System.Drawing.Point(366, 0);
            this.btnSay.Name = "btnSay";
            this.btnSay.Size = new System.Drawing.Size(56, 24);
            this.btnSay.TabIndex = 10;
            this.btnSay.Text = "Say";
            this.btnSay.Click += new System.EventHandler(this.btnSay_Click);
            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 0);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.rtbChat);
            this.splitContainer1.Panel1.SizeChanged += new System.EventHandler(this.splitContainer1_Panel1_SizeChanged);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.lvwObjects);
            this.splitContainer1.Panel2.Controls.Add(this.pnlMovement);
            this.splitContainer1.Size = new System.Drawing.Size(576, 354);
            this.splitContainer1.SplitterDistance = 445;
            this.splitContainer1.TabIndex = 1;
            this.splitContainer1.TabStop = false;
            // 
            // pnlMovement
            // 
            this.pnlMovement.Controls.Add(this.btnRun);
            this.pnlMovement.Controls.Add(this.btnFly);
            this.pnlMovement.Controls.Add(this.btnJump);
            this.pnlMovement.Controls.Add(this.btnCrouch);
            this.pnlMovement.Controls.Add(this.btnMoveBack);
            this.pnlMovement.Controls.Add(this.btnFwd);
            this.pnlMovement.Controls.Add(this.btnTurnRight);
            this.pnlMovement.Controls.Add(this.btnTurnLeft);
            this.pnlMovement.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlMovement.Location = new System.Drawing.Point(0, 310);
            this.pnlMovement.Name = "pnlMovement";
            this.pnlMovement.Size = new System.Drawing.Size(127, 44);
            this.pnlMovement.TabIndex = 11;
            // 
            // btnRun
            // 
            this.btnRun.AccessibleDescription = "";
            this.btnRun.AccessibleName = "Run";
            this.btnRun.Font = new System.Drawing.Font("Tahoma", 6F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnRun.Location = new System.Drawing.Point(64, 3);
            this.btnRun.Margin = new System.Windows.Forms.Padding(0);
            this.btnRun.Name = "btnRun";
            this.btnRun.Size = new System.Drawing.Size(29, 19);
            this.btnRun.TabIndex = 9;
            this.btnRun.TabStop = false;
            this.btnRun.Text = "🏃";
            this.btnRun.Click += new System.EventHandler(this.btnRun_Click);
            // 
            // btnFly
            // 
            this.btnFly.AccessibleDescription = "";
            this.btnFly.AccessibleName = "Fly";
            this.btnFly.Font = new System.Drawing.Font("Tahoma", 6F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnFly.Location = new System.Drawing.Point(6, 3);
            this.btnFly.Margin = new System.Windows.Forms.Padding(0);
            this.btnFly.Name = "btnFly";
            this.btnFly.Size = new System.Drawing.Size(29, 19);
            this.btnFly.TabIndex = 8;
            this.btnFly.TabStop = false;
            this.btnFly.Text = "✈️";
            this.btnFly.Click += new System.EventHandler(this.btnFly_Click);
            // 
            // btnJump
            // 
            this.btnJump.AccessibleDescription = "";
            this.btnJump.AccessibleName = "Jump/Up";
            this.btnJump.Font = new System.Drawing.Font("Tahoma", 6F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnJump.Location = new System.Drawing.Point(93, 3);
            this.btnJump.Margin = new System.Windows.Forms.Padding(0);
            this.btnJump.Name = "btnJump";
            this.btnJump.Size = new System.Drawing.Size(29, 19);
            this.btnJump.TabIndex = 6;
            this.btnJump.TabStop = false;
            this.btnJump.Text = "↑";
            this.btnJump.MouseDown += new System.Windows.Forms.MouseEventHandler(this.btnMoveUp_MouseDown);
            this.btnJump.MouseUp += new System.Windows.Forms.MouseEventHandler(this.btnMoveUp_MouseUp);
            // 
            // btnCrouch
            // 
            this.btnCrouch.AccessibleDescription = "";
            this.btnCrouch.AccessibleName = "Crouch/Down";
            this.btnCrouch.Font = new System.Drawing.Font("Tahoma", 6F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnCrouch.Location = new System.Drawing.Point(93, 22);
            this.btnCrouch.Margin = new System.Windows.Forms.Padding(0);
            this.btnCrouch.Name = "btnCrouch";
            this.btnCrouch.Size = new System.Drawing.Size(29, 19);
            this.btnCrouch.TabIndex = 5;
            this.btnCrouch.TabStop = false;
            this.btnCrouch.Text = "↓";
            this.btnCrouch.MouseDown += new System.Windows.Forms.MouseEventHandler(this.btnMoveDown_MouseDown);
            this.btnCrouch.MouseUp += new System.Windows.Forms.MouseEventHandler(this.btnMoveDown_MouseUp);
            // 
            // btnMoveBack
            // 
            this.btnMoveBack.AccessibleDescription = "";
            this.btnMoveBack.AccessibleName = "Walk backwards";
            this.btnMoveBack.Font = new System.Drawing.Font("Tahoma", 6F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnMoveBack.Location = new System.Drawing.Point(35, 22);
            this.btnMoveBack.Margin = new System.Windows.Forms.Padding(0);
            this.btnMoveBack.Name = "btnMoveBack";
            this.btnMoveBack.Size = new System.Drawing.Size(29, 19);
            this.btnMoveBack.TabIndex = 2;
            this.btnMoveBack.TabStop = false;
            this.btnMoveBack.Text = "▼";
            this.btnMoveBack.MouseDown += new System.Windows.Forms.MouseEventHandler(this.btnMoveBack_MouseDown);
            this.btnMoveBack.MouseUp += new System.Windows.Forms.MouseEventHandler(this.btnMoveBack_MouseUp);
            // 
            // btnFwd
            // 
            this.btnFwd.AccessibleDescription = "";
            this.btnFwd.AccessibleName = "Walk forward";
            this.btnFwd.Font = new System.Drawing.Font("Tahoma", 6F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnFwd.Location = new System.Drawing.Point(35, 3);
            this.btnFwd.Margin = new System.Windows.Forms.Padding(0);
            this.btnFwd.Name = "btnFwd";
            this.btnFwd.Size = new System.Drawing.Size(29, 19);
            this.btnFwd.TabIndex = 1;
            this.btnFwd.TabStop = false;
            this.btnFwd.Text = "▲";
            this.btnFwd.MouseDown += new System.Windows.Forms.MouseEventHandler(this.btnFwd_MouseDown);
            this.btnFwd.MouseUp += new System.Windows.Forms.MouseEventHandler(this.btnFwd_MouseUp);
            // 
            // btnTurnRight
            // 
            this.btnTurnRight.AccessibleDescription = "";
            this.btnTurnRight.AccessibleName = "Turn right";
            this.btnTurnRight.Font = new System.Drawing.Font("Tahoma", 6F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnTurnRight.Location = new System.Drawing.Point(64, 22);
            this.btnTurnRight.Margin = new System.Windows.Forms.Padding(0);
            this.btnTurnRight.Name = "btnTurnRight";
            this.btnTurnRight.Size = new System.Drawing.Size(29, 19);
            this.btnTurnRight.TabIndex = 4;
            this.btnTurnRight.TabStop = false;
            this.btnTurnRight.Text = "►";
            this.btnTurnRight.MouseDown += new System.Windows.Forms.MouseEventHandler(this.btnTurnRight_MouseDown);
            this.btnTurnRight.MouseUp += new System.Windows.Forms.MouseEventHandler(this.btnTurnRight_MouseUp);
            // 
            // btnTurnLeft
            // 
            this.btnTurnLeft.AccessibleDescription = "";
            this.btnTurnLeft.AccessibleName = "Turn left";
            this.btnTurnLeft.Font = new System.Drawing.Font("Tahoma", 6F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnTurnLeft.Location = new System.Drawing.Point(6, 22);
            this.btnTurnLeft.Margin = new System.Windows.Forms.Padding(0);
            this.btnTurnLeft.Name = "btnTurnLeft";
            this.btnTurnLeft.Size = new System.Drawing.Size(29, 19);
            this.btnTurnLeft.TabIndex = 3;
            this.btnTurnLeft.TabStop = false;
            this.btnTurnLeft.Text = "◄";
            this.btnTurnLeft.MouseDown += new System.Windows.Forms.MouseEventHandler(this.btnTurnLeft_MouseDown);
            this.btnTurnLeft.MouseUp += new System.Windows.Forms.MouseEventHandler(this.btnTurnLeft_MouseUp);
            // 
            // pnlChatInput
            // 
            this.pnlChatInput.Controls.Add(this.autoScrollCB);
            this.pnlChatInput.Controls.Add(this.cbChatType);
            this.pnlChatInput.Controls.Add(this.cbxInput);
            this.pnlChatInput.Controls.Add(this.btnSay);
            this.pnlChatInput.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlChatInput.Location = new System.Drawing.Point(0, 354);
            this.pnlChatInput.Name = "pnlChatInput";
            this.pnlChatInput.Size = new System.Drawing.Size(576, 24);
            this.pnlChatInput.TabIndex = 0;
            // 
            // autoScrollCB
            // 
            this.autoScrollCB.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.autoScrollCB.AutoSize = true;
            this.autoScrollCB.Checked = true;
            this.autoScrollCB.CheckState = System.Windows.Forms.CheckState.Checked;
            this.autoScrollCB.Location = new System.Drawing.Point(507, 5);
            this.autoScrollCB.Name = "autoScrollCB";
            this.autoScrollCB.Size = new System.Drawing.Size(73, 17);
            this.autoScrollCB.TabIndex = 12;
            this.autoScrollCB.Text = "Autoscroll";
            this.autoScrollCB.UseVisualStyleBackColor = true;
            // 
            // cbChatType
            // 
            this.cbChatType.AccessibleName = "Chat type";
            this.cbChatType.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cbChatType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbChatType.Enabled = false;
            this.cbChatType.FormattingEnabled = true;
            this.cbChatType.Items.AddRange(new object[] {
            "Whisper",
            "Normal",
            "Shout"});
            this.cbChatType.Location = new System.Drawing.Point(428, 2);
            this.cbChatType.Name = "cbChatType";
            this.cbChatType.Size = new System.Drawing.Size(73, 21);
            this.cbChatType.TabIndex = 11;
            // 
            // rtbChat
            // 
            this.rtbChat.AccessibleName = "Chat history";
            this.rtbChat.BackColor = System.Drawing.SystemColors.Window;
            this.rtbChat.DetectUrls = false;
            this.rtbChat.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rtbChat.HideSelection = false;
            this.rtbChat.Location = new System.Drawing.Point(0, 0);
            this.rtbChat.Name = "rtbChat";
            this.rtbChat.ReadOnly = true;
            this.rtbChat.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
            this.rtbChat.Size = new System.Drawing.Size(445, 354);
            this.rtbChat.TabIndex = 4;
            this.rtbChat.Text = "";
            this.rtbChat.LinkClicked += new System.Windows.Forms.LinkClickedEventHandler(this.rtbChat_LinkClicked);
            // 
            // lvwObjects
            // 
            this.lvwObjects.AccessibleName = "Nearby people";
            this.lvwObjects.AllowDrop = true;
            this.lvwObjects.ContextMenuStrip = this.avatarContext;
            this.lvwObjects.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lvwObjects.FullRowSelect = true;
            this.lvwObjects.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None;
            this.lvwObjects.HideSelection = false;
            this.lvwObjects.LabelWrap = false;
            this.lvwObjects.Location = new System.Drawing.Point(0, 0);
            this.lvwObjects.MultiSelect = false;
            this.lvwObjects.Name = "lvwObjects";
            this.lvwObjects.Size = new System.Drawing.Size(127, 310);
            this.lvwObjects.TabIndex = 0;
            this.lvwObjects.UseCompatibleStateImageBehavior = false;
            this.lvwObjects.View = System.Windows.Forms.View.List;
            this.lvwObjects.SelectedIndexChanged += new System.EventHandler(this.lvwObjects_SelectedIndexChanged);
            this.lvwObjects.DragDrop += new System.Windows.Forms.DragEventHandler(this.lvwObjects_DragDrop);
            this.lvwObjects.DragOver += new System.Windows.Forms.DragEventHandler(this.lvwObjects_DragOver);
            this.lvwObjects.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.lvwObjects_MouseDoubleClick);
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
            this.ctxOfferTP,
            this.ctxTeleportTo,
            this.ctxReqestLure,
            this.goToToolStripMenuItem,
            this.faceToolStripMenuItem,
            this.ctxEject,
            this.ctxBan,
            this.ctxEstateEject,
            this.muteToolStripMenuItem});
            this.avatarContext.Name = "avatarContext";
            this.avatarContext.Size = new System.Drawing.Size(163, 400);
            this.avatarContext.Opening += new System.ComponentModel.CancelEventHandler(this.avatarContext_Opening);
            // 
            // ctxProfile
            // 
            this.ctxProfile.Name = "ctxProfile";
            this.ctxProfile.Size = new System.Drawing.Size(162, 22);
            this.ctxProfile.Text = "Profile";
            this.ctxProfile.Click += new System.EventHandler(this.tbtnProfile_Click);
            // 
            // ctxPay
            // 
            this.ctxPay.Enabled = false;
            this.ctxPay.Name = "ctxPay";
            this.ctxPay.Size = new System.Drawing.Size(162, 22);
            this.ctxPay.Text = "Pay";
            this.ctxPay.Click += new System.EventHandler(this.ctxPay_Click);
            // 
            // ctxStartIM
            // 
            this.ctxStartIM.Name = "ctxStartIM";
            this.ctxStartIM.Size = new System.Drawing.Size(162, 22);
            this.ctxStartIM.Text = "Start IM";
            this.ctxStartIM.Click += new System.EventHandler(this.tbtnStartIM_Click);
            // 
            // ctxFollow
            // 
            this.ctxFollow.Name = "ctxFollow";
            this.ctxFollow.Size = new System.Drawing.Size(162, 22);
            this.ctxFollow.Text = "Follow";
            this.ctxFollow.Click += new System.EventHandler(this.tbtnFollow_Click);
            // 
            // ctxTextures
            // 
            this.ctxTextures.Name = "ctxTextures";
            this.ctxTextures.Size = new System.Drawing.Size(162, 22);
            this.ctxTextures.Text = "Textures";
            this.ctxTextures.Click += new System.EventHandler(this.dumpOufitBtn_Click);
            // 
            // ctxAttach
            // 
            this.ctxAttach.Name = "ctxAttach";
            this.ctxAttach.Size = new System.Drawing.Size(162, 22);
            this.ctxAttach.Text = "Attachments";
            this.ctxAttach.Click += new System.EventHandler(this.tbtnAttach_Click);
            // 
            // ctxMaster
            // 
            this.ctxMaster.Name = "ctxMaster";
            this.ctxMaster.Size = new System.Drawing.Size(162, 22);
            this.ctxMaster.Text = "Master controls";
            this.ctxMaster.Click += new System.EventHandler(this.tbtnMaster_Click);
            // 
            // ctxAnim
            // 
            this.ctxAnim.Name = "ctxAnim";
            this.ctxAnim.Size = new System.Drawing.Size(162, 22);
            this.ctxAnim.Text = "Animations";
            this.ctxAnim.Click += new System.EventHandler(this.tbtnAnim_Click);
            // 
            // ctxPoint
            // 
            this.ctxPoint.Name = "ctxPoint";
            this.ctxPoint.Size = new System.Drawing.Size(162, 22);
            this.ctxPoint.Text = "Point at";
            this.ctxPoint.Click += new System.EventHandler(this.ctxPoint_Click);
            // 
            // ctxOfferTP
            // 
            this.ctxOfferTP.Name = "ctxOfferTP";
            this.ctxOfferTP.Size = new System.Drawing.Size(162, 22);
            this.ctxOfferTP.Text = "Offer Teleport";
            this.ctxOfferTP.ToolTipText = " Offer Teleport ";
            this.ctxOfferTP.Click += new System.EventHandler(this.ctxOfferTP_Click);
            // 
            // ctxTeleportTo
            // 
            this.ctxTeleportTo.Name = "ctxTeleportTo";
            this.ctxTeleportTo.Size = new System.Drawing.Size(162, 22);
            this.ctxTeleportTo.Text = "Teleport To";
            this.ctxTeleportTo.ToolTipText = " Teleport To ";
            this.ctxTeleportTo.Click += new System.EventHandler(this.ctxTeleportTo_Click);
            // 
            // ctxReqestLure
            // 
            this.ctxReqestLure.Name = "ctxReqestLure";
            this.ctxReqestLure.Size = new System.Drawing.Size(162, 22);
            this.ctxReqestLure.Text = "Request Teleport";
            this.ctxReqestLure.ToolTipText = " Request Teleport ";
            this.ctxReqestLure.Click += new System.EventHandler(this.ctxReqestLure_Click);
            // 
            // goToToolStripMenuItem
            // 
            this.goToToolStripMenuItem.Name = "goToToolStripMenuItem";
            this.goToToolStripMenuItem.Size = new System.Drawing.Size(162, 22);
            this.goToToolStripMenuItem.Text = "Walk To";
            this.goToToolStripMenuItem.ToolTipText = " Walk To ";
            this.goToToolStripMenuItem.Click += new System.EventHandler(this.goToToolStripMenuItem_Click);
            // 
            // faceToolStripMenuItem
            // 
            this.faceToolStripMenuItem.Name = "faceToolStripMenuItem";
            this.faceToolStripMenuItem.Size = new System.Drawing.Size(162, 22);
            this.faceToolStripMenuItem.Text = "Face";
            this.faceToolStripMenuItem.ToolTipText = " Face Avatar";
            this.faceToolStripMenuItem.Click += new System.EventHandler(this.faceToolStripMenuItem_Click);
            // 
            // ctxEject
            // 
            this.ctxEject.Name = "ctxEject";
            this.ctxEject.Size = new System.Drawing.Size(162, 22);
            this.ctxEject.Text = "Eject";
            this.ctxEject.ToolTipText = " Eject ";
            this.ctxEject.Click += new System.EventHandler(this.ctxEject_Click);
            // 
            // ctxBan
            // 
            this.ctxBan.Name = "ctxBan";
            this.ctxBan.Size = new System.Drawing.Size(162, 22);
            this.ctxBan.Text = "Ban";
            this.ctxBan.ToolTipText = " Ban ";
            this.ctxBan.Click += new System.EventHandler(this.ctxBan_Click);
            // 
            // ctxEstateEject
            // 
            this.ctxEstateEject.Name = "ctxEstateEject";
            this.ctxEstateEject.Size = new System.Drawing.Size(162, 22);
            this.ctxEstateEject.Text = "Eject from estate";
            this.ctxEstateEject.ToolTipText = " Eject from estate ";
            this.ctxEstateEject.Click += new System.EventHandler(this.ctxEstateEject_Click);
            // 
            // muteToolStripMenuItem
            // 
            this.muteToolStripMenuItem.Name = "muteToolStripMenuItem";
            this.muteToolStripMenuItem.Size = new System.Drawing.Size(162, 22);
            this.muteToolStripMenuItem.Text = "Mute";
            this.muteToolStripMenuItem.ToolTipText = " Mute ";
            this.muteToolStripMenuItem.Click += new System.EventHandler(this.muteToolStripMenuItem_Click);
            // 
            // cbxInput
            // 
            this.cbxInput.AccessibleName = "Chat input";
            this.cbxInput.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.cbxInput.Enabled = false;
            this.cbxInput.Location = new System.Drawing.Point(0, 0);
            this.cbxInput.Name = "cbxInput";
            this.cbxInput.Size = new System.Drawing.Size(360, 21);
            this.cbxInput.TabIndex = 0;
            this.cbxInput.SizeChanged += new System.EventHandler(this.cbxInput_SizeChanged);
            this.cbxInput.TextChanged += new System.EventHandler(this.cbxInput_TextChanged);
            this.cbxInput.KeyDown += new System.Windows.Forms.KeyEventHandler(this.cbxInput_KeyDown);
            // 
            // ChatConsole
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.pnlChatInput);
            this.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Name = "ChatConsole";
            this.Size = new System.Drawing.Size(576, 378);
            this.VisibleChanged += new System.EventHandler(this.ChatConsole_VisibleChanged);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.pnlMovement.ResumeLayout(false);
            this.pnlChatInput.ResumeLayout(false);
            this.pnlChatInput.PerformLayout();
            this.avatarContext.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        public ListViewNoFlicker lvwObjects;
        public ChatInputBox cbxInput;
        public Button btnSay;
        public SplitContainer splitContainer1;
        public Panel pnlChatInput;
        public Panel pnlMovement;
        public Button btnTurnLeft;
        public Button btnTurnRight;
        public Button btnFwd;
        public Button btnMoveBack;
        public RadegastContextMenuStrip avatarContext;
        public ToolStripMenuItem ctxProfile;
        public ToolStripMenuItem ctxStartIM;
        public ToolStripMenuItem ctxFollow;
        public ToolStripMenuItem ctxTextures;
        public ToolStripMenuItem ctxAttach;
        public ToolStripMenuItem ctxMaster;
        public ToolStripMenuItem ctxAnim;
        public ToolStripMenuItem ctxPoint;
        public ToolStripMenuItem ctxPay;
        public ComboBox cbChatType;
        public RRichTextBox rtbChat;
        private ToolStripMenuItem ctxOfferTP;
        private ToolStripMenuItem ctxTeleportTo;
        private ToolStripMenuItem ctxEject;
        private ToolStripMenuItem ctxBan;
        private ToolStripMenuItem ctxEstateEject;
        private ToolStripMenuItem muteToolStripMenuItem;
        private ToolStripMenuItem goToToolStripMenuItem;
        private ToolStripMenuItem faceToolStripMenuItem;
        private ToolStripMenuItem ctxReqestLure;
        public Button btnJump;
        public Button btnCrouch;
        public Button btnFly;
        public Button btnRun;
        private CheckBox autoScrollCB;
    }
}
