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

namespace Veles.Plugin.Macros;

/// <summary>The operation to perform in a single macro step.</summary>
public enum StepType
{
    /// <summary>Say something in nearby chat (channel 0 by default).</summary>
    Say,

    /// <summary>Send an emote (/me …) in nearby chat.</summary>
    Emote,

    /// <summary>Wait for a fixed number of milliseconds before proceeding.</summary>
    Wait,

    /// <summary>Send an instant message to a specific avatar.</summary>
    IM,

    /// <summary>Stand up.</summary>
    Stand,

    /// <summary>Sit on a specific in-world object.</summary>
    Sit,

    /// <summary>Play an animation gesture by its asset UUID.</summary>
    PlayGesture,

    /// <summary>Execute a built-in Radegast command (include the // prefix).</summary>
    Command,
}

/// <summary>One step in a macro sequence.</summary>
public sealed class MacroStep
{
    public StepType Type { get; set; } = StepType.Say;

    /// <summary>Message text for Say, Emote, IM, and Command steps.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Chat channel for Say (default 0 = public chat).</summary>
    public int Channel { get; set; } = 0;

    /// <summary>Chat volume for Say: Normal, Whisper, or Shout.</summary>
    public string ChatVolume { get; set; } = "Normal";

    /// <summary>Target avatar UUID string for IM and Sit steps.</summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>Delay in milliseconds for Wait steps (capped at 60 000).</summary>
    public int DelayMs { get; set; } = 1000;

    /// <summary>Gesture asset UUID string for PlayGesture steps.</summary>
    public string AssetId { get; set; } = string.Empty;

    /// <summary>Short human-readable summary shown in the step list.</summary>
    public string Summary => Type switch
    {
        StepType.Say         => Channel == 0
                                    ? $"Say [{ChatVolume}]: \"{Truncate(Text)}\""
                                    : $"Say ch{Channel} [{ChatVolume}]: \"{Truncate(Text)}\"",
        StepType.Emote       => $"Emote: \"{Truncate(Text)}\"",
        StepType.Wait        => $"Wait {DelayMs} ms",
        StepType.IM          => $"IM {ShortId(TargetId)}: \"{Truncate(Text)}\"",
        StepType.Stand       => "Stand",
        StepType.Sit         => $"Sit on {ShortId(TargetId)}",
        StepType.PlayGesture => $"Gesture {ShortId(AssetId)}",
        StepType.Command     => $"Command: {Truncate(Text)}",
        _                    => Type.ToString(),
    };

    private static string Truncate(string s) =>
        s.Length > 40 ? s[..37] + "…" : s;

    private static string ShortId(string id) =>
        id.Length >= 8 ? id[..8] + "…" : (id.Length > 0 ? id : "(none)");
}

/// <summary>A named, reusable sequence of steps the user can trigger on demand.</summary>
public sealed class Macro
{
    /// <summary>Short unique identifier. Auto-generated.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Display name.</summary>
    public string Name { get; set; } = "New Macro";

    /// <summary>Whether this macro is available to run. Disabled macros are skipped by //macro run.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Steps executed in order when the macro runs.</summary>
    public List<MacroStep> Steps { get; set; } = [];
}
