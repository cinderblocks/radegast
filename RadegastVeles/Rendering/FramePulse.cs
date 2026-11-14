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
using System.Threading;
using System.Threading.Tasks;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Lets a background loop run once per rendered frame. The viewport signals it each frame
/// after the frame's skinning and flexi work has been handed to the GPU, so whatever the loop
/// produces is always shown by the following frame. Without a signal the wait ends after a
/// timeout, which keeps the loop alive while nothing is being rendered.
/// </summary>
internal sealed class FramePulse : IDisposable
{
    private readonly ISceneViewport _viewport;
    private TaskCompletionSource _next = NewSource();

    public FramePulse(ISceneViewport viewport)
    {
        _viewport = viewport;
        _viewport.DeformersDispatched += Signal;
    }

    public void Dispose() => _viewport.DeformersDispatched -= Signal;

    private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void Signal() => Interlocked.Exchange(ref _next, NewSource()).TrySetResult();

    public async Task WaitAsync(int timeoutMs, CancellationToken ct)
    {
        var signalled = Volatile.Read(ref _next).Task;
        await Task.WhenAny(signalled, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
    }
}
