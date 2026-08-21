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
using System.Threading.Tasks;
using Avalonia.Rendering.Composition;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Holds the process-wide shared <see cref="VkContext"/> for every Vulkan-backed viewer
/// panel. Mirrors <see cref="GlApi"/>'s "static holder, initialised once" role, but where
/// <c>GlApi.Gl</c> is rebuilt per-panel (each GL context is independent),
/// <see cref="Context"/> is created exactly once for the
/// whole process: the first panel to construct wins, every later panel reuses the same
/// device/queue/descriptor pool ("Shared device design").
/// </summary>
internal static class VkApi
{
    private static readonly object s_lock = new();
    private static Task<(VkContext? context, string info)>? s_initTask;

    /// <summary>Valid only after <see cref="EnsureInitializedAsync"/> has completed successfully.
    /// Accessing this before then, or after a failed init, throws.</summary>
    public static VkContext Context =>
        s_initTask is { IsCompletedSuccessfully: true, Result.context: { } ctx }
            ? ctx
            : throw new InvalidOperationException(
                $"{nameof(VkApi)}.{nameof(Context)} accessed before a successful {nameof(EnsureInitializedAsync)} call.");

    public static bool IsInitialized => s_initTask is { IsCompletedSuccessfully: true, Result.context: not null };

    /// <summary>
    /// Ensures the shared <see cref="VkContext"/> exists, creating it on the first call and
    /// returning the same in-flight/completed task to every concurrent caller so two panels
    /// racing to construct during app startup can't create two devices. Safe to call from
    /// multiple panels' <c>OnAttachedToVisualTree</c> handlers.
    /// </summary>
    /// <param name="gpuInterop">The calling panel's <c>Compositor.TryGetCompositionGpuInterop()</c>
    /// result. Only the first caller's instance is actually used (device creation happens once);
    /// later callers' interop instances are assumed compatible since they come from the same
    /// process-wide compositor.</param>
    /// <returns>Success flag and a human-readable info string (device name on success, failure
    /// reason otherwise), so init failures are visible without a debugger attached.</returns>
    public static Task<(bool success, string info)> EnsureInitializedAsync(ICompositionGpuInterop gpuInterop)
    {
        lock (s_lock)
        {
            s_initTask ??= Task.Run(() => VkContext.TryCreate(gpuInterop));
        }

        return s_initTask.ContinueWith(t => (t.Result.context != null, t.Result.info), TaskScheduler.Default);
    }
}
