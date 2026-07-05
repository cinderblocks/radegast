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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibreMetaverse;

namespace Radegast.Veles.Core;

public record ArchivedGroupNotice(
    string NoticeId,
    string GroupId,
    string GroupName,
    string Subject,
    string FromName,
    DateTime Timestamp,
    string? Body,
    bool HasAttachment,
    string? AttachmentName
);

/// <summary>
/// Persistent per-avatar store of group notices. Thread-safe for read/write.
/// </summary>
public class GroupNoticeArchive
{
    private readonly string _filePath;
    private readonly List<ArchivedGroupNotice> _notices = [];
    private readonly HashSet<string> _noticeIds = [];
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public GroupNoticeArchive(string clientDir)
    {
        _filePath = Path.Combine(clientDir, "group_notice_archive.json");
        Load();
    }

    public IReadOnlyList<ArchivedGroupNotice> GetAll()
    {
        lock (_lock) return [.._notices];
    }

    /// <summary>Returns true if the notice was new and added.</summary>
    public bool TryAdd(ArchivedGroupNotice notice)
    {
        lock (_lock)
        {
            if (!_noticeIds.Add(notice.NoticeId)) return false;
            _notices.Add(notice);
            return true;
        }
    }

    /// <summary>Updates the body text for a previously metadata-only notice.</summary>
    public bool UpdateBody(string noticeId, string body, bool hasAttachment, string? attachmentName)
    {
        lock (_lock)
        {
            var idx = _notices.FindIndex(n => n.NoticeId == noticeId);
            if (idx < 0) return false;
            _notices[idx] = _notices[idx] with
            {
                Body = body,
                HasAttachment = hasAttachment,
                AttachmentName = string.IsNullOrEmpty(attachmentName) ? null : attachmentName
            };
            return true;
        }
    }

    public IReadOnlyList<string> GetIdsWithoutBody()
    {
        lock (_lock)
            return _notices.Where(n => n.Body == null).Select(n => n.NoticeId).ToList();
    }

    public void Save()
    {
        try
        {
            List<ArchivedGroupNotice> snapshot;
            lock (_lock) { snapshot = [.._notices]; }
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(snapshot, s_options));
        }
        catch (Exception ex)
        {
            Logger.Warn($"GroupNoticeArchive: failed to save archive to '{_filePath}'.", ex);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var list = JsonSerializer.Deserialize<List<ArchivedGroupNotice>>(
                File.ReadAllText(_filePath), s_options) ?? [];
            lock (_lock)
            {
                foreach (var n in list)
                    if (_noticeIds.Add(n.NoticeId))
                        _notices.Add(n);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"GroupNoticeArchive: failed to load archive from '{_filePath}'.", ex);
        }
    }
}
