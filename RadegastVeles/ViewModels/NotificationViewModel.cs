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
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;

namespace Radegast.Veles.ViewModels;

/// <summary>
/// Unified notification view-model used for every in-world notification overlay card
/// (friendship offers, group notices, inventory offers, teleport offers, script permissions, etc.).
/// </summary>
public class NotificationViewModel : ObservableObject
{
    /// <summary>Primary heading — object/agent name, notice subject, etc.</summary>
    public string Title { get; }

    /// <summary>Secondary line — sender, group name, action type, etc.</summary>
    public string Subtitle { get; }

    /// <summary>Body text. Observable so countdown timers can update it.</summary>
    private string _message = string.Empty;
    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    /// <summary>Action buttons. Empty for text-box mode.</summary>
    public IReadOnlyList<NotificationButtonViewModel> Buttons { get; }

    /// <summary>True for llTextBox() dialogs — shows a text-input field instead of buttons.</summary>
    public bool IsTextBox { get; }

    /// <summary>Convenience inverse of <see cref="IsTextBox"/>.</summary>
    public bool IsButtonMode => !IsTextBox;

    /// <summary>User-entered text for text-box mode.</summary>
    private string _textInput = string.Empty;
    public string TextInput
    {
        get => _textInput;
        set => SetProperty(ref _textInput, value);
    }

    /// <summary>Command to submit text-box content (null for non-text-box notifications).</summary>
    public ICommand? SendTextCommand { get; }

    /// <summary>Dismisses without taking any action.</summary>
    public ICommand IgnoreCommand { get; }

    /// <summary>Fired when this notification should be removed from the queue.</summary>
    public event EventHandler? Dismissed;

    public void Dismiss() => Dismissed?.Invoke(this, EventArgs.Empty);

    private NotificationViewModel(
        string title,
        string subtitle,
        string message,
        List<NotificationButtonViewModel> buttons,
        bool isTextBox = false,
        Action<string>? onSendText = null)
    {
        Title = title;
        Subtitle = subtitle;
        _message = message;
        IsTextBox = isTextBox;
        Buttons = buttons.AsReadOnly();
        IgnoreCommand = new RelayCommand(Dismiss);

        if (isTextBox && onSendText != null)
            SendTextCommand = new RelayCommand(() => { onSendText(_textInput); Dismiss(); });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Factory methods
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>llDialog() / llTextBox() in-world script dialog.</summary>
    public static NotificationViewModel ForScriptDialog(
        GridClient client,
        string objectName,
        string senderName,
        string message,
        List<string> buttonLabels,
        UUID objectId,
        int channel)
    {
        NotificationViewModel vm = null!;

        bool isTextBox = buttonLabels.Count == 1 && buttonLabels[0] == "!!llTextBox!!";

        var buttons = new List<NotificationButtonViewModel>();
        if (!isTextBox)
        {
            foreach (string label in buttonLabels)
            {
                string captured = label;
                buttons.Add(new NotificationButtonViewModel(captured, () =>
                {
                    int idx = -1;
                    for (int i = 0; i < buttonLabels.Count; i++)
                    {
                        if (buttonLabels[i] == captured) { idx = i; break; }
                    }
                    client.Self.ReplyToScriptDialog(channel, idx, captured, objectId);
                    vm.Dismiss();
                }));
            }
        }

        vm = new NotificationViewModel(
            objectName,
            senderName,
            message,
            buttons,
            isTextBox: isTextBox,
            onSendText: isTextBox ? text =>
            {
                client.Self.ReplyToScriptDialog(channel, 0, text, objectId);
            } : null);

        return vm;
    }

    /// <summary>Incoming friendship offer.</summary>
    public static NotificationViewModel ForFriendshipOffer(GridClient client, InstantMessage msg)
    {
        NotificationViewModel vm = null!;
        var buttons = new List<NotificationButtonViewModel>
        {
            new("Accept", () => { client.Friends.AcceptFriendship(msg.FromAgentID, msg.IMSessionID); vm.Dismiss(); }),
            new("Decline", () => { client.Friends.DeclineFriendship(msg.FromAgentID, msg.IMSessionID); vm.Dismiss(); })
        };
        vm = new NotificationViewModel(msg.FromAgentName, "is offering you friendship", msg.Message, buttons);
        return vm;
    }

    /// <summary>Group notice (with optional inventory attachment).</summary>
    public static NotificationViewModel ForGroupNotice(GridClient client, InstantMessage msg)
    {
        NotificationViewModel vm = null!;

        // Parse binary bucket
        bool hasAttachment = msg.BinaryBucket.Length > 18 && msg.BinaryBucket[0] != 0;
        var assetType = hasAttachment ? (AssetType)msg.BinaryBucket[1] : AssetType.Unknown;
        UUID destFolder = hasAttachment ? client.Inventory.FindFolderForType(assetType) : UUID.Zero;
        string itemName = hasAttachment
            ? Utils.BytesToString(msg.BinaryBucket, 18, msg.BinaryBucket.Length - 19)
            : string.Empty;

        // Parse title|body
        int pipe = msg.Message.IndexOf('|');
        string title = pipe >= 0 ? msg.Message[..pipe] : msg.Message;
        string body = pipe >= 0 ? msg.Message[(pipe + 1)..] : string.Empty;

        string subtitle = $"From {msg.FromAgentName}";
        if (hasAttachment) subtitle += $" · Attachment: {itemName}";

        var buttons = new List<NotificationButtonViewModel>();
        if (hasAttachment)
        {
            buttons.Add(new("Save", () =>
            {
                client.Self.InstantMessage(
                    client.Self.Name, msg.FromAgentID, string.Empty, msg.IMSessionID,
                    InstantMessageDialog.GroupNoticeInventoryAccepted,
                    InstantMessageOnline.Offline,
                    client.Self.SimPosition,
                    client.Network.CurrentSim?.RegionID ?? UUID.Zero,
                    destFolder.GetBytes());
                vm.Dismiss();
            }));
        }
        buttons.Add(new("OK", () => vm.Dismiss()));

        vm = new NotificationViewModel(title, subtitle, body, buttons);
        return vm;
    }

    /// <summary>Group invitation to join a group.</summary>
    public static NotificationViewModel ForGroupInvitation(GridClient client, InstantMessage msg)
    {
        NotificationViewModel vm = null!;
        var buttons = new List<NotificationButtonViewModel>
        {
            new("Join", () =>
            {
                client.Self.InstantMessage(
                    client.Self.Name, msg.FromAgentID, string.Empty, msg.IMSessionID,
                    InstantMessageDialog.GroupInvitationAccept,
                    InstantMessageOnline.Online, Vector3.Zero, UUID.Zero, Array.Empty<byte>());
                vm.Dismiss();
            }),
            new("Decline", () =>
            {
                client.Self.InstantMessage(
                    client.Self.Name, msg.FromAgentID, string.Empty, msg.IMSessionID,
                    InstantMessageDialog.GroupInvitationDecline,
                    InstantMessageOnline.Online, Vector3.Zero, UUID.Zero, Array.Empty<byte>());
                vm.Dismiss();
            })
        };
        vm = new NotificationViewModel("Group Invitation", msg.FromAgentName, msg.Message, buttons);
        return vm;
    }

    /// <summary>Inventory offer from another agent or an in-world object.</summary>
    public static NotificationViewModel ForInventoryOffer(GridClient client, InstantMessage msg)
    {
        NotificationViewModel vm = null!;

        bool isTask = msg.Dialog == InstantMessageDialog.TaskInventoryOffered;

        AssetType assetType = AssetType.Unknown;
        UUID objectId = UUID.Zero;
        if (msg.BinaryBucket.Length > 0)
        {
            assetType = (AssetType)msg.BinaryBucket[0];
        }
        if (!isTask && msg.BinaryBucket.Length == 17)
        {
            objectId = new UUID(msg.BinaryBucket, 1);
        }

        UUID destFolder = client.Inventory.FindFolderForType(assetType);

        var acceptDialog = isTask
            ? InstantMessageDialog.TaskInventoryAccepted
            : InstantMessageDialog.InventoryAccepted;
        var declineDialog = isTask
            ? InstantMessageDialog.TaskInventoryDeclined
            : InstantMessageDialog.InventoryDeclined;

        string subtitle = isTask ? $"From object: {msg.FromAgentName}" : $"From: {msg.FromAgentName}";

        var buttons = new List<NotificationButtonViewModel>
        {
            new("Accept", () =>
            {
                client.Self.InstantMessage(
                    client.Self.Name, msg.FromAgentID, string.Empty, msg.IMSessionID,
                    acceptDialog, InstantMessageOnline.Offline,
                    client.Self.SimPosition, client.Network.CurrentSim?.RegionID ?? UUID.Zero,
                    destFolder.GetBytes());
                if (!isTask)
                {
                    client.Inventory.RequestFetchInventory(objectId, client.Self.AgentID);
                }
                vm.Dismiss();
            }),
            new("Discard", () =>
            {
                client.Self.InstantMessage(
                    client.Self.Name, msg.FromAgentID, string.Empty, msg.IMSessionID,
                    declineDialog, InstantMessageOnline.Offline,
                    client.Self.SimPosition, client.Network.CurrentSim?.RegionID ?? UUID.Zero,
                    Utils.EmptyBytes);
                vm.Dismiss();
            }),
            new("Ignore", () => vm.Dismiss())
        };

        vm = new NotificationViewModel("Inventory Offer", subtitle, msg.Message, buttons);
        return vm;
    }

    /// <summary>A script or object wants to open a URL in the user's browser.</summary>
    public static NotificationViewModel ForLoadUrl(string objectName, string ownerName, string url, string message)
    {
        NotificationViewModel vm = null!;
        string subtitle = $"From: {ownerName} ({objectName})";
        var buttons = new List<NotificationButtonViewModel>
        {
            new("Open", () =>
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
                vm.Dismiss();
            }),
            new("Ignore", () => vm.Dismiss())
        };
        vm = new NotificationViewModel("Open URL", subtitle, $"{message}\n{url}", buttons);
        return vm;
    }

    /// <summary>Another agent is offering a teleport lure (RequestTeleport).</summary>
    public static NotificationViewModel ForTeleportOffer(GridClient client, InstantMessage msg)
    {
        NotificationViewModel vm = null!;
        var buttons = new List<NotificationButtonViewModel>
        {
            new("Teleport", () =>
            {
                client.Self.TeleportLureRespond(msg.FromAgentID, msg.IMSessionID, true);
                vm.Dismiss();
            }),
            new("Decline", () =>
            {
                client.Self.TeleportLureRespond(msg.FromAgentID, msg.IMSessionID, false);
                vm.Dismiss();
            })
        };
        vm = new NotificationViewModel("Teleport Offer", msg.FromAgentName, msg.Message, buttons);
        return vm;
    }

    /// <summary>Another agent is requesting that you send them a teleport (RequestLure).</summary>
    public static NotificationViewModel ForTeleportRequest(GridClient client, InstantMessage msg)
    {
        NotificationViewModel vm = null!;
        var buttons = new List<NotificationButtonViewModel>
        {
            new("Send Teleport", () =>
            {
                client.Self.SendTeleportLure(msg.FromAgentID, "Join me!");
                vm.Dismiss();
            }),
            new("Ignore", () => vm.Dismiss())
        };
        vm = new NotificationViewModel("Teleport Request", msg.FromAgentName, msg.Message, buttons);
        return vm;
    }

    /// <summary>A script is requesting permissions (ScriptQuestion).</summary>
    public static NotificationViewModel ForPermissions(
        GridClient client,
        Simulator simulator,
        UUID taskId,
        UUID itemId,
        string objectName,
        string objectOwnerName,
        ScriptPermission questions)
    {
        NotificationViewModel vm = null!;

        string perms = questions.ToString().Replace(",", "\n·");
        string message = $"· {perms}";

        var buttons = new List<NotificationButtonViewModel>
        {
            new("Allow", () =>
            {
                client.Self.ScriptQuestionReply(simulator, itemId, taskId, questions);
                vm.Dismiss();
            }),
            new("Deny", () =>
            {
                client.Self.ScriptQuestionReply(simulator, itemId, taskId, ScriptPermission.None);
                vm.Dismiss();
            }),
            new("Mute", () =>
            {
                client.Self.UpdateMuteListEntry(MuteType.Object, taskId, objectName);
                client.Self.ScriptQuestionReply(simulator, itemId, taskId, ScriptPermission.None);
                vm.Dismiss();
            })
        };

        vm = new NotificationViewModel(
            $"{objectName} is requesting permissions",
            $"Owner: {objectOwnerName}",
            message,
            buttons);
        return vm;
    }

    /// <summary>Region restart warning from the simulator.</summary>
    public static NotificationViewModel ForRegionRestart(
        GridClient client,
        string regionName,
        int totalSeconds,
        CancellationToken cancellationToken = default)
    {
        NotificationViewModel vm = null!;
        var buttons = new List<NotificationButtonViewModel>
        {
            new("Go Home", () =>
            {
                client.Self.RequestTeleport(UUID.Zero);
                vm.Dismiss();
            }),
            new("Dismiss", () => vm.Dismiss())
        };

        vm = new NotificationViewModel(
            "Region Restart",
            regionName,
            FormatCountdown(totalSeconds),
            buttons);

        // Countdown update loop
        _ = Task.Run(async () =>
        {
            int remaining = totalSeconds;
            while (remaining > 0 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                remaining--;
                string text = FormatCountdown(remaining);
                Dispatcher.UIThread.Post(() => vm.Message = text);
            }
        }, cancellationToken);

        return vm;
    }

    /// <summary>Generic informational message (MessageBox IM dialog, alert messages, etc.).</summary>
    public static NotificationViewModel ForGenericMessage(string title, string message)
    {
        NotificationViewModel vm = null!;
        var buttons = new List<NotificationButtonViewModel>
        {
            new("OK", () => vm.Dismiss())
        };
        vm = new NotificationViewModel(title, string.Empty, message, buttons);
        return vm;
    }

    private static string FormatCountdown(int seconds)
    {
        if (seconds <= 0) return "Restarting now!";
        if (seconds >= 60)
        {
            int m = seconds / 60;
            int s = seconds % 60;
            return s > 0 ? $"Restarting in {m}m {s}s" : $"Restarting in {m} minute{(m == 1 ? "" : "s")}";
        }
        return $"Restarting in {seconds} second{(seconds == 1 ? "" : "s")}";
    }
}
