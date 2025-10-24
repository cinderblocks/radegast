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

using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    public partial class OutfitTextures : UserControl
    {
        private readonly RadegastInstanceForms instance;
        private readonly Avatar avatar;

        public OutfitTextures(RadegastInstanceForms instance, Avatar avatar)
        {
            InitializeComponent();

            this.instance = instance;
            this.avatar = avatar;

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        public void GetTextures()
        {
            if (avatar.Textures == null) return;

            lblName.Text = avatar.Name;

            for (int j = 0; j < avatar.Textures.FaceTextures.Length; j++)
            {
                Primitive.TextureEntryFace face = avatar.Textures.FaceTextures[j];

                if (face != null)
                {
                    if (face.TextureID != AppearanceManager.DEFAULT_AVATAR_TEXTURE)
                    {
                        SLImageHandler img = new SLImageHandler(instance, face.TextureID, ((AvatarTextureIndex)j).ToString());

                        GroupBox gbx = new GroupBox {Dock = DockStyle.Top, Text = img.Text, Height = 550};

                        img.Dock = DockStyle.Fill;
                        gbx.Controls.Add(img);
                        pnlImages.Controls.Add(gbx);
                    }
                }
            }
        }
    }
}
