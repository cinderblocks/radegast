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
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LibreMetaverse;

namespace Radegast.Veles.Core;

/// <summary>
/// Sets the task list Windows shows when the program's taskbar button is right-clicked. Each
/// task is a shortcut that starts the program with the arguments of one <see cref="AppCommand"/>.
/// Must be called on the UI thread, which is a single-threaded apartment.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsJumpList
{
    /// <summary>
    /// Replaces the task list. A null entry is a separator. Failures are logged and otherwise
    /// ignored; the list is a convenience.
    /// </summary>
    public static void SetTasks(IReadOnlyList<(string Title, AppCommand Command)?> tasks)
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

        try
        {
            var list = (ICustomDestinationList)Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("77F10CF0-3DB5-4966-B520-B7C54FD35ED6"))!)!;
            var collection = (IObjectCollection)Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("2D3468C1-36A7-43B6-AC24-D3F02FD9607A"))!)!;
            try
            {
                var objectArrayId = typeof(IObjectArray).GUID;
                list.BeginList(out _, ref objectArrayId, out _);

                foreach (var task in tasks)
                    collection.AddObject(CreateLink(exePath, task));

                list.AddUserTasks(collection);
                list.CommitList();
            }
            finally
            {
                Marshal.ReleaseComObject(collection);
                Marshal.ReleaseComObject(list);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Could not update the taskbar jump list: {ex.Message}");
        }
    }

    private static IShellLinkW CreateLink(string exePath, (string Title, AppCommand Command)? task)
    {
        var link = (IShellLinkW)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;
        var store = (IPropertyStore)link;

        if (task is { } t)
        {
            link.SetPath(exePath);
            link.SetArguments(t.Command.ToArguments());
            link.SetIconLocation(exePath, 0);

            var titleKey = new PropertyKey(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
            var title = new PropVariant { Type = VtLpwstr, Pointer = Marshal.StringToCoTaskMemUni(t.Title) };
            try { store.SetValue(ref titleKey, ref title); }
            finally { Marshal.FreeCoTaskMem(title.Pointer); }
        }
        else
        {
            var separatorKey = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 6);
            var isSeparator = new PropVariant { Type = VtBool, Pointer = new IntPtr(0xFFFF) };
            store.SetValue(ref separatorKey, ref isSeparator);
        }

        store.Commit();
        return link;
    }

    private const ushort VtBool = 11;
    private const ushort VtLpwstr = 31;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    // PROPVARIANT: a type tag, three reserved words, then a union two pointers wide.
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public IntPtr Pointer;
        public IntPtr Extra;
    }

    [ComImport, Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void BeginList(out uint minSlots, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object removed);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, [MarshalAs(UnmanagedType.Interface)] IObjectArray items);
        void AppendKnownCategory(int category);
        void AddUserTasks([MarshalAs(UnmanagedType.Interface)] IObjectArray items);
        void CommitList();
        void GetRemovedDestinations(ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object removed);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void AbortList();
    }

    [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object item);
    }

    [ComImport, Guid("5632B1A4-E38A-400A-928A-D4CD63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection : IObjectArray
    {
        new void GetCount(out uint count);
        new void GetAt(uint index, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object item);
        void AddObject([MarshalAs(UnmanagedType.Interface)] object item);
        void AddFromArray([MarshalAs(UnmanagedType.Interface)] IObjectArray source);
        void RemoveObjectAt(uint index);
        void Clear();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(IntPtr file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription(IntPtr name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(IntPtr directory, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments(IntPtr arguments, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out ushort hotkey);
        void SetHotkey(ushort hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation(IntPtr iconPath, int maxIconPath, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }
}
