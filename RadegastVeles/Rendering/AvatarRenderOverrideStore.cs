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
using LibreMetaverse;
using LibreMetaverse.StructuredData;
using Radegast;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Persisted set of avatars the user has explicitly chosen to always render in full,
/// regardless of computed complexity — set via the avatar context menu's "Always Render
/// Fully" toggle. Independent of friend status (a friend is already exempt without
/// needing this list; this covers everyone else the user has decided to trust).
/// <para>
/// Persisted rather than session-only: this is a durable trust decision about a specific
/// person, comparable in spirit to the (server-side) mute list, not transient UI state
/// that should reset on relog.
/// </para>
/// </summary>
public sealed class AvatarRenderOverrideStore
{
    private const string SettingsKey = "avatar_render_always_full";

    private readonly Settings _settings;
    private readonly HashSet<UUID> _alwaysRender = new();
    private readonly object _lock = new();

    /// <summary>Raised after a specific avatar's override changes, with its UUID.</summary>
    public event Action<UUID>? OverrideChanged;

    public AvatarRenderOverrideStore(Settings settings)
    {
        _settings = settings;
        if (settings[SettingsKey] is OSDArray arr)
            foreach (var item in arr)
                _alwaysRender.Add(item.AsUUID());
    }

    public bool IsAlwaysRender(UUID id)
    {
        lock (_lock) return _alwaysRender.Contains(id);
    }

    public void SetAlwaysRender(UUID id, bool value)
    {
        lock (_lock)
        {
            bool changed = value ? _alwaysRender.Add(id) : _alwaysRender.Remove(id);
            if (!changed) return;

            var arr = new OSDArray();
            foreach (var u in _alwaysRender) arr.Add(OSD.FromUUID(u));
            // Settings' indexer auto-saves on every set (Radegast.Core/Settings.cs) — no
            // separate persistence call needed here.
            _settings[SettingsKey] = arr;
        }
        OverrideChanged?.Invoke(id);
    }
}
