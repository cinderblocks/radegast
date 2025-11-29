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
using System.Threading;
using System.Windows.Forms;
using OpenMetaverse;
using OpenMetaverse.Assets;

namespace Radegast
{
    public partial class Landmark : DetachableControl
    {
        private RadegastInstanceForms instance;
        private GridClient client => instance.Client;
        private InventoryLandmark landmark;
        private AssetLandmark decodedLandmark;
        private UUID parcelID;
        private ParcelInfo parcel;
        private Vector3 localPosition;
        private readonly bool parcelLocation = false;

        public Landmark(RadegastInstanceForms instance, InventoryLandmark landmark)
        {
            this.landmark = landmark;
            Init(instance);
            client.Assets.RequestAsset(landmark.AssetUUID, landmark.AssetType, true, Assets_OnAssetReceived);
        }

        public Landmark(RadegastInstanceForms instance, UUID parcelID)
        {
            this.parcelID = parcelID;
            Init(instance);
            parcelLocation = true;
            client.Parcels.RequestParcelInfo(parcelID);
        }

        private void Init(RadegastInstanceForms instance)
        {
            InitializeComponent();
            Disposed += Landmark_Disposed;

            this.instance = instance;

            // Callbacks
            client.Grid.RegionHandleReply += Grid_RegionHandleReply;
            client.Parcels.ParcelInfoReply += Parcels_ParcelInfoReply;

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void Landmark_Disposed(object sender, EventArgs e)
        {
            client.Grid.RegionHandleReply -= Grid_RegionHandleReply;
            client.Parcels.ParcelInfoReply -= Parcels_ParcelInfoReply;
        }

        private void Parcels_ParcelInfoReply(object sender, ParcelInfoReplyEventArgs e)
        {
            if (e.Parcel.ID != parcelID) return;

            if (InvokeRequired)
            {
                ThreadingHelper.SafeInvoke(this, new Action(() => Parcels_ParcelInfoReply(sender, e)), instance.MonoRuntime);
                return;
            }

            parcel = e.Parcel;

            pnlDetail.Visible = true;
            if (parcel.SnapshotID != UUID.Zero)
            {
                SLImageHandler img = new SLImageHandler(instance, parcel.SnapshotID, "") {Dock = DockStyle.Fill};
                pnlDetail.Controls.Add(img);
                pnlDetail.Disposed += (senderx, ex) =>
                {
                    img.Dispose();
                };
                img.BringToFront();
            }

            btnTeleport.Enabled = true;
            btnShowOnMap.Enabled = true;

            if (parcelLocation)
            {
                localPosition = new Vector3
                {
                    X = parcel.GlobalX % 256,
                    Y = parcel.GlobalY % 256,
                    Z = parcel.GlobalZ
                };
            }

            txtParcelName.Text = decodedLandmark == null 
                ? $"{parcel.Name} - {parcel.SimName} " 
                : $"{parcel.Name} - {parcel.SimName} ({(int) decodedLandmark.Position.X}, {(int) decodedLandmark.Position.Y}, {(int) decodedLandmark.Position.Z}) ";

            txtParcelDescription.Text = parcel.Description;
        }

        private async void Grid_RegionHandleReply(object sender, RegionHandleReplyEventArgs e)
        {
            if (decodedLandmark == null || decodedLandmark.RegionID != e.RegionID) return;

            parcelID = await client.Parcels.RequestRemoteParcelIDAsync(decodedLandmark.Position, e.RegionHandle, e.RegionID, CancellationToken.None);
            if (parcelID != UUID.Zero)
            {
                client.Parcels.RequestParcelInfo(parcelID);
            }
        }

        private void Assets_OnAssetReceived(AssetDownload transfer, Asset asset)
        {
            if (transfer.Success && asset.AssetType == AssetType.Landmark)
            {
                decodedLandmark = (AssetLandmark)asset;
                decodedLandmark.Decode();
                localPosition = decodedLandmark.Position;
                client.Grid.RequestRegionHandle(decodedLandmark.RegionID);
            }
        }

        private void btnTeleport_Click(object sender, EventArgs e)
        {
            instance.MainForm.WorldMap.DisplayLocation(parcel.SimName,
                (int)localPosition.X,
                (int)localPosition.Y,
                (int)localPosition.Z);
            instance.MainForm.WorldMap.DoTeleport();
        }

        private void btnShowOnMap_Click(object sender, EventArgs e)
        {
            instance.ShowLocation(parcel.SimName, (int)localPosition.X, (int)localPosition.Y, (int)localPosition.Z);
        }
    }
}
