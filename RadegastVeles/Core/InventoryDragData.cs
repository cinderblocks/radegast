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
using Avalonia.Input;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Core;

/// <summary>
/// Helpers for in-process inventory drag-and-drop using Avalonia's new DataTransfer API.
/// Custom objects cannot be stored directly in DataTransfer, so we keep a short-lived
/// static slot keyed by a random token.
/// </summary>
public static class InventoryDragData
{
    /// <summary>Application-scoped string format recognised by both drag source and drop targets.</summary>
    public static readonly DataFormat<string> Format =
        DataFormat.CreateStringApplicationFormat("radegast-inventory-node");

    private static readonly Dictionary<string, InvTreeNode> _pending = new();

    /// <summary>Registers <paramref name="node"/> and returns an opaque token to pass via drag data.</summary>
    public static string SetNode(InvTreeNode node)
    {
        var token = Guid.NewGuid().ToString("N");
        _pending[token] = node;
        return token;
    }

    /// <summary>Looks up the node for <paramref name="token"/> without removing it from the pending set.</summary>
    public static InvTreeNode? PeekNode(string token)
    {
        _pending.TryGetValue(token, out var node);
        return node;
    }

    /// <summary>Retrieves and removes the node associated with <paramref name="token"/>.</summary>
    public static InvTreeNode? GetNode(string token)
    {
        return !_pending.Remove(token, out var node) ? null : node;
    }
}
