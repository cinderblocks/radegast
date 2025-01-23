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
    partial class frmSettings
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmSettings));
            this.tcGraphics = new System.Windows.Forms.TabControl();
            this.tbpGeneral = new System.Windows.Forms.TabPage();
            this.cbHighLight = new System.Windows.Forms.GroupBox();
            this.cbHighlightGroupIM = new System.Windows.Forms.CheckBox();
            this.cbHighlightIM = new System.Windows.Forms.CheckBox();
            this.cbHighlightChat = new System.Windows.Forms.CheckBox();
            this.cbFriendsHighlight = new System.Windows.Forms.CheckBox();
            this.cbTaskBarHighLight = new System.Windows.Forms.CheckBox();
            this.cbMisc = new System.Windows.Forms.GroupBox();
            this.cbThemeCompatibilityMode = new System.Windows.Forms.CheckBox();
            this.cbConfirmExit = new System.Windows.Forms.CheckBox();
            this.txtResolveURITime = new System.Windows.Forms.TextBox();
            this.cbResolveURIs = new System.Windows.Forms.CheckBox();
            this.cbShowScriptErrors = new System.Windows.Forms.CheckBox();
            this.label4 = new System.Windows.Forms.Label();
            this.cbHideLoginGraphics = new System.Windows.Forms.CheckBox();
            this.cbDisableLookAt = new System.Windows.Forms.CheckBox();
            this.cbTrasactDialog = new System.Windows.Forms.CheckBox();
            this.cbRadegastLogToFile = new System.Windows.Forms.CheckBox();
            this.cbTransactChat = new System.Windows.Forms.CheckBox();
            this.cbFriendsNotifications = new System.Windows.Forms.CheckBox();
            this.txtReconnectTime = new System.Windows.Forms.TextBox();
            this.cbAutoReconnect = new System.Windows.Forms.CheckBox();
            this.label2 = new System.Windows.Forms.Label();
            this.cbRLV = new System.Windows.Forms.CheckBox();
            this.cbRLVDebug = new System.Windows.Forms.CheckBox();
            this.cbMinToTrey = new System.Windows.Forms.CheckBox();
            this.cbSyntaxHighlight = new System.Windows.Forms.CheckBox();
            this.Chat = new System.Windows.Forms.GroupBox();
            this.btnChatLogDir = new System.Windows.Forms.Button();
            this.cbGroupIMSound = new System.Windows.Forms.CheckBox();
            this.cbNameLinks = new System.Windows.Forms.CheckBox();
            this.txtChatLogDir = new System.Windows.Forms.TextBox();
            this.cbDisableChatIMLog = new System.Windows.Forms.CheckBox();
            this.cbChatTimestamps = new System.Windows.Forms.CheckBox();
            this.lblChatLog = new System.Windows.Forms.Label();
            this.cbIMTimeStamps = new System.Windows.Forms.CheckBox();
            this.cbMUEmotes = new System.Windows.Forms.CheckBox();
            this.cbNoTyping = new System.Windows.Forms.CheckBox();
            this.gbDisplayNames = new System.Windows.Forms.GroupBox();
            this.rbDNOnlyDN = new System.Windows.Forms.RadioButton();
            this.rbDNDandUsernme = new System.Windows.Forms.RadioButton();
            this.rbDNSmart = new System.Windows.Forms.RadioButton();
            this.rbDNOff = new System.Windows.Forms.RadioButton();
            this.tbpAutoResponse = new System.Windows.Forms.TabPage();
            this.gnAutoScriptPermission = new System.Windows.Forms.GroupBox();
            this.cbAutoScriptPermission = new System.Windows.Forms.ComboBox();
            this.gnAutoInventory = new System.Windows.Forms.GroupBox();
            this.cbOnInvOffer = new System.Windows.Forms.ComboBox();
            this.txtAutoResponse = new System.Windows.Forms.TextBox();
            this.gbAutoResponse = new System.Windows.Forms.GroupBox();
            this.rbAutoAlways = new System.Windows.Forms.RadioButton();
            this.rbAutoNonFriend = new System.Windows.Forms.RadioButton();
            this.rbAutobusy = new System.Windows.Forms.RadioButton();
            this.tbpGraphics = new System.Windows.Forms.TabPage();
            this.tbpBot = new System.Windows.Forms.TabPage();
            this.gbLSLHelper = new System.Windows.Forms.GroupBox();
            this.llLSLHelperInstructios = new System.Windows.Forms.LinkLabel();
            this.cbLSLHelperEnabled = new System.Windows.Forms.CheckBox();
            this.tbLSLAllowedOwner = new System.Windows.Forms.TextBox();
            this.lblLSLUUID = new System.Windows.Forms.Label();
            this.pseudoHome = new System.Windows.Forms.GroupBox();
            this.pseudoHomeSet = new System.Windows.Forms.Button();
            this.pseudoHomeTolerance = new System.Windows.Forms.NumericUpDown();
            this.pseudoHomeTP = new System.Windows.Forms.Button();
            this.pseudoHomeLocation = new System.Windows.Forms.TextBox();
            this.pseudoHomeClear = new System.Windows.Forms.Button();
            this.pseudoHomeToleranceLabel = new System.Windows.Forms.Label();
            this.pseudoHomeEnabled = new System.Windows.Forms.CheckBox();
            this.autoSit = new System.Windows.Forms.GroupBox();
            this.autoSitEnabled = new System.Windows.Forms.CheckBox();
            this.autoSitSit = new System.Windows.Forms.Button();
            this.autoSitClear = new System.Windows.Forms.Button();
            this.autoSitUUIDLabel = new System.Windows.Forms.Label();
            this.autoSitUUID = new System.Windows.Forms.TextBox();
            this.autoSitName = new System.Windows.Forms.TextBox();
            this.autoSitNameLabel = new System.Windows.Forms.Label();
            this.tbpChat = new System.Windows.Forms.TabPage();
            this.btnSave = new System.Windows.Forms.Button();
            this.btnResetFontSettings = new System.Windows.Forms.Button();
            this.lblPreview = new System.Windows.Forms.Label();
            this.cbxItalic = new System.Windows.Forms.CheckBox();
            this.label10 = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.cbxBold = new System.Windows.Forms.CheckBox();
            this.lbxColorItems = new System.Windows.Forms.ListBox();
            this.label8 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.cbxBackground = new System.Windows.Forms.ComboBox();
            this.cbxForeground = new System.Windows.Forms.ComboBox();
            this.cbxFontSize = new System.Windows.Forms.ComboBox();
            this.cbxFont = new System.Windows.Forms.ComboBox();
            this.fontDialog1 = new System.Windows.Forms.FontDialog();
            this.tcGraphics.SuspendLayout();
            this.tbpGeneral.SuspendLayout();
            this.cbHighLight.SuspendLayout();
            this.cbMisc.SuspendLayout();
            this.Chat.SuspendLayout();
            this.gbDisplayNames.SuspendLayout();
            this.tbpAutoResponse.SuspendLayout();
            this.gnAutoScriptPermission.SuspendLayout();
            this.gnAutoInventory.SuspendLayout();
            this.gbAutoResponse.SuspendLayout();
            this.tbpBot.SuspendLayout();
            this.gbLSLHelper.SuspendLayout();
            this.pseudoHome.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pseudoHomeTolerance)).BeginInit();
            this.autoSit.SuspendLayout();
            this.tbpChat.SuspendLayout();
            this.SuspendLayout();
            // 
            // tcGraphics
            // 
            this.tcGraphics.Controls.Add(this.tbpGeneral);
            this.tcGraphics.Controls.Add(this.tbpAutoResponse);
            this.tcGraphics.Controls.Add(this.tbpGraphics);
            this.tcGraphics.Controls.Add(this.tbpBot);
            this.tcGraphics.Controls.Add(this.tbpChat);
            this.tcGraphics.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tcGraphics.Location = new System.Drawing.Point(0, 0);
            this.tcGraphics.Margin = new System.Windows.Forms.Padding(4);
            this.tcGraphics.Multiline = true;
            this.tcGraphics.Name = "tcGraphics";
            this.tcGraphics.SelectedIndex = 0;
            this.tcGraphics.Size = new System.Drawing.Size(707, 565);
            this.tcGraphics.TabIndex = 0;
            // 
            // tbpGeneral
            // 
            this.tbpGeneral.Controls.Add(this.cbHighLight);
            this.tbpGeneral.Controls.Add(this.cbMisc);
            this.tbpGeneral.Controls.Add(this.Chat);
            this.tbpGeneral.Controls.Add(this.gbDisplayNames);
            this.tbpGeneral.Location = new System.Drawing.Point(4, 25);
            this.tbpGeneral.Margin = new System.Windows.Forms.Padding(4);
            this.tbpGeneral.Name = "tbpGeneral";
            this.tbpGeneral.Padding = new System.Windows.Forms.Padding(4);
            this.tbpGeneral.Size = new System.Drawing.Size(699, 536);
            this.tbpGeneral.TabIndex = 1;
            this.tbpGeneral.Text = "General";
            this.tbpGeneral.UseVisualStyleBackColor = true;
            // 
            // cbHighLight
            // 
            this.cbHighLight.Controls.Add(this.cbHighlightGroupIM);
            this.cbHighLight.Controls.Add(this.cbHighlightIM);
            this.cbHighLight.Controls.Add(this.cbHighlightChat);
            this.cbHighLight.Controls.Add(this.cbFriendsHighlight);
            this.cbHighLight.Controls.Add(this.cbTaskBarHighLight);
            this.cbHighLight.Location = new System.Drawing.Point(11, 228);
            this.cbHighLight.Margin = new System.Windows.Forms.Padding(4);
            this.cbHighLight.Name = "cbHighLight";
            this.cbHighLight.Padding = new System.Windows.Forms.Padding(4);
            this.cbHighLight.Size = new System.Drawing.Size(341, 167);
            this.cbHighLight.TabIndex = 1;
            this.cbHighLight.TabStop = false;
            this.cbHighLight.Text = "Highlight when tab changes";
            // 
            // cbHighlightGroupIM
            // 
            this.cbHighlightGroupIM.AutoSize = true;
            this.cbHighlightGroupIM.Location = new System.Drawing.Point(36, 137);
            this.cbHighlightGroupIM.Margin = new System.Windows.Forms.Padding(4);
            this.cbHighlightGroupIM.Name = "cbHighlightGroupIM";
            this.cbHighlightGroupIM.Size = new System.Drawing.Size(164, 20);
            this.cbHighlightGroupIM.TabIndex = 5;
            this.cbHighlightGroupIM.Text = "Group Instant Message";
            this.cbHighlightGroupIM.UseVisualStyleBackColor = true;
            // 
            // cbHighlightIM
            // 
            this.cbHighlightIM.AutoSize = true;
            this.cbHighlightIM.Location = new System.Drawing.Point(36, 108);
            this.cbHighlightIM.Margin = new System.Windows.Forms.Padding(4);
            this.cbHighlightIM.Name = "cbHighlightIM";
            this.cbHighlightIM.Size = new System.Drawing.Size(124, 20);
            this.cbHighlightIM.TabIndex = 4;
            this.cbHighlightIM.Text = "Instant Message";
            this.cbHighlightIM.UseVisualStyleBackColor = true;
            // 
            // cbHighlightChat
            // 
            this.cbHighlightChat.AutoSize = true;
            this.cbHighlightChat.Location = new System.Drawing.Point(36, 52);
            this.cbHighlightChat.Margin = new System.Windows.Forms.Padding(4);
            this.cbHighlightChat.Name = "cbHighlightChat";
            this.cbHighlightChat.Size = new System.Drawing.Size(53, 20);
            this.cbHighlightChat.TabIndex = 2;
            this.cbHighlightChat.Text = "Chat";
            this.cbHighlightChat.UseVisualStyleBackColor = true;
            // 
            // cbFriendsHighlight
            // 
            this.cbFriendsHighlight.AutoSize = true;
            this.cbFriendsHighlight.Location = new System.Drawing.Point(36, 80);
            this.cbFriendsHighlight.Margin = new System.Windows.Forms.Padding(4);
            this.cbFriendsHighlight.Name = "cbFriendsHighlight";
            this.cbFriendsHighlight.Size = new System.Drawing.Size(183, 20);
            this.cbFriendsHighlight.TabIndex = 3;
            this.cbFriendsHighlight.Text = "Friends online notifications";
            this.cbFriendsHighlight.UseVisualStyleBackColor = true;
            this.cbFriendsHighlight.CheckedChanged += new System.EventHandler(this.cbTrasactChat_CheckedChanged);
            // 
            // cbTaskBarHighLight
            // 
            this.cbTaskBarHighLight.AutoSize = true;
            this.cbTaskBarHighLight.Location = new System.Drawing.Point(8, 23);
            this.cbTaskBarHighLight.Margin = new System.Windows.Forms.Padding(4);
            this.cbTaskBarHighLight.Name = "cbTaskBarHighLight";
            this.cbTaskBarHighLight.Size = new System.Drawing.Size(139, 20);
            this.cbTaskBarHighLight.TabIndex = 1;
            this.cbTaskBarHighLight.Text = "Enable highlighting";
            this.cbTaskBarHighLight.UseVisualStyleBackColor = true;
            // 
            // cbMisc
            // 
            this.cbMisc.Controls.Add(this.cbThemeCompatibilityMode);
            this.cbMisc.Controls.Add(this.cbConfirmExit);
            this.cbMisc.Controls.Add(this.txtResolveURITime);
            this.cbMisc.Controls.Add(this.cbResolveURIs);
            this.cbMisc.Controls.Add(this.cbShowScriptErrors);
            this.cbMisc.Controls.Add(this.label4);
            this.cbMisc.Controls.Add(this.cbHideLoginGraphics);
            this.cbMisc.Controls.Add(this.cbDisableLookAt);
            this.cbMisc.Controls.Add(this.cbTrasactDialog);
            this.cbMisc.Controls.Add(this.cbRadegastLogToFile);
            this.cbMisc.Controls.Add(this.cbTransactChat);
            this.cbMisc.Controls.Add(this.cbFriendsNotifications);
            this.cbMisc.Controls.Add(this.txtReconnectTime);
            this.cbMisc.Controls.Add(this.cbAutoReconnect);
            this.cbMisc.Controls.Add(this.label2);
            this.cbMisc.Controls.Add(this.cbRLV);
            this.cbMisc.Controls.Add(this.cbRLVDebug);
            this.cbMisc.Controls.Add(this.cbMinToTrey);
            this.cbMisc.Controls.Add(this.cbSyntaxHighlight);
            this.cbMisc.Location = new System.Drawing.Point(360, 7);
            this.cbMisc.Margin = new System.Windows.Forms.Padding(4);
            this.cbMisc.Name = "cbMisc";
            this.cbMisc.Padding = new System.Windows.Forms.Padding(4);
            this.cbMisc.Size = new System.Drawing.Size(325, 519);
            this.cbMisc.TabIndex = 2;
            this.cbMisc.TabStop = false;
            // 
            // cbThemeCompatibilityMode
            // 
            this.cbThemeCompatibilityMode.AutoSize = true;
            this.cbThemeCompatibilityMode.Location = new System.Drawing.Point(8, 369);
            this.cbThemeCompatibilityMode.Margin = new System.Windows.Forms.Padding(4);
            this.cbThemeCompatibilityMode.Name = "cbThemeCompatibilityMode";
            this.cbThemeCompatibilityMode.Size = new System.Drawing.Size(187, 20);
            this.cbThemeCompatibilityMode.TabIndex = 22;
            this.cbThemeCompatibilityMode.Text = "Theme Compatibility Mode";
            this.cbThemeCompatibilityMode.UseVisualStyleBackColor = true;
            this.cbThemeCompatibilityMode.CheckedChanged += new System.EventHandler(this.cbThemeCompatibilityMode_CheckedChanged);
            // 
            // cbConfirmExit
            // 
            this.cbConfirmExit.AutoSize = true;
            this.cbConfirmExit.Location = new System.Drawing.Point(8, 347);
            this.cbConfirmExit.Margin = new System.Windows.Forms.Padding(4);
            this.cbConfirmExit.Name = "cbConfirmExit";
            this.cbConfirmExit.Size = new System.Drawing.Size(95, 20);
            this.cbConfirmExit.TabIndex = 21;
            this.cbConfirmExit.Text = "Confirm Exit";
            this.cbConfirmExit.UseVisualStyleBackColor = true;
            this.cbConfirmExit.CheckedChanged += new System.EventHandler(this.cbConfirmExit_CheckedChanged);
            // 
            // txtResolveURITime
            // 
            this.txtResolveURITime.Location = new System.Drawing.Point(208, 324);
            this.txtResolveURITime.Margin = new System.Windows.Forms.Padding(4);
            this.txtResolveURITime.Name = "txtResolveURITime";
            this.txtResolveURITime.Size = new System.Drawing.Size(69, 22);
            this.txtResolveURITime.TabIndex = 20;
            this.txtResolveURITime.Text = "100";
            this.txtResolveURITime.TextChanged += new System.EventHandler(this.txtResolveURITime_TextChanged);
            // 
            // cbResolveURIs
            // 
            this.cbResolveURIs.AutoSize = true;
            this.cbResolveURIs.Location = new System.Drawing.Point(8, 304);
            this.cbResolveURIs.Margin = new System.Windows.Forms.Padding(4);
            this.cbResolveURIs.Name = "cbResolveURIs";
            this.cbResolveURIs.Size = new System.Drawing.Size(110, 20);
            this.cbResolveURIs.TabIndex = 18;
            this.cbResolveURIs.Text = "Resolve URIs";
            this.cbResolveURIs.UseVisualStyleBackColor = true;
            this.cbResolveURIs.CheckedChanged += new System.EventHandler(this.cbResolveURIs_CheckedChanged);
            // 
            // cbShowScriptErrors
            // 
            this.cbShowScriptErrors.AutoSize = true;
            this.cbShowScriptErrors.Location = new System.Drawing.Point(8, 260);
            this.cbShowScriptErrors.Margin = new System.Windows.Forms.Padding(4);
            this.cbShowScriptErrors.Name = "cbShowScriptErrors";
            this.cbShowScriptErrors.Size = new System.Drawing.Size(135, 20);
            this.cbShowScriptErrors.TabIndex = 17;
            this.cbShowScriptErrors.Text = "Show Script Errors";
            this.cbShowScriptErrors.UseVisualStyleBackColor = true;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(29, 327);
            this.label4.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(139, 16);
            this.label4.TabIndex = 19;
            this.label4.Text = "Resolve Timeout (ms)";
            // 
            // cbHideLoginGraphics
            // 
            this.cbHideLoginGraphics.AutoSize = true;
            this.cbHideLoginGraphics.Location = new System.Drawing.Point(8, 14);
            this.cbHideLoginGraphics.Margin = new System.Windows.Forms.Padding(4);
            this.cbHideLoginGraphics.Name = "cbHideLoginGraphics";
            this.cbHideLoginGraphics.Size = new System.Drawing.Size(174, 20);
            this.cbHideLoginGraphics.TabIndex = 3;
            this.cbHideLoginGraphics.Text = "Hide login splash screen";
            this.cbHideLoginGraphics.UseVisualStyleBackColor = true;
            this.cbHideLoginGraphics.CheckedChanged += new System.EventHandler(this.cbTrasactChat_CheckedChanged);
            // 
            // cbDisableLookAt
            // 
            this.cbDisableLookAt.AutoSize = true;
            this.cbDisableLookAt.Location = new System.Drawing.Point(8, 238);
            this.cbDisableLookAt.Margin = new System.Windows.Forms.Padding(4);
            this.cbDisableLookAt.Name = "cbDisableLookAt";
            this.cbDisableLookAt.Size = new System.Drawing.Size(170, 20);
            this.cbDisableLookAt.TabIndex = 15;
            this.cbDisableLookAt.Text = "Disable Look At beacon";
            this.cbDisableLookAt.UseVisualStyleBackColor = true;
            // 
            // cbTrasactDialog
            // 
            this.cbTrasactDialog.AutoSize = true;
            this.cbTrasactDialog.Location = new System.Drawing.Point(8, 127);
            this.cbTrasactDialog.Margin = new System.Windows.Forms.Padding(4);
            this.cbTrasactDialog.Name = "cbTrasactDialog";
            this.cbTrasactDialog.Size = new System.Drawing.Size(216, 20);
            this.cbTrasactDialog.TabIndex = 9;
            this.cbTrasactDialog.Text = "Display dialog on L$ transaction";
            this.cbTrasactDialog.UseVisualStyleBackColor = true;
            this.cbTrasactDialog.CheckedChanged += new System.EventHandler(this.cbTrasactDialog_CheckedChanged);
            // 
            // cbRadegastLogToFile
            // 
            this.cbRadegastLogToFile.AutoSize = true;
            this.cbRadegastLogToFile.Location = new System.Drawing.Point(8, 215);
            this.cbRadegastLogToFile.Margin = new System.Windows.Forms.Padding(4);
            this.cbRadegastLogToFile.Name = "cbRadegastLogToFile";
            this.cbRadegastLogToFile.Size = new System.Drawing.Size(83, 20);
            this.cbRadegastLogToFile.TabIndex = 14;
            this.cbRadegastLogToFile.Text = "Log to file";
            this.cbRadegastLogToFile.UseVisualStyleBackColor = true;
            this.cbRadegastLogToFile.CheckedChanged += new System.EventHandler(this.cbRadegastLogToFile_CheckedChanged);
            // 
            // cbTransactChat
            // 
            this.cbTransactChat.AutoSize = true;
            this.cbTransactChat.Location = new System.Drawing.Point(8, 149);
            this.cbTransactChat.Margin = new System.Windows.Forms.Padding(4);
            this.cbTransactChat.Name = "cbTransactChat";
            this.cbTransactChat.Size = new System.Drawing.Size(205, 20);
            this.cbTransactChat.TabIndex = 10;
            this.cbTransactChat.Text = "Display L$ transactions in chat";
            this.cbTransactChat.UseVisualStyleBackColor = true;
            this.cbTransactChat.CheckedChanged += new System.EventHandler(this.cbTrasactChat_CheckedChanged);
            // 
            // cbFriendsNotifications
            // 
            this.cbFriendsNotifications.AutoSize = true;
            this.cbFriendsNotifications.Location = new System.Drawing.Point(8, 171);
            this.cbFriendsNotifications.Margin = new System.Windows.Forms.Padding(4);
            this.cbFriendsNotifications.Name = "cbFriendsNotifications";
            this.cbFriendsNotifications.Size = new System.Drawing.Size(227, 20);
            this.cbFriendsNotifications.TabIndex = 11;
            this.cbFriendsNotifications.Text = "Display friends online notifications";
            this.cbFriendsNotifications.UseVisualStyleBackColor = true;
            this.cbFriendsNotifications.CheckedChanged += new System.EventHandler(this.cbTrasactChat_CheckedChanged);
            // 
            // txtReconnectTime
            // 
            this.txtReconnectTime.Location = new System.Drawing.Point(208, 101);
            this.txtReconnectTime.Margin = new System.Windows.Forms.Padding(4);
            this.txtReconnectTime.Name = "txtReconnectTime";
            this.txtReconnectTime.Size = new System.Drawing.Size(69, 22);
            this.txtReconnectTime.TabIndex = 8;
            this.txtReconnectTime.Text = "120";
            this.txtReconnectTime.TextChanged += new System.EventHandler(this.txtReconnectTime_TextChanged);
            // 
            // cbAutoReconnect
            // 
            this.cbAutoReconnect.AutoSize = true;
            this.cbAutoReconnect.Location = new System.Drawing.Point(8, 80);
            this.cbAutoReconnect.Margin = new System.Windows.Forms.Padding(4);
            this.cbAutoReconnect.Name = "cbAutoReconnect";
            this.cbAutoReconnect.Size = new System.Drawing.Size(201, 20);
            this.cbAutoReconnect.TabIndex = 6;
            this.cbAutoReconnect.Text = "Auto reconnect on disconnect";
            this.cbAutoReconnect.UseVisualStyleBackColor = true;
            this.cbAutoReconnect.CheckedChanged += new System.EventHandler(this.cbTrasactChat_CheckedChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(29, 105);
            this.label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(157, 16);
            this.label2.TabIndex = 7;
            this.label2.Text = "Auto reconnect time (sec)";
            // 
            // cbRLV
            // 
            this.cbRLV.AutoSize = true;
            this.cbRLV.Location = new System.Drawing.Point(8, 193);
            this.cbRLV.Margin = new System.Windows.Forms.Padding(4);
            this.cbRLV.Name = "cbRLV";
            this.cbRLV.Size = new System.Drawing.Size(100, 20);
            this.cbRLV.TabIndex = 12;
            this.cbRLV.Text = "RLV support";
            this.cbRLV.UseVisualStyleBackColor = true;
            this.cbRLV.CheckedChanged += new System.EventHandler(this.cbTrasactChat_CheckedChanged);
            // 
            // cbRLVDebug
            // 
            this.cbRLVDebug.AutoSize = true;
            this.cbRLVDebug.Location = new System.Drawing.Point(8, 282);
            this.cbRLVDebug.Margin = new System.Windows.Forms.Padding(4);
            this.cbRLVDebug.Name = "cbRLVDebug";
            this.cbRLVDebug.Size = new System.Drawing.Size(163, 20);
            this.cbRLVDebug.TabIndex = 12;
            this.cbRLVDebug.Text = "Output RLV commands";
            this.cbRLVDebug.UseVisualStyleBackColor = true;
            this.cbRLVDebug.CheckedChanged += new System.EventHandler(this.cbTrasactChat_CheckedChanged);
            // 
            // cbMinToTrey
            // 
            this.cbMinToTrey.AutoSize = true;
            this.cbMinToTrey.Location = new System.Drawing.Point(8, 36);
            this.cbMinToTrey.Margin = new System.Windows.Forms.Padding(4);
            this.cbMinToTrey.Name = "cbMinToTrey";
            this.cbMinToTrey.Size = new System.Drawing.Size(163, 20);
            this.cbMinToTrey.TabIndex = 4;
            this.cbMinToTrey.Text = "Minimize to system tray";
            this.cbMinToTrey.UseVisualStyleBackColor = true;
            this.cbMinToTrey.CheckedChanged += new System.EventHandler(this.cbTrasactChat_CheckedChanged);
            // 
            // cbSyntaxHighlight
            // 
            this.cbSyntaxHighlight.AutoSize = true;
            this.cbSyntaxHighlight.Location = new System.Drawing.Point(8, 58);
            this.cbSyntaxHighlight.Margin = new System.Windows.Forms.Padding(4);
            this.cbSyntaxHighlight.Name = "cbSyntaxHighlight";
            this.cbSyntaxHighlight.Size = new System.Drawing.Size(160, 20);
            this.cbSyntaxHighlight.TabIndex = 5;
            this.cbSyntaxHighlight.Text = "LSL syntax highlighting";
            this.cbSyntaxHighlight.UseVisualStyleBackColor = true;
            // 
            // Chat
            // 
            this.Chat.Controls.Add(this.btnChatLogDir);
            this.Chat.Controls.Add(this.cbGroupIMSound);
            this.Chat.Controls.Add(this.cbNameLinks);
            this.Chat.Controls.Add(this.txtChatLogDir);
            this.Chat.Controls.Add(this.cbDisableChatIMLog);
            this.Chat.Controls.Add(this.cbChatTimestamps);
            this.Chat.Controls.Add(this.lblChatLog);
            this.Chat.Controls.Add(this.cbIMTimeStamps);
            this.Chat.Controls.Add(this.cbMUEmotes);
            this.Chat.Controls.Add(this.cbNoTyping);
            this.Chat.Location = new System.Drawing.Point(11, 6);
            this.Chat.Margin = new System.Windows.Forms.Padding(4);
            this.Chat.Name = "Chat";
            this.Chat.Padding = new System.Windows.Forms.Padding(4);
            this.Chat.Size = new System.Drawing.Size(341, 214);
            this.Chat.TabIndex = 0;
            this.Chat.TabStop = false;
            this.Chat.Text = "Chat";
            // 
            // btnChatLogDir
            // 
            this.btnChatLogDir.AccessibleDescription = "Browse for chat log directory";
            this.btnChatLogDir.AccessibleName = "Browse Chat Log Directory";
            this.btnChatLogDir.Location = new System.Drawing.Point(261, 181);
            this.btnChatLogDir.Margin = new System.Windows.Forms.Padding(4);
            this.btnChatLogDir.Name = "btnChatLogDir";
            this.btnChatLogDir.Size = new System.Drawing.Size(76, 28);
            this.btnChatLogDir.TabIndex = 12;
            this.btnChatLogDir.Text = "Browse";
            this.btnChatLogDir.UseVisualStyleBackColor = true;
            this.btnChatLogDir.Click += new System.EventHandler(this.btnChatLogDir_Click);
            // 
            // cbGroupIMSound
            // 
            this.cbGroupIMSound.AutoSize = true;
            this.cbGroupIMSound.Location = new System.Drawing.Point(11, 159);
            this.cbGroupIMSound.Margin = new System.Windows.Forms.Padding(4);
            this.cbGroupIMSound.Name = "cbGroupIMSound";
            this.cbGroupIMSound.Size = new System.Drawing.Size(195, 20);
            this.cbGroupIMSound.TabIndex = 10;
            this.cbGroupIMSound.Text = "Play sound on new Group IM";
            this.cbGroupIMSound.UseVisualStyleBackColor = true;
            // 
            // cbNameLinks
            // 
            this.cbNameLinks.AutoSize = true;
            this.cbNameLinks.Location = new System.Drawing.Point(11, 135);
            this.cbNameLinks.Margin = new System.Windows.Forms.Padding(4);
            this.cbNameLinks.Name = "cbNameLinks";
            this.cbNameLinks.Size = new System.Drawing.Size(132, 20);
            this.cbNameLinks.TabIndex = 9;
            this.cbNameLinks.Text = "Avatar name links";
            this.cbNameLinks.UseVisualStyleBackColor = true;
            // 
            // txtChatLogDir
            // 
            this.txtChatLogDir.AccessibleDescription = "Directory storing chat logs";
            this.txtChatLogDir.AccessibleName = "Chat Log Location";
            this.txtChatLogDir.Enabled = false;
            this.txtChatLogDir.Location = new System.Drawing.Point(132, 182);
            this.txtChatLogDir.Margin = new System.Windows.Forms.Padding(4);
            this.txtChatLogDir.Name = "txtChatLogDir";
            this.txtChatLogDir.Size = new System.Drawing.Size(120, 22);
            this.txtChatLogDir.TabIndex = 11;
            // 
            // cbDisableChatIMLog
            // 
            this.cbDisableChatIMLog.AutoSize = true;
            this.cbDisableChatIMLog.Location = new System.Drawing.Point(11, 112);
            this.cbDisableChatIMLog.Margin = new System.Windows.Forms.Padding(4);
            this.cbDisableChatIMLog.Name = "cbDisableChatIMLog";
            this.cbDisableChatIMLog.Size = new System.Drawing.Size(166, 20);
            this.cbDisableChatIMLog.TabIndex = 8;
            this.cbDisableChatIMLog.Text = "Disable chat and IM log";
            this.cbDisableChatIMLog.UseVisualStyleBackColor = true;
            // 
            // cbChatTimestamps
            // 
            this.cbChatTimestamps.AutoSize = true;
            this.cbChatTimestamps.Location = new System.Drawing.Point(11, 20);
            this.cbChatTimestamps.Margin = new System.Windows.Forms.Padding(4);
            this.cbChatTimestamps.Name = "cbChatTimestamps";
            this.cbChatTimestamps.Size = new System.Drawing.Size(172, 20);
            this.cbChatTimestamps.TabIndex = 0;
            this.cbChatTimestamps.Text = "Show timestamps in chat";
            this.cbChatTimestamps.UseVisualStyleBackColor = true;
            // 
            // lblChatLog
            // 
            this.lblChatLog.AutoSize = true;
            this.lblChatLog.Location = new System.Drawing.Point(8, 187);
            this.lblChatLog.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblChatLog.Name = "lblChatLog";
            this.lblChatLog.Size = new System.Drawing.Size(114, 16);
            this.lblChatLog.TabIndex = 10;
            this.lblChatLog.Text = "Chat Log Location";
            // 
            // cbIMTimeStamps
            // 
            this.cbIMTimeStamps.AutoSize = true;
            this.cbIMTimeStamps.Location = new System.Drawing.Point(11, 43);
            this.cbIMTimeStamps.Margin = new System.Windows.Forms.Padding(4);
            this.cbIMTimeStamps.Name = "cbIMTimeStamps";
            this.cbIMTimeStamps.Size = new System.Drawing.Size(161, 20);
            this.cbIMTimeStamps.TabIndex = 1;
            this.cbIMTimeStamps.Text = "Show timestamps in IM";
            this.cbIMTimeStamps.UseVisualStyleBackColor = true;
            // 
            // cbMUEmotes
            // 
            this.cbMUEmotes.AutoSize = true;
            this.cbMUEmotes.Location = new System.Drawing.Point(11, 65);
            this.cbMUEmotes.Margin = new System.Windows.Forms.Padding(4);
            this.cbMUEmotes.Name = "cbMUEmotes";
            this.cbMUEmotes.Size = new System.Drawing.Size(131, 20);
            this.cbMUEmotes.TabIndex = 5;
            this.cbMUEmotes.Text = "MU* style emotes";
            this.cbMUEmotes.UseVisualStyleBackColor = true;
            this.cbMUEmotes.CheckedChanged += new System.EventHandler(this.cbTrasactChat_CheckedChanged);
            // 
            // cbNoTyping
            // 
            this.cbNoTyping.AutoSize = true;
            this.cbNoTyping.Location = new System.Drawing.Point(11, 89);
            this.cbNoTyping.Margin = new System.Windows.Forms.Padding(4);
            this.cbNoTyping.Name = "cbNoTyping";
            this.cbNoTyping.Size = new System.Drawing.Size(182, 20);
            this.cbNoTyping.TabIndex = 6;
            this.cbNoTyping.Text = "Don\'t use typing animation";
            this.cbNoTyping.UseVisualStyleBackColor = true;
            this.cbNoTyping.CheckedChanged += new System.EventHandler(this.cbTrasactChat_CheckedChanged);
            // 
            // gbDisplayNames
            // 
            this.gbDisplayNames.Controls.Add(this.rbDNOnlyDN);
            this.gbDisplayNames.Controls.Add(this.rbDNDandUsernme);
            this.gbDisplayNames.Controls.Add(this.rbDNSmart);
            this.gbDisplayNames.Controls.Add(this.rbDNOff);
            this.gbDisplayNames.Location = new System.Drawing.Point(11, 403);
            this.gbDisplayNames.Margin = new System.Windows.Forms.Padding(4);
            this.gbDisplayNames.Name = "gbDisplayNames";
            this.gbDisplayNames.Padding = new System.Windows.Forms.Padding(4);
            this.gbDisplayNames.Size = new System.Drawing.Size(341, 123);
            this.gbDisplayNames.TabIndex = 3;
            this.gbDisplayNames.TabStop = false;
            this.gbDisplayNames.Text = "Display names";
            // 
            // rbDNOnlyDN
            // 
            this.rbDNOnlyDN.AutoSize = true;
            this.rbDNOnlyDN.Location = new System.Drawing.Point(8, 92);
            this.rbDNOnlyDN.Margin = new System.Windows.Forms.Padding(4);
            this.rbDNOnlyDN.Name = "rbDNOnlyDN";
            this.rbDNOnlyDN.Size = new System.Drawing.Size(136, 20);
            this.rbDNOnlyDN.TabIndex = 3;
            this.rbDNOnlyDN.TabStop = true;
            this.rbDNOnlyDN.Text = "Only display name";
            this.rbDNOnlyDN.UseVisualStyleBackColor = true;
            this.rbDNOnlyDN.CheckedChanged += new System.EventHandler(this.rbDNOnlyDN_CheckedChanged);
            // 
            // rbDNDandUsernme
            // 
            this.rbDNDandUsernme.AutoSize = true;
            this.rbDNDandUsernme.Location = new System.Drawing.Point(8, 68);
            this.rbDNDandUsernme.Margin = new System.Windows.Forms.Padding(4);
            this.rbDNDandUsernme.Name = "rbDNDandUsernme";
            this.rbDNDandUsernme.Size = new System.Drawing.Size(179, 20);
            this.rbDNDandUsernme.TabIndex = 2;
            this.rbDNDandUsernme.TabStop = true;
            this.rbDNDandUsernme.Text = "Display name (username)";
            this.rbDNDandUsernme.UseVisualStyleBackColor = true;
            this.rbDNDandUsernme.CheckedChanged += new System.EventHandler(this.rbDNDandUsernme_CheckedChanged);
            // 
            // rbDNSmart
            // 
            this.rbDNSmart.AutoSize = true;
            this.rbDNSmart.Location = new System.Drawing.Point(8, 43);
            this.rbDNSmart.Margin = new System.Windows.Forms.Padding(4);
            this.rbDNSmart.Name = "rbDNSmart";
            this.rbDNSmart.Size = new System.Drawing.Size(60, 20);
            this.rbDNSmart.TabIndex = 1;
            this.rbDNSmart.TabStop = true;
            this.rbDNSmart.Text = "Smart";
            this.rbDNSmart.UseVisualStyleBackColor = true;
            this.rbDNSmart.CheckedChanged += new System.EventHandler(this.rbDNSmart_CheckedChanged);
            // 
            // rbDNOff
            // 
            this.rbDNOff.AutoSize = true;
            this.rbDNOff.Location = new System.Drawing.Point(8, 18);
            this.rbDNOff.Margin = new System.Windows.Forms.Padding(4);
            this.rbDNOff.Name = "rbDNOff";
            this.rbDNOff.Size = new System.Drawing.Size(41, 20);
            this.rbDNOff.TabIndex = 0;
            this.rbDNOff.TabStop = true;
            this.rbDNOff.Text = "Off";
            this.rbDNOff.UseVisualStyleBackColor = true;
            this.rbDNOff.CheckedChanged += new System.EventHandler(this.rbDNOff_CheckedChanged);
            // 
            // tbpAutoResponse
            // 
            this.tbpAutoResponse.Controls.Add(this.gnAutoScriptPermission);
            this.tbpAutoResponse.Controls.Add(this.gnAutoInventory);
            this.tbpAutoResponse.Controls.Add(this.txtAutoResponse);
            this.tbpAutoResponse.Controls.Add(this.gbAutoResponse);
            this.tbpAutoResponse.Location = new System.Drawing.Point(4, 25);
            this.tbpAutoResponse.Margin = new System.Windows.Forms.Padding(4);
            this.tbpAutoResponse.Name = "tbpAutoResponse";
            this.tbpAutoResponse.Padding = new System.Windows.Forms.Padding(4);
            this.tbpAutoResponse.Size = new System.Drawing.Size(699, 536);
            this.tbpAutoResponse.TabIndex = 2;
            this.tbpAutoResponse.Text = "Auto Response";
            this.tbpAutoResponse.UseVisualStyleBackColor = true;
            // 
            // gnAutoScriptPermission
            // 
            this.gnAutoScriptPermission.Controls.Add(this.cbAutoScriptPermission);
            this.gnAutoScriptPermission.Location = new System.Drawing.Point(12, 266);
            this.gnAutoScriptPermission.Margin = new System.Windows.Forms.Padding(4);
            this.gnAutoScriptPermission.Name = "gnAutoScriptPermission";
            this.gnAutoScriptPermission.Padding = new System.Windows.Forms.Padding(4);
            this.gnAutoScriptPermission.Size = new System.Drawing.Size(375, 66);
            this.gnAutoScriptPermission.TabIndex = 3;
            this.gnAutoScriptPermission.TabStop = false;
            this.gnAutoScriptPermission.Text = "On script permission questions";
            // 
            // cbAutoScriptPermission
            // 
            this.cbAutoScriptPermission.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbAutoScriptPermission.FormattingEnabled = true;
            this.cbAutoScriptPermission.Items.AddRange(new object[] {
            "Ask",
            "Auto Accept",
            "Auto Decline"});
            this.cbAutoScriptPermission.Location = new System.Drawing.Point(8, 23);
            this.cbAutoScriptPermission.Margin = new System.Windows.Forms.Padding(4);
            this.cbAutoScriptPermission.Name = "cbAutoScriptPermission";
            this.cbAutoScriptPermission.Size = new System.Drawing.Size(160, 24);
            this.cbAutoScriptPermission.TabIndex = 0;
            this.cbAutoScriptPermission.SelectedIndexChanged += new System.EventHandler(this.cbAutoScriptPermission_SelectedIndexChanged);
            // 
            // gnAutoInventory
            // 
            this.gnAutoInventory.Controls.Add(this.cbOnInvOffer);
            this.gnAutoInventory.Location = new System.Drawing.Point(12, 192);
            this.gnAutoInventory.Margin = new System.Windows.Forms.Padding(4);
            this.gnAutoInventory.Name = "gnAutoInventory";
            this.gnAutoInventory.Padding = new System.Windows.Forms.Padding(4);
            this.gnAutoInventory.Size = new System.Drawing.Size(375, 66);
            this.gnAutoInventory.TabIndex = 2;
            this.gnAutoInventory.TabStop = false;
            this.gnAutoInventory.Text = "On inventory offers";
            // 
            // cbOnInvOffer
            // 
            this.cbOnInvOffer.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbOnInvOffer.FormattingEnabled = true;
            this.cbOnInvOffer.Items.AddRange(new object[] {
            "Ask",
            "Auto Accept",
            "Auto Discard"});
            this.cbOnInvOffer.Location = new System.Drawing.Point(8, 23);
            this.cbOnInvOffer.Margin = new System.Windows.Forms.Padding(4);
            this.cbOnInvOffer.Name = "cbOnInvOffer";
            this.cbOnInvOffer.Size = new System.Drawing.Size(160, 24);
            this.cbOnInvOffer.TabIndex = 0;
            // 
            // txtAutoResponse
            // 
            this.txtAutoResponse.AccessibleName = "Auto response text";
            this.txtAutoResponse.Location = new System.Drawing.Point(11, 78);
            this.txtAutoResponse.Margin = new System.Windows.Forms.Padding(4);
            this.txtAutoResponse.Multiline = true;
            this.txtAutoResponse.Name = "txtAutoResponse";
            this.txtAutoResponse.Size = new System.Drawing.Size(375, 106);
            this.txtAutoResponse.TabIndex = 1;
            // 
            // gbAutoResponse
            // 
            this.gbAutoResponse.Controls.Add(this.rbAutoAlways);
            this.gbAutoResponse.Controls.Add(this.rbAutoNonFriend);
            this.gbAutoResponse.Controls.Add(this.rbAutobusy);
            this.gbAutoResponse.Location = new System.Drawing.Point(4, 7);
            this.gbAutoResponse.Margin = new System.Windows.Forms.Padding(4);
            this.gbAutoResponse.Name = "gbAutoResponse";
            this.gbAutoResponse.Padding = new System.Windows.Forms.Padding(4);
            this.gbAutoResponse.Size = new System.Drawing.Size(383, 63);
            this.gbAutoResponse.TabIndex = 0;
            this.gbAutoResponse.TabStop = false;
            this.gbAutoResponse.Text = "Send auto response when:";
            // 
            // rbAutoAlways
            // 
            this.rbAutoAlways.AutoSize = true;
            this.rbAutoAlways.Location = new System.Drawing.Point(288, 23);
            this.rbAutoAlways.Margin = new System.Windows.Forms.Padding(4);
            this.rbAutoAlways.Name = "rbAutoAlways";
            this.rbAutoAlways.Size = new System.Drawing.Size(68, 20);
            this.rbAutoAlways.TabIndex = 2;
            this.rbAutoAlways.Text = "Always";
            this.rbAutoAlways.UseVisualStyleBackColor = true;
            this.rbAutoAlways.CheckedChanged += new System.EventHandler(this.rbAutoAlways_CheckedChanged);
            // 
            // rbAutoNonFriend
            // 
            this.rbAutoNonFriend.AutoSize = true;
            this.rbAutoNonFriend.Location = new System.Drawing.Point(133, 23);
            this.rbAutoNonFriend.Margin = new System.Windows.Forms.Padding(4);
            this.rbAutoNonFriend.Name = "rbAutoNonFriend";
            this.rbAutoNonFriend.Size = new System.Drawing.Size(130, 20);
            this.rbAutoNonFriend.TabIndex = 1;
            this.rbAutoNonFriend.Text = "IM from non-friend";
            this.rbAutoNonFriend.UseVisualStyleBackColor = true;
            this.rbAutoNonFriend.CheckedChanged += new System.EventHandler(this.rbAutoNonFriend_CheckedChanged);
            // 
            // rbAutobusy
            // 
            this.rbAutobusy.AutoSize = true;
            this.rbAutobusy.Checked = true;
            this.rbAutobusy.Location = new System.Drawing.Point(8, 23);
            this.rbAutobusy.Margin = new System.Windows.Forms.Padding(4);
            this.rbAutobusy.Name = "rbAutobusy";
            this.rbAutobusy.Size = new System.Drawing.Size(105, 20);
            this.rbAutobusy.TabIndex = 0;
            this.rbAutobusy.TabStop = true;
            this.rbAutobusy.Text = "In busy mode";
            this.rbAutobusy.UseVisualStyleBackColor = true;
            this.rbAutobusy.CheckedChanged += new System.EventHandler(this.rbAutobusy_CheckedChanged);
            // 
            // tbpGraphics
            // 
            this.tbpGraphics.Location = new System.Drawing.Point(4, 25);
            this.tbpGraphics.Margin = new System.Windows.Forms.Padding(4);
            this.tbpGraphics.Name = "tbpGraphics";
            this.tbpGraphics.Padding = new System.Windows.Forms.Padding(4);
            this.tbpGraphics.Size = new System.Drawing.Size(699, 536);
            this.tbpGraphics.TabIndex = 3;
            this.tbpGraphics.Text = "Graphics Settings";
            this.tbpGraphics.UseVisualStyleBackColor = true;
            // 
            // tbpBot
            // 
            this.tbpBot.Controls.Add(this.gbLSLHelper);
            this.tbpBot.Controls.Add(this.pseudoHome);
            this.tbpBot.Controls.Add(this.autoSit);
            this.tbpBot.Location = new System.Drawing.Point(4, 25);
            this.tbpBot.Margin = new System.Windows.Forms.Padding(4);
            this.tbpBot.Name = "tbpBot";
            this.tbpBot.Size = new System.Drawing.Size(699, 536);
            this.tbpBot.TabIndex = 4;
            this.tbpBot.Text = "Automation";
            this.tbpBot.UseVisualStyleBackColor = true;
            // 
            // gbLSLHelper
            // 
            this.gbLSLHelper.Controls.Add(this.llLSLHelperInstructios);
            this.gbLSLHelper.Controls.Add(this.cbLSLHelperEnabled);
            this.gbLSLHelper.Controls.Add(this.tbLSLAllowedOwner);
            this.gbLSLHelper.Controls.Add(this.lblLSLUUID);
            this.gbLSLHelper.Location = new System.Drawing.Point(11, 268);
            this.gbLSLHelper.Margin = new System.Windows.Forms.Padding(4);
            this.gbLSLHelper.Name = "gbLSLHelper";
            this.gbLSLHelper.Padding = new System.Windows.Forms.Padding(4);
            this.gbLSLHelper.Size = new System.Drawing.Size(351, 209);
            this.gbLSLHelper.TabIndex = 2;
            this.gbLSLHelper.TabStop = false;
            this.gbLSLHelper.Text = "LSL Helper";
            // 
            // llLSLHelperInstructios
            // 
            this.llLSLHelperInstructios.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.llLSLHelperInstructios.AutoSize = true;
            this.llLSLHelperInstructios.Location = new System.Drawing.Point(261, 180);
            this.llLSLHelperInstructios.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.llLSLHelperInstructios.Name = "llLSLHelperInstructios";
            this.llLSLHelperInstructios.Size = new System.Drawing.Size(73, 16);
            this.llLSLHelperInstructios.TabIndex = 4;
            this.llLSLHelperInstructios.TabStop = true;
            this.llLSLHelperInstructios.Text = "Instructions";
            this.llLSLHelperInstructios.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.llLSLHelperInstructios_LinkClicked);
            // 
            // cbLSLHelperEnabled
            // 
            this.cbLSLHelperEnabled.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.cbLSLHelperEnabled.AutoSize = true;
            this.cbLSLHelperEnabled.Location = new System.Drawing.Point(12, 180);
            this.cbLSLHelperEnabled.Margin = new System.Windows.Forms.Padding(4);
            this.cbLSLHelperEnabled.Name = "cbLSLHelperEnabled";
            this.cbLSLHelperEnabled.Size = new System.Drawing.Size(77, 20);
            this.cbLSLHelperEnabled.TabIndex = 3;
            this.cbLSLHelperEnabled.Text = "Enabled";
            this.cbLSLHelperEnabled.UseVisualStyleBackColor = true;
            this.cbLSLHelperEnabled.CheckedChanged += new System.EventHandler(this.cbLSLHelperEnabled_CheckedChanged);
            // 
            // tbLSLAllowedOwner
            // 
            this.tbLSLAllowedOwner.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tbLSLAllowedOwner.Location = new System.Drawing.Point(12, 39);
            this.tbLSLAllowedOwner.Margin = new System.Windows.Forms.Padding(4);
            this.tbLSLAllowedOwner.MaxLength = 0;
            this.tbLSLAllowedOwner.Multiline = true;
            this.tbLSLAllowedOwner.Name = "tbLSLAllowedOwner";
            this.tbLSLAllowedOwner.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.tbLSLAllowedOwner.Size = new System.Drawing.Size(329, 131);
            this.tbLSLAllowedOwner.TabIndex = 2;
            this.tbLSLAllowedOwner.Leave += new System.EventHandler(this.tbLSLAllowedOwner_Leave);
            // 
            // lblLSLUUID
            // 
            this.lblLSLUUID.AutoSize = true;
            this.lblLSLUUID.Location = new System.Drawing.Point(8, 20);
            this.lblLSLUUID.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblLSLUUID.Name = "lblLSLUUID";
            this.lblLSLUUID.Size = new System.Drawing.Size(192, 16);
            this.lblLSLUUID.TabIndex = 0;
            this.lblLSLUUID.Text = "Allowed object owners (UUIDs)";
            this.lblLSLUUID.Click += new System.EventHandler(this.lblLSLUUID_Click);
            // 
            // pseudoHome
            // 
            this.pseudoHome.Controls.Add(this.pseudoHomeSet);
            this.pseudoHome.Controls.Add(this.pseudoHomeTolerance);
            this.pseudoHome.Controls.Add(this.pseudoHomeTP);
            this.pseudoHome.Controls.Add(this.pseudoHomeLocation);
            this.pseudoHome.Controls.Add(this.pseudoHomeClear);
            this.pseudoHome.Controls.Add(this.pseudoHomeToleranceLabel);
            this.pseudoHome.Controls.Add(this.pseudoHomeEnabled);
            this.pseudoHome.Location = new System.Drawing.Point(11, 128);
            this.pseudoHome.Margin = new System.Windows.Forms.Padding(4);
            this.pseudoHome.Name = "pseudoHome";
            this.pseudoHome.Padding = new System.Windows.Forms.Padding(4);
            this.pseudoHome.Size = new System.Drawing.Size(351, 133);
            this.pseudoHome.TabIndex = 1;
            this.pseudoHome.TabStop = false;
            this.pseudoHome.Text = "Pseudo Home";
            // 
            // pseudoHomeSet
            // 
            this.pseudoHomeSet.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.pseudoHomeSet.Location = new System.Drawing.Point(125, 90);
            this.pseudoHomeSet.Margin = new System.Windows.Forms.Padding(4);
            this.pseudoHomeSet.Name = "pseudoHomeSet";
            this.pseudoHomeSet.Size = new System.Drawing.Size(100, 28);
            this.pseudoHomeSet.TabIndex = 9;
            this.pseudoHomeSet.Text = "Set";
            this.pseudoHomeSet.UseVisualStyleBackColor = true;
            this.pseudoHomeSet.Click += new System.EventHandler(this.pseudoHomeSet_Click);
            // 
            // pseudoHomeTolerance
            // 
            this.pseudoHomeTolerance.Location = new System.Drawing.Point(116, 17);
            this.pseudoHomeTolerance.Margin = new System.Windows.Forms.Padding(4);
            this.pseudoHomeTolerance.Maximum = new decimal(new int[] {
            256,
            0,
            0,
            0});
            this.pseudoHomeTolerance.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.pseudoHomeTolerance.Name = "pseudoHomeTolerance";
            this.pseudoHomeTolerance.Size = new System.Drawing.Size(53, 22);
            this.pseudoHomeTolerance.TabIndex = 6;
            this.pseudoHomeTolerance.Value = new decimal(new int[] {
            256,
            0,
            0,
            0});
            this.pseudoHomeTolerance.ValueChanged += new System.EventHandler(this.pseudoHomeTolerance_ValueChanged);
            // 
            // pseudoHomeTP
            // 
            this.pseudoHomeTP.Location = new System.Drawing.Point(8, 47);
            this.pseudoHomeTP.Margin = new System.Windows.Forms.Padding(4);
            this.pseudoHomeTP.Name = "pseudoHomeTP";
            this.pseudoHomeTP.Size = new System.Drawing.Size(100, 28);
            this.pseudoHomeTP.TabIndex = 6;
            this.pseudoHomeTP.Text = "Teleport";
            this.pseudoHomeTP.UseVisualStyleBackColor = true;
            this.pseudoHomeTP.Click += new System.EventHandler(this.pseudoHomeTP_Click);
            // 
            // pseudoHomeLocation
            // 
            this.pseudoHomeLocation.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.pseudoHomeLocation.Location = new System.Drawing.Point(116, 49);
            this.pseudoHomeLocation.Margin = new System.Windows.Forms.Padding(4);
            this.pseudoHomeLocation.Name = "pseudoHomeLocation";
            this.pseudoHomeLocation.ReadOnly = true;
            this.pseudoHomeLocation.Size = new System.Drawing.Size(225, 22);
            this.pseudoHomeLocation.TabIndex = 7;
            // 
            // pseudoHomeClear
            // 
            this.pseudoHomeClear.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.pseudoHomeClear.Location = new System.Drawing.Point(243, 90);
            this.pseudoHomeClear.Margin = new System.Windows.Forms.Padding(4);
            this.pseudoHomeClear.Name = "pseudoHomeClear";
            this.pseudoHomeClear.Size = new System.Drawing.Size(100, 28);
            this.pseudoHomeClear.TabIndex = 10;
            this.pseudoHomeClear.Text = "Clear";
            this.pseudoHomeClear.UseVisualStyleBackColor = true;
            this.pseudoHomeClear.Click += new System.EventHandler(this.pseudoHomeClear_Click);
            // 
            // pseudoHomeToleranceLabel
            // 
            this.pseudoHomeToleranceLabel.AutoSize = true;
            this.pseudoHomeToleranceLabel.Location = new System.Drawing.Point(8, 20);
            this.pseudoHomeToleranceLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.pseudoHomeToleranceLabel.Name = "pseudoHomeToleranceLabel";
            this.pseudoHomeToleranceLabel.Size = new System.Drawing.Size(69, 16);
            this.pseudoHomeToleranceLabel.TabIndex = 6;
            this.pseudoHomeToleranceLabel.Text = "Tolerance";
            // 
            // pseudoHomeEnabled
            // 
            this.pseudoHomeEnabled.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.pseudoHomeEnabled.AutoSize = true;
            this.pseudoHomeEnabled.Location = new System.Drawing.Point(12, 99);
            this.pseudoHomeEnabled.Margin = new System.Windows.Forms.Padding(4);
            this.pseudoHomeEnabled.Name = "pseudoHomeEnabled";
            this.pseudoHomeEnabled.Size = new System.Drawing.Size(77, 20);
            this.pseudoHomeEnabled.TabIndex = 8;
            this.pseudoHomeEnabled.Text = "Enabled";
            this.pseudoHomeEnabled.UseVisualStyleBackColor = true;
            this.pseudoHomeEnabled.CheckedChanged += new System.EventHandler(this.pseudoHomeEnabled_CheckedChanged);
            // 
            // autoSit
            // 
            this.autoSit.Controls.Add(this.autoSitEnabled);
            this.autoSit.Controls.Add(this.autoSitSit);
            this.autoSit.Controls.Add(this.autoSitClear);
            this.autoSit.Controls.Add(this.autoSitUUIDLabel);
            this.autoSit.Controls.Add(this.autoSitUUID);
            this.autoSit.Controls.Add(this.autoSitName);
            this.autoSit.Controls.Add(this.autoSitNameLabel);
            this.autoSit.Location = new System.Drawing.Point(11, 4);
            this.autoSit.Margin = new System.Windows.Forms.Padding(4);
            this.autoSit.Name = "autoSit";
            this.autoSit.Padding = new System.Windows.Forms.Padding(4);
            this.autoSit.Size = new System.Drawing.Size(351, 117);
            this.autoSit.TabIndex = 0;
            this.autoSit.TabStop = false;
            this.autoSit.Text = "Auto-Sit";
            // 
            // autoSitEnabled
            // 
            this.autoSitEnabled.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.autoSitEnabled.AutoSize = true;
            this.autoSitEnabled.Location = new System.Drawing.Point(12, 87);
            this.autoSitEnabled.Margin = new System.Windows.Forms.Padding(4);
            this.autoSitEnabled.Name = "autoSitEnabled";
            this.autoSitEnabled.Size = new System.Drawing.Size(77, 20);
            this.autoSitEnabled.TabIndex = 2;
            this.autoSitEnabled.Text = "Enabled";
            this.autoSitEnabled.UseVisualStyleBackColor = true;
            this.autoSitEnabled.CheckedChanged += new System.EventHandler(this.autoSitEnabled_CheckedChanged);
            // 
            // autoSitSit
            // 
            this.autoSitSit.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.autoSitSit.Location = new System.Drawing.Point(125, 81);
            this.autoSitSit.Margin = new System.Windows.Forms.Padding(4);
            this.autoSitSit.Name = "autoSitSit";
            this.autoSitSit.Size = new System.Drawing.Size(100, 28);
            this.autoSitSit.TabIndex = 3;
            this.autoSitSit.Text = "Sit";
            this.autoSitSit.UseVisualStyleBackColor = true;
            this.autoSitSit.Click += new System.EventHandler(this.autoSitSit_Click);
            // 
            // autoSitClear
            // 
            this.autoSitClear.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.autoSitClear.Location = new System.Drawing.Point(243, 81);
            this.autoSitClear.Margin = new System.Windows.Forms.Padding(4);
            this.autoSitClear.Name = "autoSitClear";
            this.autoSitClear.Size = new System.Drawing.Size(100, 28);
            this.autoSitClear.TabIndex = 4;
            this.autoSitClear.Text = "Clear";
            this.autoSitClear.UseVisualStyleBackColor = true;
            this.autoSitClear.Click += new System.EventHandler(this.autoSitClear_Click);
            // 
            // autoSitUUIDLabel
            // 
            this.autoSitUUIDLabel.AutoSize = true;
            this.autoSitUUIDLabel.Location = new System.Drawing.Point(8, 53);
            this.autoSitUUIDLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.autoSitUUIDLabel.Name = "autoSitUUIDLabel";
            this.autoSitUUIDLabel.Size = new System.Drawing.Size(40, 16);
            this.autoSitUUIDLabel.TabIndex = 4;
            this.autoSitUUIDLabel.Text = "UUID";
            this.autoSitUUIDLabel.Click += new System.EventHandler(this.autoSitUUIDLabel_Click);
            // 
            // autoSitUUID
            // 
            this.autoSitUUID.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.autoSitUUID.Location = new System.Drawing.Point(63, 49);
            this.autoSitUUID.Margin = new System.Windows.Forms.Padding(4);
            this.autoSitUUID.MaxLength = 36;
            this.autoSitUUID.Name = "autoSitUUID";
            this.autoSitUUID.Size = new System.Drawing.Size(279, 22);
            this.autoSitUUID.TabIndex = 1;
            this.autoSitUUID.Leave += new System.EventHandler(this.autoSitUUID_Leave);
            // 
            // autoSitName
            // 
            this.autoSitName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.autoSitName.Location = new System.Drawing.Point(63, 16);
            this.autoSitName.Margin = new System.Windows.Forms.Padding(4);
            this.autoSitName.Name = "autoSitName";
            this.autoSitName.ReadOnly = true;
            this.autoSitName.Size = new System.Drawing.Size(279, 22);
            this.autoSitName.TabIndex = 0;
            // 
            // autoSitNameLabel
            // 
            this.autoSitNameLabel.AutoSize = true;
            this.autoSitNameLabel.Location = new System.Drawing.Point(8, 20);
            this.autoSitNameLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.autoSitNameLabel.Name = "autoSitNameLabel";
            this.autoSitNameLabel.Size = new System.Drawing.Size(44, 16);
            this.autoSitNameLabel.TabIndex = 1;
            this.autoSitNameLabel.Text = "Name";
            this.autoSitNameLabel.Click += new System.EventHandler(this.autoSitNameLabel_Click);
            // 
            // tbpChat
            // 
            this.tbpChat.Controls.Add(this.btnSave);
            this.tbpChat.Controls.Add(this.btnResetFontSettings);
            this.tbpChat.Controls.Add(this.lblPreview);
            this.tbpChat.Controls.Add(this.cbxItalic);
            this.tbpChat.Controls.Add(this.label10);
            this.tbpChat.Controls.Add(this.label9);
            this.tbpChat.Controls.Add(this.cbxBold);
            this.tbpChat.Controls.Add(this.lbxColorItems);
            this.tbpChat.Controls.Add(this.label8);
            this.tbpChat.Controls.Add(this.label7);
            this.tbpChat.Controls.Add(this.label6);
            this.tbpChat.Controls.Add(this.label5);
            this.tbpChat.Controls.Add(this.cbxBackground);
            this.tbpChat.Controls.Add(this.cbxForeground);
            this.tbpChat.Controls.Add(this.cbxFontSize);
            this.tbpChat.Controls.Add(this.cbxFont);
            this.tbpChat.Location = new System.Drawing.Point(4, 25);
            this.tbpChat.Margin = new System.Windows.Forms.Padding(4);
            this.tbpChat.Name = "tbpChat";
            this.tbpChat.Padding = new System.Windows.Forms.Padding(4);
            this.tbpChat.Size = new System.Drawing.Size(699, 536);
            this.tbpChat.TabIndex = 5;
            this.tbpChat.Text = "Chat";
            this.tbpChat.UseVisualStyleBackColor = true;
            // 
            // btnSave
            // 
            this.btnSave.Location = new System.Drawing.Point(451, 357);
            this.btnSave.Margin = new System.Windows.Forms.Padding(4);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(65, 28);
            this.btnSave.TabIndex = 18;
            this.btnSave.Text = "Save";
            this.btnSave.UseVisualStyleBackColor = true;
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);
            // 
            // btnResetFontSettings
            // 
            this.btnResetFontSettings.Location = new System.Drawing.Point(15, 357);
            this.btnResetFontSettings.Margin = new System.Windows.Forms.Padding(4);
            this.btnResetFontSettings.Name = "btnResetFontSettings";
            this.btnResetFontSettings.Size = new System.Drawing.Size(141, 28);
            this.btnResetFontSettings.TabIndex = 17;
            this.btnResetFontSettings.Text = "Reset All Colors";
            this.btnResetFontSettings.UseVisualStyleBackColor = true;
            this.btnResetFontSettings.Click += new System.EventHandler(this.btnResetFontSettings_Click);
            // 
            // lblPreview
            // 
            this.lblPreview.BackColor = System.Drawing.Color.Transparent;
            this.lblPreview.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblPreview.Location = new System.Drawing.Point(336, 272);
            this.lblPreview.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblPreview.Name = "lblPreview";
            this.lblPreview.Size = new System.Drawing.Size(179, 77);
            this.lblPreview.TabIndex = 16;
            this.lblPreview.Text = "Preview";
            this.lblPreview.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // cbxItalic
            // 
            this.cbxItalic.AutoSize = true;
            this.cbxItalic.Location = new System.Drawing.Point(407, 213);
            this.cbxItalic.Margin = new System.Windows.Forms.Padding(4);
            this.cbxItalic.Name = "cbxItalic";
            this.cbxItalic.Size = new System.Drawing.Size(53, 20);
            this.cbxItalic.TabIndex = 15;
            this.cbxItalic.Text = "Italic";
            this.cbxItalic.UseVisualStyleBackColor = true;
            this.cbxItalic.CheckStateChanged += new System.EventHandler(this.cbxItalic_CheckStateChanged);
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Location = new System.Drawing.Point(11, 85);
            this.label10.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(42, 16);
            this.label10.TabIndex = 14;
            this.label10.Text = "Items:";
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(332, 251);
            this.label9.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(58, 16);
            this.label9.TabIndex = 11;
            this.label9.Text = "Preview:";
            // 
            // cbxBold
            // 
            this.cbxBold.AutoSize = true;
            this.cbxBold.Location = new System.Drawing.Point(336, 213);
            this.cbxBold.Margin = new System.Windows.Forms.Padding(4);
            this.cbxBold.Name = "cbxBold";
            this.cbxBold.Size = new System.Drawing.Size(54, 20);
            this.cbxBold.TabIndex = 9;
            this.cbxBold.Text = "Bold";
            this.cbxBold.UseVisualStyleBackColor = true;
            this.cbxBold.CheckStateChanged += new System.EventHandler(this.cbxBold_CheckStateChanged);
            // 
            // lbxColorItems
            // 
            this.lbxColorItems.FormattingEnabled = true;
            this.lbxColorItems.ItemHeight = 16;
            this.lbxColorItems.Location = new System.Drawing.Point(15, 105);
            this.lbxColorItems.Margin = new System.Windows.Forms.Padding(4);
            this.lbxColorItems.Name = "lbxColorItems";
            this.lbxColorItems.Size = new System.Drawing.Size(308, 244);
            this.lbxColorItems.TabIndex = 8;
            this.lbxColorItems.SelectedIndexChanged += new System.EventHandler(this.lbxColorItems_SelectedIndexChanged);
            this.lbxColorItems.MouseDown += new System.Windows.Forms.MouseEventHandler(this.lbxColorItems_MouseDown);
            this.lbxColorItems.MouseMove += new System.Windows.Forms.MouseEventHandler(this.lbxColorItems_MouseMove);
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(332, 18);
            this.label8.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(36, 16);
            this.label8.TabIndex = 7;
            this.label8.Text = "Size:";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(11, 18);
            this.label7.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(36, 16);
            this.label7.TabIndex = 6;
            this.label7.Text = "Font:";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(332, 85);
            this.label6.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(80, 16);
            this.label6.TabIndex = 5;
            this.label6.Text = "Foreground:";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(332, 148);
            this.label5.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(83, 16);
            this.label5.TabIndex = 4;
            this.label5.Text = "Background:";
            // 
            // cbxBackground
            // 
            this.cbxBackground.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            this.cbxBackground.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbxBackground.FormattingEnabled = true;
            this.cbxBackground.Location = new System.Drawing.Point(336, 167);
            this.cbxBackground.Margin = new System.Windows.Forms.Padding(4);
            this.cbxBackground.Name = "cbxBackground";
            this.cbxBackground.Size = new System.Drawing.Size(179, 23);
            this.cbxBackground.TabIndex = 3;
            this.cbxBackground.DrawItem += new System.Windows.Forms.DrawItemEventHandler(this.cbxForeground_DrawItem);
            this.cbxBackground.SelectedIndexChanged += new System.EventHandler(this.SelectedIndexChanged);
            // 
            // cbxForeground
            // 
            this.cbxForeground.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            this.cbxForeground.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbxForeground.FormattingEnabled = true;
            this.cbxForeground.Location = new System.Drawing.Point(336, 105);
            this.cbxForeground.Margin = new System.Windows.Forms.Padding(4);
            this.cbxForeground.Name = "cbxForeground";
            this.cbxForeground.Size = new System.Drawing.Size(179, 23);
            this.cbxForeground.TabIndex = 2;
            this.cbxForeground.DrawItem += new System.Windows.Forms.DrawItemEventHandler(this.cbxForeground_DrawItem);
            this.cbxForeground.SelectedIndexChanged += new System.EventHandler(this.SelectedIndexChanged);
            // 
            // cbxFontSize
            // 
            this.cbxFontSize.FormattingEnabled = true;
            this.cbxFontSize.Location = new System.Drawing.Point(336, 38);
            this.cbxFontSize.Margin = new System.Windows.Forms.Padding(4);
            this.cbxFontSize.Name = "cbxFontSize";
            this.cbxFontSize.Size = new System.Drawing.Size(179, 24);
            this.cbxFontSize.TabIndex = 1;
            this.cbxFontSize.SelectedIndexChanged += new System.EventHandler(this.SelectedIndexChanged);
            // 
            // cbxFont
            // 
            this.cbxFont.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            this.cbxFont.FormattingEnabled = true;
            this.cbxFont.ItemHeight = 15;
            this.cbxFont.Location = new System.Drawing.Point(15, 38);
            this.cbxFont.Margin = new System.Windows.Forms.Padding(4);
            this.cbxFont.Name = "cbxFont";
            this.cbxFont.Size = new System.Drawing.Size(308, 21);
            this.cbxFont.TabIndex = 0;
            this.cbxFont.DrawItem += new System.Windows.Forms.DrawItemEventHandler(this.cbxFont_DrawItem);
            this.cbxFont.SelectedIndexChanged += new System.EventHandler(this.SelectedIndexChanged);
            // 
            // fontDialog1
            // 
            this.fontDialog1.ShowColor = true;
            // 
            // frmSettings
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(707, 565);
            this.Controls.Add(this.tcGraphics);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "frmSettings";
            this.Text = "Settings - Radegast";
            this.tcGraphics.ResumeLayout(false);
            this.tbpGeneral.ResumeLayout(false);
            this.cbHighLight.ResumeLayout(false);
            this.cbHighLight.PerformLayout();
            this.cbMisc.ResumeLayout(false);
            this.cbMisc.PerformLayout();
            this.Chat.ResumeLayout(false);
            this.Chat.PerformLayout();
            this.gbDisplayNames.ResumeLayout(false);
            this.gbDisplayNames.PerformLayout();
            this.tbpAutoResponse.ResumeLayout(false);
            this.tbpAutoResponse.PerformLayout();
            this.gnAutoScriptPermission.ResumeLayout(false);
            this.gnAutoInventory.ResumeLayout(false);
            this.gbAutoResponse.ResumeLayout(false);
            this.gbAutoResponse.PerformLayout();
            this.tbpBot.ResumeLayout(false);
            this.gbLSLHelper.ResumeLayout(false);
            this.gbLSLHelper.PerformLayout();
            this.pseudoHome.ResumeLayout(false);
            this.pseudoHome.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pseudoHomeTolerance)).EndInit();
            this.autoSit.ResumeLayout(false);
            this.autoSit.PerformLayout();
            this.tbpChat.ResumeLayout(false);
            this.tbpChat.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        public System.Windows.Forms.TabControl tcGraphics;
        public System.Windows.Forms.TabPage tbpGeneral;
        public System.Windows.Forms.CheckBox cbIMTimeStamps;
        public System.Windows.Forms.CheckBox cbChatTimestamps;
        public System.Windows.Forms.CheckBox cbTransactChat;
        public System.Windows.Forms.CheckBox cbTrasactDialog;
        public System.Windows.Forms.CheckBox cbFriendsNotifications;
        public System.Windows.Forms.CheckBox cbAutoReconnect;
        public System.Windows.Forms.CheckBox cbHideLoginGraphics;
        public System.Windows.Forms.CheckBox cbRLV;
        public System.Windows.Forms.CheckBox cbRLVDebug;
        public System.Windows.Forms.CheckBox cbMUEmotes;
        public System.Windows.Forms.CheckBox cbFriendsHighlight;
        public System.Windows.Forms.CheckBox cbMinToTrey;
        public System.Windows.Forms.CheckBox cbNoTyping;
        public System.Windows.Forms.TextBox txtReconnectTime;
        public System.Windows.Forms.Label label2;
        public System.Windows.Forms.TabPage tbpAutoResponse;
        public System.Windows.Forms.GroupBox gbAutoResponse;
        public System.Windows.Forms.TextBox txtAutoResponse;
        public System.Windows.Forms.RadioButton rbAutoAlways;
        public System.Windows.Forms.RadioButton rbAutoNonFriend;
        public System.Windows.Forms.RadioButton rbAutobusy;
        public System.Windows.Forms.CheckBox cbSyntaxHighlight;
        public System.Windows.Forms.GroupBox gbDisplayNames;
        public System.Windows.Forms.RadioButton rbDNOnlyDN;
        public System.Windows.Forms.RadioButton rbDNDandUsernme;
        public System.Windows.Forms.RadioButton rbDNSmart;
        public System.Windows.Forms.RadioButton rbDNOff;
        public System.Windows.Forms.CheckBox cbTaskBarHighLight;
        public System.Windows.Forms.TabPage tbpGraphics;
        public System.Windows.Forms.GroupBox gnAutoInventory;
        public System.Windows.Forms.ComboBox cbOnInvOffer;
        public System.Windows.Forms.CheckBox cbDisableChatIMLog;
        public System.Windows.Forms.GroupBox Chat;
        public System.Windows.Forms.CheckBox cbRadegastLogToFile;
        public System.Windows.Forms.GroupBox cbMisc;
        public System.Windows.Forms.CheckBox cbDisableLookAt;
        public System.Windows.Forms.GroupBox cbHighLight;
        public System.Windows.Forms.CheckBox cbHighlightGroupIM;
        public System.Windows.Forms.CheckBox cbHighlightIM;
        public System.Windows.Forms.CheckBox cbHighlightChat;
        public System.Windows.Forms.TabPage tbpBot;
        public System.Windows.Forms.GroupBox autoSit;
        public System.Windows.Forms.Button autoSitClear;
        public System.Windows.Forms.Button autoSitSit;
        public System.Windows.Forms.Label autoSitUUIDLabel;
        public System.Windows.Forms.TextBox autoSitUUID;
        public System.Windows.Forms.TextBox autoSitName;
        public System.Windows.Forms.Label autoSitNameLabel;
        public System.Windows.Forms.CheckBox autoSitEnabled;
        public System.Windows.Forms.GroupBox pseudoHome;
        public System.Windows.Forms.TextBox pseudoHomeLocation;
        public System.Windows.Forms.CheckBox pseudoHomeEnabled;
        public System.Windows.Forms.Button pseudoHomeTP;
        public System.Windows.Forms.Button pseudoHomeSet;
        public System.Windows.Forms.Button pseudoHomeClear;
        public System.Windows.Forms.NumericUpDown pseudoHomeTolerance;
        public System.Windows.Forms.Label pseudoHomeToleranceLabel;
        public System.Windows.Forms.CheckBox cbNameLinks;
        private System.Windows.Forms.GroupBox gbLSLHelper;
        private System.Windows.Forms.LinkLabel llLSLHelperInstructios;
        public System.Windows.Forms.CheckBox cbLSLHelperEnabled;
        public System.Windows.Forms.TextBox tbLSLAllowedOwner;
        private System.Windows.Forms.Label lblLSLUUID;
        public System.Windows.Forms.CheckBox cbShowScriptErrors;
        public System.Windows.Forms.GroupBox gnAutoScriptPermission;
        public System.Windows.Forms.ComboBox cbAutoScriptPermission;
        public System.Windows.Forms.TextBox txtResolveURITime;
        public System.Windows.Forms.Label label4;
        public System.Windows.Forms.CheckBox cbResolveURIs;
        private System.Windows.Forms.CheckBox cbConfirmExit;
        private System.Windows.Forms.CheckBox cbThemeCompatibilityMode;
        private System.Windows.Forms.TabPage tbpChat;
        private System.Windows.Forms.FontDialog fontDialog1;
        private System.Windows.Forms.CheckBox cbxItalic;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.CheckBox cbxBold;
        private System.Windows.Forms.ListBox lbxColorItems;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.ComboBox cbxBackground;
        private System.Windows.Forms.ComboBox cbxForeground;
        private System.Windows.Forms.ComboBox cbxFontSize;
        private System.Windows.Forms.ComboBox cbxFont;
        private System.Windows.Forms.Label lblPreview;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnResetFontSettings;
        private System.Windows.Forms.TextBox txtChatLogDir;
        private System.Windows.Forms.Label lblChatLog;
        private System.Windows.Forms.Button btnChatLogDir;
        public System.Windows.Forms.CheckBox cbGroupIMSound;
    }
}
