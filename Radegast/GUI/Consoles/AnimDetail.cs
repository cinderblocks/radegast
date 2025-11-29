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
using System.Collections.Generic;
using System.Windows.Forms;
using System.IO;
using System.Linq;
using OpenMetaverse;
using OpenMetaverse.Assets;

namespace Radegast
{
    public partial class AnimDetail : UserControl
    {
        private readonly IRadegastInstance instance;
        private readonly Avatar av;
        private readonly UUID anim;
        private readonly int n;
        private List<FriendInfo> friends;
        private FriendInfo friend;
        private byte[] animData;

        public AnimDetail(IRadegastInstance instance, Avatar av, UUID anim, int n)
        {
            InitializeComponent();
            Disposed += AnimDetail_Disposed;
            this.instance = instance;
            this.av = av;
            this.anim = anim;
            this.n = n;

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void AnimDetail_Disposed(object sender, EventArgs e)
        {
        }

        private void AnimDetail_Load(object sender, EventArgs e)
        {
            if (Animations.ToDictionary().ContainsKey(anim))
            {
                Visible = false;
                Dispose();
                return;
            }

            groupBox1.Text = $"Animation {n} ({anim}) for {av.Name}";

            friends = instance.Client.Friends.FriendList.Values.ToList();

            pnlSave.Visible = false;
            boxAnimName.Text = $"Animation {n}";
            cbFriends.DropDownStyle = ComboBoxStyle.DropDownList;

            foreach (FriendInfo f in friends)
            {
                cbFriends.Items.Add(f);
            }

            instance.Client.Assets.RequestAsset(anim, AssetType.Animation, true, Assets_OnAssetReceived);
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (Form.ActiveForm != null)
            {
                WindowWrapper mainWindow = new WindowWrapper(Form.ActiveForm.Handle);
            }
            SaveFileDialog dlg = new SaveFileDialog
            {
                AddExtension = true,
                RestoreDirectory = true,
                Title = "Save animation as...",
                Filter = "Second Life Animation (*.sla)|*.sla"
            };
            DialogResult res = dlg.ShowDialog();

            if (res == DialogResult.OK)
            {
                try
                {
                    File.WriteAllBytes(dlg.FileName, animData);
                }
                catch (Exception ex)
                {
                    Logger.Warn("Saving animation failed", ex);
                }
            }
        }

        private void Assets_OnAssetReceived(AssetDownload transfer, Asset asset)
        {
            if (InvokeRequired)
            {
                Invoke(new MethodInvoker(() => Assets_OnAssetReceived(transfer, asset)));
                return;
            }

            if (transfer.Success)
            {
                Logger.Debug($"Animation {anim} download success; {asset.AssetData.Length} bytes.");
                pnlSave.Visible = true;
                animData = asset.AssetData;
            }
            else
            {
                Logger.Debug($"Animation {anim} download failed.");
                Visible = false;
                Dispose();
            }
        }

        private void playBox_CheckStateChanged(object sender, EventArgs e)
        {
            if (playBox.CheckState == CheckState.Checked)
            {
                instance.Client.Self.AnimationStart(anim, true);
            }
            else
            {
                instance.Client.Self.AnimationStop(anim, true);
            }
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            if (animData == null) return;

            instance.Client.Inventory.RequestCreateItemFromAsset(animData, boxAnimName.Text, "(No description)", AssetType.Animation, InventoryType.Animation, instance.Client.Inventory.FindFolderForType(AssetType.Animation), On_ItemCreated);
            lblStatus.Text = "Uploading...";
            cbFriends.Enabled = false;
        }

        private void On_ItemCreated(bool success, string status, UUID itemID, UUID assetID)
        {
            if (InvokeRequired)
            {
                Invoke(new MethodInvoker(delegate()
                {
                    On_ItemCreated(success, status, itemID, assetID);
                }
                ));
                return;
            }

            if (!success)
            {
                lblStatus.Text = "Failed.";
                Logger.Debug("Failed creating asset");
                return;
            }
            else
            {
                Logger.Debug($"Created inventory item {itemID}");

                lblStatus.Text = $"Sending to {friend.Name}";
                Logger.Info($"Sending item to {friend.Name}");

                instance.Client.Inventory.GiveItem(itemID, boxAnimName.Text, AssetType.Animation, friend.UUID, false);
                lblStatus.Text = "Sent";
            }

            cbFriends.Enabled = true;
        }


        private void cbFriends_SelectedValueChanged(object sender, EventArgs e)
        {
            if (cbFriends.SelectedIndex >= 0)
            {
                btnSend.Enabled = true;
                friend = friends[cbFriends.SelectedIndex];
            }
            else
            {
                btnSend.Enabled = false;
            }
        }
    }
}
