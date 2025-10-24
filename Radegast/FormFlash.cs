// 
// Radegast Metaverse Client
// Copyright (c) 2009-2014, Radegast Development Team
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
// 
//     * Redistributions of source code must retain the above copyright notice,
//       this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above copyright
//       notice, this list of conditions and the following disclaimer in the
//       documentation and/or other materials provided with the distribution.
//     * Neither the name of the application "Radegast", nor the names of its
//       contributors may be used to endorse or promote products derived from
//       this software without specific prior written permission.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
// $Id$
//
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public static class FormFlash
{
    [DllImport("user32.dll")]
    public static extern int FlashWindowEx(ref FLASHWINFO pfwi);

    /// <summary>
    /// Flashes the form's taskbar button.
    /// </summary>
    /// <param name="form"></param>
    public static void StartFlash(Form form)
    {
        if (Type.GetType("Mono.Runtime") != null || form.Focused)
        {
            return;
        }

        FLASHWINFO fw = new FLASHWINFO()
        {
            cbSize = Convert.ToUInt32(Marshal.SizeOf(typeof(FLASHWINFO))),
            hwnd = form.Handle,
            dwFlags = (int)(FLASHWINFOFLAGS.FLASHW_TRAY | FLASHWINFOFLAGS.FLASHW_TIMERNOFG),
            uCount = 5,
            dwTimeout = 0
        };

        FlashWindowEx(ref fw);
    }

    /// <summary>
    /// Stops flashing the form's taskbar button.
    /// </summary>
    /// <param name="form"></param>
    public static void StopFlash(Form form)
    {
        if (Type.GetType("Mono.Runtime") != null)
        {
            return;
        }

        FLASHWINFO fw = new FLASHWINFO()
        {
            cbSize = Convert.ToUInt32(Marshal.SizeOf(typeof(FLASHWINFO))),
            hwnd = form.Handle,
            dwFlags = (int)(FLASHWINFOFLAGS.FLASHW_STOP),
            uCount = 0,
            dwTimeout = 0
        };

        FlashWindowEx(ref fw);
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct FLASHWINFO
{
    public UInt32 cbSize;
    public IntPtr hwnd;
    public Int32 dwFlags;
    public UInt32 uCount;
    public Int32 dwTimeout;
}

[Flags]
public enum FLASHWINFOFLAGS
{
    FLASHW_STOP = 0,
    FLASHW_CAPTION = 0x00000001,
    FLASHW_TRAY = 0x00000002,
    FLASHW_ALL = (FLASHW_CAPTION | FLASHW_TRAY),
    FLASHW_TIMER = 0x00000004,
    FLASHW_TIMERNOFG = 0x0000000C
}
