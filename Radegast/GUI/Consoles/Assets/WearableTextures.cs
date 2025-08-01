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

using OpenMetaverse;
using OpenMetaverse.Assets;
using System.Windows.Forms;

namespace Radegast
{
    public partial class WearableTextures : UserControl
    {
        private readonly RadegastInstance instance;
        private readonly InventoryWearable item;
        private AssetWearable wearable;
        private GridClient Client => instance.Client;

        public WearableTextures(RadegastInstance instance, InventoryWearable item)
        {
            InitializeComponent();

            this.instance = instance;
            this.item = item;

            if (item != null)
            {
                Client.Assets.RequestInventoryAsset(item, true, UUID.Random(), Assets_OnAssetReceived);
            }
        }

        private void Assets_OnAssetReceived(AssetDownload transfer, Asset asset)
        {
            if (!transfer.Success || !(asset.AssetType == AssetType.Clothing || asset.AssetType == AssetType.Bodypart))
            {
                return;
            }

            asset.Decode();
            wearable = (AssetWearable)asset;

            GetTextures();
        }

        public void GetTextures()
        {
            if (wearable == null)
            {
                return;
            }

            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                {
                    BeginInvoke(new MethodInvoker(GetTextures));
                }

                return;
            }

            lblName.Text = item.Name;

            foreach (var texture in wearable.Textures)
            {
                AvatarTextureIndex index = texture.Key;
                UUID uuid = texture.Value;

                if (uuid != UUID.Zero && uuid != AppearanceManager.DEFAULT_AVATAR_TEXTURE)
                {
                    SLImageHandler img = new SLImageHandler(instance, uuid, index.ToString());

                    GroupBox gbx = new GroupBox
                    {
                        Dock = DockStyle.Top,
                        Text = img.Text,
                        Height = 225
                    };

                    img.Dock = DockStyle.Fill;
                    gbx.Controls.Add(img);
                    pnlImages.Controls.Add(gbx);
                }
            }
        }
    }
}
