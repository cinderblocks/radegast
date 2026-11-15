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

using System;
using System.IO;
using NUnit.Framework;
using Radegast.Veles.Core;
using Radegast.Veles.Plugins;

namespace Radegast.Veles.Tests;

[TestFixture]
public class PluginManagerTests
{
    [Test]
    public void AssemblyBuiltAgainstThePluginApiIsAPluginCandidate()
    {
        Assert.That(PluginManager.ReferencesPluginApi(typeof(PluginManager).Assembly.Location), Is.True);
    }

    [Test]
    public void AssemblyWithoutThePluginApiIsSkipped()
    {
        Assert.That(PluginManager.ReferencesPluginApi(typeof(object).Assembly.Location), Is.False);
        Assert.That(PluginManager.ReferencesPluginApi(typeof(NUnit.Framework.Assert).Assembly.Location), Is.False);
    }

    [Test]
    public void FileThatIsNotAnAssemblyIsSkipped()
    {
        string path = Path.Combine(Path.GetTempPath(), "veles-tests-" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllText(path, "this is not a portable executable");
        try
        {
            Assert.That(PluginManager.ReferencesPluginApi(path), Is.False);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void MissingFileIsSkipped()
    {
        string path = Path.Combine(Path.GetTempPath(), "veles-tests-" + Guid.NewGuid().ToString("N") + ".dll");
        Assert.That(PluginManager.ReferencesPluginApi(path), Is.False);
    }

    [Test]
    public void ClipboardsOfTwoAccountsAreSeparate()
    {
        var first = new Radegast.Veles.Core.InventoryClipboard();
        var second = new Radegast.Veles.Core.InventoryClipboard();

        first.Copy(LibreMetaverse.UUID.Random(), "Thing", isFolder: false);

        Assert.That(first.HasContent, Is.True);
        Assert.That(second.HasContent, Is.False);
    }
}
