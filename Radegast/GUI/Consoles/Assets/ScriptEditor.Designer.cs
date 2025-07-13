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
    partial class ScriptEditor
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ScriptEditor));
            this.ttKeyWords = new System.Windows.Forms.ToolTip(this.components);
            this.tsMenu = new System.Windows.Forms.ToolStrip();
            this.tbtbFile = new System.Windows.Forms.ToolStripDropDownButton();
            this.tbtbLoadFromDisk = new System.Windows.Forms.ToolStripMenuItem();
            this.tbtbSave = new System.Windows.Forms.ToolStripMenuItem();
            this.tbtbSaveToDisk = new System.Windows.Forms.ToolStripMenuItem();
            this.tbtbSaveToDiskAs = new System.Windows.Forms.ToolStripMenuItem();
            this.tSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.tbtnExit = new System.Windows.Forms.ToolStripMenuItem();
            this.tbtnEdit = new System.Windows.Forms.ToolStripDropDownButton();
            this.undoToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.redoToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripSeparator();
            this.cutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.copyToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.pasteToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.deleteToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.selectAllToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem2 = new System.Windows.Forms.ToolStripSeparator();
            this.findToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.syntaxHiglightingToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.tbtnAttach = new System.Windows.Forms.ToolStripButton();
            this.tsStatus = new System.Windows.Forms.ToolStrip();
            this.lblScripStatus = new System.Windows.Forms.ToolStripLabel();
            this.lblCol = new System.Windows.Forms.ToolStripLabel();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.lblLine = new System.Windows.Forms.ToolStripLabel();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.btnSave = new System.Windows.Forms.ToolStripButton();
            this.cbMono = new Radegast.ToolStripCheckBox();
            this.cbRunning = new Radegast.ToolStripCheckBox();
            this.tsFindReplace = new System.Windows.Forms.ToolStrip();
            this.tfindClose = new System.Windows.Forms.ToolStripButton();
            this.tfindFindText = new System.Windows.Forms.ToolStripTextBox();
            this.tfindDoFind = new System.Windows.Forms.ToolStripButton();
            this.tfindMatchCase = new Radegast.ToolStripCheckBox();
            this.toolStripButton1 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripLabel1 = new System.Windows.Forms.ToolStripLabel();
            this.tfindReplaceText = new System.Windows.Forms.ToolStripTextBox();
            this.tfindFindNextReplace = new System.Windows.Forms.ToolStripButton();
            this.tfindReplace = new System.Windows.Forms.ToolStripButton();
            this.tfindReplaceAll = new System.Windows.Forms.ToolStripButton();
            this.rtb = new Radegast.RRichTextBox();
            this.lineNubersForRtb = new Radegast.LineNumberPanel();
            this.txtStatus = new System.Windows.Forms.TextBox();
            this.tsMenu.SuspendLayout();
            this.tsStatus.SuspendLayout();
            this.tsFindReplace.SuspendLayout();
            this.SuspendLayout();
            // 
            // tsMenu
            // 
            this.tsMenu.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.tsMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tbtbFile,
            this.tbtnEdit,
            this.tbtnAttach});
            this.tsMenu.Location = new System.Drawing.Point(0, 0);
            this.tsMenu.Name = "tsMenu";
            this.tsMenu.Size = new System.Drawing.Size(661, 25);
            this.tsMenu.TabIndex = 2;
            // 
            // tbtbFile
            // 
            this.tbtbFile.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tbtbFile.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tbtbLoadFromDisk,
            this.tbtbSave,
            this.tbtbSaveToDisk,
            this.tbtbSaveToDiskAs,
            this.tSeparator1,
            this.tbtnExit});
            this.tbtbFile.Image = ((System.Drawing.Image)(resources.GetObject("tbtbFile.Image")));
            this.tbtbFile.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tbtbFile.Name = "tbtbFile";
            this.tbtbFile.Size = new System.Drawing.Size(38, 22);
            this.tbtbFile.Text = "File";
            // 
            // tbtbLoadFromDisk
            // 
            this.tbtbLoadFromDisk.Name = "tbtbLoadFromDisk";
            this.tbtbLoadFromDisk.Size = new System.Drawing.Size(165, 22);
            this.tbtbLoadFromDisk.Text = "Open...";
            this.tbtbLoadFromDisk.Click += new System.EventHandler(this.tbtbLoadFromDisk_Click);
            // 
            // tbtbSave
            // 
            this.tbtbSave.Name = "tbtbSave";
            this.tbtbSave.ShortcutKeyDisplayString = "Ctrl-S";
            this.tbtbSave.Size = new System.Drawing.Size(165, 22);
            this.tbtbSave.Text = "&Save";
            this.tbtbSave.Click += new System.EventHandler(this.tbtbSave_Click);
            // 
            // tbtbSaveToDisk
            // 
            this.tbtbSaveToDisk.Name = "tbtbSaveToDisk";
            this.tbtbSaveToDisk.Size = new System.Drawing.Size(165, 22);
            this.tbtbSaveToDisk.Text = "Save To Disk";
            this.tbtbSaveToDisk.Click += new System.EventHandler(this.tbtbSaveToDisk_Click_1);
            // 
            // tbtbSaveToDiskAs
            // 
            this.tbtbSaveToDiskAs.Name = "tbtbSaveToDiskAs";
            this.tbtbSaveToDiskAs.Size = new System.Drawing.Size(165, 22);
            this.tbtbSaveToDiskAs.Text = "Save To Disk As...";
            this.tbtbSaveToDiskAs.Click += new System.EventHandler(this.tbtbSaveToDisk_Click);
            // 
            // tSeparator1
            // 
            this.tSeparator1.Name = "tSeparator1";
            this.tSeparator1.Size = new System.Drawing.Size(162, 6);
            this.tSeparator1.Visible = false;
            // 
            // tbtnExit
            // 
            this.tbtnExit.Name = "tbtnExit";
            this.tbtnExit.Size = new System.Drawing.Size(165, 22);
            this.tbtnExit.Text = "Close";
            this.tbtnExit.Visible = false;
            this.tbtnExit.Click += new System.EventHandler(this.tbtnExit_Click);
            // 
            // tbtnEdit
            // 
            this.tbtnEdit.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tbtnEdit.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.undoToolStripMenuItem,
            this.redoToolStripMenuItem,
            this.toolStripMenuItem1,
            this.cutToolStripMenuItem,
            this.copyToolStripMenuItem,
            this.pasteToolStripMenuItem,
            this.deleteToolStripMenuItem,
            this.selectAllToolStripMenuItem,
            this.toolStripMenuItem2,
            this.findToolStripMenuItem,
            this.syntaxHiglightingToolStripMenuItem});
            this.tbtnEdit.Image = ((System.Drawing.Image)(resources.GetObject("tbtnEdit.Image")));
            this.tbtnEdit.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tbtnEdit.Name = "tbtnEdit";
            this.tbtnEdit.Size = new System.Drawing.Size(40, 22);
            this.tbtnEdit.Text = "Edit";
            // 
            // undoToolStripMenuItem
            // 
            this.undoToolStripMenuItem.Name = "undoToolStripMenuItem";
            this.undoToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+Z";
            this.undoToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            this.undoToolStripMenuItem.Text = "Undo";
            this.undoToolStripMenuItem.Click += new System.EventHandler(this.undoToolStripMenuItem_Click);
            // 
            // redoToolStripMenuItem
            // 
            this.redoToolStripMenuItem.Name = "redoToolStripMenuItem";
            this.redoToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+Y";
            this.redoToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            this.redoToolStripMenuItem.Text = "Redo";
            this.redoToolStripMenuItem.Click += new System.EventHandler(this.redoToolStripMenuItem_Click);
            // 
            // toolStripMenuItem1
            // 
            this.toolStripMenuItem1.Name = "toolStripMenuItem1";
            this.toolStripMenuItem1.Size = new System.Drawing.Size(180, 6);
            // 
            // cutToolStripMenuItem
            // 
            this.cutToolStripMenuItem.Name = "cutToolStripMenuItem";
            this.cutToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+X";
            this.cutToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            this.cutToolStripMenuItem.Text = "Cut";
            this.cutToolStripMenuItem.Click += new System.EventHandler(this.cutToolStripMenuItem_Click);
            // 
            // copyToolStripMenuItem
            // 
            this.copyToolStripMenuItem.Name = "copyToolStripMenuItem";
            this.copyToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+C";
            this.copyToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            this.copyToolStripMenuItem.Text = "Copy";
            this.copyToolStripMenuItem.Click += new System.EventHandler(this.copyToolStripMenuItem_Click);
            // 
            // pasteToolStripMenuItem
            // 
            this.pasteToolStripMenuItem.Name = "pasteToolStripMenuItem";
            this.pasteToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+V";
            this.pasteToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            this.pasteToolStripMenuItem.Text = "Paste";
            this.pasteToolStripMenuItem.Click += new System.EventHandler(this.pasteToolStripMenuItem_Click);
            // 
            // deleteToolStripMenuItem
            // 
            this.deleteToolStripMenuItem.Name = "deleteToolStripMenuItem";
            this.deleteToolStripMenuItem.ShortcutKeyDisplayString = "Del";
            this.deleteToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            this.deleteToolStripMenuItem.Text = "Delete";
            this.deleteToolStripMenuItem.Click += new System.EventHandler(this.deleteToolStripMenuItem_Click);
            // 
            // selectAllToolStripMenuItem
            // 
            this.selectAllToolStripMenuItem.Name = "selectAllToolStripMenuItem";
            this.selectAllToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+A";
            this.selectAllToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            this.selectAllToolStripMenuItem.Text = "Select All";
            this.selectAllToolStripMenuItem.Click += new System.EventHandler(this.selectAllToolStripMenuItem_Click);
            // 
            // toolStripMenuItem2
            // 
            this.toolStripMenuItem2.Name = "toolStripMenuItem2";
            this.toolStripMenuItem2.Size = new System.Drawing.Size(180, 6);
            // 
            // findToolStripMenuItem
            // 
            this.findToolStripMenuItem.Name = "findToolStripMenuItem";
            this.findToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+F";
            this.findToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            this.findToolStripMenuItem.Text = "Find/Replace";
            this.findToolStripMenuItem.Click += new System.EventHandler(this.findToolStripMenuItem_Click);
            // 
            // syntaxHiglightingToolStripMenuItem
            // 
            this.syntaxHiglightingToolStripMenuItem.Checked = true;
            this.syntaxHiglightingToolStripMenuItem.CheckState = System.Windows.Forms.CheckState.Checked;
            this.syntaxHiglightingToolStripMenuItem.Name = "syntaxHiglightingToolStripMenuItem";
            this.syntaxHiglightingToolStripMenuItem.Size = new System.Drawing.Size(183, 22);
            this.syntaxHiglightingToolStripMenuItem.Text = "Syntax Higlighting";
            this.syntaxHiglightingToolStripMenuItem.Click += new System.EventHandler(this.syntaxHighlightingToolStripMenuItem_Click);
            // 
            // tbtnAttach
            // 
            this.tbtnAttach.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tbtnAttach.Image = ((System.Drawing.Image)(resources.GetObject("tbtnAttach.Image")));
            this.tbtnAttach.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tbtnAttach.Name = "tbtnAttach";
            this.tbtnAttach.Size = new System.Drawing.Size(48, 22);
            this.tbtnAttach.Text = "Detach";
            this.tbtnAttach.Click += new System.EventHandler(this.tbtnAttach_Click);
            // 
            // tsStatus
            // 
            this.tsStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.tsStatus.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.tsStatus.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.lblScripStatus,
            this.lblCol,
            this.toolStripSeparator1,
            this.lblLine,
            this.toolStripSeparator2,
            this.btnSave,
            this.cbMono,
            this.cbRunning});
            this.tsStatus.Location = new System.Drawing.Point(0, 420);
            this.tsStatus.Name = "tsStatus";
            this.tsStatus.Size = new System.Drawing.Size(661, 25);
            this.tsStatus.TabIndex = 3;
            // 
            // lblScripStatus
            // 
            this.lblScripStatus.Name = "lblScripStatus";
            this.lblScripStatus.Size = new System.Drawing.Size(59, 22);
            this.lblScripStatus.Text = "Loading...";
            // 
            // lblCol
            // 
            this.lblCol.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.lblCol.AutoSize = false;
            this.lblCol.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.lblCol.Name = "lblCol";
            this.lblCol.Size = new System.Drawing.Size(55, 22);
            this.lblCol.Text = "Col 1";
            this.lblCol.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(6, 25);
            // 
            // lblLine
            // 
            this.lblLine.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.lblLine.AutoSize = false;
            this.lblLine.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.lblLine.Name = "lblLine";
            this.lblLine.Size = new System.Drawing.Size(55, 22);
            this.lblLine.Text = "Ln 1";
            this.lblLine.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(6, 25);
            // 
            // btnSave
            // 
            this.btnSave.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.btnSave.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnSave.Image = ((System.Drawing.Image)(resources.GetObject("btnSave.Image")));
            this.btnSave.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(35, 22);
            this.btnSave.Text = "Save";
            this.btnSave.Click += new System.EventHandler(this.tbtbSave_Click);
            // 
            // cbMono
            // 
            this.cbMono.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.cbMono.Checked = true;
            this.cbMono.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbMono.Name = "cbMono";
            this.cbMono.Size = new System.Drawing.Size(58, 22);
            this.cbMono.Text = "Mono";
            // 
            // cbRunning
            // 
            this.cbRunning.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.cbRunning.Checked = true;
            this.cbRunning.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbRunning.Name = "cbRunning";
            this.cbRunning.Size = new System.Drawing.Size(71, 22);
            this.cbRunning.Text = "Running";
            // 
            // tsFindReplace
            // 
            this.tsFindReplace.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.tsFindReplace.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.tsFindReplace.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tfindClose,
            this.tfindFindText,
            this.tfindDoFind,
            this.tfindMatchCase,
            this.toolStripButton1,
            this.toolStripLabel1,
            this.tfindReplaceText,
            this.tfindFindNextReplace,
            this.tfindReplace,
            this.tfindReplaceAll});
            this.tsFindReplace.Location = new System.Drawing.Point(0, 395);
            this.tsFindReplace.Name = "tsFindReplace";
            this.tsFindReplace.Size = new System.Drawing.Size(661, 25);
            this.tsFindReplace.TabIndex = 4;
            this.tsFindReplace.Text = "toolStrip1";
            this.tsFindReplace.Visible = false;
            // 
            // tfindClose
            // 
            this.tfindClose.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tfindClose.Image = global::Radegast.Properties.Resources.del_trans;
            this.tfindClose.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tfindClose.Name = "tfindClose";
            this.tfindClose.Size = new System.Drawing.Size(23, 22);
            this.tfindClose.Text = "Close";
            this.tfindClose.Click += new System.EventHandler(this.tfindClose_Click);
            // 
            // tfindFindText
            // 
            this.tfindFindText.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.tfindFindText.Name = "tfindFindText";
            this.tfindFindText.Size = new System.Drawing.Size(120, 25);
            this.tfindFindText.ToolTipText = "Search string";
            this.tfindFindText.Leave += new System.EventHandler(this.tfindFindText_Leave);
            this.tfindFindText.KeyDown += new System.Windows.Forms.KeyEventHandler(this.tfindFindText_KeyDown);
            this.tfindFindText.Enter += new System.EventHandler(this.tfindFindText_Enter);
            this.tfindFindText.KeyUp += new System.Windows.Forms.KeyEventHandler(this.tfindFindText_KeyUp);
            this.tfindFindText.TextChanged += new System.EventHandler(this.tfindFindText_TextChanged);
            // 
            // tfindDoFind
            // 
            this.tfindDoFind.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tfindDoFind.Image = ((System.Drawing.Image)(resources.GetObject("tfindDoFind.Image")));
            this.tfindDoFind.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tfindDoFind.Name = "tfindDoFind";
            this.tfindDoFind.Size = new System.Drawing.Size(34, 22);
            this.tfindDoFind.Text = "Find";
            this.tfindDoFind.Click += new System.EventHandler(this.tfindDoFind_Click);
            // 
            // tfindMatchCase
            // 
            this.tfindMatchCase.BackColor = System.Drawing.Color.Transparent;
            this.tfindMatchCase.Checked = false;
            this.tfindMatchCase.CheckState = System.Windows.Forms.CheckState.Unchecked;
            this.tfindMatchCase.Name = "tfindMatchCase";
            this.tfindMatchCase.Size = new System.Drawing.Size(86, 22);
            this.tfindMatchCase.Text = "Match case";
            // 
            // toolStripButton1
            // 
            this.toolStripButton1.Name = "toolStripButton1";
            this.toolStripButton1.Size = new System.Drawing.Size(6, 25);
            // 
            // toolStripLabel1
            // 
            this.toolStripLabel1.Name = "toolStripLabel1";
            this.toolStripLabel1.Size = new System.Drawing.Size(48, 22);
            this.toolStripLabel1.Text = "Replace";
            // 
            // tfindReplaceText
            // 
            this.tfindReplaceText.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.tfindReplaceText.Name = "tfindReplaceText";
            this.tfindReplaceText.Size = new System.Drawing.Size(100, 25);
            this.tfindReplaceText.ToolTipText = "Replacement text";
            // 
            // tfindFindNextReplace
            // 
            this.tfindFindNextReplace.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tfindFindNextReplace.Image = ((System.Drawing.Image)(resources.GetObject("tfindFindNextReplace.Image")));
            this.tfindFindNextReplace.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tfindFindNextReplace.Name = "tfindFindNextReplace";
            this.tfindFindNextReplace.Size = new System.Drawing.Size(59, 22);
            this.tfindFindNextReplace.Text = "Find next";
            this.tfindFindNextReplace.ToolTipText = "Find the next occurance of search string";
            this.tfindFindNextReplace.Click += new System.EventHandler(this.tfindFindNextReplace_Click);
            // 
            // tfindReplace
            // 
            this.tfindReplace.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tfindReplace.Image = ((System.Drawing.Image)(resources.GetObject("tfindReplace.Image")));
            this.tfindReplace.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tfindReplace.Name = "tfindReplace";
            this.tfindReplace.Size = new System.Drawing.Size(52, 22);
            this.tfindReplace.Text = "Replace";
            this.tfindReplace.ToolTipText = "Replace currently highlighted text with the replacement string";
            this.tfindReplace.Click += new System.EventHandler(this.tfindReplace_Click);
            // 
            // tfindReplaceAll
            // 
            this.tfindReplaceAll.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tfindReplaceAll.Image = ((System.Drawing.Image)(resources.GetObject("tfindReplaceAll.Image")));
            this.tfindReplaceAll.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tfindReplaceAll.Name = "tfindReplaceAll";
            this.tfindReplaceAll.Size = new System.Drawing.Size(67, 22);
            this.tfindReplaceAll.Text = "Replace all";
            this.tfindReplaceAll.ToolTipText = "Replace all occurances of search string with the replacement string";
            this.tfindReplaceAll.Click += new System.EventHandler(this.tfindReplaceAll_Click);
            // 
            // rtb
            // 
            this.rtb.AcceptsTab = true;
            this.rtb.BackColor = System.Drawing.Color.WhiteSmoke;
            this.rtb.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.rtb.DetectUrls = false;
            this.rtb.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rtb.Font = new System.Drawing.Font("Courier New", 9.5F);
            this.rtb.ForeColor = System.Drawing.SystemColors.WindowText;
            this.rtb.HideSelection = false;
            this.rtb.Location = new System.Drawing.Point(33, 25);
            this.rtb.Name = "rtb";
            this.rtb.Size = new System.Drawing.Size(628, 321);
            this.rtb.TabIndex = 1;
            this.rtb.Text = "";
            this.rtb.WordWrap = false;
            this.rtb.KeyDown += new System.Windows.Forms.KeyEventHandler(this.rtb_KeyDown);
            this.rtb.SelectionChanged += new System.EventHandler(this.rtb_SelectionChanged);
            this.rtb.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.rtb_KeyPress);
            // 
            // lineNubersForRtb
            // 
            this.lineNubersForRtb.BackColor = System.Drawing.SystemColors.ControlDarkDark;
            this.lineNubersForRtb.Dock = System.Windows.Forms.DockStyle.Left;
            this.lineNubersForRtb.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.lineNubersForRtb.Location = new System.Drawing.Point(0, 25);
            this.lineNubersForRtb.Name = "lineNubersForRtb";
            this.lineNubersForRtb.RTB = this.rtb;
            this.lineNubersForRtb.Size = new System.Drawing.Size(33, 321);
            this.lineNubersForRtb.TabIndex = 6;
            this.lineNubersForRtb.TabStop = false;
            // 
            // txtStatus
            // 
            this.txtStatus.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.txtStatus.Location = new System.Drawing.Point(0, 346);
            this.txtStatus.Multiline = true;
            this.txtStatus.Name = "txtStatus";
            this.txtStatus.Size = new System.Drawing.Size(661, 74);
            this.txtStatus.TabIndex = 7;
            this.txtStatus.Visible = false;
            // 
            // ScriptEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.rtb);
            this.Controls.Add(this.lineNubersForRtb);
            this.Controls.Add(this.tsFindReplace);
            this.Controls.Add(this.tsMenu);
            this.Controls.Add(this.txtStatus);
            this.Controls.Add(this.tsStatus);
            this.Name = "ScriptEditor";
            this.Size = new System.Drawing.Size(661, 445);
            this.tsMenu.ResumeLayout(false);
            this.tsMenu.PerformLayout();
            this.tsStatus.ResumeLayout(false);
            this.tsStatus.PerformLayout();
            this.tsFindReplace.ResumeLayout(false);
            this.tsFindReplace.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public System.Windows.Forms.ToolTip ttKeyWords;
        public System.Windows.Forms.ToolStrip tsMenu;
        public System.Windows.Forms.ToolStripDropDownButton tbtbFile;
        public System.Windows.Forms.ToolStripMenuItem tbtbSave;
        public System.Windows.Forms.ToolStripMenuItem tbtbSaveToDiskAs;
        public System.Windows.Forms.ToolStripMenuItem tbtbLoadFromDisk;
        public System.Windows.Forms.ToolStripButton tbtnAttach;
        public System.Windows.Forms.ToolStripSeparator tSeparator1;
        public System.Windows.Forms.ToolStripMenuItem tbtnExit;
        public System.Windows.Forms.ToolStrip tsStatus;
        public System.Windows.Forms.ToolStripLabel lblScripStatus;
        public System.Windows.Forms.ToolStripLabel lblCol;
        public System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        public System.Windows.Forms.ToolStripLabel lblLine;
        public System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        public System.Windows.Forms.ToolStripMenuItem tbtbSaveToDisk;
        public System.Windows.Forms.ToolStripDropDownButton tbtnEdit;
        public System.Windows.Forms.ToolStripMenuItem undoToolStripMenuItem;
        public System.Windows.Forms.ToolStripMenuItem redoToolStripMenuItem;
        public System.Windows.Forms.ToolStripSeparator toolStripMenuItem1;
        public System.Windows.Forms.ToolStripMenuItem cutToolStripMenuItem;
        public System.Windows.Forms.ToolStripMenuItem copyToolStripMenuItem;
        public System.Windows.Forms.ToolStripMenuItem pasteToolStripMenuItem;
        public System.Windows.Forms.ToolStripMenuItem deleteToolStripMenuItem;
        public System.Windows.Forms.ToolStripMenuItem selectAllToolStripMenuItem;
        public System.Windows.Forms.ToolStripSeparator toolStripMenuItem2;
        public System.Windows.Forms.ToolStripMenuItem findToolStripMenuItem;
        public System.Windows.Forms.ToolStrip tsFindReplace;
        public System.Windows.Forms.ToolStripButton tfindClose;
        public System.Windows.Forms.ToolStripTextBox tfindFindText;
        public ToolStripCheckBox tfindMatchCase;
        public System.Windows.Forms.ToolStripButton tfindDoFind;
        public System.Windows.Forms.ToolStripLabel toolStripLabel1;
        public System.Windows.Forms.ToolStripTextBox tfindReplaceText;
        public System.Windows.Forms.ToolStripButton tfindFindNextReplace;
        public System.Windows.Forms.ToolStripButton tfindReplace;
        public System.Windows.Forms.ToolStripButton tfindReplaceAll;
        public System.Windows.Forms.ToolStripSeparator toolStripButton1;
        public RRichTextBox rtb;
        public LineNumberPanel lineNubersForRtb;
        public System.Windows.Forms.ToolStripMenuItem syntaxHiglightingToolStripMenuItem;
        private ToolStripCheckBox cbMono;
        private System.Windows.Forms.ToolStripButton btnSave;
        private System.Windows.Forms.TextBox txtStatus;
        private ToolStripCheckBox cbRunning;

    }
}
