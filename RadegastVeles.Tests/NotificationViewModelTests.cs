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

using NUnit.Framework;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Tests;

/// <summary>
/// AnnouncementText is what a screen reader announces automatically when a notification
/// appears (bound as the accessible Name of a live-region element in
/// ScriptDialogOverlay.axaml) - it has to include the actual message, not just the title,
/// or a JAWS/NVDA user has no way to find out what the notification says (issue #229).
/// </summary>
[TestFixture]
public class NotificationViewModelTests
{
    [Test]
    public void AnnouncementText_WithNoSubtitle_CombinesTitleAndMessage()
    {
        var vm = NotificationViewModel.ForGenericMessage("Region Notice", "The sim is restarting.");

        Assert.That(vm.AnnouncementText, Is.EqualTo("Region Notice. The sim is restarting."));
    }

    [Test]
    public void AnnouncementText_WithSubtitle_CombinesAllThree()
    {
        var vm = NotificationViewModel.ForLoadUrl("Info Kiosk", "Jane Resident", "https://example.com", "Visit our store!");

        Assert.That(vm.AnnouncementText,
            Is.EqualTo("Open URL. From: Jane Resident (Info Kiosk). Visit our store!\nhttps://example.com"));
    }

    [Test]
    public void AnnouncementText_UpdatesWhenMessageChanges()
    {
        var vm = NotificationViewModel.ForGenericMessage("Countdown", "10 seconds left");
        Assert.That(vm.AnnouncementText, Does.Contain("10 seconds left"));

        vm.Message = "5 seconds left";

        Assert.That(vm.AnnouncementText, Does.Contain("5 seconds left"));
        Assert.That(vm.AnnouncementText, Does.Not.Contain("10 seconds left"));
    }

    [Test]
    public void AnnouncementText_RaisesPropertyChanged_WhenMessageChanges()
    {
        var vm = NotificationViewModel.ForGenericMessage("Title", "Original");
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NotificationViewModel.AnnouncementText)) raised = true;
        };

        vm.Message = "Updated";

        Assert.That(raised, Is.True);
    }
}
