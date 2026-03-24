/**
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn, LLC
 * All rights reserved.
 *  
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.If not, see<https://www.gnu.org/licenses/>.
 */

namespace Radegast
{
    partial class LogViewerConsole
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
            if (InvokeRequired)
            {
                if (instance == null || !instance.MonoRuntime || IsHandleCreated)
                    Invoke((System.Windows.Forms.MethodInvoker)(() => Dispose(disposing)));
                return;
            }

            if (disposing && components != null)
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
            this.pnlFilter = new System.Windows.Forms.Panel();
            this.lblLevel = new System.Windows.Forms.Label();
            this.cmbLevel = new System.Windows.Forms.ComboBox();
            this.lblCategory = new System.Windows.Forms.Label();
            this.txtCategory = new System.Windows.Forms.TextBox();
            this.chkAutoScroll = new System.Windows.Forms.CheckBox();
            this.btnClear = new System.Windows.Forms.Button();
            this.rtbLog = new System.Windows.Forms.RichTextBox();
            this.pnlFilter.SuspendLayout();
            this.SuspendLayout();
            // 
            // pnlFilter
            // 
            this.pnlFilter.Controls.AddRange(new System.Windows.Forms.Control[] {
                this.lblLevel,
                this.cmbLevel,
                this.lblCategory,
                this.txtCategory,
                this.chkAutoScroll,
                this.btnClear});
            this.pnlFilter.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlFilter.Height = 30;
            this.pnlFilter.Name = "pnlFilter";
            this.pnlFilter.TabIndex = 1;
            // 
            // lblLevel
            // 
            this.lblLevel.AutoSize = true;
            this.lblLevel.Location = new System.Drawing.Point(3, 7);
            this.lblLevel.Name = "lblLevel";
            this.lblLevel.Size = new System.Drawing.Size(36, 13);
            this.lblLevel.TabIndex = 0;
            this.lblLevel.Text = "Level:";
            // 
            // cmbLevel
            // 
            this.cmbLevel.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbLevel.FormattingEnabled = true;
            this.cmbLevel.Location = new System.Drawing.Point(45, 4);
            this.cmbLevel.Name = "cmbLevel";
            this.cmbLevel.Size = new System.Drawing.Size(105, 21);
            this.cmbLevel.TabIndex = 1;
            // 
            // lblCategory
            // 
            this.lblCategory.AutoSize = true;
            this.lblCategory.Location = new System.Drawing.Point(160, 7);
            this.lblCategory.Name = "lblCategory";
            this.lblCategory.Size = new System.Drawing.Size(52, 13);
            this.lblCategory.TabIndex = 2;
            this.lblCategory.Text = "Category:";
            // 
            // txtCategory
            // 
            this.txtCategory.Location = new System.Drawing.Point(218, 4);
            this.txtCategory.Name = "txtCategory";
            this.txtCategory.Size = new System.Drawing.Size(150, 20);
            this.txtCategory.TabIndex = 3;
            // 
            // chkAutoScroll
            // 
            this.chkAutoScroll.AutoSize = true;
            this.chkAutoScroll.Checked = true;
            this.chkAutoScroll.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkAutoScroll.Location = new System.Drawing.Point(378, 6);
            this.chkAutoScroll.Name = "chkAutoScroll";
            this.chkAutoScroll.Size = new System.Drawing.Size(78, 17);
            this.chkAutoScroll.TabIndex = 4;
            this.chkAutoScroll.Text = "Auto-scroll";
            this.chkAutoScroll.UseVisualStyleBackColor = true;
            // 
            // btnClear
            // 
            this.btnClear.Location = new System.Drawing.Point(466, 3);
            this.btnClear.Name = "btnClear";
            this.btnClear.Size = new System.Drawing.Size(55, 23);
            this.btnClear.TabIndex = 5;
            this.btnClear.Text = "Clear";
            this.btnClear.UseVisualStyleBackColor = true;
            this.btnClear.Click += new System.EventHandler(this.btnClear_Click);
            // 
            // rtbLog
            // 
            this.rtbLog.AccessibleName = "Log Viewer";
            this.rtbLog.BackColor = System.Drawing.SystemColors.Window;
            this.rtbLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rtbLog.HideSelection = false;
            this.rtbLog.Location = new System.Drawing.Point(0, 30);
            this.rtbLog.Name = "rtbLog";
            this.rtbLog.ReadOnly = true;
            this.rtbLog.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
            this.rtbLog.Size = new System.Drawing.Size(600, 370);
            this.rtbLog.TabIndex = 0;
            this.rtbLog.Text = "";
            this.rtbLog.LinkClicked += new System.Windows.Forms.LinkClickedEventHandler(this.rtbLog_LinkClicked);
            // 
            // LogViewerConsole
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.rtbLog);
            this.Controls.Add(this.pnlFilter);
            this.Name = "LogViewerConsole";
            this.Size = new System.Drawing.Size(600, 400);
            this.pnlFilter.ResumeLayout(false);
            this.pnlFilter.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel pnlFilter;
        private System.Windows.Forms.Label lblLevel;
        private System.Windows.Forms.ComboBox cmbLevel;
        private System.Windows.Forms.Label lblCategory;
        private System.Windows.Forms.TextBox txtCategory;
        private System.Windows.Forms.CheckBox chkAutoScroll;
        private System.Windows.Forms.Button btnClear;
        public System.Windows.Forms.RichTextBox rtbLog;
    }
}
