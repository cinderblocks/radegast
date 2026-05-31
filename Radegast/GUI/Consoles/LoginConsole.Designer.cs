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
    partial class LoginConsole
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
            this.pnlLoginPrompt = new System.Windows.Forms.Panel();
            this.cbRemember = new System.Windows.Forms.CheckBox();
            this.label6 = new System.Windows.Forms.Label();
            this.txtCustomLoginUri = new System.Windows.Forms.TextBox();
            this.cbxGrid = new System.Windows.Forms.ComboBox();
            this.label5 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.cbxUsername = new System.Windows.Forms.ComboBox();
            this.label2 = new System.Windows.Forms.Label();
            this.txtPassword = new System.Windows.Forms.TextBox();
            this.cbxLocation = new System.Windows.Forms.ComboBox();
            this.label3 = new System.Windows.Forms.Label();
            this.btnLogin = new System.Windows.Forms.Button();
            this.pnlLoggingIn = new System.Windows.Forms.Panel();
            this.btnCancelLogin = new System.Windows.Forms.Button();
            this.proLogin = new System.Windows.Forms.ProgressBar();
            this.lblLoginStatus = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.lblVersion = new System.Windows.Forms.Label();
            this.pnlSplash = new System.Windows.Forms.Panel();
            this.pnlTos = new System.Windows.Forms.Panel();
            this.cbTOS = new System.Windows.Forms.CheckBox();
            this.txtTOS = new System.Windows.Forms.TextBox();
            this.pnlLoginPrompt.SuspendLayout();
            this.pnlLoggingIn.SuspendLayout();
            this.panel1.SuspendLayout();
            this.pnlSplash.SuspendLayout();
            this.pnlTos.SuspendLayout();
            this.SuspendLayout();
            // 
            // pnlLoginPrompt
            // 
            this.pnlLoginPrompt.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.pnlLoginPrompt.Controls.Add(this.cbRemember);
            this.pnlLoginPrompt.Controls.Add(this.label6);
            this.pnlLoginPrompt.Controls.Add(this.txtCustomLoginUri);
            this.pnlLoginPrompt.Controls.Add(this.cbxGrid);
            this.pnlLoginPrompt.Controls.Add(this.label5);
            this.pnlLoginPrompt.Controls.Add(this.label1);
            this.pnlLoginPrompt.Controls.Add(this.cbxUsername);
            this.pnlLoginPrompt.Controls.Add(this.label2);
            this.pnlLoginPrompt.Controls.Add(this.txtPassword);
            this.pnlLoginPrompt.Controls.Add(this.cbxLocation);
            this.pnlLoginPrompt.Controls.Add(this.label3);
            this.pnlLoginPrompt.Location = new System.Drawing.Point(128, 3);
            this.pnlLoginPrompt.Name = "pnlLoginPrompt";
            this.pnlLoginPrompt.Size = new System.Drawing.Size(554, 94);
            this.pnlLoginPrompt.TabIndex = 13;
            // 
            // cbRemember
            // 
            this.cbRemember.AutoSize = true;
            this.cbRemember.Checked = true;
            this.cbRemember.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbRemember.Location = new System.Drawing.Point(6, 45);
            this.cbRemember.Name = "cbRemember";
            this.cbRemember.Size = new System.Drawing.Size(197, 17);
            this.cbRemember.TabIndex = 5;
            this.cbRemember.Text = "Remember username and password";
            this.cbRemember.UseVisualStyleBackColor = true;
            this.cbRemember.CheckedChanged += new System.EventHandler(this.cbRemember_CheckedChanged);
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.ForeColor = System.Drawing.SystemColors.ControlText;
            this.label6.Location = new System.Drawing.Point(272, 73);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(48, 13);
            this.label6.TabIndex = 14;
            this.label6.Text = "Login Uri";
            // 
            // txtCustomLoginUri
            // 
            this.txtCustomLoginUri.AccessibleName = "Login URL";
            this.txtCustomLoginUri.Enabled = false;
            this.txtCustomLoginUri.Location = new System.Drawing.Point(326, 70);
            this.txtCustomLoginUri.Name = "txtCustomLoginUri";
            this.txtCustomLoginUri.Size = new System.Drawing.Size(224, 21);
            this.txtCustomLoginUri.TabIndex = 13;
            // 
            // cbxGrid
            // 
            this.cbxGrid.AccessibleName = "Grid";
            this.cbxGrid.DropDownHeight = 300;
            this.cbxGrid.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbxGrid.IntegralHeight = false;
            this.cbxGrid.Location = new System.Drawing.Point(56, 70);
            this.cbxGrid.Name = "cbxGrid";
            this.cbxGrid.Size = new System.Drawing.Size(210, 21);
            this.cbxGrid.TabIndex = 12;
            this.cbxGrid.SelectedIndexChanged += new System.EventHandler(this.cbxGrid_SelectedIndexChanged);
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.ForeColor = System.Drawing.SystemColors.ControlText;
            this.label5.Location = new System.Drawing.Point(3, 73);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(26, 13);
            this.label5.TabIndex = 11;
            this.label5.Text = "Grid";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.ForeColor = System.Drawing.SystemColors.ControlText;
            this.label1.Location = new System.Drawing.Point(0, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(55, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Username";
            // 
            // cbxUsername
            // 
            this.cbxUsername.AccessibleName = "Username";
            this.cbxUsername.Location = new System.Drawing.Point(3, 16);
            this.cbxUsername.Name = "cbxUsername";
            this.cbxUsername.Size = new System.Drawing.Size(263, 21);
            this.cbxUsername.TabIndex = 1;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.ForeColor = System.Drawing.SystemColors.ControlText;
            this.label2.Location = new System.Drawing.Point(272, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(53, 13);
            this.label2.TabIndex = 3;
            this.label2.Text = "Password";
            // 
            // txtPassword
            // 
            this.txtPassword.AccessibleName = "Password";
            this.txtPassword.Location = new System.Drawing.Point(275, 16);
            this.txtPassword.Name = "txtPassword";
            this.txtPassword.Size = new System.Drawing.Size(275, 21);
            this.txtPassword.TabIndex = 4;
            this.txtPassword.UseSystemPasswordChar = true;
            // 
            // cbxLocation
            // 
            this.cbxLocation.AccessibleName = "Start Location";
            this.cbxLocation.Items.AddRange(new object[] {
            "My Home",
            "My Last Location"});
            this.cbxLocation.Location = new System.Drawing.Point(275, 43);
            this.cbxLocation.Name = "cbxLocation";
            this.cbxLocation.Size = new System.Drawing.Size(275, 21);
            this.cbxLocation.TabIndex = 6;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.ForeColor = System.Drawing.SystemColors.ControlText;
            this.label3.Location = new System.Drawing.Point(219, 46);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(47, 13);
            this.label3.TabIndex = 5;
            this.label3.Text = "Location";
            // 
            // btnLogin
            // 
            this.btnLogin.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnLogin.ForeColor = System.Drawing.SystemColors.ControlText;
            this.btnLogin.Location = new System.Drawing.Point(6, 71);
            this.btnLogin.Name = "btnLogin";
            this.btnLogin.Size = new System.Drawing.Size(116, 23);
            this.btnLogin.TabIndex = 15;
            this.btnLogin.Text = "Login";
            this.btnLogin.UseVisualStyleBackColor = true;
            this.btnLogin.Click += new System.EventHandler(this.btnLogin_Click);
            // 
            // pnlLoggingIn
            // 
            this.pnlLoggingIn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.pnlLoggingIn.Controls.Add(this.proLogin);
            this.pnlLoggingIn.Controls.Add(this.lblLoginStatus);
            this.pnlLoggingIn.Controls.Add(this.btnCancelLogin);
            this.pnlLoggingIn.Location = new System.Drawing.Point(128, 3);
            this.pnlLoggingIn.Name = "pnlLoggingIn";
            this.pnlLoggingIn.Size = new System.Drawing.Size(554, 94);
            this.pnlLoggingIn.TabIndex = 17;
            this.pnlLoggingIn.Visible = false;
            // 
            // proLogin
            // 
            this.proLogin.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.proLogin.Location = new System.Drawing.Point(0, 62);
            this.proLogin.MarqueeAnimationSpeed = 50;
            this.proLogin.Name = "proLogin";
            this.proLogin.Size = new System.Drawing.Size(550, 23);
            this.proLogin.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.proLogin.TabIndex = 11;
            // 
            // lblLoginStatus
            // 
            this.lblLoginStatus.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblLoginStatus.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblLoginStatus.Location = new System.Drawing.Point(3, 0);
            this.lblLoginStatus.Name = "lblLoginStatus";
            this.lblLoginStatus.Size = new System.Drawing.Size(548, 86);
            this.lblLoginStatus.TabIndex = 12;
            this.lblLoginStatus.Text = "Login status goes here.";
            this.lblLoginStatus.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // btnCancelLogin
            // 
            this.btnCancelLogin.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancelLogin.Location = new System.Drawing.Point(470, 62);
            this.btnCancelLogin.Name = "btnCancelLogin";
            this.btnCancelLogin.Size = new System.Drawing.Size(80, 23);
            this.btnCancelLogin.TabIndex = 13;
            this.btnCancelLogin.Text = "Cancel";
            this.btnCancelLogin.UseVisualStyleBackColor = true;
            this.btnCancelLogin.Click += new System.EventHandler(this.btnCancelLogin_Click);
            // 
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.SystemColors.Control;
            this.panel1.Controls.Add(this.lblVersion);
            this.panel1.Controls.Add(this.pnlLoginPrompt);
            this.panel1.Controls.Add(this.btnLogin);
            this.panel1.Controls.Add(this.pnlLoggingIn);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel1.Location = new System.Drawing.Point(0, 335);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(682, 97);
            this.panel1.TabIndex = 18;
            // 
            // lblVersion
            // 
            this.lblVersion.AutoSize = true;
            this.lblVersion.Location = new System.Drawing.Point(3, 3);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new System.Drawing.Size(94, 39);
            this.lblVersion.TabIndex = 18;
            this.lblVersion.Text = "Radegast 2.0.000\r\n.NET 4.8\r\nWindows 10\r\n";
            // 
            // pnlSplash
            // 
            this.pnlSplash.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.pnlSplash.Controls.Add(this.pnlTos);
            this.pnlSplash.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlSplash.Location = new System.Drawing.Point(0, 0);
            this.pnlSplash.Name = "pnlSplash";
            this.pnlSplash.Size = new System.Drawing.Size(682, 335);
            this.pnlSplash.TabIndex = 19;
            // 
            // pnlTos
            // 
            this.pnlTos.Controls.Add(this.cbTOS);
            this.pnlTos.Controls.Add(this.txtTOS);
            this.pnlTos.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlTos.Location = new System.Drawing.Point(0, 0);
            this.pnlTos.Name = "pnlTos";
            this.pnlTos.Size = new System.Drawing.Size(682, 335);
            this.pnlTos.TabIndex = 0;
            this.pnlTos.Visible = false;
            // 
            // cbTOS
            // 
            this.cbTOS.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.cbTOS.AutoSize = true;
            this.cbTOS.Location = new System.Drawing.Point(6, 308);
            this.cbTOS.Name = "cbTOS";
            this.cbTOS.Size = new System.Drawing.Size(176, 17);
            this.cbTOS.TabIndex = 1;
            this.cbTOS.Text = "I agree to the Terms of Service";
            this.cbTOS.CheckedChanged += new System.EventHandler(this.cbTOS_CheckedChanged);
            // 
            // txtTOS
            // 
            this.txtTOS.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtTOS.Location = new System.Drawing.Point(0, 0);
            this.txtTOS.Multiline = true;
            this.txtTOS.Name = "txtTOS";
            this.txtTOS.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtTOS.Size = new System.Drawing.Size(682, 302);
            this.txtTOS.TabIndex = 0;
            // 
            // LoginConsole
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.Controls.Add(this.pnlSplash);
            this.Controls.Add(this.panel1);
            this.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Name = "LoginConsole";
            this.Size = new System.Drawing.Size(682, 432);
            this.pnlLoginPrompt.ResumeLayout(false);
            this.pnlLoginPrompt.PerformLayout();
            this.pnlLoggingIn.ResumeLayout(false);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.pnlSplash.ResumeLayout(false);
            this.pnlTos.ResumeLayout(false);
            this.pnlTos.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        public System.Windows.Forms.Panel pnlLoginPrompt;
        public System.Windows.Forms.Label label1;
        public System.Windows.Forms.ComboBox cbxUsername;
        public System.Windows.Forms.Label label2;
        public System.Windows.Forms.TextBox txtPassword;
        public System.Windows.Forms.ComboBox cbxLocation;
        public System.Windows.Forms.Label label3;
        public System.Windows.Forms.Button btnLogin;
        public System.Windows.Forms.Panel pnlLoggingIn;
        public System.Windows.Forms.Label lblLoginStatus;
        public System.Windows.Forms.ProgressBar proLogin;
        public System.Windows.Forms.Button btnCancelLogin;
        public System.Windows.Forms.ComboBox cbxGrid;
        public System.Windows.Forms.Label label5;
        public System.Windows.Forms.TextBox txtCustomLoginUri;
        public System.Windows.Forms.Label label6;
        public System.Windows.Forms.Panel panel1;
        public System.Windows.Forms.Panel pnlSplash;
        public System.Windows.Forms.Panel pnlTos;
        public System.Windows.Forms.TextBox txtTOS;
        public System.Windows.Forms.CheckBox cbTOS;
        public System.Windows.Forms.Label lblVersion;
        public System.Windows.Forms.CheckBox cbRemember;

    }
}
