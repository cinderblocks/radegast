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
using System.IO;
using System.Runtime.InteropServices;

namespace Radegast.Veles.Core;

/// <summary>
/// Sets the macOS Dock icon at runtime. An app launched from a bundle gets its icon from the
/// bundle's Info.plist; one launched as a bare executable (a local build) has no bundle, so
/// the Dock shows the generic executable icon unless the application sets one itself.
/// </summary>
internal static class MacDockIcon
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string IconFileName = "veles.icns";

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);

    /// <summary>
    /// Applies the icon when running on macOS outside an app bundle. Must be called on the
    /// main thread after the application object exists. Does nothing if the icon file is
    /// missing or cannot be decoded.
    /// </summary>
    public static void Apply()
    {
        if (!OperatingSystem.IsMacOS()) return;

        try
        {
            string baseDir = AppContext.BaseDirectory;
            // Inside a bundle the executable lives in Contents/MacOS and the bundle's own
            // icon already applies.
            if (baseDir.TrimEnd(Path.DirectorySeparatorChar).EndsWith(
                    Path.Combine("Contents", "MacOS"), StringComparison.Ordinal))
                return;

            string path = Path.Combine(baseDir, IconFileName);
            if (!File.Exists(path)) return;
            byte[] bytes = File.ReadAllBytes(path);

            IntPtr data;
            unsafe
            {
                fixed (byte* p = bytes)
                    data = Send(objc_getClass("NSData"), sel_registerName("dataWithBytes:length:"),
                        (IntPtr)p, (nuint)bytes.Length);
            }
            if (data == IntPtr.Zero) return;

            IntPtr image = Send(Send(objc_getClass("NSImage"), sel_registerName("alloc")),
                sel_registerName("initWithData:"), data);
            if (image == IntPtr.Zero) return;

            IntPtr app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            Send(app, sel_registerName("setApplicationIconImage:"), image);
            Send(image, sel_registerName("release"));
        }
        catch (Exception e)
        {
            LibreMetaverse.Logger.Warn($"Could not set the Dock icon: {e.Message}");
        }
    }
}
