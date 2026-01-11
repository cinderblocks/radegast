/*
 * Created by SharpDevelop.
 * User: eumario
 * Date: 7/18/2013
 * Time: 4:13 PM
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
namespace Radegast
{
	partial class ImportConsole
	{
		/// <summary>
		/// Designer variable used to keep track of non-visual components.
		/// </summary>
		private System.ComponentModel.IContainer components = null;
		
		/// <summary>
		/// Disposes resources used by the control.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing) {
				if (components != null) {
					components.Dispose();
				}
			}
			base.Dispose(disposing);
		}
		
		/// <summary>
		/// This method is required for Windows Forms designer support.
		/// Do not change the method contents inside the source code editor. The Forms designer might
		/// not be able to load this method if it was changed manually.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ImportConsole));
			this.btnUpload = new System.Windows.Forms.Button();
			this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
			this.chckRezAtLoc = new System.Windows.Forms.CheckBox();
			this.cmbImageOptions = new System.Windows.Forms.ComboBox();
			this.groupBox1 = new System.Windows.Forms.GroupBox();
			this.btnBrowse = new System.Windows.Forms.Button();
			this.txtFileName = new System.Windows.Forms.TextBox();
			this.groupBox4 = new System.Windows.Forms.GroupBox();
			this.textureCount = new System.Windows.Forms.Label();
			this.label5 = new System.Windows.Forms.Label();
			this.primCount = new System.Windows.Forms.Label();
			this.label6 = new System.Windows.Forms.Label();
			this.objectName = new System.Windows.Forms.Label();
			this.label8 = new System.Windows.Forms.Label();
			this.groupBox2 = new System.Windows.Forms.GroupBox();
			this.txtZ = new System.Windows.Forms.TextBox();
			this.label4 = new System.Windows.Forms.Label();
			this.txtY = new System.Windows.Forms.TextBox();
			this.label3 = new System.Windows.Forms.Label();
			this.txtX = new System.Windows.Forms.TextBox();
			this.label2 = new System.Windows.Forms.Label();
			this.label1 = new System.Windows.Forms.Label();
			this.groupBox3 = new System.Windows.Forms.GroupBox();
			this.txtLog = new System.Windows.Forms.TextBox();
			this.groupBox1.SuspendLayout();
			this.groupBox4.SuspendLayout();
			this.groupBox2.SuspendLayout();
			this.groupBox3.SuspendLayout();
			this.SuspendLayout();
			// 
			// btnUpload
			// 
			this.btnUpload.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.btnUpload.Location = new System.Drawing.Point(483, 335);
			this.btnUpload.Name = "btnUpload";
			this.btnUpload.Size = new System.Drawing.Size(75, 23);
			this.btnUpload.TabIndex = 3;
			this.btnUpload.Text = "Upload";
			this.btnUpload.UseVisualStyleBackColor = true;
			this.btnUpload.Click += new System.EventHandler(this.BtnUploadClick);
			// 
			// chckRezAtLoc
			// 
			this.chckRezAtLoc.Location = new System.Drawing.Point(6, 36);
			this.chckRezAtLoc.Name = "chckRezAtLoc";
			this.chckRezAtLoc.Size = new System.Drawing.Size(102, 24);
			this.chckRezAtLoc.TabIndex = 2;
			this.chckRezAtLoc.Text = "Rez At Location";
			this.toolTip1.SetToolTip(this.chckRezAtLoc, "Rez at a specific location.  If not checked, objects will be rezzed at .500 meter" +
						"s above the Avatar\'s location.");
			this.chckRezAtLoc.UseVisualStyleBackColor = true;
			this.chckRezAtLoc.CheckedChanged += new System.EventHandler(this.CheckRezAtLocCheckedChanged);
			// 
			// cmbImageOptions
			// 
			this.cmbImageOptions.FormattingEnabled = true;
			this.cmbImageOptions.Items.AddRange(new object[] {
									"Use Original UUIDs for Textures",
									"Upload Textures with new UUIDs",
									"Upload Sculpt Textures Only",
									"Do not Set/Upload Textures"});
			this.cmbImageOptions.Location = new System.Drawing.Point(93, 13);
			this.cmbImageOptions.Name = "cmbImageOptions";
			this.cmbImageOptions.Size = new System.Drawing.Size(192, 21);
			this.cmbImageOptions.TabIndex = 1;
			this.toolTip1.SetToolTip(this.cmbImageOptions, resources.GetString("cmbImageOptions.ToolTip"));
			// 
			// groupBox1
			// 
			this.groupBox1.AutoSize = true;
			this.groupBox1.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.groupBox1.Controls.Add(this.btnBrowse);
			this.groupBox1.Controls.Add(this.txtFileName);
			this.groupBox1.Dock = System.Windows.Forms.DockStyle.Top;
			this.groupBox1.Location = new System.Drawing.Point(0, 0);
			this.groupBox1.Name = "groupBox1";
			this.groupBox1.Size = new System.Drawing.Size(564, 59);
			this.groupBox1.TabIndex = 10;
			this.groupBox1.TabStop = false;
			this.groupBox1.Text = "File to Import";
			// 
			// btnBrowse
			// 
			this.btnBrowse.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.btnBrowse.Location = new System.Drawing.Point(483, 17);
			this.btnBrowse.Name = "btnBrowse";
			this.btnBrowse.Size = new System.Drawing.Size(75, 23);
			this.btnBrowse.TabIndex = 1;
			this.btnBrowse.Text = "Browse";
			this.btnBrowse.UseVisualStyleBackColor = true;
			this.btnBrowse.Click += new System.EventHandler(this.BtnBrowseClick);
			// 
			// txtFileName
			// 
			this.txtFileName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
									| System.Windows.Forms.AnchorStyles.Right)));
			this.txtFileName.Location = new System.Drawing.Point(6, 19);
			this.txtFileName.Name = "txtFileName";
			this.txtFileName.Size = new System.Drawing.Size(471, 20);
			this.txtFileName.TabIndex = 0;
			this.txtFileName.Leave += new System.EventHandler(this.TxtFileNameLeave);
			// 
			// groupBox4
			// 
			this.groupBox4.AutoSize = true;
			this.groupBox4.Controls.Add(this.textureCount);
			this.groupBox4.Controls.Add(this.label5);
			this.groupBox4.Controls.Add(this.primCount);
			this.groupBox4.Controls.Add(this.label6);
			this.groupBox4.Controls.Add(this.objectName);
			this.groupBox4.Controls.Add(this.label8);
			this.groupBox4.Dock = System.Windows.Forms.DockStyle.Top;
			this.groupBox4.Location = new System.Drawing.Point(0, 59);
			this.groupBox4.Name = "groupBox4";
			this.groupBox4.Size = new System.Drawing.Size(564, 82);
			this.groupBox4.TabIndex = 11;
			this.groupBox4.TabStop = false;
			this.groupBox4.Text = "Object Information";
			// 
			// textureCount
			// 
			this.textureCount.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
									| System.Windows.Forms.AnchorStyles.Right)));
			this.textureCount.Location = new System.Drawing.Point(94, 51);
			this.textureCount.Name = "textureCount";
			this.textureCount.Size = new System.Drawing.Size(464, 15);
			this.textureCount.TabIndex = 7;
			this.textureCount.Text = "label5";
			// 
			// label5
			// 
			this.label5.Location = new System.Drawing.Point(6, 49);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(82, 17);
			this.label5.TabIndex = 6;
			this.label5.Text = "Texture Count:";
			this.label5.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// primCount
			// 
			this.primCount.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
									| System.Windows.Forms.AnchorStyles.Right)));
			this.primCount.Location = new System.Drawing.Point(94, 32);
			this.primCount.Name = "primCount";
			this.primCount.Size = new System.Drawing.Size(464, 17);
			this.primCount.TabIndex = 5;
			this.primCount.Text = "label4";
			this.primCount.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			// 
			// label6
			// 
			this.label6.Location = new System.Drawing.Point(6, 32);
			this.label6.Name = "label6";
			this.label6.Size = new System.Drawing.Size(82, 17);
			this.label6.TabIndex = 4;
			this.label6.Text = "Prim Count:";
			this.label6.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// objectName
			// 
			this.objectName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
									| System.Windows.Forms.AnchorStyles.Right)));
			this.objectName.Location = new System.Drawing.Point(94, 16);
			this.objectName.Name = "objectName";
			this.objectName.Size = new System.Drawing.Size(464, 16);
			this.objectName.TabIndex = 1;
			this.objectName.Text = "label2";
			this.objectName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			// 
			// label8
			// 
			this.label8.Location = new System.Drawing.Point(6, 16);
			this.label8.Name = "label8";
			this.label8.Size = new System.Drawing.Size(82, 16);
			this.label8.TabIndex = 0;
			this.label8.Text = "Object Name:";
			this.label8.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// groupBox2
			// 
			this.groupBox2.AutoSize = true;
			this.groupBox2.Controls.Add(this.txtZ);
			this.groupBox2.Controls.Add(this.label4);
			this.groupBox2.Controls.Add(this.txtY);
			this.groupBox2.Controls.Add(this.label3);
			this.groupBox2.Controls.Add(this.txtX);
			this.groupBox2.Controls.Add(this.label2);
			this.groupBox2.Controls.Add(this.chckRezAtLoc);
			this.groupBox2.Controls.Add(this.cmbImageOptions);
			this.groupBox2.Controls.Add(this.label1);
			this.groupBox2.Dock = System.Windows.Forms.DockStyle.Top;
			this.groupBox2.Location = new System.Drawing.Point(0, 141);
			this.groupBox2.Name = "groupBox2";
			this.groupBox2.Size = new System.Drawing.Size(564, 79);
			this.groupBox2.TabIndex = 12;
			this.groupBox2.TabStop = false;
			this.groupBox2.Text = "Import Options";
			// 
			// txtZ
			// 
			this.txtZ.Enabled = false;
			this.txtZ.Location = new System.Drawing.Point(313, 38);
			this.txtZ.Name = "txtZ";
			this.txtZ.Size = new System.Drawing.Size(60, 20);
			this.txtZ.TabIndex = 8;
			this.txtZ.Text = "1000.00";
			// 
			// label4
			// 
			this.label4.Location = new System.Drawing.Point(290, 41);
			this.label4.Name = "label4";
			this.label4.Size = new System.Drawing.Size(17, 19);
			this.label4.TabIndex = 7;
			this.label4.Text = "Z:";
			// 
			// txtY
			// 
			this.txtY.Enabled = false;
			this.txtY.Location = new System.Drawing.Point(225, 38);
			this.txtY.Name = "txtY";
			this.txtY.Size = new System.Drawing.Size(60, 20);
			this.txtY.TabIndex = 6;
			this.txtY.Text = "1000.00";
			// 
			// label3
			// 
			this.label3.Location = new System.Drawing.Point(202, 41);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(17, 19);
			this.label3.TabIndex = 5;
			this.label3.Text = "Y:";
			// 
			// txtX
			// 
			this.txtX.Enabled = false;
			this.txtX.Location = new System.Drawing.Point(137, 38);
			this.txtX.Name = "txtX";
			this.txtX.Size = new System.Drawing.Size(60, 20);
			this.txtX.TabIndex = 4;
			this.txtX.Text = "1000.00";
			// 
			// label2
			// 
			this.label2.Location = new System.Drawing.Point(114, 41);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(17, 19);
			this.label2.TabIndex = 3;
			this.label2.Text = "X:";
			// 
			// label1
			// 
			this.label1.Location = new System.Drawing.Point(6, 16);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(81, 17);
			this.label1.TabIndex = 0;
			this.label1.Text = "Image Options:";
			// 
			// groupBox3
			// 
			this.groupBox3.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
									| System.Windows.Forms.AnchorStyles.Left) 
									| System.Windows.Forms.AnchorStyles.Right)));
			this.groupBox3.Controls.Add(this.txtLog);
			this.groupBox3.Location = new System.Drawing.Point(0, 220);
			this.groupBox3.Name = "groupBox3";
			this.groupBox3.Size = new System.Drawing.Size(564, 109);
			this.groupBox3.TabIndex = 13;
			this.groupBox3.TabStop = false;
			this.groupBox3.Text = "Log";
			// 
			// txtLog
			// 
			this.txtLog.Dock = System.Windows.Forms.DockStyle.Fill;
			this.txtLog.Location = new System.Drawing.Point(3, 16);
			this.txtLog.Multiline = true;
			this.txtLog.Name = "txtLog";
			this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Both;
			this.txtLog.Size = new System.Drawing.Size(558, 90);
			this.txtLog.TabIndex = 0;
			// 
			// ImportConsole
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.groupBox3);
			this.Controls.Add(this.groupBox2);
			this.Controls.Add(this.groupBox4);
			this.Controls.Add(this.groupBox1);
			this.Controls.Add(this.btnUpload);
			this.Name = "ImportConsole";
			this.Size = new System.Drawing.Size(564, 361);
			this.groupBox1.ResumeLayout(false);
			this.groupBox1.PerformLayout();
			this.groupBox4.ResumeLayout(false);
			this.groupBox2.ResumeLayout(false);
			this.groupBox2.PerformLayout();
			this.groupBox3.ResumeLayout(false);
			this.groupBox3.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();
		}
		private System.Windows.Forms.Label label8;
		private System.Windows.Forms.Label objectName;
		private System.Windows.Forms.Label label6;
		private System.Windows.Forms.Label primCount;
		private System.Windows.Forms.Label label5;
		private System.Windows.Forms.Label textureCount;
		private System.Windows.Forms.GroupBox groupBox4;
		private System.Windows.Forms.TextBox txtLog;
		private System.Windows.Forms.ToolTip toolTip1;
		private System.Windows.Forms.Button btnUpload;
		private System.Windows.Forms.GroupBox groupBox3;
		private System.Windows.Forms.CheckBox chckRezAtLoc;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.TextBox txtX;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.TextBox txtY;
		private System.Windows.Forms.Label label4;
		private System.Windows.Forms.TextBox txtZ;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.ComboBox cmbImageOptions;
		private System.Windows.Forms.GroupBox groupBox2;
		private System.Windows.Forms.TextBox txtFileName;
		private System.Windows.Forms.Button btnBrowse;
		private System.Windows.Forms.GroupBox groupBox1;
	}
}
