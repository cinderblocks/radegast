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
using System.Threading;
using OpenMetaverse;
using OpenMetaverse.Assets;

namespace Radegast
{
    public partial class Gesture : DetachableControl
    {
        private readonly IRadegastInstance instance;
        private GridClient client => instance.Client;
        private readonly InventoryGesture gesture;
        private AssetGesture gestureAsset;

        public Gesture(IRadegastInstance instance, InventoryGesture gesture)
        {
            InitializeComponent();
            Disposed += Gesture_Disposed;

            tbtnReupload.Visible = false;

            this.instance = instance;
            this.gesture = gesture;

            // Start download
            tlblStatus.Text = "Downloading...";
            client.Assets.RequestAsset(gesture.AssetUUID, AssetType.Gesture, true, Assets_OnAssetReceived);

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void Gesture_Disposed(object sender, EventArgs e)
        {
        }

        private void Assets_OnAssetReceived(AssetDownload transfer, Asset asset)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(delegate() { Assets_OnAssetReceived(transfer, asset); }));
                return;
            }

            if (!transfer.Success)
            {
                tlblStatus.Text = "Download failed";
                return;
            }

            tlblStatus.Text = "OK";
            tbtnPlay.Enabled = true;

            gestureAsset = (AssetGesture)asset;
            if (gestureAsset.Decode())
            {
                foreach (GestureStep step in gestureAsset.Sequence)
                {
                    rtbInfo.AppendText(step.ToString().Trim() + Environment.NewLine);
                }
            }
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
        }

        protected override void Retach()
        {
            base.Retach();
            tbtnAttach.Text = "Detach";
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

        private void tbtnPlay_Click(object sender, EventArgs e)
        {
            client.Self.PlayGesture(gesture.AssetUUID);
        }

        private void UpdateStatus(string msg)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(delegate() { UpdateStatus(msg); }));
                return;
            }

            tlblStatus.Text = msg;
        }

        private async void tbtnReupload_Click(object sender, EventArgs e)
        {
            if (gestureAsset?.AssetData == null)
            {
                UpdateStatus("No gesture data to upload");
                return;
            }

            UpdateStatus("Creating and uploading item...");

            var perms = new Permissions
            {
                EveryoneMask = PermissionMask.None,
                GroupMask = PermissionMask.None,
                NextOwnerMask = PermissionMask.All
            };

            try
            {
                var result = await client.Inventory.CreateItemFromAssetAsync(
                    gestureAsset.AssetData,
                    $"Copy of {gesture.Name}",
                    gesture.Description,
                    AssetType.Gesture,
                    InventoryType.Gesture,
                    gesture.ParentUUID,
                    perms,
                    CancellationToken.None).ConfigureAwait(false);

                if (result != null && result.Success)
                {
                    // Update local gesture asset id if provided
                    if (result.AssetID != UUID.Zero)
                    {
                        gesture.AssetUUID = result.AssetID;
                    }

                    UpdateStatus("OK");
                }
                else
                {
                    var status = result?.Status ?? "Unknown error";
                    UpdateStatus($"Failed: {status}");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Exception: {ex.Message}");
            }
        }
    }
}
