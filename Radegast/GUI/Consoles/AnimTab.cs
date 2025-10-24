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
using System.Drawing;
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    public partial class AnimTab : UserControl
    {
        private readonly GridClient client;
        private readonly IRadegastInstance instance;
        private readonly Avatar av;
        private readonly List<UUID> seenAnim = new List<UUID>();
        private int n = 0;

        public AnimTab(IRadegastInstance instance, Avatar av)
        {
            InitializeComponent();
            Disposed += AnimTab_Disposed;
            this.instance = instance;
            this.av = av;
            client = instance.Client;

            // Callbacks
            client.Avatars.AvatarAnimation += Avatars_AvatarAnimation;

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void AnimTab_Disposed(object sender, EventArgs e)
        {
            client.Avatars.AvatarAnimation -= Avatars_AvatarAnimation;
        }

        private void Avatars_AvatarAnimation(object sender, AvatarAnimationEventArgs e)
        {
            if (InvokeRequired) {
                Invoke(new MethodInvoker(() => Avatars_AvatarAnimation(sender, e)));
                return;
            }
            if (e.AvatarID== av.ID)
            {
                foreach (Animation a in e.Animations)
                {
                    if (!seenAnim.Contains(a.AnimationID))
                    {
                        Logger.Log("New anim for " + av.Name + ": " + a.AnimationID, Helpers.LogLevel.Debug);
                        seenAnim.Add(a.AnimationID);
                        AnimDetail ad = new AnimDetail(instance, av, a.AnimationID, n);
                        ad.Location = new Point(0, n++ * ad.Height);
                        ad.Dock = DockStyle.Top;
                        Controls.Add(ad);
                    }
                }
            }
        }

        private void AnimTab_Load(object sender, EventArgs e)
        {
        }
    }
}
