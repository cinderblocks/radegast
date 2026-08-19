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

// Vendored from Avalonia's own source (MIT licensed, https://github.com/AvaloniaUI/Avalonia,
// System.Reactive.Disposables.Disposable's internal Avalonia.Reactive counterpart) -- not a
// public Avalonia API, needed only as SwapchainBase.cs's `Disposable.Create(...)` dependency.
// Validated working (byte-for-byte) in experiments/VulkanEmbeddingSpike/Vendored/Disposable.cs
// before being ported here for the real Section 8a render loop -- see plan Section 8a.

using System;
using System.Threading;

namespace Radegast.Veles.Rendering;

internal static class Disposable
{
    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        private EmptyDisposable() { }
        public void Dispose() { }
    }

    internal sealed class AnonymousDisposable : IDisposable
    {
        private volatile Action? _dispose;
        public AnonymousDisposable(Action dispose) => _dispose = dispose;
        public bool IsDisposed => _dispose == null;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }

    public static IDisposable Empty => EmptyDisposable.Instance;

    public static IDisposable Create(Action dispose)
    {
        ArgumentNullException.ThrowIfNull(dispose);
        return new AnonymousDisposable(dispose);
    }
}
