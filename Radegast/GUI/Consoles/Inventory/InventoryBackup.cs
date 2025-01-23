/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2020, Sjofn, LLC
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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenMetaverse;
using OpenMetaverse.Assets;
using OpenMetaverse.Imaging;

namespace Radegast
{
    public partial class InventoryBackup : Form
    {
        private RadegastInstance instance;
        GridClient client => instance.Client;
        private Inventory inv;
        private string folderName;
        private int fetched = 0;
        private TextWriter csvFile = null;
        private int traversed = 0;
        private InventoryNode rootNode;
        private CancellationTokenSource backupTaskCancelToken;

        public InventoryBackup(RadegastInstance instance, UUID rootFolder)
        {
            InitializeComponent();
            Disposed += InventoryBackup_Disposed;

            this.instance = instance;

            inv = client.Inventory.Store;
            rootNode = inv.RootNode;
            if (inv.Contains(rootFolder) && inv.GetNodeFor(rootFolder).Data is InventoryFolder)
            {
                rootNode = inv.GetNodeFor(rootFolder);
            }

            backupTaskCancelToken = new CancellationTokenSource();

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        void InventoryBackup_Disposed(object sender, EventArgs e)
        {
            try
            {
                backupTaskCancelToken.Cancel();
                backupTaskCancelToken.Dispose();
            } catch (ObjectDisposedException) { }
        }

        private void WriteCSVLine(params object[] args)
        {
            if (csvFile != null)
            {
                try
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        string s = args[i].ToString();
                        s = s.Replace("\"", "\"\"");
                        csvFile.Write("\"{0}\"", s);
                        if (i != args.Length - 1) csvFile.Write(",");
                    }
                    csvFile.WriteLine();
                }
                catch { }
            }
        }

        private void btnFolder_Click(object sender, EventArgs e)
        {
            openFileDialog1.CheckFileExists = false;
            DialogResult res = openFileDialog1.ShowDialog();
            traversed = 0;

            if (res == DialogResult.OK)
            {
                string dir = Path.GetDirectoryName(openFileDialog1.FileName);
                txtFolderName.Text = folderName = dir;
                btnFolder.Enabled = false;

                lvwFiles.Items.Clear();

                if (cbList.Checked)
                {
                    try
                    {
                        csvFile = new StreamWriter(Path.Combine(dir, "inventory.csv"), false);
                        WriteCSVLine("Type", "Path", "Name", "Description", "Created", "Creator", "Last Owner");
                    }
                    catch
                    {
                        csvFile = null;
                    }
                }

                lblStatus.Text = "Fetching items...";
                sbrProgress.Style = ProgressBarStyle.Marquee;
                fetched = 0;

                Task backupTask = Task.Run(() =>
                {
                    TraverseDir(rootNode, Path.DirectorySeparatorChar.ToString());
                    if (csvFile != null)
                    {
                        try
                        {
                            csvFile.Close();
                            csvFile.Dispose();
                        }
                        catch
                        {
                        }
                    }
                    if (backupTaskCancelToken.IsCancellationRequested) { return; }
                    BeginInvoke(new MethodInvoker(() =>
                        {
                            lblStatus.Text = $"Done ({fetched} items saved).";
                            sbrProgress.Style = ProgressBarStyle.Blocks;
                            btnFolder.Enabled = true;
                        }
                    ));
                }, backupTaskCancelToken.Token);
            }

        }

        private void TraverseDir(InventoryNode node, string path)
        {
            var nodes = new List<InventoryNode>(node.Nodes.Values);
            foreach (InventoryNode n in nodes)
            {
                traversed++;
                try
                {
                    backupTaskCancelToken.Token.ThrowIfCancellationRequested();
                    if (IsHandleCreated && (traversed % 13 == 0))
                    {
                        BeginInvoke(new MethodInvoker(() =>
                        {
                            lblStatus.Text = string.Format("Traversed {0} nodes...", traversed);
                        }));
                    }

                    if (n.Data is InventoryFolder)
                    {
                        WriteCSVLine("Folder", path, n.Data.Name, "", "", "", "");
                        TraverseDir(n, Path.Combine(path, RadegastInstance.SafeFileName(n.Data.Name)));
                    }
                    else
                    {
                        InventoryItem item = (InventoryItem)n.Data;
                        string creator = item.CreatorID == UUID.Zero ? string.Empty : instance.Names.Get(item.CreatorID, true);
                        string lastOwner = item.LastOwnerID == UUID.Zero ? string.Empty : instance.Names.Get(item.LastOwnerID, true);
                        string type = item.AssetType.ToString();
                        if (item.InventoryType == InventoryType.Wearable) type = ((WearableType)item.Flags).ToString();
                        string created = item.CreationDate.ToString("yyyy-MM-dd HH:mm:ss");
                        WriteCSVLine(type, path, item.Name, item.Description, created, creator, lastOwner);

                        PermissionMask fullPerm = PermissionMask.Modify | PermissionMask.Copy | PermissionMask.Transfer;
                        if ((item.Permissions.OwnerMask & fullPerm) != fullPerm)
                            continue;

                        string filePartial = Path.Combine(path, RadegastInstance.SafeFileName(n.Data.Name));
                        string fullName = folderName + filePartial;
                        switch (item.AssetType)
                        {
                            case AssetType.LSLText:
                                client.Settings.USE_ASSET_CACHE = false;
                                fullName += ".lsl";
                                break;
                            case AssetType.Notecard: fullName += ".txt"; break;
                            case AssetType.Texture: fullName += ".png"; break;
                            default: fullName += ".bin"; break;
                        }
                        string dirName = Path.GetDirectoryName(fullName);
                        bool dateOK = item.CreationDate > new DateTime(1970, 1, 2);

                        if (
                            (item.AssetType == AssetType.LSLText && cbScripts.Checked) ||
                            (item.AssetType == AssetType.Notecard && cbNoteCards.Checked) ||
                            (item.AssetType == AssetType.Texture && cbImages.Checked)
                            )
                        {
                            ListViewItem lvi = new ListViewItem
                            {
                                Text = n.Data.Name, Tag = n.Data, Name = n.Data.UUID.ToString()
                            };

                            ListViewItem.ListViewSubItem fileName = new ListViewItem.ListViewSubItem(lvi, filePartial);
                            lvi.SubItems.Add(fileName);

                            ListViewItem.ListViewSubItem status = new ListViewItem.ListViewSubItem(lvi, "Fetching asset");
                            lvi.SubItems.Add(status);

                            //bool cached = dateOK && File.Exists(fullName) && File.GetCreationTimeUtc(fullName) == item.CreationDate;

                            //if (cached)
                            //{
                            //    status.Text = "Cached";
                            //}

                            backupTaskCancelToken.Token.ThrowIfCancellationRequested();

                            BeginInvoke(new MethodInvoker(() =>
                            {
                                lvwFiles.Items.Add(lvi);
                                lvwFiles.EnsureVisible(lvwFiles.Items.Count - 1);
                            }));

                            //if (cached) continue;
                            backupTaskCancelToken.Token.ThrowIfCancellationRequested();

                            Asset receivedAsset = null;
                            using (AutoResetEvent done = new AutoResetEvent(false))
                            {
                                if (item.AssetType == AssetType.Texture)
                                {
                                    client.Assets.RequestImage(item.AssetUUID, (state, asset) =>
                                    {
                                        if (state == TextureRequestState.Finished && asset != null && asset.Decode())
                                        {
                                            receivedAsset = asset;
                                            done.Set();
                                        }
                                    });
                                }
                                else
                                {
                                    var transferID = UUID.Random();
                                    client.Assets.RequestInventoryAsset(item, true, transferID, (transfer, asset) =>
                                        {
                                            if (transfer.Success && transfer.ID == transferID)
                                            {
                                                receivedAsset = asset;
                                            }
                                            done.Set();
                                        }
                                    );
                                }

                                backupTaskCancelToken.Token.ThrowIfCancellationRequested();
                                done.WaitOne(30 * 1000, false);
                            }

                            client.Settings.USE_ASSET_CACHE = true;

                            backupTaskCancelToken.Token.ThrowIfCancellationRequested();
                            if (receivedAsset == null)
                            {
                                BeginInvoke(new MethodInvoker(() => status.Text = "Failed to fetch asset"));
                            }
                            else
                            {
                                BeginInvoke(new MethodInvoker(() => status.Text = "Saving..."));

                                try
                                {
                                    backupTaskCancelToken.Token.ThrowIfCancellationRequested();
                                    if (!Directory.Exists(dirName))
                                    {
                                        Directory.CreateDirectory(dirName);
                                    }

                                    switch (item.AssetType)
                                    {
                                        case AssetType.Notecard:
                                            AssetNotecard note = (AssetNotecard)receivedAsset;
                                            if (note.Decode())
                                            {
                                                File.WriteAllText(fullName, note.BodyText, System.Text.Encoding.UTF8);
                                                if (dateOK)
                                                {
                                                    File.SetCreationTimeUtc(fullName, item.CreationDate);
                                                    File.SetLastWriteTimeUtc(fullName, item.CreationDate);
                                                }
                                            }
                                            else
                                            {
                                                Logger.Log(string.Format("Falied to decode asset for '{0}' - {1}", item.Name, receivedAsset.AssetID), Helpers.LogLevel.Warning, client);
                                            }

                                            break;

                                        case AssetType.LSLText:
                                            AssetScriptText script = (AssetScriptText)receivedAsset;
                                            if (script.Decode())
                                            {
                                                File.WriteAllText(fullName, script.Source, System.Text.Encoding.UTF8);
                                                if (dateOK)
                                                {
                                                    File.SetCreationTimeUtc(fullName, item.CreationDate);
                                                    File.SetLastWriteTimeUtc(fullName, item.CreationDate);
                                                }
                                            }
                                            else
                                            {
                                                Logger.Log(string.Format("Falied to decode asset for '{0}' - {1}", item.Name, receivedAsset.AssetID), Helpers.LogLevel.Warning, client);
                                            }

                                            break;

                                        case AssetType.Texture:
                                            AssetTexture imgAsset = (AssetTexture)receivedAsset;
                                            var img = Targa.Encode(imgAsset.Image);
                                            File.WriteAllBytes(fullName, img);
                                            if (dateOK)
                                            {
                                                File.SetCreationTimeUtc(fullName, item.CreationDate);
                                                File.SetLastWriteTimeUtc(fullName, item.CreationDate);
                                            }
                                            break;

                                    }

                                    BeginInvoke(new MethodInvoker(() =>
                                    {
                                        fileName.Text = fullName;
                                        status.Text = "Saved";
                                        lblStatus.Text = $"Saved {++fetched} items";
                                    }));

                                }
                                catch (OperationCanceledException)
                                {
                                    BeginInvoke(new MethodInvoker(() => status.Text = "Operation cancelled."));
                                    return;
                                }
                                catch (Exception ex)
                                {
                                    BeginInvoke(new MethodInvoker(() => status.Text = "Failed to save " + Path.GetFileName(fullName) + ": " + ex.Message));
                                }

                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch { }
            }
        }

        private void InventoryBackup_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                backupTaskCancelToken.Cancel();
                backupTaskCancelToken.Dispose();
            } catch (ObjectDisposedException) { }
        }

        private void lvwFiles_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                if (lvwFiles.SelectedItems.Count == 1)
                {
                    ListViewItem item = lvwFiles.SelectedItems[0];

                    if (item.SubItems.Count >= 3)
                    {
                        if ("Saved" == item.SubItems[2].Text)
                        {
                            System.Diagnostics.Process.Start(item.SubItems[1].Text);
                        }
                    }
                }
            }
            catch (Exception) { }
        }
    }
}
