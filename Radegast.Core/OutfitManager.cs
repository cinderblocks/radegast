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
using System.Threading;
using System.Threading.Tasks;

namespace Radegast
{
    /// <summary>
    /// Radegast-specific adapter of LibreMetaverse's CurrentOutfitFolder that adds support for
    /// IRadegastInstance and handles client changes. Core outfit management lives in the base class.
    /// </summary>
    public class OutfitManager : LibreMetaverse.Appearance.CurrentOutfitFolder
    {
        private readonly IRadegastInstance instance;

        public OutfitManager(IRadegastInstance instance)
            : base(instance.Client)
        {
            this.instance = instance;
            this.instance.ClientChanged += instance_ClientChanged;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                instance.ClientChanged -= instance_ClientChanged;
            }
            base.Dispose(disposing);
        }

        private void instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            // Atomically update the underlying GridClient in the base class
            UpdateClient(e.Client);
        }
    }
}
