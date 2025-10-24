/**
 * Radegast Metaverse Client
 * Copyright(c) 2016-2025, Sjofn, LLC
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

namespace Radegast.GUI.Dialogs
{
    partial class MiniMapForm
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
            this.miniMap1 = new Radegast.WinForms.MiniMap();
            ((System.ComponentModel.ISupportInitialize)(this.miniMap1)).BeginInit();
            this.SuspendLayout();
            // 
            // miniMap1
            // 
            this.miniMap1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.miniMap1.Client = null;
            this.miniMap1.Location = new System.Drawing.Point(0, 0);
            this.miniMap1.Name = "miniMap1";
            this.miniMap1.Size = new System.Drawing.Size(200, 200);
            this.miniMap1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.miniMap1.TabIndex = 0;
            this.miniMap1.TabStop = false;
            this.miniMap1.UseWaitCursor = true;
            // 
            // MiniMapForm
            // 
            this.AccessibleDescription = "MiniMap";
            this.AccessibleName = "MiniMap";
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(199, 199);
            this.Controls.Add(this.miniMap1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.Name = "MiniMapForm";
            this.ShowIcon = false;
            this.Text = "Mini Map";
            this.UseWaitCursor = true;
            ((System.ComponentModel.ISupportInitialize)(this.miniMap1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private Radegast.WinForms.MiniMap miniMap1;
    }
}