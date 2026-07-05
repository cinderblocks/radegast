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
    partial class ntfSendLureOffer
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
            this.btnOffer = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.txtHead = new System.Windows.Forms.TextBox();
            this.txtMessage = new System.Windows.Forms.TextBox();
            this.SuspendLayout();
            // 
            // btnOffer
            // 
            this.btnOffer.Location = new System.Drawing.Point(11, 106);
            this.btnOffer.Name = "btnOffer";
            this.btnOffer.Size = new System.Drawing.Size(75, 23);
            this.btnOffer.TabIndex = 0;
            this.btnOffer.Text = "Offer";
            this.btnOffer.UseVisualStyleBackColor = true;
            this.btnOffer.Click += new System.EventHandler(this.btnOffer_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.Location = new System.Drawing.Point(92, 106);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 1;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // txtHead
            // 
            this.txtHead.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtHead.Location = new System.Drawing.Point(11, 11);
            this.txtHead.Multiline = true;
            this.txtHead.Name = "txtHead";
            this.txtHead.ReadOnly = true;
            this.txtHead.Size = new System.Drawing.Size(256, 34);
            this.txtHead.TabIndex = 2;
            this.txtHead.TabStop = false;
            this.txtHead.Text = "Offer a teleport to [NAME] with the following message:";
            // 
            // txtMessage
            // 
            this.txtMessage.Location = new System.Drawing.Point(11, 51);
            this.txtMessage.Multiline = true;
            this.txtMessage.Name = "txtMessage";
            this.txtMessage.Size = new System.Drawing.Size(256, 49);
            this.txtMessage.TabIndex = 3;
            this.txtMessage.Text = "Join me in [LOCATION]!";
            // 
            // ntfSendLureOffer
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.txtMessage);
            this.Controls.Add(this.txtHead);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOffer);
            this.Name = "ntfSendLureOffer";
            this.Size = new System.Drawing.Size(275, 137);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public System.Windows.Forms.Button btnOffer;
        public System.Windows.Forms.Button btnCancel;
        public System.Windows.Forms.TextBox txtHead;
        private System.Windows.Forms.TextBox txtMessage;
    }
}
