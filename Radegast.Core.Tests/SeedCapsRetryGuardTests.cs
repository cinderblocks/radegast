/**
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn, LLC
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

using System.Reflection;
using System.Threading;
using LibreMetaverse;
using NUnit.Framework;

namespace Radegast.Tests
{
    [TestFixture]
    public class SeedCapsRetryGuardTests
    {
        // SeedCapsRetryGuard silently does nothing if LibreMetaverse renames or retypes this field.
        [Test]
        public void CapsStillHasPrivateHttpCtsField()
        {
            var field = typeof(Caps).GetField("_HttpCts", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            Assert.That(field!.FieldType, Is.EqualTo(typeof(CancellationTokenSource)));
        }
    }
}
