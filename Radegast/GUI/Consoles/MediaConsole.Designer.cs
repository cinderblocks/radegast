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
// $Id: Sound.cs 208 2009-09-08 03:37:37Z latifer@gmail.com $
//
namespace Radegast
{
    partial class MediaConsole
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
            this.volAudioStream = new System.Windows.Forms.TrackBar();
            this.pnlParcelAudio = new System.Windows.Forms.GroupBox();
            this.lblStation = new System.Windows.Forms.Label();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnPlay = new System.Windows.Forms.Button();
            this.cbKeep = new System.Windows.Forms.CheckBox();
            this.cbPlayAudioStream = new System.Windows.Forms.CheckBox();
            this.txtSongTitle = new System.Windows.Forms.TextBox();
            this.txtAudioURL = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.cbObjSoundEnable = new System.Windows.Forms.CheckBox();
            this.ObjSoundGroup = new System.Windows.Forms.GroupBox();
            this.objVolume = new System.Windows.Forms.TrackBar();
            this.UISoundsGroup = new System.Windows.Forms.GroupBox();
            this.UIVolume = new System.Windows.Forms.TrackBar();
            this.AudioDeviceGroup = new System.Windows.Forms.GroupBox();
            this.lblAudioDeviceStatus = new System.Windows.Forms.Label();
            this.btnRetryInit = new System.Windows.Forms.Button();
            this.btnRefreshDevices = new System.Windows.Forms.Button();
            this.cmbAudioDevice = new System.Windows.Forms.ComboBox();
            this.label3 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.volAudioStream)).BeginInit();
            this.pnlParcelAudio.SuspendLayout();
            this.ObjSoundGroup.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.objVolume)).BeginInit();
            this.UISoundsGroup.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.UIVolume)).BeginInit();
            this.AudioDeviceGroup.SuspendLayout();
            this.SuspendLayout();
            // 
            // volAudioStream
            // 
            this.volAudioStream.AccessibleName = "Parcel music volume";
            this.volAudioStream.LargeChange = 10;
            this.volAudioStream.Location = new System.Drawing.Point(6, 19);
            this.volAudioStream.Maximum = 50;
            this.volAudioStream.Name = "volAudioStream";
            this.volAudioStream.Size = new System.Drawing.Size(347, 45);
            this.volAudioStream.SmallChange = 2;
            this.volAudioStream.TabIndex = 0;
            this.volAudioStream.Value = 25;
            this.volAudioStream.Scroll += new System.EventHandler(this.volAudioStream_Scroll);
            // 
            // pnlParcelAudio
            // 
            this.pnlParcelAudio.Controls.Add(this.lblStation);
            this.pnlParcelAudio.Controls.Add(this.btnStop);
            this.pnlParcelAudio.Controls.Add(this.btnPlay);
            this.pnlParcelAudio.Controls.Add(this.cbKeep);
            this.pnlParcelAudio.Controls.Add(this.cbPlayAudioStream);
            this.pnlParcelAudio.Controls.Add(this.txtSongTitle);
            this.pnlParcelAudio.Controls.Add(this.txtAudioURL);
            this.pnlParcelAudio.Controls.Add(this.label2);
            this.pnlParcelAudio.Controls.Add(this.label1);
            this.pnlParcelAudio.Controls.Add(this.volAudioStream);
            this.pnlParcelAudio.Location = new System.Drawing.Point(3, 3);
            this.pnlParcelAudio.Name = "pnlParcelAudio";
            this.pnlParcelAudio.Size = new System.Drawing.Size(359, 138);
            this.pnlParcelAudio.TabIndex = 0;
            this.pnlParcelAudio.TabStop = false;
            this.pnlParcelAudio.Text = "Parcel Audio Stream";
            // 
            // lblStation
            // 
            this.lblStation.AutoSize = true;
            this.lblStation.ForeColor = System.Drawing.Color.Blue;
            this.lblStation.Location = new System.Drawing.Point(76, 93);
            this.lblStation.Name = "lblStation";
            this.lblStation.Size = new System.Drawing.Size(40, 13);
            this.lblStation.TabIndex = 6;
            this.lblStation.Text = "Station";
            // 
            // btnStop
            // 
            this.btnStop.AccessibleName = "Stop parcel music";
            this.btnStop.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnStop.Image = global::Radegast.Properties.Resources.btn_stop;
            this.btnStop.Location = new System.Drawing.Point(37, 99);
            this.btnStop.Margin = new System.Windows.Forms.Padding(0);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(25, 25);
            this.btnStop.TabIndex = 4;
            this.btnStop.TextImageRelation = System.Windows.Forms.TextImageRelation.TextAboveImage;
            this.btnStop.UseVisualStyleBackColor = true;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);
            // 
            // btnPlay
            // 
            this.btnPlay.AccessibleName = "Play parcel music";
            this.btnPlay.Image = global::Radegast.Properties.Resources.btn_play;
            this.btnPlay.Location = new System.Drawing.Point(10, 99);
            this.btnPlay.Margin = new System.Windows.Forms.Padding(0);
            this.btnPlay.Name = "btnPlay";
            this.btnPlay.Size = new System.Drawing.Size(25, 25);
            this.btnPlay.TabIndex = 3;
            this.btnPlay.TextImageRelation = System.Windows.Forms.TextImageRelation.TextAboveImage;
            this.btnPlay.UseVisualStyleBackColor = true;
            this.btnPlay.Click += new System.EventHandler(this.btnPlay_Click);
            // 
            // cbKeep
            // 
            this.cbKeep.AutoSize = true;
            this.cbKeep.Location = new System.Drawing.Point(232, 109);
            this.cbKeep.Name = "cbKeep";
            this.cbKeep.Size = new System.Drawing.Size(121, 17);
            this.cbKeep.TabIndex = 6;
            this.cbKeep.Text = "Keep current stream";
            this.toolTip1.SetToolTip(this.cbKeep, "When checked the audio player will keep the current audio stream and not change w" +
        "hen parcel audio changes");
            this.cbKeep.UseVisualStyleBackColor = true;
            // 
            // cbPlayAudioStream
            // 
            this.cbPlayAudioStream.AutoSize = true;
            this.cbPlayAudioStream.Location = new System.Drawing.Point(79, 109);
            this.cbPlayAudioStream.Name = "cbPlayAudioStream";
            this.cbPlayAudioStream.Size = new System.Drawing.Size(108, 17);
            this.cbPlayAudioStream.TabIndex = 5;
            this.cbPlayAudioStream.Text = "Auto start at login";
            this.cbPlayAudioStream.UseVisualStyleBackColor = true;
            // 
            // txtSongTitle
            // 
            this.txtSongTitle.AccessibleName = "Song title";
            this.txtSongTitle.BackColor = System.Drawing.SystemColors.Control;
            this.txtSongTitle.Location = new System.Drawing.Point(49, 70);
            this.txtSongTitle.Name = "txtSongTitle";
            this.txtSongTitle.ReadOnly = true;
            this.txtSongTitle.Size = new System.Drawing.Size(304, 20);
            this.txtSongTitle.TabIndex = 2;
            // 
            // txtAudioURL
            // 
            this.txtAudioURL.AccessibleName = "Stream URL";
            this.txtAudioURL.BackColor = System.Drawing.SystemColors.Control;
            this.txtAudioURL.Location = new System.Drawing.Point(49, 48);
            this.txtAudioURL.Name = "txtAudioURL";
            this.txtAudioURL.Size = new System.Drawing.Size(304, 20);
            this.txtAudioURL.TabIndex = 1;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(6, 73);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(32, 13);
            this.label2.TabIndex = 2;
            this.label2.Text = "Song";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(6, 51);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(40, 13);
            this.label1.TabIndex = 2;
            this.label1.Text = "Stream";
            // 
            // cbObjSoundEnable
            // 
            this.cbObjSoundEnable.AutoSize = true;
            this.cbObjSoundEnable.Location = new System.Drawing.Point(10, 44);
            this.cbObjSoundEnable.Name = "cbObjSoundEnable";
            this.cbObjSoundEnable.Size = new System.Drawing.Size(59, 17);
            this.cbObjSoundEnable.TabIndex = 1;
            this.cbObjSoundEnable.Text = "Enable";
            this.toolTip1.SetToolTip(this.cbObjSoundEnable, "When checked inworld sounds will play");
            this.cbObjSoundEnable.UseVisualStyleBackColor = true;
            // 
            // ObjSoundGroup
            // 
            this.ObjSoundGroup.AccessibleName = "In-world sounds volume";
            this.ObjSoundGroup.Controls.Add(this.cbObjSoundEnable);
            this.ObjSoundGroup.Controls.Add(this.objVolume);
            this.ObjSoundGroup.Location = new System.Drawing.Point(3, 147);
            this.ObjSoundGroup.Name = "ObjSoundGroup";
            this.ObjSoundGroup.Size = new System.Drawing.Size(358, 67);
            this.ObjSoundGroup.TabIndex = 1;
            this.ObjSoundGroup.TabStop = false;
            this.ObjSoundGroup.Text = "Object Sounds";
            // 
            // objVolume
            // 
            this.objVolume.AccessibleName = "Object sounds volume";
            this.objVolume.LargeChange = 10;
            this.objVolume.Location = new System.Drawing.Point(4, 12);
            this.objVolume.Maximum = 50;
            this.objVolume.Name = "objVolume";
            this.objVolume.Size = new System.Drawing.Size(347, 45);
            this.objVolume.SmallChange = 2;
            this.objVolume.TabIndex = 0;
            this.objVolume.Value = 40;
            // 
            // UISoundsGroup
            // 
            this.UISoundsGroup.AccessibleName = "UI sounds volume";
            this.UISoundsGroup.Controls.Add(this.UIVolume);
            this.UISoundsGroup.Location = new System.Drawing.Point(4, 220);
            this.UISoundsGroup.Name = "UISoundsGroup";
            this.UISoundsGroup.Size = new System.Drawing.Size(358, 53);
            this.UISoundsGroup.TabIndex = 3;
            this.UISoundsGroup.TabStop = false;
            this.UISoundsGroup.Text = "UI Sounds";
            // 
            // UIVolume
            // 
            this.UIVolume.AccessibleName = "UI sounds volume";
            this.UIVolume.LargeChange = 10;
            this.UIVolume.Location = new System.Drawing.Point(4, 12);
            this.UIVolume.Maximum = 50;
            this.UIVolume.Name = "UIVolume";
            this.UIVolume.Size = new System.Drawing.Size(347, 45);
            this.UIVolume.SmallChange = 2;
            this.UIVolume.TabIndex = 0;
            this.UIVolume.Value = 20;
            this.UIVolume.Scroll += new System.EventHandler(this.UIVolume_Scroll);
            // 
            // AudioDeviceGroup
            // 
            this.AudioDeviceGroup.Controls.Add(this.lblAudioDeviceStatus);
            this.AudioDeviceGroup.Controls.Add(this.btnRetryInit);
            this.AudioDeviceGroup.Controls.Add(this.btnRefreshDevices);
            this.AudioDeviceGroup.Controls.Add(this.cmbAudioDevice);
            this.AudioDeviceGroup.Controls.Add(this.label3);
            this.AudioDeviceGroup.Location = new System.Drawing.Point(4, 279);
            this.AudioDeviceGroup.Name = "AudioDeviceGroup";
            this.AudioDeviceGroup.Size = new System.Drawing.Size(358, 85);
            this.AudioDeviceGroup.TabIndex = 4;
            this.AudioDeviceGroup.TabStop = false;
            this.AudioDeviceGroup.Text = "Audio Device";
            // 
            // lblAudioDeviceStatus
            // 
            this.lblAudioDeviceStatus.AutoSize = true;
            this.lblAudioDeviceStatus.ForeColor = System.Drawing.Color.Green;
            this.lblAudioDeviceStatus.Location = new System.Drawing.Point(9, 62);
            this.lblAudioDeviceStatus.Name = "lblAudioDeviceStatus";
            this.lblAudioDeviceStatus.Size = new System.Drawing.Size(99, 13);
            this.lblAudioDeviceStatus.TabIndex = 4;
            this.lblAudioDeviceStatus.Text = "Sound system ready";
            // 
            // btnRetryInit
            // 
            this.btnRetryInit.Location = new System.Drawing.Point(261, 36);
            this.btnRetryInit.Name = "btnRetryInit";
            this.btnRetryInit.Size = new System.Drawing.Size(90, 23);
            this.btnRetryInit.TabIndex = 3;
            this.btnRetryInit.Text = "Retry Init";
            this.toolTip1.SetToolTip(this.btnRetryInit, "Retry sound system initialization if it failed");
            this.btnRetryInit.UseVisualStyleBackColor = true;
            this.btnRetryInit.Click += new System.EventHandler(this.btnRetryInit_Click);
            // 
            // btnRefreshDevices
            // 
            this.btnRefreshDevices.Location = new System.Drawing.Point(261, 14);
            this.btnRefreshDevices.Name = "btnRefreshDevices";
            this.btnRefreshDevices.Size = new System.Drawing.Size(90, 23);
            this.btnRefreshDevices.TabIndex = 2;
            this.btnRefreshDevices.Text = "Refresh";
            this.toolTip1.SetToolTip(this.btnRefreshDevices, "Refresh the list of available audio devices");
            this.btnRefreshDevices.UseVisualStyleBackColor = true;
            this.btnRefreshDevices.Click += new System.EventHandler(this.btnRefreshDevices_Click);
            // 
            // cmbAudioDevice
            // 
            this.cmbAudioDevice.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbAudioDevice.FormattingEnabled = true;
            this.cmbAudioDevice.Location = new System.Drawing.Point(56, 16);
            this.cmbAudioDevice.Name = "cmbAudioDevice";
            this.cmbAudioDevice.Size = new System.Drawing.Size(199, 21);
            this.cmbAudioDevice.TabIndex = 1;
            this.toolTip1.SetToolTip(this.cmbAudioDevice, "Select the audio output device to use");
            this.cmbAudioDevice.SelectedIndexChanged += new System.EventHandler(this.cmbAudioDevice_SelectedIndexChanged);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(9, 19);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(41, 13);
            this.label3.TabIndex = 0;
            this.label3.Text = "Device";
            // 
            // MediaConsole
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.AudioDeviceGroup);
            this.Controls.Add(this.UISoundsGroup);
            this.Controls.Add(this.ObjSoundGroup);
            this.Controls.Add(this.pnlParcelAudio);
            this.Name = "MediaConsole";
            this.Size = new System.Drawing.Size(368, 370);
            ((System.ComponentModel.ISupportInitialize)(this.volAudioStream)).EndInit();
            this.pnlParcelAudio.ResumeLayout(false);
            this.pnlParcelAudio.PerformLayout();
            this.ObjSoundGroup.ResumeLayout(false);
            this.ObjSoundGroup.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.objVolume)).EndInit();
            this.UISoundsGroup.ResumeLayout(false);
            this.UISoundsGroup.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.UIVolume)).EndInit();
            this.AudioDeviceGroup.ResumeLayout(false);
            this.AudioDeviceGroup.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        public System.Windows.Forms.TrackBar volAudioStream;
        public System.Windows.Forms.GroupBox pnlParcelAudio;
        public System.Windows.Forms.TextBox txtAudioURL;
        public System.Windows.Forms.Label label1;
        public System.Windows.Forms.CheckBox cbKeep;
        public System.Windows.Forms.CheckBox cbPlayAudioStream;
        public System.Windows.Forms.Button btnPlay;
        public System.Windows.Forms.Button btnStop;
        public System.Windows.Forms.TextBox txtSongTitle;
        public System.Windows.Forms.Label label2;
        public System.Windows.Forms.ToolTip toolTip1;
        public System.Windows.Forms.Label lblStation;
        private System.Windows.Forms.GroupBox ObjSoundGroup;
        public System.Windows.Forms.CheckBox cbObjSoundEnable;
        public System.Windows.Forms.TrackBar objVolume;
        private System.Windows.Forms.GroupBox UISoundsGroup;
        public System.Windows.Forms.TrackBar UIVolume;
        private System.Windows.Forms.GroupBox AudioDeviceGroup;
        private System.Windows.Forms.ComboBox cmbAudioDevice;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Button btnRefreshDevices;
        private System.Windows.Forms.Button btnRetryInit;
        private System.Windows.Forms.Label lblAudioDeviceStatus;

    }
}
