/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
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

using System;
using System.Windows.Forms;
using System.IO;
using OpenMetaverse;

namespace Radegast
{
	public partial class ExportCollada : RadegastTabControl
	{
		#region Private Variables

        private readonly DAEExport Exporter;
		#endregion
		
		#region Constructor
        public ExportCollada(RadegastInstanceForms instance, Primitive prim)
            : base(instance)
		{
			InitializeComponent();
            Exporter = new DAEExport(instance, prim);
            Exporter.Progress += Exporter_Progress;
            UpdateInfo();
            cbImageType.Text = "TGA";

            GUI.GuiHelpers.ApplyGuiFixes(this);
		}
		#endregion
		
		#region Private Methods

        private void LogMessage(string format, params object[] args)
		{
			if (InvokeRequired)
			{
				if (IsHandleCreated || !instance.MonoRuntime)
					BeginInvoke(new MethodInvoker(() => LogMessage(format, args)));
				return;
			}
			txtLog.AppendText(string.Format(format + "\r\n",args));
			txtLog.SelectionStart = txtLog.TextLength;
			txtLog.ScrollToCaret();
		}

        private void UpdateInfo()
        {
            Primitive root = Exporter.Prims[0];
            objectName.Text = root.Properties != null ? root.Properties.Name : "Object";
            objectUUID.Text = root.ID.ToString();
            primCount.Text = Exporter.Prims.Count.ToString();
            exportablePrims.Text = Exporter.ExportablePrims.ToString();
            textureCount.Text = Exporter.Textures.Count.ToString();
            exportableTextures.Text = Exporter.ExportableTextures.ToString();
            texturesPanel.Controls.Clear();
            foreach (UUID textureID in Exporter.Textures)
            {
                var img = new SLImageHandler(instance, textureID, string.Empty)
                {
                    Height = 96, Width = 96, BorderStyle = BorderStyle.FixedSingle
                };
                texturesPanel.Controls.Add(img);
            }
        }

        private void ValidatePath(string fname)
        {
            string path = Path.GetDirectoryName(fname);
            btnExport.Enabled = Directory.Exists(path);
        }
		#endregion
		
		#region Event Handlers

        private void TxtFileNameTextChanged(object sender, EventArgs e)
		{
			ValidatePath(txtFileName.Text);
		}

        private void BtnBrowseClick(object sender, EventArgs e)
		{
            SaveFileDialog dlg = new SaveFileDialog
            {
                Title = "Export Collada File",
                Filter = "Collada (*.dae)|*.dae|All Files (*.*)|*.*",
                FileName = txtFileName.Text.Trim() == string.Empty
                    ? RadegastInstance.SafeFileName(objectName.Text)
                    : Path.GetFileName(txtFileName.Text)
            };

            DialogResult res = dlg.ShowDialog();
			
			if (res == DialogResult.OK)
			{
				txtFileName.Text = dlg.FileName;
				ValidatePath(dlg.FileName);
			}
		}

        private void BtnExportClick(object sender, EventArgs e)
		{
            Exporter.ImageFormat = cbImageType.Text;
            try
            {
                Exporter.Export(txtFileName.Text);
            }
            catch (Exception ex)
            {
                LogMessage("Export failed: {0}", ex.ToString());
            }
		}

        private void cbExportTextures_CheckedChanged(object sender, EventArgs e)
        {
            Exporter.ExportTextures = cbImageType.Enabled = cbExportTextures.Checked;
        }

        private void Exporter_Progress(object sender, DAEStatutsEventArgs e)
        {
            LogMessage(e.Message);
        }
        #endregion
    }
}
