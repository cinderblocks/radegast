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
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using OpenMetaverse;
using OpenMetaverse.Assets;
using System.IO;
using CoreJ2K;
using OpenMetaverse.Imaging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Radegast
{
    public partial class SLImageHandler : DetachableControl
    {
        private RadegastInstanceForms instance;
        private GridClient client => instance.Client;
        private UUID imageID;

        private byte[] j2kdata;
        private Image image;
        private readonly bool allowSave = false;

        public event EventHandler<ImageUpdatedEventArgs> ImageUpdated;

        public PictureBoxSizeMode SizeMode
        {
            get => pictureBox1.SizeMode;
            set => pictureBox1.SizeMode = value;
        }

        public override string Text
        {
            get => base.Text;
            set
            {
                base.Text = value;

                if (image != null)
                {
                    base.Text += $" ({image.Width}x{image.Height})";
                }

                SetTitle();
            }
        }

        public bool AllowUpdateImage = false;

        public SLImageHandler()
        {
            InitializeComponent();

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        public SLImageHandler(RadegastInstanceForms instance, UUID image, string label)
            : this(instance, image, label, false)
        {
        }

        public SLImageHandler(RadegastInstanceForms instance, UUID image, string label, bool allowSave)
        {
            this.allowSave = allowSave;
            InitializeComponent();
            Init(instance, image, label);

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        public void Init(RadegastInstanceForms instance, UUID image, string label)
        {
            Disposed += SLImageHandler_Disposed;
            pictureBox1.AllowDrop = true;

            this.instance = instance;
            imageID = image;

            Text = string.IsNullOrEmpty(label) ? "Image" : label;

            if (image == UUID.Zero)
            {
                progressBar1.Hide();
                lblProgress.Hide();
                pictureBox1.Enabled = true;
                return;
            }

            // Callbacks
            client.Assets.ImageReceiveProgress += Assets_ImageReceiveProgress;
            UpdateImage(imageID);
        }

        public void UpdateImage(UUID imageID)
        {
            this.imageID = imageID;
            progressBar1.Visible = true;
            pictureBox1.Image = null;
            
            if (imageID == UUID.Zero)
            {
                progressBar1.Visible = false;
                return;
            }

            client.Assets.RequestImage(imageID, ImageType.Normal, 101300.0f, 0, 0, 
                delegate(TextureRequestState state, AssetTexture assetTexture)
            {
                if (state == TextureRequestState.Finished || state == TextureRequestState.Timeout)
                {
                    Assets_OnImageReceived(assetTexture);
                }
                else if (state == TextureRequestState.Progress)
                {
                    DisplayPartialImage(assetTexture);
                }
            },
            true);
        }

        private void SLImageHandler_Disposed(object sender, EventArgs e)
        {
            client.Assets.ImageReceiveProgress -= Assets_ImageReceiveProgress;
        }

        private void Assets_ImageReceiveProgress(object sender, ImageReceiveProgressEventArgs e)
        {
            if (imageID != e.ImageID)
            {
                return;
            }

            if (InvokeRequired)
            {
                if (IsHandleCreated || !instance.MonoRuntime)
                    BeginInvoke(new MethodInvoker(() => Assets_ImageReceiveProgress(sender, e)));
                return;
            }

            int pct = 0;
            if (e.Total> 0)
            {
                pct = (e.Received * 100) / e.Total;
            }
            if (pct < 0 || pct > 100)
            {
                return;
            }
            lblProgress.Text = $"{(int)e.Received / 1024} of {(int)e.Total / 1024}KB ({pct}%)";
            progressBar1.Value = pct;
        }

        private void DisplayPartialImage(AssetTexture assetTexture)
        {
            if (InvokeRequired)
            {
                if (IsHandleCreated || !instance.MonoRuntime)
                    BeginInvoke(new MethodInvoker(() => DisplayPartialImage(assetTexture)));
                return;
            }

            try
            {
                pictureBox1.Image = J2kImage.FromBytes(assetTexture.AssetData).As<SKBitmap>().ToBitmap();
                pictureBox1.Enabled = true;
            }
            catch (Exception e) {
                Hide();
                Console.WriteLine("Error decoding image: " + e.Message);
            }
        }

        private void Assets_OnImageReceived(AssetTexture assetTexture)
        {
            if (assetTexture.AssetID != imageID)
            {
                return;
            }

            if (InvokeRequired)
            {
                if (IsHandleCreated || !instance.MonoRuntime)
                    BeginInvoke(new MethodInvoker(() => Assets_OnImageReceived(assetTexture)));
                return;
            }

            try
            {
                progressBar1.Hide();
                lblProgress.Hide();

                image = J2kImage.FromBytes(assetTexture.AssetData).As<SKBitmap>().ToBitmap();
                Text = Text; // yeah, really ;)

                pictureBox1.Image = image;
                pictureBox1.Enabled = true;
                j2kdata = assetTexture.AssetData;
                if (Detached)
                {
                    ClientSize = pictureBox1.Size = new Size(image.Width, image.Height);
                }
            }
            catch (Exception e)
            {
                Hide();
                Console.WriteLine("Error decoding image: " + e.Message);
            }
        }

        protected override void Detach()
        {
            base.Detach();
            if (image != null)
            {
                ClientSize = pictureBox1.Size = new Size(image.Width, image.Height);
            }
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog dlg = new SaveFileDialog
            {
                AddExtension = true,
                RestoreDirectory = true,
                Title = "Save image as...",
                Filter =
                    "Targa (*.tga)|*.tga|Jpeg2000 (*.j2c)|*.j2c|PNG (*.png)|*.png|Jpeg (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp"
            };



            if (dlg.ShowDialog() == DialogResult.OK)
            {
                int type = dlg.FilterIndex;
                if (type == 2)
                { // jpeg2000
                    File.WriteAllBytes(dlg.FileName, j2kdata);
                }
                else if (type == 1)
                { // targa
                    var bmp = (Bitmap)image;
                    var tga = Targa.Encode(bmp.ToSKBitmap());
                    File.WriteAllBytes(dlg.FileName, tga);
                }
                else if (type == 3)
                { // png
                    image.Save(dlg.FileName, ImageFormat.Png);
                }
                else if (type == 4)
                { // jpg
                    image.Save(dlg.FileName, ImageFormat.Jpeg);
                }
                else
                { // BMP
                    image.Save(dlg.FileName, ImageFormat.Bmp);
                }
            }

            dlg.Dispose();
            dlg = null;
        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {
            if (!Detached)
            {
                Detached = true;
            }
        }

        private void copyUUIDToClipboardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Clipboard.SetText(imageID.ToString(), TextDataFormat.Text);
        }

        private void tbtnCopy_Click(object sender, EventArgs e)
        {
            Clipboard.SetImage(pictureBox1.Image);
        }

        private void pictureBox1_DragEnter(object sender, DragEventArgs e)
        {
            TreeNode node = e.Data.GetData(typeof(TreeNode)) as TreeNode;
            if (!AllowUpdateImage || node == null)
            {
                e.Effect = DragDropEffects.None;
            }
            else if (node.Tag is InventorySnapshot || node.Tag is InventoryTexture)
            {
                e.Effect = DragDropEffects.Copy | DragDropEffects.Move;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void pictureBox1_DragDrop(object sender, DragEventArgs e)
        {
            TreeNode node = e.Data.GetData(typeof(TreeNode)) as TreeNode;
            if (!AllowUpdateImage || node == null) return;

            if (node.Tag is InventorySnapshot || node.Tag is InventoryTexture)
            {
                UUID imgID = UUID.Zero;
                if (node.Tag is InventorySnapshot snapshot)
                {
                    imgID = snapshot.AssetUUID;
                }
                else
                {
                    imgID = ((InventoryTexture)node.Tag).AssetUUID;
                }

                var handler = ImageUpdated;
                handler?.Invoke(this, new ImageUpdatedEventArgs(imgID));
            }
        }

        private void cmsImage_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = false;
            if (AllowUpdateImage)
            {
                tbtnClear.Visible = tbtnPaste.Visible = true;
                tbtnPaste.Enabled = false;
                if (instance.InventoryClipboard != null)
                {
                    if (instance.InventoryClipboard.Item is InventoryTexture ||
                        instance.InventoryClipboard.Item is InventorySnapshot)
                    {
                        tbtnPaste.Enabled = true;
                    }
                }
            }
            else
            {
                tbtnClear.Visible = tbtnPaste.Visible = false;
            }

            tbtbInvShow.Enabled = false;

            InventoryItem invItem = null;
            if (client.Inventory.Store.Contains(imageID))
            {
                invItem = client.Inventory.Store[imageID] as InventoryItem;
            }

            bool save = allowSave;

            if (invItem == null)
            {
                tbtbInvShow.Enabled = false;
                tbtbInvShow.Tag = null;
            }
            else
            {
                tbtbInvShow.Enabled = true;
                tbtbInvShow.Tag = invItem;
                save |= InventoryConsole.IsFullPerm(invItem);
            }

            if (save)
            {
                tbtnCopy.Visible = true;
                tbtnCopyUUID.Visible = true;
                tbtnSave.Visible = true;
            }
            else
            {
                tbtnCopy.Visible = false;
                tbtnCopyUUID.Visible = false;
                tbtnSave.Visible = false;
            }
        }


        private void tbtnClear_Click(object sender, EventArgs e)
        {
            if (AllowUpdateImage)
            {
                UpdateImage(UUID.Zero);
                var handler = ImageUpdated;
                handler?.Invoke(this, new ImageUpdatedEventArgs(UUID.Zero));
            }
        }

        private void tbtnPaste_Click(object sender, EventArgs e)
        {
            if (!AllowUpdateImage) return;
            if (instance.InventoryClipboard != null)
            {
                UUID newID = UUID.Zero;

                if (instance.InventoryClipboard.Item is InventoryTexture texture)
                {
                    newID = texture.AssetUUID;
                }
                else if (instance.InventoryClipboard.Item is InventorySnapshot snapshot)
                {
                    newID = snapshot.AssetUUID;
                }
                else
                {
                    return;
                }

                UpdateImage(newID);

                var handler = ImageUpdated;
                handler?.Invoke(this, new ImageUpdatedEventArgs(newID));
            }
        }

        private void tbtbInvShow_Click(object sender, EventArgs e)
        {
            if (!(tbtbInvShow.Tag is InventoryItem item)) { return; }

            if (instance.TabConsole.TabExists("inventory"))
            {
                instance.TabConsole.SelectTab("inventory");
                InventoryConsole inv = (InventoryConsole)instance.TabConsole.Tabs["inventory"].Control;
                inv.SelectInventoryNode(item.UUID);
            }
        }
    }

    public class ImageUpdatedEventArgs : EventArgs
    {
        public UUID NewImageID;

        public ImageUpdatedEventArgs(UUID imageID)
        {
            NewImageID = imageID;
        }
    }
}
