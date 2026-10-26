/*
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn LLC
 * All rights reserved.
 *
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System.Collections.Generic;
using LibreMetaverse;

namespace Radegast
{
    /// <summary>
    /// Decides whether an incoming group-notice IM should open a notification.
    /// The simulator is supposed to stop delivering notices when AcceptNotices is
    /// false, but queued notices (and some live ones) still arrive. The membership
    /// flag is the user's preference, so the client has to honor it too.
    /// </summary>
    public static class GroupNotices
    {
        /// <summary>
        /// Group-notice binary buckets carry the group UUID at byte 2, after the
        /// attachment flag and asset type. FromAgentID is the same id on current
        /// grids and the only id on older ones, so either match is accepted.
        /// </summary>
        public static bool ShouldPresent(IReadOnlyDictionary<UUID, Group> groups, InstantMessage msg)
        {
            if (!TryFindMembership(groups, msg, out Group group))
                return true;

            return group.AcceptNotices;
        }

        /// <summary>
        /// Write the notice flags back into <paramref name="groups"/>. <see cref="Group"/> is a
        /// struct, so changing a copy from the dictionary does not change the cache.
        /// </summary>
        public static bool TryStore(IDictionary<UUID, Group> groups, UUID groupId, bool acceptNotices, bool listInProfile)
        {
            if (!groups.TryGetValue(groupId, out Group group))
                return false;

            group.AcceptNotices = acceptNotices;
            group.ListInProfile = listInProfile;
            groups[groupId] = group;
            return true;
        }

        /// <summary>
        /// Keep a preference the user just set when a membership refresh still carries the old flags.
        /// Drops the override once the refresh echoes it, so a later change from another session can apply.
        /// </summary>
        public static Group MergeMembership(UUID groupId, Group incoming, IDictionary<UUID, (bool AcceptNotices, bool ListInProfile)> pending)
        {
            if (!pending.TryGetValue(groupId, out var pref))
                return incoming;

            if (incoming.AcceptNotices == pref.AcceptNotices && incoming.ListInProfile == pref.ListInProfile)
                pending.Remove(groupId);
            else
            {
                incoming.AcceptNotices = pref.AcceptNotices;
                incoming.ListInProfile = pref.ListInProfile;
            }

            return incoming;
        }

        private static bool TryFindMembership(IReadOnlyDictionary<UUID, Group> groups, InstantMessage msg, out Group group)
        {
            group = default;
            if (msg.BinaryBucket != null && msg.BinaryBucket.Length >= 18)
            {
                UUID bucketId = new UUID(msg.BinaryBucket, 2);
                if (bucketId != UUID.Zero && groups.TryGetValue(bucketId, out group))
                    return true;
            }

            return msg.FromAgentID != UUID.Zero && groups.TryGetValue(msg.FromAgentID, out group);
        }
    }
}
