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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LibreMetaverse;

namespace Radegast.Veles.Core;

/// <summary>
/// Passively captures group notices into a persistent per-avatar archive.
/// Subscribes to incoming notice IMs and notice-list replies, and background-fetches
/// notice bodies it hasn't yet stored (throttled to one request every 1.5 s).
/// </summary>
public sealed class GroupNoticeArchiveService : IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;

    public GroupNoticeArchive? Archive { get; private set; }

    private readonly ConcurrentQueue<UUID> _pendingBodyFetches = new();
    private UUID _currentFetchId = UUID.Zero;
    private DateTime _currentFetchStarted;
    private bool _groupsInitialized;
    private readonly Timer _fetchTimer;

    public GroupNoticeArchiveService(RadegastInstanceAvalonia instance)
    {
        _instance = instance;
        Client.Self.IM += Self_IM;
        Client.Groups.GroupNoticesListReply += Groups_NoticesListReply;
        Client.Groups.CurrentGroups += Groups_CurrentGroups;
        // Start 3 s after construction, tick every 1.5 s
        _fetchTimer = new Timer(FetchTimerCallback, null, 3000, 1500);
    }

    // ── Login hook ────────────────────────────────────────────────────────────

    private void Groups_CurrentGroups(object? sender, CurrentGroupsEventArgs e)
    {
        // ClientDir requires the logged-in avatar name; initialize archive lazily here.
        Archive ??= new GroupNoticeArchive(_instance.ClientDir);

        if (_groupsInitialized) return;
        _groupsInitialized = true;

        // Queue body fetches for any notices we stored without a body in a previous session.
        foreach (var id in Archive.GetIdsWithoutBody())
            if (UUID.TryParse(id, out var uuid))
                _pendingBodyFetches.Enqueue(uuid);

        // Bulk-request notice lists for every group the avatar belongs to.
        _ = Task.Run(async () =>
        {
            foreach (var groupId in e.Groups.Keys)
            {
                Client.Groups.RequestGroupNoticesList(groupId);
                await Task.Delay(300);
            }
        });
    }

    // ── Notice list reply ─────────────────────────────────────────────────────

    private void Groups_NoticesListReply(object? sender, GroupNoticesListReplyEventArgs e)
    {
        if (Archive == null) return;

        _instance.Groups.TryGetValue(e.GroupID, out var group);
        var groupName = !string.IsNullOrEmpty(group.Name) ? group.Name : e.GroupID.ToString();

        bool anyNew = false;
        foreach (var notice in e.Notices)
        {
            var ts = notice.Timestamp != 0
                ? Utils.UnixTimeToDateTime(notice.Timestamp)
                : DateTime.UtcNow;

            var record = new ArchivedGroupNotice(
                notice.NoticeID.ToString(),
                e.GroupID.ToString(),
                groupName,
                notice.Subject,
                notice.FromName,
                ts,
                null,           // body fetched separately
                notice.HasAttachment,
                null
            );

            if (Archive.TryAdd(record))
            {
                _pendingBodyFetches.Enqueue(notice.NoticeID);
                anyNew = true;
            }
        }

        if (anyNew) Archive.Save();
    }

    // ── Incoming IM handler ───────────────────────────────────────────────────

    private void Self_IM(object? sender, InstantMessageEventArgs e)
    {
        switch (e.IM.Dialog)
        {
            case InstantMessageDialog.GroupNotice:
                ArchiveLiveNotice(e.IM);
                break;
            case InstantMessageDialog.GroupNoticeRequested:
                ArchiveNoticeBody(e.IM);
                break;
        }
    }

    // A live incoming notice already carries the full body in Message (format: "Subject|Body").
    private void ArchiveLiveNotice(InstantMessage msg)
    {
        if (Archive == null) return;

        bool hasAttachment = msg.BinaryBucket.Length > 18 && msg.BinaryBucket[0] != 0;
        string? attachmentName = hasAttachment
            ? Utils.BytesToString(msg.BinaryBucket, 18, msg.BinaryBucket.Length - 19)
            : null;

        int pipe = msg.Message.IndexOf('|');
        string subject = pipe >= 0 ? msg.Message[..pipe] : msg.Message;
        string body = pipe >= 0 ? msg.Message[(pipe + 1)..] : string.Empty;

        // For live group notice IMs, FromAgentID is the group UUID.
        _instance.Groups.TryGetValue(msg.FromAgentID, out var group);
        var groupName = !string.IsNullOrEmpty(group.Name) ? group.Name : msg.FromAgentID.ToString();

        var record = new ArchivedGroupNotice(
            msg.IMSessionID.ToString(),
            msg.FromAgentID.ToString(),
            groupName,
            subject,
            msg.FromAgentName,
            DateTime.UtcNow,
            body,
            hasAttachment,
            attachmentName
        );

        if (Archive.TryAdd(record))
            Archive.Save();
    }

    // Response to RequestGroupNotice — body for whichever notice we last requested.
    private void ArchiveNoticeBody(InstantMessage msg)
    {
        if (Archive == null || _currentFetchId == UUID.Zero) return;

        bool hasAttachment = msg.BinaryBucket.Length > 18 && msg.BinaryBucket[0] != 0;
        string? attachmentName = hasAttachment
            ? Utils.BytesToString(msg.BinaryBucket, 18, msg.BinaryBucket.Length - 19)
            : null;

        var raw = msg.Message ?? string.Empty;
        int sep = raw.IndexOf('|');
        string body = sep >= 0 ? raw[(sep + 1)..] : raw;

        if (Archive.UpdateBody(_currentFetchId.ToString(), body, hasAttachment, attachmentName))
            Archive.Save();

        _currentFetchId = UUID.Zero;
    }

    // ── Background body-fetch timer ───────────────────────────────────────────

    private void FetchTimerCallback(object? state)
    {
        if (Archive == null) return;

        // If a fetch is in flight, wait up to 15 s before giving up on it.
        if (_currentFetchId != UUID.Zero)
        {
            if ((DateTime.UtcNow - _currentFetchStarted).TotalSeconds < 15) return;
            _currentFetchId = UUID.Zero;
        }

        if (!_pendingBodyFetches.TryDequeue(out var noticeId)) return;
        _currentFetchId = noticeId;
        _currentFetchStarted = DateTime.UtcNow;
        Client.Groups.RequestGroupNotice(noticeId);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _fetchTimer.Dispose();
        Client.Self.IM -= Self_IM;
        Client.Groups.GroupNoticesListReply -= Groups_NoticesListReply;
        Client.Groups.CurrentGroups -= Groups_CurrentGroups;
    }
}
