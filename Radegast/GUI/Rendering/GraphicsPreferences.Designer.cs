namespace Radegast.Rendering
{
    partial class GraphicsPreferences
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
            this.cbAA = new System.Windows.Forms.CheckBox();
            this.chkWireFrame = new System.Windows.Forms.CheckBox();
            this.lblDrawDistance = new System.Windows.Forms.Label();
            this.tbDrawDistance = new System.Windows.Forms.TrackBar();
            this.cbWaterReflections = new System.Windows.Forms.CheckBox();
            this.cbOcclusionCulling = new System.Windows.Forms.CheckBox();
            this.cbShiny = new System.Windows.Forms.CheckBox();
            this.lblAmbient = new System.Windows.Forms.Label();
            this.tbAmbient = new System.Windows.Forms.TrackBar();
            this.lblDiffuse = new System.Windows.Forms.Label();
            this.tbDiffuse = new System.Windows.Forms.TrackBar();
            this.lblSpecular = new System.Windows.Forms.Label();
            this.tbSpecular = new System.Windows.Forms.TrackBar();
            this.cbFallbackAnim = new System.Windows.Forms.CheckBox();
            this.lblFallbackSpeed = new System.Windows.Forms.Label();
            this.nudFallbackSpeed = new System.Windows.Forms.NumericUpDown();
            this.lblFallbackAmp = new System.Windows.Forms.Label();
            this.nudFallbackAmp = new System.Windows.Forms.NumericUpDown();
            this.lblFallbackBaseAlpha = new System.Windows.Forms.Label();
            this.nudFallbackBaseAlpha = new System.Windows.Forms.NumericUpDown();
            this.tbGamma = new System.Windows.Forms.TrackBar();
            this.lblGamma = new System.Windows.Forms.Label();
            this.tbEmissive = new System.Windows.Forms.TrackBar();
            this.lblEmissive = new System.Windows.Forms.Label();
            this.cbMaterials = new System.Windows.Forms.CheckBox();
            this.cbGlow = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.tbDrawDistance)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.tbAmbient)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.tbDiffuse)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.tbSpecular)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudFallbackSpeed)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudFallbackAmp)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudFallbackBaseAlpha)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.tbGamma)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.tbEmissive)).BeginInit();
            this.SuspendLayout();
            // 
            // cbAA
            // 
            this.cbAA.AutoSize = true;
            this.cbAA.Location = new System.Drawing.Point(3, 62);
            this.cbAA.Name = "cbAA";
            this.cbAA.Size = new System.Drawing.Size(160, 17);
            this.cbAA.TabIndex = 2;
            this.cbAA.Text = "Anti-aliasing (requires restart)";
            this.cbAA.UseVisualStyleBackColor = true;
            this.cbAA.CheckedChanged += new System.EventHandler(this.cbAA_CheckedChanged);
            // 
            // chkWireFrame
            // 
            this.chkWireFrame.AutoSize = true;
            this.chkWireFrame.Location = new System.Drawing.Point(3, 6);
            this.chkWireFrame.Name = "chkWireFrame";
            this.chkWireFrame.Size = new System.Drawing.Size(74, 17);
            this.chkWireFrame.TabIndex = 1;
            this.chkWireFrame.Text = "Wireframe";
            this.chkWireFrame.UseVisualStyleBackColor = true;
            this.chkWireFrame.CheckedChanged += new System.EventHandler(this.chkWireFrame_CheckedChanged);
            // 
            // lblDrawDistance
            // 
            this.lblDrawDistance.AutoSize = true;
            this.lblDrawDistance.Location = new System.Drawing.Point(3, 100);
            this.lblDrawDistance.Name = "lblDrawDistance";
            this.lblDrawDistance.Size = new System.Drawing.Size(93, 13);
            this.lblDrawDistance.TabIndex = 21;
            this.lblDrawDistance.Text = "Draw distance: 48";
            this.lblDrawDistance.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // tbDrawDistance
            // 
            this.tbDrawDistance.Location = new System.Drawing.Point(90, 96);
            this.tbDrawDistance.Maximum = 256;
            this.tbDrawDistance.Minimum = 32;
            this.tbDrawDistance.Name = "tbDrawDistance";
            this.tbDrawDistance.Size = new System.Drawing.Size(183, 45);
            this.tbDrawDistance.TabIndex = 20;
            this.tbDrawDistance.TickStyle = System.Windows.Forms.TickStyle.None;
            this.tbDrawDistance.Value = 48;
            this.tbDrawDistance.Scroll += new System.EventHandler(this.tbDrawDistance_Scroll);
            // 
            // cbWaterReflections
            // 
            this.cbWaterReflections.AutoSize = true;
            this.cbWaterReflections.Location = new System.Drawing.Point(6, 321);
            this.cbWaterReflections.Name = "cbWaterReflections";
            this.cbWaterReflections.Size = new System.Drawing.Size(111, 17);
            this.cbWaterReflections.TabIndex = 3;
            this.cbWaterReflections.Text = "Water Reflections";
            this.cbWaterReflections.UseVisualStyleBackColor = true;
            this.cbWaterReflections.CheckedChanged += new System.EventHandler(this.cbWaterReflections_CheckedChanged);
            // 
            // cbOcclusionCulling
            // 
            this.cbOcclusionCulling.AutoSize = true;
            this.cbOcclusionCulling.Location = new System.Drawing.Point(150, 6);
            this.cbOcclusionCulling.Name = "cbOcclusionCulling";
            this.cbOcclusionCulling.Size = new System.Drawing.Size(107, 17);
            this.cbOcclusionCulling.TabIndex = 5;
            this.cbOcclusionCulling.Text = "Occlusion Culling";
            this.cbOcclusionCulling.UseVisualStyleBackColor = true;
            this.cbOcclusionCulling.CheckedChanged += new System.EventHandler(this.cbOcclusionCulling_CheckedChanged);
            // 
            // cbShiny
            // 
            this.cbShiny.AutoSize = true;
            this.cbShiny.Location = new System.Drawing.Point(3, 34);
            this.cbShiny.Name = "cbShiny";
            this.cbShiny.Size = new System.Drawing.Size(52, 17);
            this.cbShiny.TabIndex = 4;
            this.cbShiny.Text = "Shiny";
            this.cbShiny.UseVisualStyleBackColor = true;
            this.cbShiny.CheckedChanged += new System.EventHandler(this.cbShiny_CheckedChanged);
            // 
            // lblAmbient
            // 
            this.lblAmbient.AutoSize = true;
            this.lblAmbient.Location = new System.Drawing.Point(3, 218);
            this.lblAmbient.Name = "lblAmbient";
            this.lblAmbient.Size = new System.Drawing.Size(72, 13);
            this.lblAmbient.TabIndex = 27;
            this.lblAmbient.Text = "Ambient: 0.70";
            this.lblAmbient.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // tbAmbient
            // 
            this.tbAmbient.Location = new System.Drawing.Point(90, 215);
            this.tbAmbient.Maximum = 100;
            this.tbAmbient.Name = "tbAmbient";
            this.tbAmbient.Size = new System.Drawing.Size(183, 45);
            this.tbAmbient.TabIndex = 26;
            this.tbAmbient.TickStyle = System.Windows.Forms.TickStyle.None;
            this.tbAmbient.Value = 70;
            this.tbAmbient.Scroll += new System.EventHandler(this.tbAmbient_Scroll);
            // 
            // lblDiffuse
            // 
            this.lblDiffuse.AutoSize = true;
            this.lblDiffuse.Location = new System.Drawing.Point(3, 252);
            this.lblDiffuse.Name = "lblDiffuse";
            this.lblDiffuse.Size = new System.Drawing.Size(67, 13);
            this.lblDiffuse.TabIndex = 29;
            this.lblDiffuse.Text = "Diffuse: 0.80";
            this.lblDiffuse.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // tbDiffuse
            // 
            this.tbDiffuse.Location = new System.Drawing.Point(90, 249);
            this.tbDiffuse.Maximum = 100;
            this.tbDiffuse.Name = "tbDiffuse";
            this.tbDiffuse.Size = new System.Drawing.Size(183, 45);
            this.tbDiffuse.TabIndex = 28;
            this.tbDiffuse.TickStyle = System.Windows.Forms.TickStyle.None;
            this.tbDiffuse.Value = 80;
            this.tbDiffuse.Scroll += new System.EventHandler(this.tbDiffuse_Scroll);
            // 
            // lblSpecular
            // 
            this.lblSpecular.AutoSize = true;
            this.lblSpecular.Location = new System.Drawing.Point(3, 285);
            this.lblSpecular.Name = "lblSpecular";
            this.lblSpecular.Size = new System.Drawing.Size(76, 13);
            this.lblSpecular.TabIndex = 31;
            this.lblSpecular.Text = "Specular: 0.50";
            this.lblSpecular.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // tbSpecular
            // 
            this.tbSpecular.Location = new System.Drawing.Point(90, 282);
            this.tbSpecular.Maximum = 100;
            this.tbSpecular.Name = "tbSpecular";
            this.tbSpecular.Size = new System.Drawing.Size(183, 45);
            this.tbSpecular.TabIndex = 30;
            this.tbSpecular.TickStyle = System.Windows.Forms.TickStyle.None;
            this.tbSpecular.Value = 50;
            this.tbSpecular.Scroll += new System.EventHandler(this.tbSpecular_Scroll);
            // 
            // cbFallbackAnim
            // 
            this.cbFallbackAnim.AutoSize = true;
            this.cbFallbackAnim.Location = new System.Drawing.Point(123, 321);
            this.cbFallbackAnim.Name = "cbFallbackAnim";
            this.cbFallbackAnim.Size = new System.Drawing.Size(103, 17);
            this.cbFallbackAnim.TabIndex = 32;
            this.cbFallbackAnim.Text = "Water animation";
            this.cbFallbackAnim.UseVisualStyleBackColor = true;
            this.cbFallbackAnim.CheckedChanged += new System.EventHandler(this.cbFallbackAnim_CheckedChanged);
            // 
            // lblFallbackSpeed
            // 
            this.lblFallbackSpeed.AutoSize = true;
            this.lblFallbackSpeed.Location = new System.Drawing.Point(6, 346);
            this.lblFallbackSpeed.Name = "lblFallbackSpeed";
            this.lblFallbackSpeed.Size = new System.Drawing.Size(41, 13);
            this.lblFallbackSpeed.TabIndex = 33;
            this.lblFallbackSpeed.Text = "Speed:";
            // 
            // nudFallbackSpeed
            // 
            this.nudFallbackSpeed.DecimalPlaces = 2;
            this.nudFallbackSpeed.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.nudFallbackSpeed.Location = new System.Drawing.Point(48, 344);
            this.nudFallbackSpeed.Maximum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.nudFallbackSpeed.Name = "nudFallbackSpeed";
            this.nudFallbackSpeed.Size = new System.Drawing.Size(41, 20);
            this.nudFallbackSpeed.TabIndex = 34;
            this.nudFallbackSpeed.ValueChanged += new System.EventHandler(this.nudFallbackSpeed_ValueChanged);
            // 
            // lblFallbackAmp
            // 
            this.lblFallbackAmp.AutoSize = true;
            this.lblFallbackAmp.Location = new System.Drawing.Point(95, 346);
            this.lblFallbackAmp.Name = "lblFallbackAmp";
            this.lblFallbackAmp.Size = new System.Drawing.Size(56, 13);
            this.lblFallbackAmp.TabIndex = 35;
            this.lblFallbackAmp.Text = "Amplitude:";
            // 
            // nudFallbackAmp
            // 
            this.nudFallbackAmp.DecimalPlaces = 2;
            this.nudFallbackAmp.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.nudFallbackAmp.Location = new System.Drawing.Point(152, 344);
            this.nudFallbackAmp.Maximum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.nudFallbackAmp.Name = "nudFallbackAmp";
            this.nudFallbackAmp.Size = new System.Drawing.Size(41, 20);
            this.nudFallbackAmp.TabIndex = 36;
            this.nudFallbackAmp.ValueChanged += new System.EventHandler(this.nudFallbackAmp_ValueChanged);
            // 
            // lblFallbackBaseAlpha
            // 
            this.lblFallbackBaseAlpha.AutoSize = true;
            this.lblFallbackBaseAlpha.Location = new System.Drawing.Point(202, 346);
            this.lblFallbackBaseAlpha.Name = "lblFallbackBaseAlpha";
            this.lblFallbackBaseAlpha.Size = new System.Drawing.Size(37, 13);
            this.lblFallbackBaseAlpha.TabIndex = 37;
            this.lblFallbackBaseAlpha.Text = "Alpha:";
            // 
            // nudFallbackBaseAlpha
            // 
            this.nudFallbackBaseAlpha.DecimalPlaces = 2;
            this.nudFallbackBaseAlpha.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.nudFallbackBaseAlpha.Location = new System.Drawing.Point(238, 342);
            this.nudFallbackBaseAlpha.Maximum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.nudFallbackBaseAlpha.Name = "nudFallbackBaseAlpha";
            this.nudFallbackBaseAlpha.Size = new System.Drawing.Size(41, 20);
            this.nudFallbackBaseAlpha.TabIndex = 38;
            this.nudFallbackBaseAlpha.ValueChanged += new System.EventHandler(this.nudFallbackBaseAlpha_ValueChanged);
            // 
            // tbGamma
            // 
            this.tbGamma.Location = new System.Drawing.Point(90, 148);
            this.tbGamma.Maximum = 300;
            this.tbGamma.Minimum = 50;
            this.tbGamma.Name = "tbGamma";
            this.tbGamma.Size = new System.Drawing.Size(183, 45);
            this.tbGamma.TabIndex = 22;
            this.tbGamma.TickStyle = System.Windows.Forms.TickStyle.None;
            this.tbGamma.Value = 100;
            this.tbGamma.Scroll += new System.EventHandler(this.tbGamma_Scroll);
            // 
            // lblGamma
            // 
            this.lblGamma.AutoSize = true;
            this.lblGamma.Location = new System.Drawing.Point(3, 151);
            this.lblGamma.Name = "lblGamma";
            this.lblGamma.Size = new System.Drawing.Size(70, 13);
            this.lblGamma.TabIndex = 23;
            this.lblGamma.Text = "Gamma: 1.00";
            this.lblGamma.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // tbEmissive
            // 
            this.tbEmissive.Location = new System.Drawing.Point(90, 181);
            this.tbEmissive.Maximum = 300;
            this.tbEmissive.Name = "tbEmissive";
            this.tbEmissive.Size = new System.Drawing.Size(183, 45);
            this.tbEmissive.TabIndex = 24;
            this.tbEmissive.TickStyle = System.Windows.Forms.TickStyle.None;
            this.tbEmissive.Value = 100;
            this.tbEmissive.Scroll += new System.EventHandler(this.tbEmissive_Scroll);
            // 
            // lblEmissive
            // 
            this.lblEmissive.AutoSize = true;
            this.lblEmissive.Location = new System.Drawing.Point(3, 184);
            this.lblEmissive.Name = "lblEmissive";
            this.lblEmissive.Size = new System.Drawing.Size(75, 13);
            this.lblEmissive.TabIndex = 25;
            this.lblEmissive.Text = "Emissive: 1.00";
            this.lblEmissive.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // cbMaterials
            // 
            this.cbMaterials.AutoSize = true;
            this.cbMaterials.Location = new System.Drawing.Point(150, 34);
            this.cbMaterials.Name = "cbMaterials";
            this.cbMaterials.Size = new System.Drawing.Size(69, 17);
            this.cbMaterials.TabIndex = 39;
            this.cbMaterials.Text = "Materials";
            this.cbMaterials.UseVisualStyleBackColor = true;
            // 
            // cbGlow
            // 
            this.cbGlow.AutoSize = true;
            this.cbGlow.Location = new System.Drawing.Point(70, 34);
            this.cbGlow.Name = "cbGlow";
            this.cbGlow.Size = new System.Drawing.Size(50, 17);
            this.cbGlow.TabIndex = 40;
            this.cbGlow.Text = "Glow";
            this.cbGlow.UseVisualStyleBackColor = true;
            // 
            // GraphicsPreferences
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.cbGlow);
            this.Controls.Add(this.cbMaterials);
            this.Controls.Add(this.nudFallbackBaseAlpha);
            this.Controls.Add(this.lblFallbackBaseAlpha);
            this.Controls.Add(this.nudFallbackAmp);
            this.Controls.Add(this.lblFallbackAmp);
            this.Controls.Add(this.nudFallbackSpeed);
            this.Controls.Add(this.lblFallbackSpeed);
            this.Controls.Add(this.cbFallbackAnim);
            this.Controls.Add(this.lblSpecular);
            this.Controls.Add(this.tbSpecular);
            this.Controls.Add(this.lblDiffuse);
            this.Controls.Add(this.tbDiffuse);
            this.Controls.Add(this.lblAmbient);
            this.Controls.Add(this.tbAmbient);
            this.Controls.Add(this.lblEmissive);
            this.Controls.Add(this.tbEmissive);
            this.Controls.Add(this.lblGamma);
            this.Controls.Add(this.tbGamma);
            this.Controls.Add(this.lblDrawDistance);
            this.Controls.Add(this.tbDrawDistance);
            this.Controls.Add(this.cbAA);
            this.Controls.Add(this.cbWaterReflections);
            this.Controls.Add(this.cbShiny);
            this.Controls.Add(this.cbOcclusionCulling);
            this.Controls.Add(this.chkWireFrame);
            this.Name = "GraphicsPreferences";
            this.Size = new System.Drawing.Size(283, 373);
            ((System.ComponentModel.ISupportInitialize)(this.tbDrawDistance)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.tbAmbient)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.tbDiffuse)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.tbSpecular)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudFallbackSpeed)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudFallbackAmp)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudFallbackBaseAlpha)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.tbGamma)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.tbEmissive)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public System.Windows.Forms.CheckBox cbAA;
        public System.Windows.Forms.CheckBox chkWireFrame;
        public System.Windows.Forms.Label lblDrawDistance;
        public System.Windows.Forms.TrackBar tbDrawDistance;
        public System.Windows.Forms.CheckBox cbWaterReflections;
        private System.Windows.Forms.CheckBox cbOcclusionCulling;
        private System.Windows.Forms.CheckBox cbShiny;
        private System.Windows.Forms.Label lblAmbient;
        public System.Windows.Forms.TrackBar tbAmbient;
        public System.Windows.Forms.Label lblDiffuse;
        public System.Windows.Forms.TrackBar tbDiffuse;
        public System.Windows.Forms.Label lblSpecular;
        public System.Windows.Forms.TrackBar tbSpecular;
        public System.Windows.Forms.Label lblGamma;
        public System.Windows.Forms.TrackBar tbGamma;
        public System.Windows.Forms.Label lblEmissive;
        public System.Windows.Forms.TrackBar tbEmissive;
        private System.Windows.Forms.CheckBox cbFallbackAnim;
        private System.Windows.Forms.Label lblFallbackSpeed;
        private System.Windows.Forms.NumericUpDown nudFallbackSpeed;
        private System.Windows.Forms.Label lblFallbackAmp;
        private System.Windows.Forms.NumericUpDown nudFallbackAmp;
        private System.Windows.Forms.Label lblFallbackBaseAlpha;
        private System.Windows.Forms.NumericUpDown nudFallbackBaseAlpha;
        private System.Windows.Forms.CheckBox cbGlow;
        private System.Windows.Forms.CheckBox cbMaterials;
    }
}
