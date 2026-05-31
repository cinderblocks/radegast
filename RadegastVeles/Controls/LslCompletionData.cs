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
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using LibreMetaverse;

namespace Radegast.Veles.Controls;

/// <summary>
/// A single LSL keyword offered as an auto-complete entry.
/// </summary>
public sealed class LslCompletionData : ICompletionData
{
    private readonly LslSyntax.LslKeyword _keyword;

    public LslCompletionData(LslSyntax.LslKeyword keyword)
    {
        _keyword = keyword;
        Text = keyword.Keyword;
        Description = string.IsNullOrWhiteSpace(keyword.Tooltip) ? null : keyword.Tooltip;
        Priority = keyword.Category switch
        {
            LslSyntax.LslCategory.Function  => 1.0,
            LslSyntax.LslCategory.Event     => 0.9,
            LslSyntax.LslCategory.Constant  => 0.8,
            LslSyntax.LslCategory.Datatype  => 0.7,
            LslSyntax.LslCategory.Control   => 0.6,
            LslSyntax.LslCategory.Flow      => 0.6,
            _                               => 0.5,
        };
    }

    // ICompletionData
    public IImage? Image => null;
    public string Text { get; }
    public object Content => Text;
    public object? Description { get; }
    public double Priority { get; }

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);

        // Auto-insert parens for functions that take arguments.
        if (_keyword.Category == LslSyntax.LslCategory.Function && !string.IsNullOrWhiteSpace(_keyword.Tooltip))
        {
            // Only add () if the tooltip suggests parameters (contains a '(')
            if (_keyword.Tooltip.Contains('('))
            {
                var offset = textArea.Caret.Offset;
                textArea.Document.Insert(offset, "()");
                // Place cursor between the parens
                textArea.Caret.Offset = offset + 1;
            }
        }
    }
}

/// <summary>
/// Builds and caches the full list of <see cref="LslCompletionData"/> items from <see cref="LslSyntax"/>.
/// </summary>
public static class LslCompletionProvider
{
    private static IReadOnlyList<LslCompletionData>? _items;

    /// <summary>
    /// Returns all non-god-mode LSL keyword completion items.
    /// </summary>
    public static IReadOnlyList<LslCompletionData> GetItems()
    {
        if (_items != null) return _items;

        // Ensure keywords are loaded (reads keywords_lsl_default.xml once)
        _ = new LslSyntax();

        _items = LslSyntax.Keywords.Values
            .Where(k => !k.GodMode)
            .OrderBy(k => k.Keyword, StringComparer.OrdinalIgnoreCase)
            .Select(k => new LslCompletionData(k))
            .ToList();

        return _items;
    }

    /// <summary>
    /// Returns completion items whose <see cref="LslCompletionData.Text"/> starts with
    /// <paramref name="prefix"/> (case-insensitive).
    /// </summary>
    public static IEnumerable<LslCompletionData> GetItemsForPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return GetItems();
        return GetItems().Where(i =>
            i.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
