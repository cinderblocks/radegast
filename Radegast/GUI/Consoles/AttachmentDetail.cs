/*
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
using System.Linq;
using System.Windows.Forms;
using System.Threading;

using OpenMetaverse;

namespace Radegast
{
    public partial class AttachmentDetail : UserControl
    {
        private readonly RadegastInstance instance;
        private GridClient client => instance.Client;
        private Avatar av;
        private readonly Primitive attachment;

        public AttachmentDetail(RadegastInstance instance, Avatar av, Primitive attachment)
        {
            InitializeComponent();
            Disposed += AttachmentDetail_Disposed;

            this.instance = instance;
            this.av = av;
            this.attachment = attachment;

            if (!instance.advancedDebugging)
            {
                btnSave.Visible = false;
                boxID.Visible = false;
                lblAttachment.Visible = false;
            }

            // Callbacks
            client.Objects.ObjectProperties += Objects_ObjectProperties;

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        void AttachmentDetail_Disposed(object sender, EventArgs e)
        {
            client.Objects.ObjectProperties -= Objects_ObjectProperties;
        }

        private void AttachmentDetail_Load(object sender, EventArgs e)
        {
            boxID.Text = attachment.ID.ToString();

            if (attachment.Properties == null)
            {
                client.Objects.SelectObject(client.Network.CurrentSim, attachment.LocalID);
            }
            else
            {
                UpdateControls();
            }
        }

        private void UpdateControls()
        {
            if (InvokeRequired)
            {
                Invoke(new MethodInvoker(UpdateControls));
                return;
            }

            lblAttachmentPoint.Text = attachment.PrimData.AttachmentPoint.ToString();
            boxName.Text = attachment.Properties.Name;

            if ((attachment.Flags & PrimFlags.Touch) == 0)
            {
                btnTouch.Visible = false;
            }

            // FIXME: Seems overkill
            var parts = (from p in client.Network.CurrentSim.ObjectsPrimitives
                where p.Value != null
                where p.Value.LocalID == attachment.LocalID || p.Value.ParentID == attachment.LocalID
                select p.Value).ToList();

            lblPrimCount.Text = $"Prims: {parts.Count}";
        }

        void Objects_ObjectProperties(object sender, ObjectPropertiesEventArgs e)
        {
            if (e.Properties.ObjectID != attachment.ID) { return; }

            attachment.Properties = e.Properties;
            UpdateControls();
        }

        private void btnTouch_Click(object sender, EventArgs e)
        {
            client.Self.Touch(attachment.LocalID);
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (Form.ActiveForm == null) return;
            WindowWrapper mainWindow = new WindowWrapper(Form.ActiveForm.Handle);
            SaveFileDialog dlg = new SaveFileDialog
            {
                AddExtension = true,
                RestoreDirectory = true,
                Title = "Save object as...",
                Filter = "XML file (*.xml)|*.xml"
            };
            DialogResult res = dlg.ShowDialog();

            if (res == DialogResult.OK)
            {
                Thread t = new Thread(delegate()
                {
                    try
                    {
                        PrimSerializer s = new PrimSerializer(client);
                        string primsXmls =
                            s.GetSerializedAttachmentPrims(client.Network.CurrentSim, attachment.LocalID);
                        System.IO.File.WriteAllText(dlg.FileName, primsXmls);
                        s.CleanUp();
                        s = null;
                        MessageBox.Show(mainWindow, "Successfully saved " + dlg.FileName, "Success",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception excp)
                    {
                        MessageBox.Show(mainWindow, excp.Message, "Saving failed", MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }) {IsBackground = true};
                t.Start();
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Rendering.frmPrimWorkshop pw = new Rendering.frmPrimWorkshop(instance, attachment.LocalID);
            pw.Show();
            pw.SetView(new Vector3(0f, 0.5f, 0f), 0, 0, 90, -10);
        }
    }
}
