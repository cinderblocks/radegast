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

// Adapted from Avalonia's own samples/GpuInterop/VulkanDemo (MIT licensed,
// https://github.com/AvaloniaUI/Avalonia), validated working against this project's
// pinned Avalonia version in experiments/VulkanEmbeddingSpike before porting here.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Radegast.Veles.Rendering;

/// <summary>Marshals a single managed string to an unmanaged null-terminated ANSI buffer.</summary>
internal unsafe class VkByteString : IDisposable
{
    public IntPtr Pointer { get; }

    public VkByteString(string s) => Pointer = Marshal.StringToHGlobalAnsi(s);

    public void Dispose() => Marshal.FreeHGlobal(Pointer);

    public static implicit operator byte*(VkByteString h) => (byte*)h.Pointer;
}

/// <summary>Marshals a list of managed strings to a <c>const char* const*</c>-shaped buffer,
/// the layout Vulkan's <c>ppEnabledExtensionNames</c>/<c>ppEnabledLayerNames</c> expect.</summary>
internal unsafe class VkByteStringList : IDisposable
{
    private readonly List<VkByteString> _inner;
    private readonly byte** _ptr;

    public VkByteStringList(IEnumerable<string> items)
    {
        _inner = items.Select(x => new VkByteString(x)).ToList();
        _ptr = (byte**)Marshal.AllocHGlobal(IntPtr.Size * _inner.Count + 1);
        for (var c = 0; c < _inner.Count; c++)
            _ptr[c] = (byte*)_inner[c].Pointer;
    }

    public int Count => _inner.Count;
    public uint UCount => (uint)_inner.Count;

    public void Dispose()
    {
        foreach (var s in _inner) s.Dispose();
        Marshal.FreeHGlobal(new IntPtr(_ptr));
    }

    public static implicit operator byte**(VkByteStringList h) => h._ptr;
}
