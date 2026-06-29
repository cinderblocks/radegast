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
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class GroupNoticeArchiveViewModel : InstanceViewModelBase, IDisposable
{
    private readonly GroupNoticeArchiveService _service;

    public ObservableCollection<ArchivedNoticeItem> Notices { get; } = [];
    public ObservableCollection<string> GroupFilters { get; } = ["All"];

    [ObservableProperty] private ArchivedNoticeItem? _selectedNotice;
    [ObservableProperty] private string _selectedBody = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedGroupFilter = "All";
    [ObservableProperty] private string _statusText = string.Empty;

    public GroupNoticeArchiveViewModel(RadegastInstanceAvalonia instance) : base(instance)
    {
        _service = instance.NoticeArchive;
        Reload();
    }

    partial void OnSearchTextChanged(string value) => Reload();
    partial void OnSelectedGroupFilterChanged(string value) => Reload();

    partial void OnSelectedNoticeChanged(ArchivedNoticeItem? value)
    {
        if (value == null)
        {
            SelectedBody = string.Empty;
            return;
        }
        SelectedBody = value.Record.Body != null
            ? value.Record.Body.Replace("\n", Environment.NewLine)
            : "(Body not yet archived — it will appear here after it is fetched in the background)";
    }

    [RelayCommand]
    private void Refresh() => Reload();

    private void Reload()
    {
        var archive = _service.Archive;
        if (archive == null)
        {
            StatusText = "Not logged in.";
            return;
        }

        var all = archive.GetAll()
            .OrderByDescending(n => n.Timestamp)
            .ToList();

        var groups = all
            .Select(n => n.GroupName)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct()
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();

        GroupFilters.Clear();
        GroupFilters.Add("All");
        foreach (var g in groups) GroupFilters.Add(g);

        if (!GroupFilters.Contains(SelectedGroupFilter))
            SelectedGroupFilter = "All";

        var filtered = all.AsEnumerable();

        if (SelectedGroupFilter != "All")
            filtered = filtered.Where(n => n.GroupName == SelectedGroupFilter);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            filtered = filtered.Where(n =>
                n.Subject.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                n.FromName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (n.Body?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        Notices.Clear();
        foreach (var n in filtered)
            Notices.Add(new ArchivedNoticeItem(n));

        StatusText = $"{all.Count} notices archived · {Notices.Count} shown";
    }

    public void Dispose() { }
}

public class ArchivedNoticeItem(ArchivedGroupNotice record)
{
    public ArchivedGroupNotice Record { get; } = record;
    public string Subject => Record.Subject;
    public string FromName => Record.FromName;
    public string GroupName => Record.GroupName;
    public string Date => Record.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public bool HasAttachment => Record.HasAttachment;
    public string AttachmentInfo => Record.AttachmentName is { } n ? $"Attachment: {n}" : "Attachment (name not stored)";
}
