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
using System.ComponentModel;

namespace Radegast
{
    partial class InventoryConsole
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
                if(inventoryUpdateCancelToken != null)
                {
                    inventoryUpdateCancelToken.Cancel();
                    inventoryUpdateCancelToken.Dispose();
                    inventoryUpdateCancelToken = null;
                }

                if (_EditTimer != null)
                {
                    _EditTimer.Dispose();
                    _EditTimer = null;
                }

                if (TreeUpdateTimer != null)
                {
                    TreeUpdateTimer.Dispose();
                    TreeUpdateTimer = null;
                }

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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(InventoryConsole));
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.invTree = new Radegast.WinForms.BufferedTreeView();
            this.ctxInv = new Radegast.RadegastContextMenuStrip(this.components);
            this.tstripInventory = new System.Windows.Forms.ToolStrip();
            this.tlabelStatus = new System.Windows.Forms.ToolStripLabel();
            this.tbtnFile = new System.Windows.Forms.ToolStripDropDownButton();
            this.reloadInventoryToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.saveAllTToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.tbtbSort = new System.Windows.Forms.ToolStripDropDownButton();
            this.tbtbSortByName = new System.Windows.Forms.ToolStripMenuItem();
            this.tbtnSortByDate = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripSeparator();
            this.tbtbFoldersByName = new System.Windows.Forms.ToolStripMenuItem();
            this.tbtnSystemFoldersFirst = new System.Windows.Forms.ToolStripMenuItem();
            this.tabsInventory = new System.Windows.Forms.TabControl();
            this.tabSearch = new System.Windows.Forms.TabPage();
            this.lstInventorySearch = new Radegast.ListViewNoFlicker();
            this.chResItemName = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.pnlSearchOptions = new System.Windows.Forms.Panel();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.cbSrchRecent = new System.Windows.Forms.RadioButton();
            this.cbSrchWorn = new System.Windows.Forms.RadioButton();
            this.cbSrchGestures = new System.Windows.Forms.RadioButton();
            this.rbSrchAll = new System.Windows.Forms.RadioButton();
            this.lblSearchStatus = new System.Windows.Forms.Label();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.cbSrchCreator = new System.Windows.Forms.CheckBox();
            this.cbSrchDesc = new System.Windows.Forms.CheckBox();
            this.cbSrchName = new System.Windows.Forms.CheckBox();
            this.txtSearch = new System.Windows.Forms.TextBox();
            this.btnInvSearch = new System.Windows.Forms.Button();
            this.tabDetail = new System.Windows.Forms.TabPage();
            this.pnlDetail = new System.Windows.Forms.Panel();
            this.pnlItemProperties = new System.Windows.Forms.Panel();
            this.txtInvID = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.txtAssetID = new System.Windows.Forms.TextBox();
            this.gbxPerms = new System.Windows.Forms.GroupBox();
            this.cbNextOwnTransfer = new System.Windows.Forms.CheckBox();
            this.cbNextOwnCopy = new System.Windows.Forms.CheckBox();
            this.cbOwnerTransfer = new System.Windows.Forms.CheckBox();
            this.cbNextOwnModify = new System.Windows.Forms.CheckBox();
            this.cbOwnerCopy = new System.Windows.Forms.CheckBox();
            this.cbOwnerModify = new System.Windows.Forms.CheckBox();
            this.label8 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.btnProfile = new System.Windows.Forms.Button();
            this.txtCreator = new Radegast.AgentNameTextBox();
            this.txtCreated = new System.Windows.Forms.TextBox();
            this.lblCreated = new System.Windows.Forms.Label();
            this.txtItemDescription = new System.Windows.Forms.TextBox();
            this.txtItemName = new System.Windows.Forms.TextBox();
            this.lblAsset = new System.Windows.Forms.Label();
            this.lblCreator = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.lblItemName = new System.Windows.Forms.Label();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.tstripInventory.SuspendLayout();
            this.tabsInventory.SuspendLayout();
            this.tabSearch.SuspendLayout();
            this.pnlSearchOptions.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.tabDetail.SuspendLayout();
            this.pnlItemProperties.SuspendLayout();
            this.gbxPerms.SuspendLayout();
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
            this.splitContainer1.Panel1.Controls.Add(this.invTree);
            this.splitContainer1.Panel1.Controls.Add(this.tstripInventory);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.tabsInventory);
            this.splitContainer1.Size = new System.Drawing.Size(834, 483);
            this.splitContainer1.SplitterDistance = 364;
            this.splitContainer1.TabIndex = 1;
            // 
            // invTree
            // 
            this.invTree.AllowDrop = true;
            this.invTree.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(62)))), ((int)(((byte)(62)))), ((int)(((byte)(62)))));
            this.invTree.ContextMenuStrip = this.ctxInv;
            this.invTree.Dock = System.Windows.Forms.DockStyle.Fill;
            this.invTree.ForeColor = System.Drawing.Color.White;
            this.invTree.HideSelection = false;
            this.invTree.LabelEdit = true;
            this.invTree.LineColor = System.Drawing.Color.White;
            this.invTree.Location = new System.Drawing.Point(0, 25);
            this.invTree.Name = "invTree";
            this.invTree.Size = new System.Drawing.Size(364, 458);
            this.invTree.TabIndex = 0;
            this.invTree.BeforeLabelEdit += new System.Windows.Forms.NodeLabelEditEventHandler(this.invTree_BeforeLabelEdit);
            this.invTree.AfterLabelEdit += new System.Windows.Forms.NodeLabelEditEventHandler(this.invTree_AfterLabelEdit);
            this.invTree.ItemDrag += new System.Windows.Forms.ItemDragEventHandler(this.invTree_ItemDrag);
            this.invTree.DragDrop += new System.Windows.Forms.DragEventHandler(this.invTree_DragDrop);
            this.invTree.DragEnter += new System.Windows.Forms.DragEventHandler(this.invTree_DragEnter);
            this.invTree.DragOver += new System.Windows.Forms.DragEventHandler(this.invTree_DragOver);
            this.invTree.KeyUp += new System.Windows.Forms.KeyEventHandler(this.invTree_KeyUp);
            // 
            // ctxInv
            // 
            this.ctxInv.Name = "folderContext";
            this.ctxInv.ShowImageMargin = false;
            this.ctxInv.Size = new System.Drawing.Size(36, 4);
            this.ctxInv.Text = "Inventory Folder";
            this.ctxInv.Opening += new System.ComponentModel.CancelEventHandler(this.ctxInv_Opening);
            // 
            // tstripInventory
            // 
            this.tstripInventory.GripMargin = new System.Windows.Forms.Padding(0);
            this.tstripInventory.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.tstripInventory.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tlabelStatus,
            this.tbtnFile,
            this.tbtbSort});
            this.tstripInventory.Location = new System.Drawing.Point(0, 0);
            this.tstripInventory.Name = "tstripInventory";
            this.tstripInventory.Size = new System.Drawing.Size(364, 25);
            this.tstripInventory.TabIndex = 1;
            this.tstripInventory.TabStop = true;
            this.tstripInventory.Text = "toolStrip1";
            // 
            // tlabelStatus
            // 
            this.tlabelStatus.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.tlabelStatus.Name = "tlabelStatus";
            this.tlabelStatus.Size = new System.Drawing.Size(59, 22);
            this.tlabelStatus.Text = "Loading...";
            this.tlabelStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // tbtnFile
            // 
            this.tbtnFile.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tbtnFile.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.reloadInventoryToolStripMenuItem,
            this.saveAllTToolStripMenuItem});
            this.tbtnFile.Image = ((System.Drawing.Image)(resources.GetObject("tbtnFile.Image")));
            this.tbtnFile.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tbtnFile.Name = "tbtnFile";
            this.tbtnFile.Size = new System.Drawing.Size(70, 22);
            this.tbtnFile.Text = "&Inventory";
            // 
            // reloadInventoryToolStripMenuItem
            // 
            this.reloadInventoryToolStripMenuItem.Name = "reloadInventoryToolStripMenuItem";
            this.reloadInventoryToolStripMenuItem.Size = new System.Drawing.Size(163, 22);
            this.reloadInventoryToolStripMenuItem.Text = "&Reload Inventory";
            this.reloadInventoryToolStripMenuItem.ToolTipText = "Clears inventory cache, and downloads whole inventory from server again";
            this.reloadInventoryToolStripMenuItem.Click += new System.EventHandler(this.reloadInventoryToolStripMenuItem_Click);
            // 
            // saveAllTToolStripMenuItem
            // 
            this.saveAllTToolStripMenuItem.Enabled = false;
            this.saveAllTToolStripMenuItem.Name = "saveAllTToolStripMenuItem";
            this.saveAllTToolStripMenuItem.Size = new System.Drawing.Size(163, 22);
            this.saveAllTToolStripMenuItem.Text = "&Backup";
            this.saveAllTToolStripMenuItem.ToolTipText = "Saves all notecards and scripts to folder on local disk";
            this.saveAllTToolStripMenuItem.Click += new System.EventHandler(this.saveAllTToolStripMenuItem_Click);
            // 
            // tbtbSort
            // 
            this.tbtbSort.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tbtbSort.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tbtbSortByName,
            this.tbtnSortByDate,
            this.toolStripMenuItem1,
            this.tbtbFoldersByName,
            this.tbtnSystemFoldersFirst});
            this.tbtbSort.Image = ((System.Drawing.Image)(resources.GetObject("tbtbSort.Image")));
            this.tbtbSort.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tbtbSort.Name = "tbtbSort";
            this.tbtbSort.Size = new System.Drawing.Size(41, 22);
            this.tbtbSort.Text = "S&ort";
            // 
            // tbtbSortByName
            // 
            this.tbtbSortByName.Name = "tbtbSortByName";
            this.tbtbSortByName.Size = new System.Drawing.Size(203, 22);
            this.tbtbSortByName.Text = "By Name";
            this.tbtbSortByName.Click += new System.EventHandler(this.tbtbSortByName_Click);
            // 
            // tbtnSortByDate
            // 
            this.tbtnSortByDate.Name = "tbtnSortByDate";
            this.tbtnSortByDate.Size = new System.Drawing.Size(203, 22);
            this.tbtnSortByDate.Text = "By Date";
            this.tbtnSortByDate.Click += new System.EventHandler(this.tbtnSortByDate_Click);
            // 
            // toolStripMenuItem1
            // 
            this.toolStripMenuItem1.Name = "toolStripMenuItem1";
            this.toolStripMenuItem1.Size = new System.Drawing.Size(200, 6);
            // 
            // tbtbFoldersByName
            // 
            this.tbtbFoldersByName.Checked = true;
            this.tbtbFoldersByName.CheckState = System.Windows.Forms.CheckState.Checked;
            this.tbtbFoldersByName.Enabled = false;
            this.tbtbFoldersByName.Name = "tbtbFoldersByName";
            this.tbtbFoldersByName.Size = new System.Drawing.Size(203, 22);
            this.tbtbFoldersByName.Text = "Folders Always By Name";
            this.tbtbFoldersByName.Visible = false;
            // 
            // tbtnSystemFoldersFirst
            // 
            this.tbtnSystemFoldersFirst.Name = "tbtnSystemFoldersFirst";
            this.tbtnSystemFoldersFirst.Size = new System.Drawing.Size(203, 22);
            this.tbtnSystemFoldersFirst.Text = "System Folders On Top";
            this.tbtnSystemFoldersFirst.Click += new System.EventHandler(this.tbtnSystemFoldersFirst_Click);
            // 
            // tabsInventory
            // 
            this.tabsInventory.Controls.Add(this.tabSearch);
            this.tabsInventory.Controls.Add(this.tabDetail);
            this.tabsInventory.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabsInventory.Location = new System.Drawing.Point(0, 0);
            this.tabsInventory.Name = "tabsInventory";
            this.tabsInventory.SelectedIndex = 0;
            this.tabsInventory.Size = new System.Drawing.Size(466, 483);
            this.tabsInventory.TabIndex = 3;
            // 
            // tabSearch
            // 
            this.tabSearch.BackColor = System.Drawing.Color.Transparent;
            this.tabSearch.Controls.Add(this.lstInventorySearch);
            this.tabSearch.Controls.Add(this.pnlSearchOptions);
            this.tabSearch.Location = new System.Drawing.Point(4, 22);
            this.tabSearch.Name = "tabSearch";
            this.tabSearch.Padding = new System.Windows.Forms.Padding(3);
            this.tabSearch.Size = new System.Drawing.Size(458, 457);
            this.tabSearch.TabIndex = 1;
            this.tabSearch.Text = "Search";
            this.tabSearch.UseVisualStyleBackColor = true;
            // 
            // lstInventorySearch
            // 
            this.lstInventorySearch.AccessibleName = "Search results";
            this.lstInventorySearch.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.chResItemName});
            this.lstInventorySearch.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lstInventorySearch.FullRowSelect = true;
            this.lstInventorySearch.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None;
            this.lstInventorySearch.HideSelection = false;
            this.lstInventorySearch.Location = new System.Drawing.Point(3, 91);
            this.lstInventorySearch.MultiSelect = false;
            this.lstInventorySearch.Name = "lstInventorySearch";
            this.lstInventorySearch.OwnerDraw = true;
            this.lstInventorySearch.ShowGroups = false;
            this.lstInventorySearch.Size = new System.Drawing.Size(452, 363);
            this.lstInventorySearch.Sorting = System.Windows.Forms.SortOrder.Ascending;
            this.lstInventorySearch.TabIndex = 30;
            this.lstInventorySearch.UseCompatibleStateImageBehavior = false;
            this.lstInventorySearch.View = System.Windows.Forms.View.Details;
            this.lstInventorySearch.VirtualMode = true;
            this.lstInventorySearch.DrawItem += new System.Windows.Forms.DrawListViewItemEventHandler(this.lstInventorySearch_DrawItem);
            this.lstInventorySearch.RetrieveVirtualItem += new System.Windows.Forms.RetrieveVirtualItemEventHandler(this.lstInventorySearch_RetrieveVirtualItem);
            this.lstInventorySearch.SizeChanged += new System.EventHandler(this.lstInventorySearch_SizeChanged);
            this.lstInventorySearch.KeyDown += new System.Windows.Forms.KeyEventHandler(this.lstInventorySearch_KeyDown);
            this.lstInventorySearch.MouseClick += new System.Windows.Forms.MouseEventHandler(this.lstInventorySearch_MouseClick);
            this.lstInventorySearch.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.lstInventorySearch_MouseDoubleClick);
            // 
            // chResItemName
            // 
            this.chResItemName.Text = "Item";
            this.chResItemName.Width = 382;
            // 
            // pnlSearchOptions
            // 
            this.pnlSearchOptions.Controls.Add(this.groupBox2);
            this.pnlSearchOptions.Controls.Add(this.lblSearchStatus);
            this.pnlSearchOptions.Controls.Add(this.groupBox1);
            this.pnlSearchOptions.Controls.Add(this.txtSearch);
            this.pnlSearchOptions.Controls.Add(this.btnInvSearch);
            this.pnlSearchOptions.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlSearchOptions.Location = new System.Drawing.Point(3, 3);
            this.pnlSearchOptions.Name = "pnlSearchOptions";
            this.pnlSearchOptions.Size = new System.Drawing.Size(452, 88);
            this.pnlSearchOptions.TabIndex = 20;
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.cbSrchGestures);
            this.groupBox2.Controls.Add(this.cbSrchRecent);
            this.groupBox2.Controls.Add(this.cbSrchWorn);
            this.groupBox2.Controls.Add(this.rbSrchAll);
            this.groupBox2.Location = new System.Drawing.Point(3, 29);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(227, 50);
            this.groupBox2.TabIndex = 2;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Filter";
            // 
            // cbSrchGestures
            // 
            this.cbSrchGestures.AutoSize = true;
            this.cbSrchGestures.Location = new System.Drawing.Point(167, 19);
            this.cbSrchGestures.Name = "cbSrchGestures";
            this.cbSrchGestures.Size = new System.Drawing.Size(60, 17);
            this.cbSrchGestures.TabIndex = 3;
            this.cbSrchGestures.Text = "Gestures";
            this.cbSrchGestures.UseVisualStyleBackColor = true;
            this.cbSrchGestures.CheckedChanged += new System.EventHandler(this.rbSrchAll_CheckedChanged);
            // 
            // cbSrchRecent
            // 
            this.cbSrchRecent.AutoSize = true;
            this.cbSrchRecent.Location = new System.Drawing.Point(105, 19);
            this.cbSrchRecent.Name = "cbSrchRecent";
            this.cbSrchRecent.Size = new System.Drawing.Size(60, 17);
            this.cbSrchRecent.TabIndex = 2;
            this.cbSrchRecent.Text = "Recent";
            this.cbSrchRecent.UseVisualStyleBackColor = true;
            this.cbSrchRecent.CheckedChanged += new System.EventHandler(this.rbSrchAll_CheckedChanged);
            // 
            // cbSrchWorn
            // 
            this.cbSrchWorn.AutoSize = true;
            this.cbSrchWorn.Location = new System.Drawing.Point(48, 19);
            this.cbSrchWorn.Name = "cbSrchWorn";
            this.cbSrchWorn.Size = new System.Drawing.Size(51, 17);
            this.cbSrchWorn.TabIndex = 1;
            this.cbSrchWorn.Text = "Worn";
            this.cbSrchWorn.UseVisualStyleBackColor = true;
            this.cbSrchWorn.CheckedChanged += new System.EventHandler(this.rbSrchAll_CheckedChanged);
            // 
            // rbSrchAll
            // 
            this.rbSrchAll.AutoSize = true;
            this.rbSrchAll.Checked = true;
            this.rbSrchAll.Location = new System.Drawing.Point(6, 19);
            this.rbSrchAll.Name = "rbSrchAll";
            this.rbSrchAll.Size = new System.Drawing.Size(36, 17);
            this.rbSrchAll.TabIndex = 0;
            this.rbSrchAll.TabStop = true;
            this.rbSrchAll.Text = "All";
            this.rbSrchAll.UseVisualStyleBackColor = true;
            this.rbSrchAll.CheckedChanged += new System.EventHandler(this.rbSrchAll_CheckedChanged);
            // 
            // lblSearchStatus
            // 
            this.lblSearchStatus.AutoSize = true;
            this.lblSearchStatus.Location = new System.Drawing.Point(401, 6);
            this.lblSearchStatus.Name = "lblSearchStatus";
            this.lblSearchStatus.Size = new System.Drawing.Size(46, 13);
            this.lblSearchStatus.TabIndex = 3;
            this.lblSearchStatus.Text = "0 results";
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.cbSrchCreator);
            this.groupBox1.Controls.Add(this.cbSrchDesc);
            this.groupBox1.Controls.Add(this.cbSrchName);
            this.groupBox1.Location = new System.Drawing.Point(236, 30);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(213, 49);
            this.groupBox1.TabIndex = 3;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Search in";
            // 
            // cbSrchCreator
            // 
            this.cbSrchCreator.AutoSize = true;
            this.cbSrchCreator.Location = new System.Drawing.Point(151, 19);
            this.cbSrchCreator.Name = "cbSrchCreator";
            this.cbSrchCreator.Size = new System.Drawing.Size(60, 17);
            this.cbSrchCreator.TabIndex = 3;
            this.cbSrchCreator.Text = "Creator";
            this.cbSrchCreator.UseVisualStyleBackColor = true;
            this.cbSrchCreator.Visible = false;
            this.cbSrchCreator.CheckedChanged += new System.EventHandler(this.cbSrchName_CheckedChanged);
            // 
            // cbSrchDesc
            // 
            this.cbSrchDesc.AutoSize = true;
            this.cbSrchDesc.Location = new System.Drawing.Point(66, 19);
            this.cbSrchDesc.Name = "cbSrchDesc";
            this.cbSrchDesc.Size = new System.Drawing.Size(79, 17);
            this.cbSrchDesc.TabIndex = 1;
            this.cbSrchDesc.Text = "Description";
            this.cbSrchDesc.UseVisualStyleBackColor = true;
            this.cbSrchDesc.CheckedChanged += new System.EventHandler(this.cbSrchName_CheckedChanged);
            // 
            // cbSrchName
            // 
            this.cbSrchName.AutoSize = true;
            this.cbSrchName.Checked = true;
            this.cbSrchName.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbSrchName.Location = new System.Drawing.Point(6, 19);
            this.cbSrchName.Name = "cbSrchName";
            this.cbSrchName.Size = new System.Drawing.Size(54, 17);
            this.cbSrchName.TabIndex = 0;
            this.cbSrchName.Text = "Name";
            this.cbSrchName.UseVisualStyleBackColor = true;
            this.cbSrchName.CheckedChanged += new System.EventHandler(this.cbSrchName_CheckedChanged);
            // 
            // txtSearch
            // 
            this.txtSearch.AccessibleName = "Search input";
            this.txtSearch.Location = new System.Drawing.Point(3, 3);
            this.txtSearch.Name = "txtSearch";
            this.txtSearch.Size = new System.Drawing.Size(293, 20);
            this.txtSearch.TabIndex = 0;
            this.txtSearch.TextChanged += new System.EventHandler(this.txtSearch_TextChanged);
            this.txtSearch.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtSearch_KeyDown);
            // 
            // btnInvSearch
            // 
            this.btnInvSearch.Location = new System.Drawing.Point(302, 1);
            this.btnInvSearch.Name = "btnInvSearch";
            this.btnInvSearch.Size = new System.Drawing.Size(96, 23);
            this.btnInvSearch.TabIndex = 1;
            this.btnInvSearch.Text = "Search/Refresh";
            this.btnInvSearch.UseVisualStyleBackColor = true;
            this.btnInvSearch.Click += new System.EventHandler(this.btnInvSearch_Click);
            // 
            // tabDetail
            // 
            this.tabDetail.BackColor = System.Drawing.Color.Transparent;
            this.tabDetail.Controls.Add(this.pnlDetail);
            this.tabDetail.Controls.Add(this.pnlItemProperties);
            this.tabDetail.Location = new System.Drawing.Point(4, 22);
            this.tabDetail.Name = "tabDetail";
            this.tabDetail.Padding = new System.Windows.Forms.Padding(3);
            this.tabDetail.Size = new System.Drawing.Size(458, 457);
            this.tabDetail.TabIndex = 0;
            this.tabDetail.Text = "Detail";
            this.tabDetail.UseVisualStyleBackColor = true;
            // 
            // pnlDetail
            // 
            this.pnlDetail.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlDetail.Location = new System.Drawing.Point(3, 3);
            this.pnlDetail.Name = "pnlDetail";
            this.pnlDetail.Size = new System.Drawing.Size(452, 289);
            this.pnlDetail.TabIndex = 2;
            // 
            // pnlItemProperties
            // 
            this.pnlItemProperties.Controls.Add(this.txtInvID);
            this.pnlItemProperties.Controls.Add(this.label2);
            this.pnlItemProperties.Controls.Add(this.txtAssetID);
            this.pnlItemProperties.Controls.Add(this.gbxPerms);
            this.pnlItemProperties.Controls.Add(this.btnProfile);
            this.pnlItemProperties.Controls.Add(this.txtCreator);
            this.pnlItemProperties.Controls.Add(this.txtCreated);
            this.pnlItemProperties.Controls.Add(this.lblCreated);
            this.pnlItemProperties.Controls.Add(this.txtItemDescription);
            this.pnlItemProperties.Controls.Add(this.txtItemName);
            this.pnlItemProperties.Controls.Add(this.lblAsset);
            this.pnlItemProperties.Controls.Add(this.lblCreator);
            this.pnlItemProperties.Controls.Add(this.label1);
            this.pnlItemProperties.Controls.Add(this.lblItemName);
            this.pnlItemProperties.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlItemProperties.Location = new System.Drawing.Point(3, 292);
            this.pnlItemProperties.Name = "pnlItemProperties";
            this.pnlItemProperties.Size = new System.Drawing.Size(452, 162);
            this.pnlItemProperties.TabIndex = 0;
            // 
            // txtInvID
            // 
            this.txtInvID.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtInvID.Location = new System.Drawing.Point(258, 81);
            this.txtInvID.Name = "txtInvID";
            this.txtInvID.ReadOnly = true;
            this.txtInvID.Size = new System.Drawing.Size(185, 20);
            this.txtInvID.TabIndex = 16;
            this.txtInvID.Enter += new System.EventHandler(this.txtInvID_Enter);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(213, 84);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(36, 13);
            this.label2.TabIndex = 15;
            this.label2.Text = "Inv ID";
            // 
            // txtAssetID
            // 
            this.txtAssetID.Location = new System.Drawing.Point(51, 81);
            this.txtAssetID.Name = "txtAssetID";
            this.txtAssetID.ReadOnly = true;
            this.txtAssetID.Size = new System.Drawing.Size(156, 20);
            this.txtAssetID.TabIndex = 14;
            this.txtAssetID.Enter += new System.EventHandler(this.txtAssetID_Enter);
            // 
            // gbxPerms
            // 
            this.gbxPerms.Controls.Add(this.cbNextOwnTransfer);
            this.gbxPerms.Controls.Add(this.cbNextOwnCopy);
            this.gbxPerms.Controls.Add(this.cbOwnerTransfer);
            this.gbxPerms.Controls.Add(this.cbNextOwnModify);
            this.gbxPerms.Controls.Add(this.cbOwnerCopy);
            this.gbxPerms.Controls.Add(this.cbOwnerModify);
            this.gbxPerms.Controls.Add(this.label8);
            this.gbxPerms.Controls.Add(this.label7);
            this.gbxPerms.Location = new System.Drawing.Point(6, 100);
            this.gbxPerms.Name = "gbxPerms";
            this.gbxPerms.Size = new System.Drawing.Size(267, 58);
            this.gbxPerms.TabIndex = 17;
            this.gbxPerms.TabStop = false;
            // 
            // cbNextOwnTransfer
            // 
            this.cbNextOwnTransfer.AutoSize = true;
            this.cbNextOwnTransfer.Location = new System.Drawing.Point(195, 36);
            this.cbNextOwnTransfer.Name = "cbNextOwnTransfer";
            this.cbNextOwnTransfer.Size = new System.Drawing.Size(55, 17);
            this.cbNextOwnTransfer.TabIndex = 69;
            this.cbNextOwnTransfer.Text = "Resell";
            this.cbNextOwnTransfer.UseVisualStyleBackColor = true;
            // 
            // cbNextOwnCopy
            // 
            this.cbNextOwnCopy.AutoSize = true;
            this.cbNextOwnCopy.Location = new System.Drawing.Point(140, 36);
            this.cbNextOwnCopy.Name = "cbNextOwnCopy";
            this.cbNextOwnCopy.Size = new System.Drawing.Size(50, 17);
            this.cbNextOwnCopy.TabIndex = 68;
            this.cbNextOwnCopy.Text = "Copy";
            this.cbNextOwnCopy.UseVisualStyleBackColor = true;
            // 
            // cbOwnerTransfer
            // 
            this.cbOwnerTransfer.AutoSize = true;
            this.cbOwnerTransfer.Enabled = false;
            this.cbOwnerTransfer.Location = new System.Drawing.Point(195, 15);
            this.cbOwnerTransfer.Name = "cbOwnerTransfer";
            this.cbOwnerTransfer.Size = new System.Drawing.Size(55, 17);
            this.cbOwnerTransfer.TabIndex = 66;
            this.cbOwnerTransfer.Text = "Resell";
            this.cbOwnerTransfer.UseVisualStyleBackColor = true;
            // 
            // cbNextOwnModify
            // 
            this.cbNextOwnModify.AutoSize = true;
            this.cbNextOwnModify.Location = new System.Drawing.Point(90, 36);
            this.cbNextOwnModify.Name = "cbNextOwnModify";
            this.cbNextOwnModify.Size = new System.Drawing.Size(47, 17);
            this.cbNextOwnModify.TabIndex = 67;
            this.cbNextOwnModify.Text = "Mod";
            this.cbNextOwnModify.UseVisualStyleBackColor = true;
            // 
            // cbOwnerCopy
            // 
            this.cbOwnerCopy.AutoSize = true;
            this.cbOwnerCopy.Enabled = false;
            this.cbOwnerCopy.Location = new System.Drawing.Point(140, 15);
            this.cbOwnerCopy.Name = "cbOwnerCopy";
            this.cbOwnerCopy.Size = new System.Drawing.Size(50, 17);
            this.cbOwnerCopy.TabIndex = 65;
            this.cbOwnerCopy.Text = "Copy";
            this.cbOwnerCopy.UseVisualStyleBackColor = true;
            // 
            // cbOwnerModify
            // 
            this.cbOwnerModify.AutoSize = true;
            this.cbOwnerModify.Enabled = false;
            this.cbOwnerModify.Location = new System.Drawing.Point(90, 15);
            this.cbOwnerModify.Name = "cbOwnerModify";
            this.cbOwnerModify.Size = new System.Drawing.Size(47, 17);
            this.cbOwnerModify.TabIndex = 64;
            this.cbOwnerModify.Text = "Mod";
            this.cbOwnerModify.UseVisualStyleBackColor = true;
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(6, 16);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(67, 13);
            this.label8.TabIndex = 62;
            this.label8.Text = "Owner perm.";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(6, 37);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(81, 13);
            this.label7.TabIndex = 63;
            this.label7.Text = "Next own perm.";
            // 
            // btnProfile
            // 
            this.btnProfile.AccessibleDescription = "Open profile";
            this.btnProfile.Enabled = false;
            this.btnProfile.Image = global::Radegast.Properties.Resources.applications_16;
            this.btnProfile.Location = new System.Drawing.Point(48, 53);
            this.btnProfile.Name = "btnProfile";
            this.btnProfile.Size = new System.Drawing.Size(26, 23);
            this.btnProfile.TabIndex = 12;
            this.btnProfile.UseVisualStyleBackColor = true;
            this.btnProfile.Click += new System.EventHandler(this.btnProfile_Click);
            // 
            // txtCreator
            // 
            this.txtCreator.AgentID = ((OpenMetaverse.UUID)(resources.GetObject("txtCreator.AgentID")));
            this.txtCreator.BackColor = System.Drawing.SystemColors.Window;
            this.txtCreator.Location = new System.Drawing.Point(80, 55);
            this.txtCreator.Name = "txtCreator";
            this.txtCreator.ReadOnly = true;
            this.txtCreator.Size = new System.Drawing.Size(169, 20);
            this.txtCreator.TabIndex = 12;
            // 
            // txtCreated
            // 
            this.txtCreated.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtCreated.Location = new System.Drawing.Point(305, 55);
            this.txtCreated.Name = "txtCreated";
            this.txtCreated.ReadOnly = true;
            this.txtCreated.Size = new System.Drawing.Size(138, 20);
            this.txtCreated.TabIndex = 13;
            // 
            // lblCreated
            // 
            this.lblCreated.AutoSize = true;
            this.lblCreated.Location = new System.Drawing.Point(255, 58);
            this.lblCreated.Name = "lblCreated";
            this.lblCreated.Size = new System.Drawing.Size(44, 13);
            this.lblCreated.TabIndex = 0;
            this.lblCreated.Text = "Created";
            // 
            // txtItemDescription
            // 
            this.txtItemDescription.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtItemDescription.Location = new System.Drawing.Point(80, 29);
            this.txtItemDescription.Name = "txtItemDescription";
            this.txtItemDescription.Size = new System.Drawing.Size(363, 20);
            this.txtItemDescription.TabIndex = 11;
            this.txtItemDescription.Leave += new System.EventHandler(this.txtItemDescription_Leave);
            // 
            // txtItemName
            // 
            this.txtItemName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtItemName.Location = new System.Drawing.Point(80, 3);
            this.txtItemName.Name = "txtItemName";
            this.txtItemName.Size = new System.Drawing.Size(363, 20);
            this.txtItemName.TabIndex = 10;
            this.txtItemName.Leave += new System.EventHandler(this.txtItemName_Leave);
            // 
            // lblAsset
            // 
            this.lblAsset.AutoSize = true;
            this.lblAsset.Location = new System.Drawing.Point(3, 84);
            this.lblAsset.Name = "lblAsset";
            this.lblAsset.Size = new System.Drawing.Size(47, 13);
            this.lblAsset.TabIndex = 0;
            this.lblAsset.Text = "Asset ID";
            // 
            // lblCreator
            // 
            this.lblCreator.AutoSize = true;
            this.lblCreator.Location = new System.Drawing.Point(3, 58);
            this.lblCreator.Name = "lblCreator";
            this.lblCreator.Size = new System.Drawing.Size(41, 13);
            this.lblCreator.TabIndex = 0;
            this.lblCreator.Text = "Creator";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(3, 32);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(60, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Description";
            // 
            // lblItemName
            // 
            this.lblItemName.AutoSize = true;
            this.lblItemName.Location = new System.Drawing.Point(3, 6);
            this.lblItemName.Name = "lblItemName";
            this.lblItemName.Size = new System.Drawing.Size(27, 13);
            this.lblItemName.TabIndex = 0;
            this.lblItemName.Text = "Item";
            // 
            // InventoryConsole
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.splitContainer1);
            this.Name = "InventoryConsole";
            this.Size = new System.Drawing.Size(834, 483);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel1.PerformLayout();
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.tstripInventory.ResumeLayout(false);
            this.tstripInventory.PerformLayout();
            this.tabsInventory.ResumeLayout(false);
            this.tabSearch.ResumeLayout(false);
            this.pnlSearchOptions.ResumeLayout(false);
            this.pnlSearchOptions.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.tabDetail.ResumeLayout(false);
            this.pnlItemProperties.ResumeLayout(false);
            this.pnlItemProperties.PerformLayout();
            this.gbxPerms.ResumeLayout(false);
            this.gbxPerms.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        public System.Windows.Forms.CheckBox cbNextOwnTransfer;
        public System.Windows.Forms.CheckBox cbNextOwnCopy;
        public System.Windows.Forms.CheckBox cbOwnerTransfer;
        public System.Windows.Forms.CheckBox cbNextOwnModify;
        public System.Windows.Forms.CheckBox cbOwnerCopy;
        public System.Windows.Forms.CheckBox cbOwnerModify;
        public System.Windows.Forms.Label label8;
        public System.Windows.Forms.Label label7;
        public System.Windows.Forms.Panel pnlSearchOptions;
        public System.Windows.Forms.Button btnInvSearch;
        public System.Windows.Forms.GroupBox groupBox1;
        public System.Windows.Forms.CheckBox cbSrchName;
        public System.Windows.Forms.TextBox txtSearch;
        public System.Windows.Forms.CheckBox cbSrchCreator;
        public System.Windows.Forms.CheckBox cbSrchDesc;
        public System.Windows.Forms.Label lblSearchStatus;
        public System.Windows.Forms.GroupBox groupBox2;
        public System.Windows.Forms.RadioButton rbSrchAll;
        public System.Windows.Forms.RadioButton cbSrchRecent;
        public System.Windows.Forms.RadioButton cbSrchWorn;
        public System.Windows.Forms.RadioButton cbSrchGestures;
        public System.Windows.Forms.GroupBox gbxPerms;
        public Radegast.WinForms.BufferedTreeView invTree;
        public System.Windows.Forms.SplitContainer splitContainer1;
        public RadegastContextMenuStrip ctxInv;
        public System.Windows.Forms.Panel pnlItemProperties;
        public System.Windows.Forms.TextBox txtItemName;
        public System.Windows.Forms.Label lblCreator;
        public System.Windows.Forms.Label lblItemName;
        public AgentNameTextBox txtCreator;
        public System.Windows.Forms.TextBox txtAssetID;
        public System.Windows.Forms.Label lblAsset;
        public System.Windows.Forms.Panel pnlDetail;
        public System.Windows.Forms.Button btnProfile;
        public System.Windows.Forms.TextBox txtCreated;
        public System.Windows.Forms.Label lblCreated;
        public System.Windows.Forms.ToolStrip tstripInventory;
        public System.Windows.Forms.ToolStripLabel tlabelStatus;
        public System.Windows.Forms.ToolStripDropDownButton tbtnFile;
        public System.Windows.Forms.ToolStripMenuItem saveAllTToolStripMenuItem;
        public System.Windows.Forms.ToolStripDropDownButton tbtbSort;
        public System.Windows.Forms.ToolStripMenuItem tbtbSortByName;
        public System.Windows.Forms.ToolStripMenuItem tbtnSortByDate;
        public System.Windows.Forms.ToolStripSeparator toolStripMenuItem1;
        public System.Windows.Forms.ToolStripMenuItem tbtbFoldersByName;
        public System.Windows.Forms.ToolStripMenuItem tbtnSystemFoldersFirst;
        public System.Windows.Forms.ToolStripMenuItem reloadInventoryToolStripMenuItem;
        public System.Windows.Forms.TabControl tabsInventory;
        public System.Windows.Forms.TabPage tabDetail;
        public System.Windows.Forms.TabPage tabSearch;
        public ListViewNoFlicker lstInventorySearch;
        public System.Windows.Forms.ColumnHeader chResItemName;
        public System.Windows.Forms.TextBox txtItemDescription;
        public System.Windows.Forms.Label label1;
        public System.Windows.Forms.TextBox txtInvID;
        public System.Windows.Forms.Label label2;
    }
}
