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
    partial class IMTabWindow
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
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.pnlChatInput = new System.Windows.Forms.Panel();
            this.autoScrollCB = new System.Windows.Forms.CheckBox();
            this.cbxInput = new Radegast.ChatInputBox();
            this.btnPay = new System.Windows.Forms.Button();
            this.btnOfferTeleport = new System.Windows.Forms.Button();
            this.btnProfile = new System.Windows.Forms.Button();
            this.cbAlwaysDing = new System.Windows.Forms.CheckBox();
            this.pnlChatInput.SuspendLayout();
            this.SuspendLayout();
            // 
            // rtbIMText
            // 
            this.rtbIMText.BackColor = System.Drawing.SystemColors.Window;
            this.rtbIMText.DetectUrls = false;
            this.rtbIMText.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rtbIMText.HideSelection = false;
            this.rtbIMText.Location = new System.Drawing.Point(0, 25);
            this.rtbIMText.Name = "rtbIMText";
            this.rtbIMText.ReadOnly = true;
            this.rtbIMText.Size = new System.Drawing.Size(500, 273);
            this.rtbIMText.TabIndex = 3;
            this.rtbIMText.Text = "";
            this.rtbIMText.LinkClicked += new System.Windows.Forms.LinkClickedEventHandler(this.rtbIMText_LinkClicked);
            // 
            // btnSend
            // 
            this.btnSend.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSend.Enabled = false;
            this.btnSend.Location = new System.Drawing.Point(366, 3);
            this.btnSend.Name = "btnSend";
            this.btnSend.Size = new System.Drawing.Size(55, 23);
            this.btnSend.TabIndex = 1;
            this.btnSend.Text = "Send";
            this.btnSend.UseVisualStyleBackColor = true;
            this.btnSend.Click += new System.EventHandler(this.btnSend_Click);
            // 
            // toolStrip1
            // 
            this.toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip1.Location = new System.Drawing.Point(0, 0);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Size = new System.Drawing.Size(500, 25);
            this.toolStrip1.TabIndex = 2;
            this.toolStrip1.Text = "toolStrip1";
            // 
            // pnlChatInput
            // 
            this.pnlChatInput.Controls.Add(this.autoScrollCB);
            this.pnlChatInput.Controls.Add(this.cbxInput);
            this.pnlChatInput.Controls.Add(this.btnSend);
            this.pnlChatInput.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlChatInput.Location = new System.Drawing.Point(0, 298);
            this.pnlChatInput.Name = "pnlChatInput";
            this.pnlChatInput.Size = new System.Drawing.Size(500, 32);
            this.pnlChatInput.TabIndex = 4;
            // 
            // autoScrollCB
            // 
            this.autoScrollCB.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.autoScrollCB.AutoSize = true;
            this.autoScrollCB.Checked = true;
            this.autoScrollCB.CheckState = System.Windows.Forms.CheckState.Checked;
            this.autoScrollCB.Location = new System.Drawing.Point(427, 7);
            this.autoScrollCB.Name = "autoScrollCB";
            this.autoScrollCB.Size = new System.Drawing.Size(73, 17);
            this.autoScrollCB.TabIndex = 13;
            this.autoScrollCB.Text = "Autoscroll";
            this.autoScrollCB.UseVisualStyleBackColor = true;
            // 
            // cbxInput
            // 
            this.cbxInput.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.cbxInput.Location = new System.Drawing.Point(3, 5);
            this.cbxInput.Name = "cbxInput";
            this.cbxInput.Size = new System.Drawing.Size(357, 21);
            this.cbxInput.TabIndex = 0;
            this.cbxInput.SizeChanged += new System.EventHandler(this.cbxInput_SizeChanged);
            this.cbxInput.TextChanged += new System.EventHandler(this.cbxInput_TextChanged);
            this.cbxInput.KeyDown += new System.Windows.Forms.KeyEventHandler(this.cbxInput_KeyDown);
            // 
            // btnPay
            // 
            this.btnPay.Location = new System.Drawing.Point(199, 0);
            this.btnPay.Name = "btnPay";
            this.btnPay.Size = new System.Drawing.Size(75, 23);
            this.btnPay.TabIndex = 8;
            this.btnPay.Text = "Pay...";
            this.btnPay.UseVisualStyleBackColor = true;
            this.btnPay.Click += new System.EventHandler(this.btnPay_Click);
            // 
            // btnOfferTeleport
            // 
            this.btnOfferTeleport.Location = new System.Drawing.Point(81, 0);
            this.btnOfferTeleport.Name = "btnOfferTeleport";
            this.btnOfferTeleport.Size = new System.Drawing.Size(112, 23);
            this.btnOfferTeleport.TabIndex = 7;
            this.btnOfferTeleport.Text = "Offer Teleport";
            this.btnOfferTeleport.UseVisualStyleBackColor = true;
            this.btnOfferTeleport.Click += new System.EventHandler(this.btnOfferTeleport_Click);
            // 
            // btnProfile
            // 
            this.btnProfile.Location = new System.Drawing.Point(0, 0);
            this.btnProfile.Name = "btnProfile";
            this.btnProfile.Size = new System.Drawing.Size(75, 23);
            this.btnProfile.TabIndex = 6;
            this.btnProfile.Text = "Profile";
            this.btnProfile.UseVisualStyleBackColor = true;
            this.btnProfile.Click += new System.EventHandler(this.tbtnProfile_Click);
            // 
            // cbAlwaysDing
            // 
            this.cbAlwaysDing.AutoSize = true;
            this.cbAlwaysDing.Location = new System.Drawing.Point(280, 4);
            this.cbAlwaysDing.Name = "cbAlwaysDing";
            this.cbAlwaysDing.Size = new System.Drawing.Size(108, 17);
            this.cbAlwaysDing.TabIndex = 9;
            this.cbAlwaysDing.Text = "Ding on every IM";
            this.cbAlwaysDing.UseVisualStyleBackColor = true;
            this.cbAlwaysDing.CheckedChanged += new System.EventHandler(this.cbAlwaysDing_CheckedChanged);
            // 
            // IMTabWindow
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.cbAlwaysDing);
            this.Controls.Add(this.btnPay);
            this.Controls.Add(this.btnOfferTeleport);
            this.Controls.Add(this.btnProfile);
            this.Controls.Add(this.rtbIMText);
            this.Controls.Add(this.pnlChatInput);
            this.Controls.Add(this.toolStrip1);
            this.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Name = "IMTabWindow";
            this.Size = new System.Drawing.Size(500, 330);
            this.VisibleChanged += new System.EventHandler(this.cbxInput_VisibleChanged);
            this.pnlChatInput.ResumeLayout(false);
            this.pnlChatInput.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public Radegast.RRichTextBox rtbIMText;
        public ChatInputBox cbxInput;
        public System.Windows.Forms.Button btnSend;
        public System.Windows.Forms.ToolStrip toolStrip1;
        public System.Windows.Forms.Button btnPay;
        public System.Windows.Forms.Button btnOfferTeleport;
        public System.Windows.Forms.Button btnProfile;
        public System.Windows.Forms.CheckBox cbAlwaysDing;
        public System.Windows.Forms.Panel pnlChatInput;
        private System.Windows.Forms.CheckBox autoScrollCB;
    }
}
