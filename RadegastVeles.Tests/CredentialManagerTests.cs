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
using System.Linq;
using System.Runtime.Versioning;
using NUnit.Framework;
using Radegast.Veles.Core;

namespace Radegast.Veles.Tests;

[TestFixture]
public class CredentialManagerTests
{
    private string _dir = string.Empty;

    private string StorePath => Path.Combine(_dir, "credentials.dat");
    private string KeyPath => Path.Combine(_dir, "credentials.key");

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "veles-cred-tests-" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Test]
    public void SavedAccount_IsReadBackByANewInstance()
    {
        using (var first = new CredentialManager(_dir))
            first.SaveAccount("Test Resident", "s3cret", "agni", "Second Life");

        using var second = new CredentialManager(_dir);
        Assert.That(second.GetPassword("test resident", "agni"), Is.EqualTo("s3cret"));
        var accounts = second.GetSavedAccounts();
        Assert.That(accounts, Has.Count.EqualTo(1));
        Assert.That(accounts[0].Username, Is.EqualTo("Test Resident"));
        Assert.That(accounts[0].GridName, Is.EqualTo("Second Life"));
    }

    [Test]
    public void MostRecentlySavedAccount_IsListedFirst()
    {
        using var manager = new CredentialManager(_dir);
        manager.SaveAccount("First Resident", "a", "agni", "Second Life");
        manager.SaveAccount("Second Resident", "b", "agni", "Second Life");
        manager.SaveAccount("First Resident", "c", "agni", "Second Life");

        var names = manager.GetSavedAccounts().Select(a => a.Username).ToArray();
        Assert.That(names, Is.EqualTo(new[] { "First Resident", "Second Resident" }));
        Assert.That(manager.GetPassword("First Resident", "agni"), Is.EqualTo("c"));
    }

    [Test]
    public void RemoveAccount_ForgetsThePassword()
    {
        using var manager = new CredentialManager(_dir);
        manager.SaveAccount("Test Resident", "s3cret", "agni", "Second Life");
        manager.RemoveAccount("Test Resident", "agni");

        Assert.That(manager.GetSavedAccounts(), Is.Empty);
        Assert.That(manager.GetPassword("Test Resident", "agni"), Is.Null);
    }

    [Test]
    public void Store_DoesNotContainThePasswordInPlainText()
    {
        using var manager = new CredentialManager(_dir);
        manager.SaveAccount("Test Resident", "plain-text-marker", "agni", "Second Life");

        Assert.That(File.ReadAllText(StorePath), Does.Not.Contain("plain-text-marker"));
        Assert.That(File.ReadAllText(StorePath), Does.Not.Contain("Test Resident"));
    }

    [Test]
    public void Save_LeavesNoTemporaryFiles()
    {
        using var manager = new CredentialManager(_dir);
        manager.SaveAccount("Test Resident", "s3cret", "agni", "Second Life");
        manager.SaveLoginPreferences(2, "Region/1/2/3");

        var names = Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.That(names, Is.EqualTo(new[] { "credentials.dat", "credentials.key" }));
    }

    [Test]
    [Platform(Exclude = "Win")]
    [UnsupportedOSPlatform("windows")]
    public void Files_AreAccessibleToTheirOwnerOnly()
    {
        using var manager = new CredentialManager(_dir);
        manager.SaveAccount("Test Resident", "s3cret", "agni", "Second Life");

        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        Assert.That(File.GetUnixFileMode(StorePath), Is.EqualTo(ownerOnly));
        Assert.That(File.GetUnixFileMode(KeyPath), Is.EqualTo(ownerOnly));
    }

    [Test]
    [Platform(Exclude = "Win")]
    [UnsupportedOSPlatform("windows")]
    public void ExistingFilesWithWiderAccess_AreRestrictedOnLoad()
    {
        using (var first = new CredentialManager(_dir))
            first.SaveAccount("Test Resident", "s3cret", "agni", "Second Life");

        const UnixFileMode worldReadable =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        File.SetUnixFileMode(StorePath, worldReadable);
        File.SetUnixFileMode(KeyPath, worldReadable);

        using var second = new CredentialManager(_dir);

        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        Assert.That(File.GetUnixFileMode(StorePath), Is.EqualTo(ownerOnly));
        Assert.That(File.GetUnixFileMode(KeyPath), Is.EqualTo(ownerOnly));
        Assert.That(second.GetPassword("Test Resident", "agni"), Is.EqualTo("s3cret"));
    }

    [Test]
    public void UnreadableStore_IsSetAsideAndReplaced()
    {
        using (var first = new CredentialManager(_dir))
            first.SaveAccount("Test Resident", "s3cret", "agni", "Second Life");

        File.WriteAllText(StorePath, "this is not a credential store");

        using var second = new CredentialManager(_dir);

        Assert.That(second.GetSavedAccounts(), Is.Empty);
        Assert.That(File.ReadAllText(StorePath + ".unreadable"), Is.EqualTo("this is not a credential store"));
    }

    [Test]
    public void LostKey_KeepsTheOldStoreInsteadOfDeletingIt()
    {
        using (var first = new CredentialManager(_dir))
            first.SaveAccount("Test Resident", "s3cret", "agni", "Second Life");

        var original = File.ReadAllText(StorePath);
        File.Delete(KeyPath);

        using var second = new CredentialManager(_dir);

        Assert.That(second.GetPassword("Test Resident", "agni"), Is.Null);
        Assert.That(File.ReadAllText(StorePath + ".unreadable"), Is.EqualTo(original));
    }

    [Test]
    public void LoginPreferencesAndFavorites_RoundTrip()
    {
        using (var first = new CredentialManager(_dir))
        {
            first.SaveLoginPreferences(2, "Region/1/2/3");
            first.SaveFavoriteLocations("test resident:agni", [("Home", "Region/10/20/30"), ("Shop", "Mall/1/1/1")]);
        }

        using var second = new CredentialManager(_dir);
        Assert.That(second.LoadLoginPreferences(), Is.EqualTo((2, "Region/1/2/3")));
        Assert.That(second.GetFavoriteLocations("test resident:agni"),
            Is.EqualTo(new[] { ("Home", "Region/10/20/30"), ("Shop", "Mall/1/1/1") }));
        Assert.That(second.GetFavoriteLocations("someone else:agni"), Is.Empty);
    }
}
