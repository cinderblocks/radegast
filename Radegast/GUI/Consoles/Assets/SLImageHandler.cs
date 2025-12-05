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
using System.Threading.Tasks;
using System.Runtime.Caching;
using System.Threading;
using OpenMetaverse.StructuredData;

namespace Radegast
{
    public partial class SLImageHandler : DetachableControl
    {
        // Shared cache for decoded images to avoid repeated expensive J2K decodes
        private static readonly MemoryCache DecodedImageCache = new MemoryCache("SLImageHandlerDecoded");

        // Per-instance settings (configurable via instance.GlobalSettings)
        private bool cacheEnabled = true;
        private TimeSpan cacheSlidingExpiration = TimeSpan.FromMinutes(30);
        private SemaphoreSlim instanceDecodeSemaphore;
        private int decodeConcurrency = Math.Max(1, Environment.ProcessorCount / 2);
        private readonly object settingsLock = new object();

        private RadegastInstanceForms instance;
        private GridClient client => instance.Client;
        private UUID imageID;

        // Synchronization context captured from UI thread to reliably marshal UI updates
        private SynchronizationContext uiContext;

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

            // Capture the UI synchronization context so we can marshal to the UI safely
            uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            // Apply settings from globals (with validation) and subscribe for live changes
            ApplySettingsFromGlobals();
            try
            {
                instance.GlobalSettings.OnSettingChanged += GlobalSettings_OnSettingChanged;
            }
            catch { }

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

        private void SLImageHandler_Disposed(object sender, EventArgs e)
        {
            client.Assets.ImageReceiveProgress -= Assets_ImageReceiveProgress;
            try
            {
                if (instance?.GlobalSettings != null)
                {
                    instance.GlobalSettings.OnSettingChanged -= GlobalSettings_OnSettingChanged;
                }
            }
            catch { }

            try
            {
                lock (settingsLock)
                {
                    instanceDecodeSemaphore?.Dispose();
                    instanceDecodeSemaphore = null;
                }
            }
            catch { }
        }

        private void GlobalSettings_OnSettingChanged(object sender, SettingsEventArgs e)
        {
            if (e == null || string.IsNullOrEmpty(e.Key)) return;

            // Only react to relevant keys
            if (e.Key == "image_cache_enabled" || e.Key == "image_cache_expire_minutes" || e.Key == "image_decode_concurrency")
            {
                // Apply settings on UI thread to keep behavior consistent
                try
                {
                    if (SynchronizationContext.Current != uiContext && uiContext != null)
                    {
                        uiContext.Post(_ => ApplySettingsFromGlobals(), null);
                    }
                    else
                    {
                        ApplySettingsFromGlobals();
                    }
                }
                catch { }
            }
        }

        private void ApplySettingsFromGlobals()
        {
            if (instance?.GlobalSettings == null) return;

            bool newCacheEnabled = cacheEnabled;
            TimeSpan newCacheExpiration = cacheSlidingExpiration;
            int newConcurrency = decodeConcurrency;

            try
            {
                if (!instance.GlobalSettings.ContainsKey("image_cache_enabled"))
                {
                    instance.GlobalSettings["image_cache_enabled"] = OSD.FromBoolean(true);
                }
                newCacheEnabled = instance.GlobalSettings["image_cache_enabled"].AsBoolean();
            }
            catch { newCacheEnabled = true; }

            try
            {
                if (!instance.GlobalSettings.ContainsKey("image_cache_expire_minutes"))
                {
                    instance.GlobalSettings["image_cache_expire_minutes"] = OSD.FromInteger((int)cacheSlidingExpiration.TotalMinutes);
                }
                int minutes = instance.GlobalSettings["image_cache_expire_minutes"].AsInteger();
                // validate range
                minutes = Math.Max(1, Math.Min(minutes, 1440));
                newCacheExpiration = TimeSpan.FromMinutes(minutes);
            }
            catch { newCacheExpiration = cacheSlidingExpiration; }

            try
            {
                if (!instance.GlobalSettings.ContainsKey("image_decode_concurrency"))
                {
                    instance.GlobalSettings["image_decode_concurrency"] = OSD.FromInteger(decodeConcurrency);
                }
                int concurrency = instance.GlobalSettings["image_decode_concurrency"].AsInteger();
                // validate range: at least 1, and not excessively large
                concurrency = Math.Max(1, Math.Min(concurrency, Math.Max(1, Environment.ProcessorCount * 4)));
                newConcurrency = concurrency;
            }
            catch { newConcurrency = decodeConcurrency; }

            lock (settingsLock)
            {
                cacheEnabled = newCacheEnabled;
                cacheSlidingExpiration = newCacheExpiration;

                if (instanceDecodeSemaphore == null || decodeConcurrency != newConcurrency)
                {
                    try
                    {
                        instanceDecodeSemaphore?.Dispose();
                    }
                    catch { }

                    decodeConcurrency = newConcurrency;
                    instanceDecodeSemaphore = new SemaphoreSlim(Math.Max(1, decodeConcurrency));
                }
            }
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
                delegate (TextureRequestState state, AssetTexture assetTexture)
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

        private void Assets_ImageReceiveProgress(object sender, ImageReceiveProgressEventArgs e)
        {
            if (imageID != e.ImageID)
            {
                return;
            }

            if (SynchronizationContext.Current != uiContext && uiContext != null)
            {
                uiContext.Post(_ => Assets_ImageReceiveProgress(sender, e), null);
                return;
            }

            int pct = 0;
            if (e.Total > 0)
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
            if (SynchronizationContext.Current != uiContext && uiContext != null)
            {
                uiContext.Post(_ => DisplayPartialImage(assetTexture), null);
                return;
            }

            string cacheKey = assetTexture.AssetID.ToString();

            // If we have a cached decoded image, use it directly
            if (cacheEnabled && DecodedImageCache.Contains(cacheKey))
            {
                var cached = DecodedImageCache.Get(cacheKey) as Image;
                if (cached != null)
                {
                    var oldImg = pictureBox1.Image as Image;
                    pictureBox1.Image = cached;
                    pictureBox1.Enabled = true;
                    if (oldImg != null && oldImg != cached)
                    {
                        try { oldImg.Dispose(); } catch { }
                    }
                    return;
                }
            }

            // Decode off the UI thread and then marshal the bitmap back to the UI
            byte[] data = assetTexture.AssetData;
            Task.Run(async () =>
            {
                // Ensure semaphore exists
                SemaphoreSlim sem;
                lock (settingsLock) { sem = instanceDecodeSemaphore ?? new SemaphoreSlim(Math.Max(1, decodeConcurrency)); }
                await sem.WaitAsync().ConfigureAwait(false);
                try
                {
                    try
                    {
                        var bmp = J2kImage.FromBytes(data).As<SKBitmap>().ToBitmap();
                        return bmp;
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }
                finally
                {
                    try { sem.Release(); } catch { }
                }
            }).ContinueWith(t =>
            {
                if (t.IsCanceled || t.Result == null)
                {
                    // decoding failed; hide the control on UI thread
                    try { Hide(); } catch { }
                    return;
                }

                try
                {
                    // replace image on UI thread
                    var newBmp = t.Result;

                    // Store in cache for future reuse if enabled. Use configured sliding expiration.
                    if (cacheEnabled)
                    {
                        try
                        {
                            DecodedImageCache.Set(cacheKey, newBmp, new CacheItemPolicy { SlidingExpiration = cacheSlidingExpiration });
                        }
                        catch { /* cache failures shouldn't break the UI update */ }
                    }

                    var old = pictureBox1.Image as Image;
                    pictureBox1.Image = newBmp;
                    pictureBox1.Enabled = true;
                    if (old != null && old != newBmp)
                    {
                        try { old.Dispose(); } catch { }
                    }
                }
                catch (Exception e)
                {
                    Hide();
                    Console.WriteLine("Error decoding image: " + e.Message);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void Assets_OnImageReceived(AssetTexture assetTexture)
        {
            if (assetTexture.AssetID != imageID)
            {
                return;
            }

            if (SynchronizationContext.Current != uiContext && uiContext != null)
            {
                uiContext.Post(_ => Assets_OnImageReceived(assetTexture), null);
                return;
            }

            try
            {
                progressBar1.Hide();
                lblProgress.Hide();

                byte[] data = assetTexture.AssetData;
                string cacheKey = assetTexture.AssetID.ToString();

                // If cached decoded image exists, use it and avoid decoding
                if (cacheEnabled && DecodedImageCache.Contains(cacheKey))
                {
                    var cachedImg = DecodedImageCache.Get(cacheKey) as Image;
                    if (cachedImg != null)
                    {
                        var oldCached = image as Image;
                        image = cachedImg;
                        Text = Text; // yeah, really ;)

                        pictureBox1.Image = image;
                        pictureBox1.Enabled = true;
                        j2kdata = data;
                        if (Detached)
                        {
                            ClientSize = pictureBox1.Size = new Size(image.Width, image.Height);
                        }

                        if (oldCached != null && oldCached != image)
                        {
                            try { oldCached.Dispose(); } catch { }
                        }

                        return;
                    }
                }

                // Decode off the UI thread and then update UI
                Task.Run(async () =>
                {
                    // Ensure semaphore exists
                    SemaphoreSlim sem;
                    lock (settingsLock) { sem = instanceDecodeSemaphore ?? new SemaphoreSlim(Math.Max(1, decodeConcurrency)); }
                    await sem.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        try
                        {
                            return J2kImage.FromBytes(data).As<SKBitmap>().ToBitmap();
                        }
                        catch (Exception)
                        {
                            return null;
                        }
                    }
                    finally
                    {
                        try { sem.Release(); } catch { }
                    }
                }).ContinueWith(t =>
                {
                    if (t.IsCanceled || t.Result == null)
                    {
                        try { Hide(); } catch { }
                        return;
                    }

                    try
                    {
                        var bmp = t.Result;

                        // Attempt to cache decoded image for reuse
                        if (cacheEnabled)
                        {
                            try
                            {
                                DecodedImageCache.Set(cacheKey, bmp, new CacheItemPolicy { SlidingExpiration = cacheSlidingExpiration });
                            }
                            catch { }
                        }

                        // dispose previous image if present and not the same as cached one
                        var old = image as Image;
                        image = bmp;
                        Text = Text; // yeah, really ;)

                        pictureBox1.Image = image;
                        pictureBox1.Enabled = true;
                        j2kdata = data;
                        if (Detached)
                        {
                            ClientSize = pictureBox1.Size = new Size(image.Width, image.Height);
                        }

                        if (old != null && old != image)
                        {
                            try { old.Dispose(); } catch { }
                        }
                    }
                    catch (Exception e)
                    {
                        Hide();
                        Console.WriteLine("Error decoding image: " + e.Message);
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
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
