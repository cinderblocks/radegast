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
using NUnit.Framework;

namespace Radegast.Tests
{
    [TestFixture]
    public class GroupNoticesTests
    {
        [Test]
        public void ShouldPresent_FalseWhenMembershipRejectsNotices()
        {
            UUID groupId = UUID.Random();
            var groups = Membership(groupId, acceptNotices: false);

            Assert.That(GroupNotices.ShouldPresent(groups, NoticeFrom(groupId)), Is.False);
        }

        [Test]
        public void ShouldPresent_TrueWhenMembershipAcceptsNotices()
        {
            UUID groupId = UUID.Random();
            var groups = Membership(groupId, acceptNotices: true);

            Assert.That(GroupNotices.ShouldPresent(groups, NoticeFrom(groupId)), Is.True);
        }

        [Test]
        public void ShouldPresent_TrueWhenTheGroupIsNotKnownYet()
        {
            var groups = new Dictionary<UUID, Group>();
            Assert.That(GroupNotices.ShouldPresent(groups, NoticeFrom(UUID.Random())), Is.True);
        }

        [Test]
        public void TryStore_WritesTheStructBackIntoTheCache()
        {
            UUID groupId = UUID.Random();
            var groups = Membership(groupId, acceptNotices: true);

            Assert.That(GroupNotices.TryStore(groups, groupId, acceptNotices: false, listInProfile: false), Is.True);
            Assert.That(groups[groupId].AcceptNotices, Is.False);
        }

        [Test]
        public void MergeMembership_KeepsAPreferenceTheRefreshHasNotEchoed()
        {
            UUID groupId = UUID.Random();
            var pending = new Dictionary<UUID, (bool AcceptNotices, bool ListInProfile)>
            {
                [groupId] = (false, true)
            };
            Group stale = new Group { ID = groupId, AcceptNotices = true, ListInProfile = true };

            Group merged = GroupNotices.MergeMembership(groupId, stale, pending);

            Assert.Multiple(() =>
            {
                Assert.That(merged.AcceptNotices, Is.False);
                Assert.That(merged.ListInProfile, Is.True);
                Assert.That(pending.ContainsKey(groupId), Is.True);
            });
        }

        [Test]
        public void MergeMembership_DropsTheOverrideOnceTheServerAgrees()
        {
            UUID groupId = UUID.Random();
            var pending = new Dictionary<UUID, (bool AcceptNotices, bool ListInProfile)>
            {
                [groupId] = (false, true)
            };
            Group echoed = new Group { ID = groupId, AcceptNotices = false, ListInProfile = true };

            Group merged = GroupNotices.MergeMembership(groupId, echoed, pending);

            Assert.Multiple(() =>
            {
                Assert.That(merged.AcceptNotices, Is.False);
                Assert.That(pending.ContainsKey(groupId), Is.False);
            });
        }

        [Test]
        public void ShouldPresent_UsesGroupIdFromTheBinaryBucket()
        {
            UUID groupId = UUID.Random();
            UUID senderId = UUID.Random();
            var groups = Membership(groupId, acceptNotices: false);

            var msg = new InstantMessage
            {
                FromAgentID = senderId,
                Dialog = InstantMessageDialog.GroupNotice,
                BinaryBucket = BucketWithGroup(groupId)
            };

            Assert.That(GroupNotices.ShouldPresent(groups, msg), Is.False);
        }

        private static Dictionary<UUID, Group> Membership(UUID groupId, bool acceptNotices)
        {
            return new Dictionary<UUID, Group>
            {
                [groupId] = new Group { ID = groupId, AcceptNotices = acceptNotices, Name = "Test" }
            };
        }

        private static InstantMessage NoticeFrom(UUID groupId)
        {
            return new InstantMessage
            {
                FromAgentID = groupId,
                Dialog = InstantMessageDialog.GroupNotice,
                BinaryBucket = System.Array.Empty<byte>()
            };
        }

        private static byte[] BucketWithGroup(UUID groupId)
        {
            byte[] bucket = new byte[18];
            bucket[0] = 0;
            bucket[1] = 0;
            groupId.ToBytes(bucket, 2);
            return bucket;
        }
    }
}
