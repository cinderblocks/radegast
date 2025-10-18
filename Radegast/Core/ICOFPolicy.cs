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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Radegast
{
    public interface ICOFPolicy
    {
        bool CanAttach(InventoryItem item);
        bool CanDetach(InventoryItem item);

        Task ReportItemChange(List<InventoryItem> addedItems, List<InventoryItem> removedItems, CancellationToken cancellationToken = default);
    }
}
