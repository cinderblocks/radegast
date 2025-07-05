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
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Radegast
{
    internal class CompositeCOFPolicy : ICOFPolicy
    {
        private readonly object policiesLock = new object();
        private ImmutableHashSet<ICOFPolicy> policies = ImmutableHashSet<ICOFPolicy>.Empty;

        private ImmutableHashSet<ICOFPolicy> GetCurrentPolicies()
        {
            lock (policiesLock)
            {
                return policies;
            }
        }

        public CompositeCOFPolicy AddPolicy(ICOFPolicy policyToAdd)
        {
            if (policyToAdd == null)
            {
                throw new ArgumentNullException(nameof(policyToAdd));
            }

            lock (policiesLock)
            {
                policies = policies.Add(policyToAdd);
            }

            return this;
        }

        public void RemovePolicy(ICOFPolicy policyToRemove)
        {
            lock (policiesLock)
            {
                policies = policies.Remove(policyToRemove);
            }
        }

        public bool CanAttach(InventoryItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            return GetCurrentPolicies()
                .All(n => n.CanAttach(item));
        }

        public bool CanDetach(InventoryItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            return GetCurrentPolicies()
                .All(n => n.CanDetach(item));
        }
    }
}
