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
    partial class frmProfile
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmProfile));
            this.tabProfile = new System.Windows.Forms.TabControl();
            this.tpgProfile = new System.Windows.Forms.TabPage();
            this.btnRequestTeleport = new System.Windows.Forms.Button();
            this.btnGive = new System.Windows.Forms.Button();
            this.lvwGroups = new Radegast.ListViewNoFlicker();
            this.clGroupName = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.clGroupTitle = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.txtFullName = new Radegast.AgentNameTextBox();
            this.anPartner = new Radegast.AgentNameTextBox();
            this.btnIM = new System.Windows.Forms.Button();
            this.btnFriend = new System.Windows.Forms.Button();
            this.slPicPanel = new System.Windows.Forms.Panel();
            this.btnOfferTeleport = new System.Windows.Forms.Button();
            this.btnUnmute = new System.Windows.Forms.Button();
            this.btnMute = new System.Windows.Forms.Button();
            this.btnPay = new System.Windows.Forms.Button();
            this.rtbAccountInfo = new System.Windows.Forms.RichTextBox();
            this.rtbAbout = new System.Windows.Forms.RichTextBox();
            this.labelPartner = new System.Windows.Forms.Label();
            this.labelInfo = new System.Windows.Forms.Label();
            this.txtBornOn = new System.Windows.Forms.TextBox();
            this.labelBornOn = new System.Windows.Forms.Label();
            this.labelName = new System.Windows.Forms.Label();
            this.tpgWeb = new System.Windows.Forms.TabPage();
            this.pnlWeb = new System.Windows.Forms.Panel();
            this.btnWebOpen = new System.Windows.Forms.Button();
            this.btnWebView = new System.Windows.Forms.Button();
            this.txtWebURL = new System.Windows.Forms.TextBox();
            this.labelUrl = new System.Windows.Forms.Label();
            this.tabInterests = new System.Windows.Forms.TabPage();
            this.txtLanguages = new System.Windows.Forms.TextBox();
            this.txtSkills = new System.Windows.Forms.TextBox();
            this.txtWantTo = new System.Windows.Forms.TextBox();
            this.checkBoxEventPlanning = new System.Windows.Forms.CheckBox();
            this.checkBoxCustomCharacters = new System.Windows.Forms.CheckBox();
            this.checkBoxArchitecture = new System.Windows.Forms.CheckBox();
            this.checkBoxScripting = new System.Windows.Forms.CheckBox();
            this.checkBoxModeling = new System.Windows.Forms.CheckBox();
            this.checkBoxTextures = new System.Windows.Forms.CheckBox();
            this.checkBoxHire = new System.Windows.Forms.CheckBox();
            this.checkBoxBuy = new System.Windows.Forms.CheckBox();
            this.checkBoxBeHired = new System.Windows.Forms.CheckBox();
            this.checkBoxExplore = new System.Windows.Forms.CheckBox();
            this.checkBoxSell = new System.Windows.Forms.CheckBox();
            this.checkBoxGroup = new System.Windows.Forms.CheckBox();
            this.checkBoxMeet = new System.Windows.Forms.CheckBox();
            this.checkBoxBuild = new System.Windows.Forms.CheckBox();
            this.labelLanguages = new System.Windows.Forms.Label();
            this.labelSkills = new System.Windows.Forms.Label();
            this.labelWantTo = new System.Windows.Forms.Label();
            this.tbpPicks = new System.Windows.Forms.TabPage();
            this.pickDetailPanel = new System.Windows.Forms.Panel();
            this.pickDetail = new System.Windows.Forms.RichTextBox();
            this.pickLocation = new System.Windows.Forms.TextBox();
            this.picksLowerPanel = new System.Windows.Forms.Panel();
            this.btnDeletePick = new System.Windows.Forms.Button();
            this.btnShowOnMap = new System.Windows.Forms.Button();
            this.btnTeleport = new System.Windows.Forms.Button();
            this.pickTitle = new System.Windows.Forms.TextBox();
            this.pickPicturePanel = new System.Windows.Forms.Panel();
            this.pickListPanel = new System.Windows.Forms.Panel();
            this.btnNewPick = new System.Windows.Forms.Button();
            this.tpgFirstLife = new System.Windows.Forms.TabPage();
            this.txtUUID = new System.Windows.Forms.TextBox();
            this.labelKey = new System.Windows.Forms.Label();
            this.rlPicPanel = new System.Windows.Forms.Panel();
            this.rtbAboutFL = new System.Windows.Forms.RichTextBox();
            this.tabNotes = new System.Windows.Forms.TabPage();
            this.rtbNotes = new System.Windows.Forms.RichTextBox();
            this.labelNotes = new System.Windows.Forms.Label();
            this.btnClose = new System.Windows.Forms.Button();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.tabProfile.SuspendLayout();
            this.tpgProfile.SuspendLayout();
            this.tpgWeb.SuspendLayout();
            this.tabInterests.SuspendLayout();
            this.tbpPicks.SuspendLayout();
            this.pickDetailPanel.SuspendLayout();
            this.picksLowerPanel.SuspendLayout();
            this.pickListPanel.SuspendLayout();
            this.tpgFirstLife.SuspendLayout();
            this.tabNotes.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabProfile
            // 
            this.tabProfile.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tabProfile.Controls.Add(this.tpgProfile);
            this.tabProfile.Controls.Add(this.tpgWeb);
            this.tabProfile.Controls.Add(this.tabInterests);
            this.tabProfile.Controls.Add(this.tbpPicks);
            this.tabProfile.Controls.Add(this.tpgFirstLife);
            this.tabProfile.Controls.Add(this.tabNotes);
            this.tabProfile.Location = new System.Drawing.Point(12, 12);
            this.tabProfile.Name = "tabProfile";
            this.tabProfile.SelectedIndex = 0;
            this.tabProfile.Size = new System.Drawing.Size(468, 502);
            this.tabProfile.TabIndex = 0;
            this.tabProfile.SelectedIndexChanged += new System.EventHandler(this.tabProfile_SelectedIndexChanged);
            // 
            // tpgProfile
            // 
            this.tpgProfile.Controls.Add(this.btnRequestTeleport);
            this.tpgProfile.Controls.Add(this.btnGive);
            this.tpgProfile.Controls.Add(this.lvwGroups);
            this.tpgProfile.Controls.Add(this.txtFullName);
            this.tpgProfile.Controls.Add(this.anPartner);
            this.tpgProfile.Controls.Add(this.btnIM);
            this.tpgProfile.Controls.Add(this.btnFriend);
            this.tpgProfile.Controls.Add(this.slPicPanel);
            this.tpgProfile.Controls.Add(this.btnOfferTeleport);
            this.tpgProfile.Controls.Add(this.btnUnmute);
            this.tpgProfile.Controls.Add(this.btnMute);
            this.tpgProfile.Controls.Add(this.btnPay);
            this.tpgProfile.Controls.Add(this.rtbAccountInfo);
            this.tpgProfile.Controls.Add(this.rtbAbout);
            this.tpgProfile.Controls.Add(this.labelPartner);
            this.tpgProfile.Controls.Add(this.labelInfo);
            this.tpgProfile.Controls.Add(this.txtBornOn);
            this.tpgProfile.Controls.Add(this.labelBornOn);
            this.tpgProfile.Controls.Add(this.labelName);
            this.tpgProfile.Location = new System.Drawing.Point(4, 22);
            this.tpgProfile.Name = "tpgProfile";
            this.tpgProfile.Padding = new System.Windows.Forms.Padding(3);
            this.tpgProfile.Size = new System.Drawing.Size(460, 476);
            this.tpgProfile.TabIndex = 0;
            this.tpgProfile.Text = "Profile";
            this.tpgProfile.UseVisualStyleBackColor = true;
            // 
            // btnRequestTeleport
            // 
            this.btnRequestTeleport.AccessibleDescription = "Request this person to teleport you to their location";
            this.btnRequestTeleport.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnRequestTeleport.Location = new System.Drawing.Point(312, 447);
            this.btnRequestTeleport.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this.btnRequestTeleport.Name = "btnRequestTeleport";
            this.btnRequestTeleport.Size = new System.Drawing.Size(107, 23);
            this.btnRequestTeleport.TabIndex = 28;
            this.btnRequestTeleport.Text = "Request Teleport";
            this.btnRequestTeleport.UseVisualStyleBackColor = true;
            this.btnRequestTeleport.Click += new System.EventHandler(this.btnRequestTeleport_Click);
            // 
            // btnGive
            // 
            this.btnGive.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnGive.Enabled = false;
            this.btnGive.Location = new System.Drawing.Point(312, 418);
            this.btnGive.Name = "btnGive";
            this.btnGive.Size = new System.Drawing.Size(107, 23);
            this.btnGive.TabIndex = 24;
            this.btnGive.Text = "Give Inventory";
            this.btnGive.UseVisualStyleBackColor = true;
            this.btnGive.Click += new System.EventHandler(this.btnGive_Click);
            // 
            // lvwGroups
            // 
            this.lvwGroups.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lvwGroups.BackColor = System.Drawing.SystemColors.Window;
            this.lvwGroups.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.clGroupName,
            this.clGroupTitle});
            this.lvwGroups.FullRowSelect = true;
            this.lvwGroups.GridLines = true;
            this.lvwGroups.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
            this.lvwGroups.HideSelection = false;
            this.lvwGroups.LabelWrap = false;
            this.lvwGroups.Location = new System.Drawing.Point(9, 199);
            this.lvwGroups.MultiSelect = false;
            this.lvwGroups.Name = "lvwGroups";
            this.lvwGroups.ShowGroups = false;
            this.lvwGroups.Size = new System.Drawing.Size(445, 78);
            this.lvwGroups.Sorting = System.Windows.Forms.SortOrder.Ascending;
            this.lvwGroups.TabIndex = 5;
            this.lvwGroups.UseCompatibleStateImageBehavior = false;
            this.lvwGroups.View = System.Windows.Forms.View.Details;
            this.lvwGroups.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.lvwGroups_MouseDoubleClick);
            // 
            // clGroupName
            // 
            this.clGroupName.Text = "Group Name";
            this.clGroupName.Width = 212;
            // 
            // clGroupTitle
            // 
            this.clGroupTitle.Text = "Active Title";
            this.clGroupTitle.Width = 196;
            // 
            // txtFullName
            // 
            this.txtFullName.AccessibleName = "Name";
            this.txtFullName.AgentID = ((LibreMetaverse.UUID)(resources.GetObject("txtFullName.AgentID")));
            this.txtFullName.Location = new System.Drawing.Point(50, 6);
            this.txtFullName.Name = "txtFullName";
            this.txtFullName.ReadOnly = true;
            this.txtFullName.Size = new System.Drawing.Size(172, 21);
            this.txtFullName.TabIndex = 1;
            // 
            // anPartner
            // 
            this.anPartner.AccessibleName = "Partner";
            this.anPartner.AgentID = ((LibreMetaverse.UUID)(resources.GetObject("anPartner.AgentID")));
            this.anPartner.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.anPartner.Location = new System.Drawing.Point(306, 34);
            this.anPartner.Name = "anPartner";
            this.anPartner.ReadOnly = true;
            this.anPartner.Size = new System.Drawing.Size(148, 21);
            this.anPartner.TabIndex = 3;
            // 
            // btnIM
            // 
            this.btnIM.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnIM.Location = new System.Drawing.Point(108, 418);
            this.btnIM.Name = "btnIM";
            this.btnIM.Size = new System.Drawing.Size(96, 23);
            this.btnIM.TabIndex = 22;
            this.btnIM.Text = "Start IM";
            this.btnIM.UseVisualStyleBackColor = true;
            this.btnIM.Click += new System.EventHandler(this.btnIM_Click);
            // 
            // btnFriend
            // 
            this.btnFriend.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnFriend.Location = new System.Drawing.Point(210, 418);
            this.btnFriend.Name = "btnFriend";
            this.btnFriend.Size = new System.Drawing.Size(96, 23);
            this.btnFriend.TabIndex = 23;
            this.btnFriend.Text = "Add Friend";
            this.btnFriend.UseVisualStyleBackColor = true;
            this.btnFriend.Click += new System.EventHandler(this.btnFriend_Click);
            // 
            // slPicPanel
            // 
            this.slPicPanel.BackColor = System.Drawing.SystemColors.Control;
            this.slPicPanel.Location = new System.Drawing.Point(9, 33);
            this.slPicPanel.Name = "slPicPanel";
            this.slPicPanel.Size = new System.Drawing.Size(213, 160);
            this.slPicPanel.TabIndex = 17;
            // 
            // btnOfferTeleport
            // 
            this.btnOfferTeleport.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnOfferTeleport.Location = new System.Drawing.Point(6, 418);
            this.btnOfferTeleport.Name = "btnOfferTeleport";
            this.btnOfferTeleport.Size = new System.Drawing.Size(96, 23);
            this.btnOfferTeleport.TabIndex = 20;
            this.btnOfferTeleport.Text = "Offer Teleport";
            this.btnOfferTeleport.UseVisualStyleBackColor = true;
            this.btnOfferTeleport.Click += new System.EventHandler(this.btnOfferTeleport_Click);
            // 
            // btnUnmute
            // 
            this.btnUnmute.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnUnmute.Location = new System.Drawing.Point(210, 447);
            this.btnUnmute.Name = "btnUnmute";
            this.btnUnmute.Size = new System.Drawing.Size(96, 23);
            this.btnUnmute.TabIndex = 27;
            this.btnUnmute.Text = "Unmute";
            this.btnUnmute.UseVisualStyleBackColor = true;
            this.btnUnmute.Click += new System.EventHandler(this.btnUnmute_Click);
            // 
            // btnMute
            // 
            this.btnMute.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnMute.Location = new System.Drawing.Point(108, 447);
            this.btnMute.Name = "btnMute";
            this.btnMute.Size = new System.Drawing.Size(96, 23);
            this.btnMute.TabIndex = 26;
            this.btnMute.Text = "Mute";
            this.btnMute.UseVisualStyleBackColor = true;
            this.btnMute.Click += new System.EventHandler(this.btnMute_Click);
            // 
            // btnPay
            // 
            this.btnPay.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnPay.Location = new System.Drawing.Point(6, 447);
            this.btnPay.Name = "btnPay";
            this.btnPay.Size = new System.Drawing.Size(96, 23);
            this.btnPay.TabIndex = 25;
            this.btnPay.Text = "Pay...";
            this.btnPay.UseVisualStyleBackColor = true;
            this.btnPay.Click += new System.EventHandler(this.btnPay_Click);
            // 
            // rtbAccountInfo
            // 
            this.rtbAccountInfo.AccessibleName = "Info";
            this.rtbAccountInfo.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.rtbAccountInfo.Location = new System.Drawing.Point(306, 60);
            this.rtbAccountInfo.Name = "rtbAccountInfo";
            this.rtbAccountInfo.ReadOnly = true;
            this.rtbAccountInfo.Size = new System.Drawing.Size(148, 133);
            this.rtbAccountInfo.TabIndex = 4;
            this.rtbAccountInfo.Text = "";
            // 
            // rtbAbout
            // 
            this.rtbAbout.AccessibleName = "Profile details";
            this.rtbAbout.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.rtbAbout.Location = new System.Drawing.Point(6, 296);
            this.rtbAbout.Name = "rtbAbout";
            this.rtbAbout.ReadOnly = true;
            this.rtbAbout.Size = new System.Drawing.Size(448, 116);
            this.rtbAbout.TabIndex = 0;
            this.rtbAbout.Text = "";
            this.rtbAbout.LinkClicked += new System.Windows.Forms.LinkClickedEventHandler(this.rtbAbout_LinkClicked);
            this.rtbAbout.Leave += new System.EventHandler(this.rtbAbout_Leave);
            // 
            // labelPartner
            // 
            this.labelPartner.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.labelPartner.AutoSize = true;
            this.labelPartner.Location = new System.Drawing.Point(252, 36);
            this.labelPartner.Name = "labelPartner";
            this.labelPartner.Size = new System.Drawing.Size(47, 13);
            this.labelPartner.TabIndex = 9;
            this.labelPartner.Text = "Partner:";
            // 
            // labelInfo
            // 
            this.labelInfo.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.labelInfo.AutoSize = true;
            this.labelInfo.Location = new System.Drawing.Point(251, 63);
            this.labelInfo.Name = "labelInfo";
            this.labelInfo.Size = new System.Drawing.Size(31, 13);
            this.labelInfo.TabIndex = 7;
            this.labelInfo.Text = "Info:";
            // 
            // txtBornOn
            // 
            this.txtBornOn.AccessibleName = "Date of birth";
            this.txtBornOn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.txtBornOn.Location = new System.Drawing.Point(306, 6);
            this.txtBornOn.Name = "txtBornOn";
            this.txtBornOn.ReadOnly = true;
            this.txtBornOn.Size = new System.Drawing.Size(148, 21);
            this.txtBornOn.TabIndex = 2;
            // 
            // labelBornOn
            // 
            this.labelBornOn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.labelBornOn.AutoSize = true;
            this.labelBornOn.Location = new System.Drawing.Point(252, 9);
            this.labelBornOn.Name = "labelBornOn";
            this.labelBornOn.Size = new System.Drawing.Size(48, 13);
            this.labelBornOn.TabIndex = 3;
            this.labelBornOn.Text = "Born on:";
            // 
            // labelName
            // 
            this.labelName.AutoSize = true;
            this.labelName.Location = new System.Drawing.Point(6, 9);
            this.labelName.Name = "labelName";
            this.labelName.Size = new System.Drawing.Size(38, 13);
            this.labelName.TabIndex = 1;
            this.labelName.Text = "Name:";
            // 
            // tpgWeb
            // 
            this.tpgWeb.Controls.Add(this.pnlWeb);
            this.tpgWeb.Controls.Add(this.btnWebOpen);
            this.tpgWeb.Controls.Add(this.btnWebView);
            this.tpgWeb.Controls.Add(this.txtWebURL);
            this.tpgWeb.Controls.Add(this.labelUrl);
            this.tpgWeb.Location = new System.Drawing.Point(4, 22);
            this.tpgWeb.Name = "tpgWeb";
            this.tpgWeb.Padding = new System.Windows.Forms.Padding(3);
            this.tpgWeb.Size = new System.Drawing.Size(460, 476);
            this.tpgWeb.TabIndex = 1;
            this.tpgWeb.Text = "Web";
            this.tpgWeb.UseVisualStyleBackColor = true;
            // 
            // pnlWeb
            // 
            this.pnlWeb.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.pnlWeb.Location = new System.Drawing.Point(6, 33);
            this.pnlWeb.Name = "pnlWeb";
            this.pnlWeb.Size = new System.Drawing.Size(448, 418);
            this.pnlWeb.TabIndex = 4;
            // 
            // btnWebOpen
            // 
            this.btnWebOpen.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnWebOpen.Enabled = false;
            this.btnWebOpen.Location = new System.Drawing.Point(379, 4);
            this.btnWebOpen.Name = "btnWebOpen";
            this.btnWebOpen.Size = new System.Drawing.Size(75, 23);
            this.btnWebOpen.TabIndex = 3;
            this.btnWebOpen.Text = "Open";
            this.btnWebOpen.UseVisualStyleBackColor = true;
            this.btnWebOpen.Click += new System.EventHandler(this.btnWebOpen_Click);
            // 
            // btnWebView
            // 
            this.btnWebView.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnWebView.Enabled = false;
            this.btnWebView.Location = new System.Drawing.Point(298, 4);
            this.btnWebView.Name = "btnWebView";
            this.btnWebView.Size = new System.Drawing.Size(75, 23);
            this.btnWebView.TabIndex = 2;
            this.btnWebView.Text = "View";
            this.btnWebView.UseVisualStyleBackColor = true;
            this.btnWebView.Click += new System.EventHandler(this.btnWebView_Click);
            // 
            // txtWebURL
            // 
            this.txtWebURL.AccessibleName = "Web profile link";
            this.txtWebURL.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtWebURL.Location = new System.Drawing.Point(42, 6);
            this.txtWebURL.Name = "txtWebURL";
            this.txtWebURL.ReadOnly = true;
            this.txtWebURL.Size = new System.Drawing.Size(250, 21);
            this.txtWebURL.TabIndex = 1;
            this.txtWebURL.Leave += new System.EventHandler(this.txtWebURL_Leave);
            // 
            // labelUrl
            // 
            this.labelUrl.AutoSize = true;
            this.labelUrl.Location = new System.Drawing.Point(6, 9);
            this.labelUrl.Name = "labelUrl";
            this.labelUrl.Size = new System.Drawing.Size(30, 13);
            this.labelUrl.TabIndex = 0;
            this.labelUrl.Text = "URL:";
            // 
            // tabInterests
            // 
            this.tabInterests.Controls.Add(this.txtLanguages);
            this.tabInterests.Controls.Add(this.txtSkills);
            this.tabInterests.Controls.Add(this.txtWantTo);
            this.tabInterests.Controls.Add(this.checkBoxEventPlanning);
            this.tabInterests.Controls.Add(this.checkBoxCustomCharacters);
            this.tabInterests.Controls.Add(this.checkBoxArchitecture);
            this.tabInterests.Controls.Add(this.checkBoxScripting);
            this.tabInterests.Controls.Add(this.checkBoxModeling);
            this.tabInterests.Controls.Add(this.checkBoxTextures);
            this.tabInterests.Controls.Add(this.checkBoxHire);
            this.tabInterests.Controls.Add(this.checkBoxBuy);
            this.tabInterests.Controls.Add(this.checkBoxBeHired);
            this.tabInterests.Controls.Add(this.checkBoxExplore);
            this.tabInterests.Controls.Add(this.checkBoxSell);
            this.tabInterests.Controls.Add(this.checkBoxGroup);
            this.tabInterests.Controls.Add(this.checkBoxMeet);
            this.tabInterests.Controls.Add(this.checkBoxBuild);
            this.tabInterests.Controls.Add(this.labelLanguages);
            this.tabInterests.Controls.Add(this.labelSkills);
            this.tabInterests.Controls.Add(this.labelWantTo);
            this.tabInterests.Location = new System.Drawing.Point(4, 22);
            this.tabInterests.Name = "tabInterests";
            this.tabInterests.Padding = new System.Windows.Forms.Padding(3);
            this.tabInterests.Size = new System.Drawing.Size(460, 476);
            this.tabInterests.TabIndex = 5;
            this.tabInterests.Text = "Interests";
            this.tabInterests.UseVisualStyleBackColor = true;
            // 
            // txtLanguages
            // 
            this.txtLanguages.Location = new System.Drawing.Point(77, 271);
            this.txtLanguages.Name = "txtLanguages";
            this.txtLanguages.ReadOnly = true;
            this.txtLanguages.Size = new System.Drawing.Size(285, 21);
            this.txtLanguages.TabIndex = 19;
            this.txtLanguages.Leave += new System.EventHandler(this.interestsUpdated);
            // 
            // txtSkills
            // 
            this.txtSkills.Location = new System.Drawing.Point(77, 233);
            this.txtSkills.Name = "txtSkills";
            this.txtSkills.ReadOnly = true;
            this.txtSkills.Size = new System.Drawing.Size(285, 21);
            this.txtSkills.TabIndex = 18;
            this.txtSkills.Leave += new System.EventHandler(this.interestsUpdated);
            // 
            // txtWantTo
            // 
            this.txtWantTo.Location = new System.Drawing.Point(77, 118);
            this.txtWantTo.Name = "txtWantTo";
            this.txtWantTo.ReadOnly = true;
            this.txtWantTo.Size = new System.Drawing.Size(285, 21);
            this.txtWantTo.TabIndex = 17;
            this.txtWantTo.Leave += new System.EventHandler(this.interestsUpdated);
            // 
            // checkBoxEventPlanning
            // 
            this.checkBoxEventPlanning.AutoSize = true;
            this.checkBoxEventPlanning.Enabled = false;
            this.checkBoxEventPlanning.Location = new System.Drawing.Point(239, 187);
            this.checkBoxEventPlanning.Name = "checkBoxEventPlanning";
            this.checkBoxEventPlanning.Size = new System.Drawing.Size(97, 17);
            this.checkBoxEventPlanning.TabIndex = 16;
            this.checkBoxEventPlanning.Text = "Event Planning";
            this.checkBoxEventPlanning.UseVisualStyleBackColor = true;
            this.checkBoxEventPlanning.CheckedChanged += new System.EventHandler(this.interestsUpdated);
            // 
            // checkBoxCustomCharacters
            // 
            this.checkBoxCustomCharacters.AutoSize = true;
            this.checkBoxCustomCharacters.Enabled = false;
            this.checkBoxCustomCharacters.Location = new System.Drawing.Point(239, 210);
            this.checkBoxCustomCharacters.Name = "checkBoxCustomCharacters";
            this.checkBoxCustomCharacters.Size = new System.Drawing.Size(118, 17);
            this.checkBoxCustomCharacters.TabIndex = 15;
            this.checkBoxCustomCharacters.Text = "Custom Characters";
            this.checkBoxCustomCharacters.UseVisualStyleBackColor = true;
            this.checkBoxCustomCharacters.CheckedChanged += new System.EventHandler(this.interestsUpdated);
            // 
            // checkBoxArchitecture
            // 
            this.checkBoxArchitecture.AutoSize = true;
            this.checkBoxArchitecture.Enabled = false;
            this.checkBoxArchitecture.Location = new System.Drawing.Point(239, 164);
            this.checkBoxArchitecture.Name = "checkBoxArchitecture";
            this.checkBoxArchitecture.Size = new System.Drawing.Size(85, 17);
            this.checkBoxArchitecture.TabIndex = 14;
            this.checkBoxArchitecture.Text = "Architecture";
            this.checkBoxArchitecture.UseVisualStyleBackColor = true;
            this.checkBoxArchitecture.CheckedChanged += new System.EventHandler(this.interestsUpdated);
            // 
            // checkBoxScripting
            // 
            this.checkBoxScripting.AutoSize = true;
            this.checkBoxScripting.Enabled = false;
            this.checkBoxScripting.Location = new System.Drawing.Point(77, 210);
            this.checkBoxScripting.Name = "checkBoxScripting";
            this.checkBoxScripting.Size = new System.Drawing.Size(67, 17);
            this.checkBoxScripting.TabIndex = 13;
            this.checkBoxScripting.Text = "Scripting";
            this.checkBoxScripting.UseVisualStyleBackColor = true;
            this.checkBoxScripting.CheckedChanged += new System.EventHandler(this.interestsUpdated);
            // 
            // checkBoxModeling
            // 
            this.checkBoxModeling.AutoSize = true;
            this.checkBoxModeling.Enabled = false;
            this.checkBoxModeling.Location = new System.Drawing.Point(77, 187);
            this.checkBoxModeling.Name = "checkBoxModeling";
            this.checkBoxModeling.Size = new System.Drawing.Size(68, 17);
            this.checkBoxModeling.TabIndex = 12;
            this.checkBoxModeling.Text = "Modeling";
            this.checkBoxModeling.UseVisualStyleBackColor = true;
            this.checkBoxModeling.CheckedChanged += new System.EventHandler(this.interestsUpdated);
            // 
            // checkBoxTextures
            // 
            this.checkBoxTextures.AutoSize = true;
            this.checkBoxTextures.Enabled = false;
            this.checkBoxTextures.Location = new System.Drawing.Point(77, 164);
            this.checkBoxTextures.Name = "checkBoxTextures";
            this.checkBoxTextures.Size = new System.Drawing.Size(69, 17);
            this.checkBoxTextures.TabIndex = 11;
            this.checkBoxTextures.Text = "Textures";
            this.checkBoxTextures.UseVisualStyleBackColor = true;
            this.checkBoxTextures.CheckedChanged += new System.EventHandler(this.interestsUpdated);
            // 
            // checkBoxHire
            // 
            this.checkBoxHire.AutoSize = true;
            this.checkBoxHire.Enabled = false;
            this.checkBoxHire.Location = new System.Drawing.Point(239, 95);
            this.checkBoxHire.Name = "checkBoxHire";
            this.checkBoxHire.Size = new System.Drawing.Size(45, 17);
            this.checkBoxHire.TabIndex = 10;
            this.checkBoxHire.Text = "Hire";
            this.checkBoxHire.UseVisualStyleBackColor = true;
            this.checkBoxHire.CheckedChanged += new System.EventHandler(this.interestsUpdated);
            // 
            // checkBoxBuy
            // 
            this.checkBoxBuy.AutoSize = true;
            this.checkBoxBuy.Enabled = false;
            this.checkBoxBuy.Location = new System.Drawing.Point(239, 49);
            this.checkBoxBuy.Name = "checkBoxBuy";
            this.checkBoxBuy.Size = new System.Drawing.Size(44, 17);
            this.checkBoxBuy.TabIndex = 9;
            this.checkBoxBuy.Text = "Buy";
            this.checkBoxBuy.UseVisualStyleBackColor = true;
            this.checkBoxBuy.CheckedChanged += new System.EventHandler(this.interestsUpdated);
            // 
            // checkBoxBeHired
            // 
            this.checkBoxBeHired.AutoSize = true;
            this.checkBoxBeHired.Enabled = false;
            this.checkBoxBeHired.Location = new System.Drawing.Point(239, 72);
            this.checkBoxBeHired.Name = "checkBoxBeHired";
            this.checkBoxBeHired.Size = new System.Drawing.Size(66, 17);
            this.checkBoxBeHired.TabIndex = 8;
            this.checkBoxBeHired.Text = "Be Hired";
            this.checkBoxBeHired.UseVisualStyleBackColor = true;
            this.checkBoxBeHired.CheckedChanged += new System.EventHandler(this.interestsUpdated);
            // 
            // checkBoxExplore
            // 
            this.checkBoxExplore.AutoSize = true;
            this.checkBoxExplore.Enabled = false;
            this.checkBoxExplore.Location = new System.Drawing.Point(77, 95);
            this.checkBoxExplore.Name = "checkBoxExplore";
            this.checkBoxExplore.Size = new System.Drawing.Size(62, 17);
            this.checkBoxExplore.TabIndex = 7;
            this.checkBoxExplore.Text = "Explore";
            this.checkBoxExplore.UseVisualStyleBackColor = true;
            this.checkBoxExplore.CheckedChanged += new System.EventHandler(this.interestsUpdated);
            // 
            // checkBoxSell
            // 
            this.checkBoxSell.AutoSize = true;
            this.checkBoxSell.Enabled = false;
            this.checkBoxSell.Location = new System.Drawing.Point(239, 26);
            this.checkBoxSell.Name = "checkBoxSell";
            this.checkBoxSell.Size = new System.Drawing.Size(42, 17);
            this.checkBoxSell.TabIndex = 6;
            this.checkBoxSell.Text = "Sell";
            this.checkBoxSell.UseVisualStyleBackColor = true;
            this.checkBoxSell.CheckedChanged += new System.EventHandler(this.interestsUpdated);
            // 
            // checkBoxGroup
            // 
            this.checkBoxGroup.AutoSize = true;
            this.checkBoxGroup.Enabled = false;
            this.checkBoxGroup.Location = new System.Drawing.Point(77, 72);
            this.checkBoxGroup.Name = "checkBoxGroup";
            this.checkBoxGroup.Size = new System.Drawing.Size(55, 17);
            this.checkBoxGroup.TabIndex = 5;
            this.checkBoxGroup.Text = "Group";
            this.checkBoxGroup.UseVisualStyleBackColor = true;
            this.checkBoxGroup.CheckedChanged += new System.EventHandler(this.interestsUpdated);
            // 
            // checkBoxMeet
            // 
            this.checkBoxMeet.AutoSize = true;
            this.checkBoxMeet.Enabled = false;
            this.checkBoxMeet.Location = new System.Drawing.Point(77, 49);
            this.checkBoxMeet.Name = "checkBoxMeet";
            this.checkBoxMeet.Size = new System.Drawing.Size(50, 17);
            this.checkBoxMeet.TabIndex = 4;
            this.checkBoxMeet.Text = "Meet";
            this.checkBoxMeet.UseVisualStyleBackColor = true;
            this.checkBoxMeet.CheckedChanged += new System.EventHandler(this.interestsUpdated);
            // 
            // checkBoxBuild
            // 
            this.checkBoxBuild.AutoSize = true;
            this.checkBoxBuild.Enabled = false;
            this.checkBoxBuild.Location = new System.Drawing.Point(77, 26);
            this.checkBoxBuild.Name = "checkBoxBuild";
            this.checkBoxBuild.Size = new System.Drawing.Size(48, 17);
            this.checkBoxBuild.TabIndex = 3;
            this.checkBoxBuild.Text = "Build";
            this.checkBoxBuild.UseVisualStyleBackColor = true;
            this.checkBoxBuild.CheckedChanged += new System.EventHandler(this.interestsUpdated);
            // 
            // labelLanguages
            // 
            this.labelLanguages.AutoSize = true;
            this.labelLanguages.Location = new System.Drawing.Point(8, 271);
            this.labelLanguages.Name = "labelLanguages";
            this.labelLanguages.Size = new System.Drawing.Size(63, 13);
            this.labelLanguages.TabIndex = 2;
            this.labelLanguages.Text = "Languages:";
            // 
            // labelSkills
            // 
            this.labelSkills.AutoSize = true;
            this.labelSkills.Location = new System.Drawing.Point(37, 164);
            this.labelSkills.Name = "labelSkills";
            this.labelSkills.Size = new System.Drawing.Size(33, 13);
            this.labelSkills.TabIndex = 1;
            this.labelSkills.Text = "Skills:";
            // 
            // labelWantTo
            // 
            this.labelWantTo.AutoSize = true;
            this.labelWantTo.Location = new System.Drawing.Point(15, 26);
            this.labelWantTo.Name = "labelWantTo";
            this.labelWantTo.Size = new System.Drawing.Size(55, 13);
            this.labelWantTo.TabIndex = 0;
            this.labelWantTo.Text = "I want to:";
            // 
            // tbpPicks
            // 
            this.tbpPicks.Controls.Add(this.pickDetailPanel);
            this.tbpPicks.Controls.Add(this.pickListPanel);
            this.tbpPicks.Location = new System.Drawing.Point(4, 22);
            this.tbpPicks.Name = "tbpPicks";
            this.tbpPicks.Padding = new System.Windows.Forms.Padding(3);
            this.tbpPicks.Size = new System.Drawing.Size(460, 476);
            this.tbpPicks.TabIndex = 3;
            this.tbpPicks.Text = "Picks";
            this.tbpPicks.UseVisualStyleBackColor = true;
            // 
            // pickDetailPanel
            // 
            this.pickDetailPanel.Controls.Add(this.pickDetail);
            this.pickDetailPanel.Controls.Add(this.pickLocation);
            this.pickDetailPanel.Controls.Add(this.picksLowerPanel);
            this.pickDetailPanel.Controls.Add(this.pickTitle);
            this.pickDetailPanel.Controls.Add(this.pickPicturePanel);
            this.pickDetailPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pickDetailPanel.Location = new System.Drawing.Point(142, 3);
            this.pickDetailPanel.Name = "pickDetailPanel";
            this.pickDetailPanel.Size = new System.Drawing.Size(315, 470);
            this.pickDetailPanel.TabIndex = 1;
            this.pickDetailPanel.Visible = false;
            // 
            // pickDetail
            // 
            this.pickDetail.AccessibleName = "Pick detail";
            this.pickDetail.BackColor = System.Drawing.SystemColors.Window;
            this.pickDetail.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pickDetail.Location = new System.Drawing.Point(0, 259);
            this.pickDetail.Name = "pickDetail";
            this.pickDetail.ReadOnly = true;
            this.pickDetail.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
            this.pickDetail.Size = new System.Drawing.Size(315, 149);
            this.pickDetail.TabIndex = 2;
            this.pickDetail.Text = "";
            this.pickDetail.LinkClicked += new System.Windows.Forms.LinkClickedEventHandler(this.rtbAbout_LinkClicked);
            this.pickDetail.Leave += new System.EventHandler(this.pickTitle_Leave);
            // 
            // pickLocation
            // 
            this.pickLocation.AccessibleName = "Pick parcel";
            this.pickLocation.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pickLocation.Location = new System.Drawing.Point(0, 408);
            this.pickLocation.Name = "pickLocation";
            this.pickLocation.Size = new System.Drawing.Size(315, 21);
            this.pickLocation.TabIndex = 4;
            // 
            // picksLowerPanel
            // 
            this.picksLowerPanel.Controls.Add(this.btnDeletePick);
            this.picksLowerPanel.Controls.Add(this.btnShowOnMap);
            this.picksLowerPanel.Controls.Add(this.btnTeleport);
            this.picksLowerPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.picksLowerPanel.Location = new System.Drawing.Point(0, 429);
            this.picksLowerPanel.Name = "picksLowerPanel";
            this.picksLowerPanel.Size = new System.Drawing.Size(315, 41);
            this.picksLowerPanel.TabIndex = 3;
            // 
            // btnDeletePick
            // 
            this.btnDeletePick.Location = new System.Drawing.Point(191, 10);
            this.btnDeletePick.Name = "btnDeletePick";
            this.btnDeletePick.Size = new System.Drawing.Size(75, 23);
            this.btnDeletePick.TabIndex = 5;
            this.btnDeletePick.Text = "Delete";
            this.btnDeletePick.UseVisualStyleBackColor = true;
            this.btnDeletePick.Visible = false;
            this.btnDeletePick.Click += new System.EventHandler(this.btnDeletePick_Click);
            // 
            // btnShowOnMap
            // 
            this.btnShowOnMap.Location = new System.Drawing.Point(87, 10);
            this.btnShowOnMap.Name = "btnShowOnMap";
            this.btnShowOnMap.Size = new System.Drawing.Size(98, 23);
            this.btnShowOnMap.TabIndex = 4;
            this.btnShowOnMap.Text = "Show on Map";
            this.btnShowOnMap.UseVisualStyleBackColor = true;
            this.btnShowOnMap.Click += new System.EventHandler(this.btnShowOnMap_Click);
            // 
            // btnTeleport
            // 
            this.btnTeleport.Location = new System.Drawing.Point(6, 10);
            this.btnTeleport.Name = "btnTeleport";
            this.btnTeleport.Size = new System.Drawing.Size(75, 23);
            this.btnTeleport.TabIndex = 3;
            this.btnTeleport.Text = "Teleport";
            this.btnTeleport.UseVisualStyleBackColor = true;
            this.btnTeleport.Click += new System.EventHandler(this.btnTeleport_Click);
            // 
            // pickTitle
            // 
            this.pickTitle.AccessibleName = "Pick title";
            this.pickTitle.BackColor = System.Drawing.SystemColors.Window;
            this.pickTitle.Dock = System.Windows.Forms.DockStyle.Top;
            this.pickTitle.Location = new System.Drawing.Point(0, 238);
            this.pickTitle.Name = "pickTitle";
            this.pickTitle.ReadOnly = true;
            this.pickTitle.Size = new System.Drawing.Size(315, 21);
            this.pickTitle.TabIndex = 1;
            this.pickTitle.Leave += new System.EventHandler(this.pickTitle_Leave);
            // 
            // pickPicturePanel
            // 
            this.pickPicturePanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.pickPicturePanel.Location = new System.Drawing.Point(0, 0);
            this.pickPicturePanel.Name = "pickPicturePanel";
            this.pickPicturePanel.Size = new System.Drawing.Size(315, 238);
            this.pickPicturePanel.TabIndex = 99;
            // 
            // pickListPanel
            // 
            this.pickListPanel.Controls.Add(this.btnNewPick);
            this.pickListPanel.Dock = System.Windows.Forms.DockStyle.Left;
            this.pickListPanel.Location = new System.Drawing.Point(3, 3);
            this.pickListPanel.Name = "pickListPanel";
            this.pickListPanel.Size = new System.Drawing.Size(139, 470);
            this.pickListPanel.TabIndex = 0;
            // 
            // btnNewPick
            // 
            this.btnNewPick.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnNewPick.Location = new System.Drawing.Point(33, 439);
            this.btnNewPick.Name = "btnNewPick";
            this.btnNewPick.Size = new System.Drawing.Size(75, 23);
            this.btnNewPick.TabIndex = 0;
            this.btnNewPick.Text = "New Pick";
            this.btnNewPick.UseVisualStyleBackColor = true;
            this.btnNewPick.Visible = false;
            this.btnNewPick.Click += new System.EventHandler(this.btnNewPick_Click);
            // 
            // tpgFirstLife
            // 
            this.tpgFirstLife.Controls.Add(this.txtUUID);
            this.tpgFirstLife.Controls.Add(this.labelKey);
            this.tpgFirstLife.Controls.Add(this.rlPicPanel);
            this.tpgFirstLife.Controls.Add(this.rtbAboutFL);
            this.tpgFirstLife.Location = new System.Drawing.Point(4, 22);
            this.tpgFirstLife.Name = "tpgFirstLife";
            this.tpgFirstLife.Padding = new System.Windows.Forms.Padding(3);
            this.tpgFirstLife.Size = new System.Drawing.Size(460, 476);
            this.tpgFirstLife.TabIndex = 2;
            this.tpgFirstLife.Text = "First Life";
            this.tpgFirstLife.UseVisualStyleBackColor = true;
            // 
            // txtUUID
            // 
            this.txtUUID.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.txtUUID.Location = new System.Drawing.Point(43, 430);
            this.txtUUID.Name = "txtUUID";
            this.txtUUID.ReadOnly = true;
            this.txtUUID.Size = new System.Drawing.Size(259, 21);
            this.txtUUID.TabIndex = 17;
            // 
            // labelKey
            // 
            this.labelKey.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.labelKey.AutoSize = true;
            this.labelKey.Location = new System.Drawing.Point(8, 433);
            this.labelKey.Name = "labelKey";
            this.labelKey.Size = new System.Drawing.Size(29, 13);
            this.labelKey.TabIndex = 16;
            this.labelKey.Text = "Key:";
            // 
            // rlPicPanel
            // 
            this.rlPicPanel.Location = new System.Drawing.Point(6, 22);
            this.rlPicPanel.Name = "rlPicPanel";
            this.rlPicPanel.Size = new System.Drawing.Size(245, 163);
            this.rlPicPanel.TabIndex = 1;
            // 
            // rtbAboutFL
            // 
            this.rtbAboutFL.AccessibleName = "About first life";
            this.rtbAboutFL.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.rtbAboutFL.Location = new System.Drawing.Point(6, 201);
            this.rtbAboutFL.Name = "rtbAboutFL";
            this.rtbAboutFL.ReadOnly = true;
            this.rtbAboutFL.Size = new System.Drawing.Size(448, 150);
            this.rtbAboutFL.TabIndex = 0;
            this.rtbAboutFL.Text = "";
            this.rtbAboutFL.LinkClicked += new System.Windows.Forms.LinkClickedEventHandler(this.rtbAboutFL_LinkClicked);
            this.rtbAboutFL.Leave += new System.EventHandler(this.rtbAboutFL_Leave);
            // 
            // tabNotes
            // 
            this.tabNotes.Controls.Add(this.rtbNotes);
            this.tabNotes.Controls.Add(this.labelNotes);
            this.tabNotes.Location = new System.Drawing.Point(4, 22);
            this.tabNotes.Name = "tabNotes";
            this.tabNotes.Padding = new System.Windows.Forms.Padding(3);
            this.tabNotes.Size = new System.Drawing.Size(460, 476);
            this.tabNotes.TabIndex = 4;
            this.tabNotes.Text = "Notes";
            this.tabNotes.UseVisualStyleBackColor = true;
            // 
            // rtbNotes
            // 
            this.rtbNotes.AccessibleName = "Private Notes on Avatar";
            this.rtbNotes.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.rtbNotes.Location = new System.Drawing.Point(6, 19);
            this.rtbNotes.Name = "rtbNotes";
            this.rtbNotes.ReadOnly = false;
            this.rtbNotes.Size = new System.Drawing.Size(448, 277);
            this.rtbNotes.TabIndex = 1;
            this.rtbNotes.Text = "";
            this.rtbNotes.Leave += new System.EventHandler(this.rtbNotes_Leave);
            // 
            // labelNotes
            // 
            this.labelNotes.AutoSize = true;
            this.labelNotes.Location = new System.Drawing.Point(6, 3);
            this.labelNotes.Name = "labelNotes";
            this.labelNotes.Size = new System.Drawing.Size(146, 13);
            this.labelNotes.TabIndex = 0;
            this.labelNotes.Text = "Private notes on this Avatar:";
            // 
            // btnClose
            // 
            this.btnClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClose.Location = new System.Drawing.Point(405, 520);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(75, 23);
            this.btnClose.TabIndex = 25;
            this.btnClose.Text = "Close";
            this.btnClose.UseVisualStyleBackColor = true;
            this.btnClose.Click += new System.EventHandler(this.btnClose_Click);
            // 
            // textBox1
            // 
            this.textBox1.AllowDrop = true;
            this.textBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.textBox1.BackColor = System.Drawing.SystemColors.Info;
            this.textBox1.Location = new System.Drawing.Point(18, 522);
            this.textBox1.Name = "textBox1";
            this.textBox1.ReadOnly = true;
            this.textBox1.Size = new System.Drawing.Size(360, 21);
            this.textBox1.TabIndex = 2;
            this.textBox1.TabStop = false;
            this.textBox1.Text = "Drop items from inventory here";
            this.textBox1.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.textBox1.DragDrop += new System.Windows.Forms.DragEventHandler(this.textBox1_DragDrop);
            this.textBox1.DragEnter += new System.Windows.Forms.DragEventHandler(this.textBox1_DragEnter);
            // 
            // frmProfile
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(492, 555);
            this.Controls.Add(this.textBox1);
            this.Controls.Add(this.btnClose);
            this.Controls.Add(this.tabProfile);
            this.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MinimumSize = new System.Drawing.Size(508, 574);
            this.Name = "frmProfile";
            this.Text = "Profile";
            this.tabProfile.ResumeLayout(false);
            this.tpgProfile.ResumeLayout(false);
            this.tpgProfile.PerformLayout();
            this.tpgWeb.ResumeLayout(false);
            this.tpgWeb.PerformLayout();
            this.tabInterests.ResumeLayout(false);
            this.tabInterests.PerformLayout();
            this.tbpPicks.ResumeLayout(false);
            this.pickDetailPanel.ResumeLayout(false);
            this.pickDetailPanel.PerformLayout();
            this.picksLowerPanel.ResumeLayout(false);
            this.pickListPanel.ResumeLayout(false);
            this.tpgFirstLife.ResumeLayout(false);
            this.tpgFirstLife.PerformLayout();
            this.tabNotes.ResumeLayout(false);
            this.tabNotes.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public System.Windows.Forms.TabControl tabProfile;
        public System.Windows.Forms.TabPage tpgProfile;
        public System.Windows.Forms.TabPage tpgWeb;
        public System.Windows.Forms.TabPage tpgFirstLife;
        public System.Windows.Forms.Label labelName;
        public System.Windows.Forms.Label labelBornOn;
        public System.Windows.Forms.Label labelInfo;
        public System.Windows.Forms.TextBox txtBornOn;
        public System.Windows.Forms.Label labelPartner;
        public System.Windows.Forms.Button btnClose;
        public System.Windows.Forms.Panel pnlWeb;
        public System.Windows.Forms.Button btnWebOpen;
        public System.Windows.Forms.Button btnWebView;
        public System.Windows.Forms.TextBox txtWebURL;
        public System.Windows.Forms.Label labelUrl;
        public System.Windows.Forms.RichTextBox rtbAbout;
        public System.Windows.Forms.RichTextBox rtbAboutFL;
        public System.Windows.Forms.RichTextBox rtbAccountInfo;
        public System.Windows.Forms.Button btnPay;
        public System.Windows.Forms.Button btnOfferTeleport;
        public System.Windows.Forms.Panel slPicPanel;
        public System.Windows.Forms.Panel rlPicPanel;
        public System.Windows.Forms.TabPage tbpPicks;
        public System.Windows.Forms.Panel pickDetailPanel;
        public System.Windows.Forms.Panel picksLowerPanel;
        public System.Windows.Forms.RichTextBox pickDetail;
        public System.Windows.Forms.TextBox pickTitle;
        public System.Windows.Forms.Panel pickPicturePanel;
        public System.Windows.Forms.Panel pickListPanel;
        public System.Windows.Forms.TextBox textBox1;
        public System.Windows.Forms.Button btnFriend;
        public System.Windows.Forms.Button btnIM;
        public System.Windows.Forms.TextBox pickLocation;
        public System.Windows.Forms.Button btnShowOnMap;
        public System.Windows.Forms.Button btnTeleport;
        public AgentNameTextBox anPartner;
        public AgentNameTextBox txtFullName;
        public System.Windows.Forms.TextBox txtUUID;
        public System.Windows.Forms.Label labelKey;
        public ListViewNoFlicker lvwGroups;
        private System.Windows.Forms.ColumnHeader clGroupName;
        private System.Windows.Forms.ColumnHeader clGroupTitle;
        public System.Windows.Forms.Button btnGive;
        private System.Windows.Forms.Button btnDeletePick;
        private System.Windows.Forms.Button btnNewPick;
        public System.Windows.Forms.Button btnUnmute;
        public System.Windows.Forms.Button btnMute;
        private System.Windows.Forms.Button btnRequestTeleport;
        private System.Windows.Forms.TabPage tabNotes;
        private System.Windows.Forms.TabPage tabInterests;
        private System.Windows.Forms.TextBox txtLanguages;
        private System.Windows.Forms.TextBox txtSkills;
        private System.Windows.Forms.TextBox txtWantTo;
        private System.Windows.Forms.CheckBox checkBoxEventPlanning;
        private System.Windows.Forms.CheckBox checkBoxCustomCharacters;
        private System.Windows.Forms.CheckBox checkBoxArchitecture;
        private System.Windows.Forms.CheckBox checkBoxScripting;
        private System.Windows.Forms.CheckBox checkBoxModeling;
        private System.Windows.Forms.CheckBox checkBoxTextures;
        private System.Windows.Forms.CheckBox checkBoxHire;
        private System.Windows.Forms.CheckBox checkBoxBuy;
        private System.Windows.Forms.CheckBox checkBoxBeHired;
        private System.Windows.Forms.CheckBox checkBoxExplore;
        private System.Windows.Forms.CheckBox checkBoxSell;
        private System.Windows.Forms.CheckBox checkBoxGroup;
        private System.Windows.Forms.CheckBox checkBoxMeet;
        private System.Windows.Forms.CheckBox checkBoxBuild;
        private System.Windows.Forms.Label labelLanguages;
        private System.Windows.Forms.Label labelSkills;
        private System.Windows.Forms.Label labelWantTo;
        public System.Windows.Forms.RichTextBox rtbNotes;
        private System.Windows.Forms.Label labelNotes;
    }
}