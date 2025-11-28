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
namespace Radegast.Rendering
{
    partial class SceneWindow
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
            RenderingEnabled = false;

            if (disposing)
            {
                DisposeInternal();
                if (components != null)
                {
                    components.Dispose();
                }
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
            this.components = new System.ComponentModel.Container();
            this.pnlDebug = new System.Windows.Forms.GroupBox();
            this.cbMisc = new System.Windows.Forms.CheckBox();
            this.button_driver = new System.Windows.Forms.Button();
            this.textBox_driveramount = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.comboBox_driver = new System.Windows.Forms.ComboBox();
            this.button1 = new System.Windows.Forms.Button();
            this.textBox_morphamount = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.comboBox_morph = new System.Windows.Forms.ComboBox();
            this.textBox_sz = new System.Windows.Forms.TextBox();
            this.textBox_sy = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.textBox_sx = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.textBox_z = new System.Windows.Forms.TextBox();
            this.textBox_y = new System.Windows.Forms.TextBox();
            this.comboBox1 = new System.Windows.Forms.ComboBox();
            this.button_vparam = new System.Windows.Forms.Button();
            this.textBox_x = new System.Windows.Forms.TextBox();
            this.hsLOD = new System.Windows.Forms.HScrollBar();
            this.hsSpecular = new System.Windows.Forms.HScrollBar();
            this.hsDiffuse = new System.Windows.Forms.HScrollBar();
            this.hsAmbient = new System.Windows.Forms.HScrollBar();
            this.label2 = new System.Windows.Forms.Label();
            this.btnResetView = new System.Windows.Forms.Button();
            this.ctxMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.pnlChat = new System.Windows.Forms.Panel();
            this.cbChatType = new System.Windows.Forms.ComboBox();
            this.btnSay = new System.Windows.Forms.Button();
            this.txtChat = new Radegast.Rendering.ChatBox();
            this.pnlDebug.SuspendLayout();
            this.pnlChat.SuspendLayout();
            this.SuspendLayout();
            // 
            // pnlDebug
            // 
            this.pnlDebug.Controls.Add(this.cbMisc);
            this.pnlDebug.Controls.Add(this.button_driver);
            this.pnlDebug.Controls.Add(this.textBox_driveramount);
            this.pnlDebug.Controls.Add(this.label6);
            this.pnlDebug.Controls.Add(this.comboBox_driver);
            this.pnlDebug.Controls.Add(this.button1);
            this.pnlDebug.Controls.Add(this.textBox_morphamount);
            this.pnlDebug.Controls.Add(this.label5);
            this.pnlDebug.Controls.Add(this.comboBox_morph);
            this.pnlDebug.Controls.Add(this.textBox_sz);
            this.pnlDebug.Controls.Add(this.textBox_sy);
            this.pnlDebug.Controls.Add(this.label4);
            this.pnlDebug.Controls.Add(this.textBox_sx);
            this.pnlDebug.Controls.Add(this.label3);
            this.pnlDebug.Controls.Add(this.label1);
            this.pnlDebug.Controls.Add(this.textBox_z);
            this.pnlDebug.Controls.Add(this.textBox_y);
            this.pnlDebug.Controls.Add(this.comboBox1);
            this.pnlDebug.Controls.Add(this.button_vparam);
            this.pnlDebug.Controls.Add(this.textBox_x);
            this.pnlDebug.Controls.Add(this.hsLOD);
            this.pnlDebug.Controls.Add(this.hsSpecular);
            this.pnlDebug.Controls.Add(this.hsDiffuse);
            this.pnlDebug.Controls.Add(this.hsAmbient);
            this.pnlDebug.Controls.Add(this.label2);
            this.pnlDebug.Controls.Add(this.btnResetView);
            this.pnlDebug.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlDebug.Location = new System.Drawing.Point(0, 337);
            this.pnlDebug.Name = "pnlDebug";
            this.pnlDebug.Size = new System.Drawing.Size(779, 163);
            this.pnlDebug.TabIndex = 8;
            this.pnlDebug.TabStop = false;
            this.pnlDebug.Visible = false;
            // 
            // cbMisc
            // 
            this.cbMisc.AutoSize = true;
            this.cbMisc.Checked = true;
            this.cbMisc.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbMisc.Location = new System.Drawing.Point(358, 142);
            this.cbMisc.Name = "cbMisc";
            this.cbMisc.Size = new System.Drawing.Size(48, 17);
            this.cbMisc.TabIndex = 49;
            this.cbMisc.Text = "Misc";
            this.cbMisc.UseVisualStyleBackColor = true;
            this.cbMisc.CheckedChanged += new System.EventHandler(this.cbMisc_CheckedChanged);
            // 
            // button_driver
            // 
            this.button_driver.Location = new System.Drawing.Point(277, 140);
            this.button_driver.Name = "button_driver";
            this.button_driver.Size = new System.Drawing.Size(75, 23);
            this.button_driver.TabIndex = 46;
            this.button_driver.Text = "Driver";
            this.button_driver.UseVisualStyleBackColor = true;
            this.button_driver.Click += new System.EventHandler(this.button_driver_Click);
            // 
            // textBox_driveramount
            // 
            this.textBox_driveramount.Location = new System.Drawing.Point(204, 142);
            this.textBox_driveramount.Name = "textBox_driveramount";
            this.textBox_driveramount.Size = new System.Drawing.Size(67, 20);
            this.textBox_driveramount.TabIndex = 45;
            this.textBox_driveramount.Text = "0";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(9, 144);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(35, 13);
            this.label6.TabIndex = 44;
            this.label6.Text = "Driver";
            // 
            // comboBox_driver
            // 
            this.comboBox_driver.FormattingEnabled = true;
            this.comboBox_driver.Location = new System.Drawing.Point(74, 141);
            this.comboBox_driver.Name = "comboBox_driver";
            this.comboBox_driver.Size = new System.Drawing.Size(121, 21);
            this.comboBox_driver.TabIndex = 43;
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(277, 114);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(75, 23);
            this.button1.TabIndex = 42;
            this.button1.Text = "Apply";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // textBox_morphamount
            // 
            this.textBox_morphamount.Location = new System.Drawing.Point(204, 116);
            this.textBox_morphamount.Name = "textBox_morphamount";
            this.textBox_morphamount.Size = new System.Drawing.Size(67, 20);
            this.textBox_morphamount.TabIndex = 41;
            this.textBox_morphamount.Text = "0";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(9, 118);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(37, 13);
            this.label5.TabIndex = 40;
            this.label5.Text = "Morph";
            // 
            // comboBox_morph
            // 
            this.comboBox_morph.FormattingEnabled = true;
            this.comboBox_morph.Items.AddRange(new object[] {
            "mPelvis",
            "mTorso",
            "mChest",
            "mNeck",
            "mHead",
            "mCollarLeft",
            "mCollarRight",
            "mShoulderLeft",
            "mShoulderRight",
            "mElbowLeft",
            "mElbowRight",
            "mWristRight",
            "mWristLeft",
            "mHipRight",
            "mHipLeft",
            "mKneeRight",
            "mKneeLeft",
            "mAnkleRight",
            "mAnkleLeft",
            "mFootLeft",
            "mFootRight",
            "mToeLeft",
            "mToeRight"});
            this.comboBox_morph.Location = new System.Drawing.Point(74, 115);
            this.comboBox_morph.Name = "comboBox_morph";
            this.comboBox_morph.Size = new System.Drawing.Size(121, 21);
            this.comboBox_morph.TabIndex = 39;
            this.comboBox_morph.SelectedIndexChanged += new System.EventHandler(this.comboBox_morph_SelectedIndexChanged);
            // 
            // textBox_sz
            // 
            this.textBox_sz.Location = new System.Drawing.Point(466, 90);
            this.textBox_sz.Name = "textBox_sz";
            this.textBox_sz.Size = new System.Drawing.Size(32, 20);
            this.textBox_sz.TabIndex = 38;
            this.textBox_sz.Text = "0";
            // 
            // textBox_sy
            // 
            this.textBox_sy.Location = new System.Drawing.Point(428, 91);
            this.textBox_sy.Name = "textBox_sy";
            this.textBox_sy.Size = new System.Drawing.Size(32, 20);
            this.textBox_sy.TabIndex = 37;
            this.textBox_sy.Text = "0";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(353, 94);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(34, 13);
            this.label4.TabIndex = 36;
            this.label4.Text = "Scale";
            // 
            // textBox_sx
            // 
            this.textBox_sx.Location = new System.Drawing.Point(390, 91);
            this.textBox_sx.Name = "textBox_sx";
            this.textBox_sx.Size = new System.Drawing.Size(32, 20);
            this.textBox_sx.TabIndex = 33;
            this.textBox_sx.Text = "0";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(201, 92);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(24, 13);
            this.label3.TabIndex = 32;
            this.label3.Text = "Rot";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(9, 92);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(59, 13);
            this.label1.TabIndex = 31;
            this.label1.Text = "Bone/Joint";
            // 
            // textBox_z
            // 
            this.textBox_z.Location = new System.Drawing.Point(315, 90);
            this.textBox_z.Name = "textBox_z";
            this.textBox_z.Size = new System.Drawing.Size(32, 20);
            this.textBox_z.TabIndex = 30;
            this.textBox_z.Text = "0";
            this.textBox_z.TextChanged += new System.EventHandler(this.textBox_z_TextChanged);
            // 
            // textBox_y
            // 
            this.textBox_y.Location = new System.Drawing.Point(277, 90);
            this.textBox_y.Name = "textBox_y";
            this.textBox_y.Size = new System.Drawing.Size(32, 20);
            this.textBox_y.TabIndex = 29;
            this.textBox_y.Text = "0";
            this.textBox_y.TextChanged += new System.EventHandler(this.textBox_y_TextChanged);
            // 
            // comboBox1
            // 
            this.comboBox1.FormattingEnabled = true;
            this.comboBox1.Items.AddRange(new object[] {
            "mPelvis",
            "mTorso",
            "mChest",
            "mNeck",
            "mHead",
            "mCollarLeft",
            "mCollarRight",
            "mShoulderLeft",
            "mShoulderRight",
            "mElbowLeft",
            "mElbowRight",
            "mWristRight",
            "mWristLeft",
            "mHipRight",
            "mHipLeft",
            "mKneeRight",
            "mKneeLeft",
            "mAnkleRight",
            "mAnkleLeft",
            "mFootLeft",
            "mFootRight",
            "mToeLeft",
            "mToeRight"});
            this.comboBox1.Location = new System.Drawing.Point(74, 88);
            this.comboBox1.Name = "comboBox1";
            this.comboBox1.Size = new System.Drawing.Size(121, 21);
            this.comboBox1.TabIndex = 28;
            this.comboBox1.SelectedIndexChanged += new System.EventHandler(this.comboBox1_SelectedIndexChanged);
            // 
            // button_vparam
            // 
            this.button_vparam.Location = new System.Drawing.Point(514, 88);
            this.button_vparam.Name = "button_vparam";
            this.button_vparam.Size = new System.Drawing.Size(75, 23);
            this.button_vparam.TabIndex = 27;
            this.button_vparam.Text = "Apply";
            this.button_vparam.UseVisualStyleBackColor = true;
            this.button_vparam.Click += new System.EventHandler(this.button_vparam_Click);
            // 
            // textBox_x
            // 
            this.textBox_x.Location = new System.Drawing.Point(239, 89);
            this.textBox_x.Name = "textBox_x";
            this.textBox_x.Size = new System.Drawing.Size(32, 20);
            this.textBox_x.TabIndex = 26;
            this.textBox_x.Text = "0";
            this.textBox_x.TextChanged += new System.EventHandler(this.textBox_vparamid_TextChanged);
            // 
            // hsLOD
            // 
            this.hsLOD.Location = new System.Drawing.Point(343, 67);
            this.hsLOD.Name = "hsLOD";
            this.hsLOD.Size = new System.Drawing.Size(292, 17);
            this.hsLOD.TabIndex = 24;
            this.hsLOD.Value = 25;
            this.hsLOD.Scroll += new System.Windows.Forms.ScrollEventHandler(this.hsLOD_Scroll);
            // 
            // hsSpecular
            // 
            this.hsSpecular.Location = new System.Drawing.Point(343, 50);
            this.hsSpecular.Name = "hsSpecular";
            this.hsSpecular.Size = new System.Drawing.Size(292, 17);
            this.hsSpecular.TabIndex = 24;
            this.hsSpecular.Value = 30;
            this.hsSpecular.Scroll += new System.Windows.Forms.ScrollEventHandler(this.hsSpecular_Scroll);
            // 
            // hsDiffuse
            // 
            this.hsDiffuse.Location = new System.Drawing.Point(343, 33);
            this.hsDiffuse.Name = "hsDiffuse";
            this.hsDiffuse.Size = new System.Drawing.Size(292, 17);
            this.hsDiffuse.TabIndex = 24;
            this.hsDiffuse.Value = 25;
            this.hsDiffuse.Scroll += new System.Windows.Forms.ScrollEventHandler(this.hsDiffuse_Scroll);
            // 
            // hsAmbient
            // 
            this.hsAmbient.Location = new System.Drawing.Point(343, 16);
            this.hsAmbient.Name = "hsAmbient";
            this.hsAmbient.Size = new System.Drawing.Size(292, 17);
            this.hsAmbient.TabIndex = 24;
            this.hsAmbient.Value = 20;
            this.hsAmbient.Scroll += new System.Windows.Forms.ScrollEventHandler(this.hsAmbient_Scroll);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(9, 3);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(167, 13);
            this.label2.TabIndex = 23;
            this.label2.Text = "Alt-Drag to Zoom, Ctrl-Drag to pan";
            // 
            // btnResetView
            // 
            this.btnResetView.Location = new System.Drawing.Point(12, 19);
            this.btnResetView.Name = "btnResetView";
            this.btnResetView.Size = new System.Drawing.Size(94, 23);
            this.btnResetView.TabIndex = 22;
            this.btnResetView.Text = "Reset View";
            this.btnResetView.UseVisualStyleBackColor = true;
            this.btnResetView.Click += new System.EventHandler(this.btnReset_Click);
            // 
            // ctxMenu
            // 
            this.ctxMenu.Name = "ctxObjects";
            this.ctxMenu.Size = new System.Drawing.Size(61, 4);
            this.ctxMenu.Opening += new System.ComponentModel.CancelEventHandler(this.ctxObjects_Opening);
            // 
            // pnlChat
            // 
            this.pnlChat.Controls.Add(this.cbChatType);
            this.pnlChat.Controls.Add(this.btnSay);
            this.pnlChat.Controls.Add(this.txtChat);
            this.pnlChat.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlChat.Location = new System.Drawing.Point(0, 314);
            this.pnlChat.Name = "pnlChat";
            this.pnlChat.Size = new System.Drawing.Size(779, 23);
            this.pnlChat.TabIndex = 9;
            // 
            // cbChatType
            // 
            this.cbChatType.AccessibleName = "Chat type";
            this.cbChatType.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cbChatType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbChatType.Enabled = false;
            this.cbChatType.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.cbChatType.FormattingEnabled = true;
            this.cbChatType.Items.AddRange(new object[] {
            "Whisper",
            "Normal",
            "Shout"});
            this.cbChatType.Location = new System.Drawing.Point(700, 0);
            this.cbChatType.Name = "cbChatType";
            this.cbChatType.Size = new System.Drawing.Size(73, 21);
            this.cbChatType.TabIndex = 2;
            // 
            // btnSay
            // 
            this.btnSay.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSay.Enabled = false;
            this.btnSay.FlatAppearance.BorderSize = 0;
            this.btnSay.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSay.Location = new System.Drawing.Point(650, 0);
            this.btnSay.Name = "btnSay";
            this.btnSay.Size = new System.Drawing.Size(50, 22);
            this.btnSay.TabIndex = 1;
            this.btnSay.Text = "Say";
            this.btnSay.UseVisualStyleBackColor = true;
            this.btnSay.Click += new System.EventHandler(this.btnSay_Click);
            // 
            // txtChat
            // 
            this.txtChat.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.txtChat.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtChat.Location = new System.Drawing.Point(0, 1);
            this.txtChat.Name = "txtChat";
            this.txtChat.Size = new System.Drawing.Size(650, 20);
            this.txtChat.TabIndex = 0;
            this.txtChat.TextChanged += new System.EventHandler(this.txtChat_TextChanged);
            this.txtChat.KeyDown += new System.Windows.Forms.KeyEventHandler(this.txtChat_KeyDown);
            // 
            // SceneWindow
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.pnlChat);
            this.Controls.Add(this.pnlDebug);
            this.Name = "SceneWindow";
            this.Size = new System.Drawing.Size(779, 500);
            this.Load += new System.EventHandler(this.ControlLoaded);
            this.pnlDebug.ResumeLayout(false);
            this.pnlDebug.PerformLayout();
            this.pnlChat.ResumeLayout(false);
            this.pnlChat.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        public System.Windows.Forms.GroupBox pnlDebug;
        public System.Windows.Forms.ContextMenuStrip ctxMenu;
        public System.Windows.Forms.Button btnResetView;
        public System.Windows.Forms.Label label2;
        public System.Windows.Forms.Panel pnlChat;
        public Radegast.Rendering.ChatBox txtChat;
        public System.Windows.Forms.HScrollBar hsSpecular;
        public System.Windows.Forms.HScrollBar hsDiffuse;
        public System.Windows.Forms.HScrollBar hsAmbient;
        public System.Windows.Forms.HScrollBar hsLOD;
        public System.Windows.Forms.Button button_vparam;
        public System.Windows.Forms.TextBox textBox_x;
        public System.Windows.Forms.ComboBox comboBox1;
        public System.Windows.Forms.TextBox textBox_z;
        public System.Windows.Forms.TextBox textBox_y;
        public System.Windows.Forms.Label label4;
        public System.Windows.Forms.TextBox textBox_sx;
        public System.Windows.Forms.Label label3;
        public System.Windows.Forms.Label label1;
        public System.Windows.Forms.TextBox textBox_sz;
        public System.Windows.Forms.TextBox textBox_sy;
        public System.Windows.Forms.Button button1;
        public System.Windows.Forms.TextBox textBox_morphamount;
        public System.Windows.Forms.Label label5;
        public System.Windows.Forms.ComboBox comboBox_morph;
        public System.Windows.Forms.Button button_driver;
        public System.Windows.Forms.TextBox textBox_driveramount;
        public System.Windows.Forms.Label label6;
        public System.Windows.Forms.ComboBox comboBox_driver;
        public System.Windows.Forms.CheckBox cbMisc;
        public System.Windows.Forms.Button btnSay;
        public System.Windows.Forms.ComboBox cbChatType;

    }
}

