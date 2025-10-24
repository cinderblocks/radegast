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
using System.Linq;
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    public partial class AttachmentTab : UserControl
    {
        private readonly RadegastInstanceForms instance;
        private GridClient client => instance.Client;
        private readonly Avatar av;

        public AttachmentTab(RadegastInstanceForms instance, Avatar iav)
        {
            this.instance = instance;
            av = iav;
            InitializeComponent();
            AutoScrollPosition = new Point(0, 0);

            InitializeComponent(); // TODO: Was this second initialization intentional...?

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void AttachmentTab_Load(object sender, EventArgs e)
        {
            RefreshList();
        }

        public void RefreshList()
        {
            var attachments = (from p in client.Network.CurrentSim.ObjectsPrimitives
                where p.Value != null
                where p.Value.ParentID == av.LocalID
                select p.Value);

            var toRemove = Controls.OfType<AttachmentDetail>().Cast<Control>().ToList();

            foreach (var control in toRemove)
            {
                Controls.Remove(control);
                control.Dispose();
            }

            var added = new List<UUID>();

            var n = 0;
            foreach (var attachment in attachments)
            {
                if (added.Contains(attachment.ID)) { continue; }

                var ad = new AttachmentDetail(instance, av, attachment);
                ad.Location = new Point(0, pnlControls.Height + n * ad.Height);
                ad.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                ad.Width = ClientSize.Width;
                Controls.Add(ad);
                added.Add(attachment.ID);
                n++;
            }

            AutoScrollPosition = new Point(0, 0);
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            RefreshList();
        }
    }
}
