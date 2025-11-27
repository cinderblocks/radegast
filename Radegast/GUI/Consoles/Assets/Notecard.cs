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
using System.Collections.Generic;
using System.Windows.Forms;
using OpenMetaverse;
using OpenMetaverse.Assets;

namespace Radegast
{
    public partial class Notecard : DetachableControl
    {
        private readonly RadegastInstanceForms instance;
        private GridClient client => instance.Client;
        private readonly InventoryNotecard notecard;
        private AssetNotecard receivedNotecard;
        private readonly Primitive prim;

        public Notecard(RadegastInstanceForms instance, InventoryNotecard notecard)
            : this(instance, notecard, null)
        {
        }

        public Notecard(RadegastInstanceForms instance, InventoryNotecard notecard, Primitive prim)
        {
            InitializeComponent();
            Disposed += Notecard_Disposed;

            this.instance = instance;
            this.notecard = notecard;
            this.prim = prim;

            Text = notecard.Name;

            rtbContent.DetectUrls = false;


            if (notecard.AssetUUID == UUID.Zero)
            {
                UpdateStatus("Blank");
            }
            else
            {
                rtbContent.Text = " ";
                UpdateStatus("Loading...");

                var transferID = UUID.Random();
                if (prim == null)
                {
                    client.Assets.RequestInventoryAsset(notecard, true, transferID, Assets_OnAssetReceived);
                }
                else
                {
                    client.Assets.RequestInventoryAsset(notecard.AssetUUID, notecard.UUID, prim.ID, prim.OwnerID, 
                        notecard.AssetType, true, transferID, Assets_OnAssetReceived);
                }
            }

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void Notecard_Disposed(object sender, EventArgs e)
        {
        }

        private void Assets_OnAssetReceived(AssetDownload transfer, Asset asset)
        {
            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                    BeginInvoke(new MethodInvoker(() => Assets_OnAssetReceived(transfer, asset)));
                return;
            }

            if (transfer.Success)
            {
                AssetNotecard n = (AssetNotecard)asset;
                n.Decode();
                receivedNotecard = n;

                string noteText = string.Empty;
                rtbContent.Clear();

                for (int i = 0; i < n.BodyText.Length; i++)
                {
                    char c = n.BodyText[i];

                    if ((int)c == 0xdbc0)
                    {
                        int index = (int)n.BodyText[++i] - 0xdc00;
                        InventoryItem e = n.EmbeddedItems[index];
                        rtbContent.AppendText(noteText);
                        rtbContent.InsertLink(e.Name, $"radegast://embeddedasset/{index}");
                        noteText = string.Empty;
                    }
                    else
                    {
                        noteText += c;
                    }
                }

                rtbContent.Text += noteText;

                if (n.EmbeddedItems != null && n.EmbeddedItems.Count > 0)
                {
                    tbtnAttachments.Enabled = true;
                    tbtnAttachments.Visible = true;
                    foreach (InventoryItem item in n.EmbeddedItems)
                    {
                        int ix = InventoryConsole.GetItemImageIndex(item.AssetType.ToString().ToLower());
                        ToolStripMenuItem titem = new ToolStripMenuItem(item.Name);

                        if (ix != -1)
                        {
                            titem.Image = frmMain.ResourceImages.Images[ix];
                            titem.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
                        }
                        else
                        {
                            titem.DisplayStyle = ToolStripItemDisplayStyle.Text;
                        }

                        titem.Name = item.UUID.ToString();
                        titem.Tag = item;
                        titem.Click += attachmentMenuItem_Click;

                        var saveToInv = new ToolStripMenuItem("Save to inventory");
                        saveToInv.Click += (xsender, xe) =>
                            {
                                client.Inventory.RequestCopyItemFromNotecard(UUID.Zero,
                                    notecard.UUID,
                                    client.Inventory.FindFolderForType(item.AssetType),
                                    item.UUID,
                                    Inventory_OnInventoryItemCopied);
                            };

                        titem.DropDownItems.Add(saveToInv);
                        tbtnAttachments.DropDownItems.Add(titem);
                    }
                }
                UpdateStatus("OK");
                rtbContent.Focus();
            }
            else
            {
                UpdateStatus("Failed");
                rtbContent.Text = "Failed to download notecard. " + transfer.Status;
            }
        }

        private void Inventory_OnInventoryItemCopied(InventoryBase item)
        {
            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                    BeginInvoke(new MethodInvoker(() => Inventory_OnInventoryItemCopied(item)));
                return;
            }

            if (null == item) return;

            instance.ShowNotificationInChat($"{item.Name} saved to inventory",
                ChatBufferTextStyle.Invisible);

            tlblStatus.Text = "Saved";
            
            if (item is InventoryNotecard inventoryNotecard)
            {
                Notecard nc = new Notecard(instance, inventoryNotecard) {pnlKeepDiscard = {Visible = true}};
                nc.ShowDetached();
            }
        }

        private void attachmentMenuItem_Click(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem titem)
            {
                InventoryItem item = (InventoryItem)titem.Tag;

                switch (item.AssetType)
                {
                    case AssetType.Texture:
                        SLImageHandler ih = new SLImageHandler(instance, item.AssetUUID, string.Empty);
                        ih.Text = item.Name;
                        ih.ShowDetached();
                        break;

                    case AssetType.Landmark:
                        Landmark ln = new Landmark(instance, (InventoryLandmark)item);
                        ln.ShowDetached();
                        break;

                    case AssetType.Notecard:
                        client.Inventory.RequestCopyItemFromNotecard(UUID.Zero,
                            notecard.UUID,
                            notecard.ParentUUID,
                            item.UUID,
                            Inventory_OnInventoryItemCopied);
                        break;
                }
            }
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            if (notecard.AssetUUID == UUID.Zero) return;

            rtbContent.Text = "Loading...";
            var transferID = UUID.Random();
            client.Assets.RequestInventoryAsset(notecard, true, transferID, Assets_OnAssetReceived);
        }

        private void rtbContent_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            //instance.MainForm.processLink(e.LinkText);
        }


        #region Detach/Attach
        protected override void ControlIsNotRetachable()
        {
            tbtnAttach.Visible = false;
        }

        protected override void Detach()
        {
            base.Detach();
            tbtnAttach.Text = "Attach";
            tbtnExit.Enabled = true;
        }

        protected override void Retach()
        {
            base.Retach();
            tbtnAttach.Text = "Detach";
            tbtnExit.Enabled = false;
        }

        private void tbtnAttach_Click(object sender, EventArgs e)
        {
            if (Detached)
            {
                Retach();
            }
            else
            {
                Detach();
            }
        }
        #endregion

        private void tbtnExit_Click(object sender, EventArgs e)
        {
            if (Detached)
            {
                FindForm()?.Close();
            }
        }

        private void tbtnSave_Click(object sender, EventArgs e)
        {
            bool success = false;
            string message = "";
            AssetNotecard n = new AssetNotecard
            {
                BodyText = rtbContent.Text,
                EmbeddedItems = new List<InventoryItem>()
            };

            if (receivedNotecard != null)
            {
                for (var i = 0; i < receivedNotecard.EmbeddedItems.Count; i++)
                {
                    n.EmbeddedItems.Add(receivedNotecard.EmbeddedItems[i]);
                    int indexChar = 0xdc00 + i;
                    n.BodyText += (char)0xdbc0;
                    n.BodyText += (char)indexChar;
                }
            }

            n.Encode();

            UpdateStatus("Saving...");

            InventoryManager.InventoryUploadedAssetCallback handler = delegate(bool uploadSuccess, string status, UUID itemID, UUID assetID)
                    {
                        success = uploadSuccess;
                        if (itemID == notecard.UUID)
                        {
                            if (success)
                            {
                                UpdateStatus("OK");
                                notecard.AssetUUID = assetID;
                            }
                            else
                            {
                                UpdateStatus("Failed");
                            }

                        }
                        message = status ?? "Unknown error uploading notecard asset";
                    };

            if (prim == null)
            {
                client.Inventory.RequestUploadNotecardAsset(n.AssetData, notecard.UUID, handler);
            }
            else
            {
                _ = client.Inventory.RequestUpdateNotecardTaskAsync(n.AssetData, notecard.UUID, prim.ID, handler);
            }
        }

        private void UpdateStatus(string status)
        {
            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                    BeginInvoke(new MethodInvoker(() => UpdateStatus(status)));
                return;
            }
            instance.ShowNotificationInChat($"Notecard status: {status}", ChatBufferTextStyle.Invisible);
            tlblStatus.Text = status;
        }

        private void rtbContent_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.S && e.Control)
            {
                if (e.Shift)
                {
                }
                else
                {
                    tbtnSave_Click(this, EventArgs.Empty);
                    e.Handled = e.SuppressKeyPress = true;
                }
            }

        }

        private void rtbContent_Enter(object sender, EventArgs e)
        {
            instance.ShowNotificationInChat("Editing notecard", ChatBufferTextStyle.Invisible);
        }

        private void btnKeep_Click(object sender, EventArgs e)
        {
            Retach();
        }

        private void btnDiscard_Click(object sender, EventArgs e)
        {
            client.Inventory.MoveItem(notecard.UUID, client.Inventory.FindFolderForType(FolderType.Trash), notecard.Name);
            Retach();
        }
    }
}
