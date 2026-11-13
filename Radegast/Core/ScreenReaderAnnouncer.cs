/*
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2026, Sjofn, LLC
 * All rights reserved.
 *
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.If not, see<https://www.gnu.org/licenses/>.
 */

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Automation;
using LibreMetaverse;

namespace Radegast
{
    /// <summary>
    /// Reads incoming chat, IMs and notifications out through the user's own screen reader, so they
    /// don't have to go and look for them in the history. Off unless "announce_to_screen_reader" is set.
    /// NVDA is spoken to through its controller client (shipped in dll\), anything else (JAWS, Narrator...)
    /// through UI Automation notifications raised from the main window. Lines queue behind whatever the
    /// screen reader is saying; the screen reader's own keys silence them as usual.
    /// </summary>
    public class ScreenReaderAnnouncer
    {
        public const string EnabledSetting = "announce_to_screen_reader";
        public const string ChatSetting = "announce_chat";
        public const string IMSetting = "announce_ims";
        public const string GroupIMSetting = "announce_group_ims";
        public const string NotificationSetting = "announce_notifications";

        private readonly RadegastInstanceForms instance;
        private readonly bool nvdaAvailable;
        private DateTime nvdaCheckedAt = DateTime.MinValue;
        private bool nvdaRunning;

        public ScreenReaderAnnouncer(RadegastInstanceForms instance)
        {
            this.instance = instance;
            nvdaAvailable = LoadNvdaClient();
        }

        /// <summary>
        /// Announce text if announcements and the given kind of announcement are turned on
        /// </summary>
        /// <param name="kindSetting">One of the announce_* settings</param>
        /// <param name="text">What to say</param>
        /// <param name="important">Notifications that need an answer, as opposed to chat</param>
        public void Announce(string kindSetting, string text, bool important = false)
        {
            var settings = instance.GlobalSettings;
            if (!settings[EnabledSetting].AsBoolean() || !settings[kindSetting].AsBoolean()) return;
            if (string.IsNullOrWhiteSpace(text)) return;

            try
            {
                if (IsNvdaRunning())
                {
                    if (Environment.Is64BitProcess)
                    {
                        Nvda64.nvdaController_speakText(text);
                        Nvda64.nvdaController_brailleMessage(text);
                    }
                    else
                    {
                        Nvda32.nvdaController_speakText(text);
                        Nvda32.nvdaController_brailleMessage(text);
                    }
                    return;
                }

                var form = instance.MainForm;
                if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
                if (form.InvokeRequired)
                {
                    form.BeginInvoke(new MethodInvoker(() => RaiseNotification(form, text, important)));
                }
                else
                {
                    RaiseNotification(form, text, important);
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Screen reader announcement failed: {ex.Message}");
            }
        }

        private static void RaiseNotification(Form form, string text, bool important)
        {
            if (form.IsDisposed) return;
            form.AccessibilityObject.RaiseAutomationNotification(
                AutomationNotificationKind.Other,
                important ? AutomationNotificationProcessing.ImportantAll : AutomationNotificationProcessing.All,
                text);
        }

        private bool IsNvdaRunning()
        {
            if (!nvdaAvailable) return false;

            // Asking NVDA is a quick local RPC call, but chat can come in bursts
            if ((DateTime.UtcNow - nvdaCheckedAt).TotalSeconds > 2)
            {
                nvdaRunning = (Environment.Is64BitProcess
                    ? Nvda64.nvdaController_testIfRunning()
                    : Nvda32.nvdaController_testIfRunning()) == 0;
                nvdaCheckedAt = DateTime.UtcNow;
            }
            return nvdaRunning;
        }

        private static bool LoadNvdaClient()
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64 &&
                RuntimeInformation.ProcessArchitecture != Architecture.X86)
            {
                return false;
            }

            var arch = Environment.Is64BitProcess ? "x64" : "x86";
            var file = Environment.Is64BitProcess ? Nvda64.Dll : Nvda32.Dll;
            var path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "dll", arch, file);

            // Loaded by full path here, so the DllImports below bind to it by name
            if (!File.Exists(path) || LoadLibrary(path) == IntPtr.Zero)
            {
                Logger.Debug($"NVDA controller client not available at {path}");
                return false;
            }
            return true;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string path);

        private static class Nvda64
        {
            public const string Dll = "nvdaControllerClient64.dll";
            [DllImport(Dll)] public static extern int nvdaController_testIfRunning();
            [DllImport(Dll, CharSet = CharSet.Unicode)] public static extern int nvdaController_speakText(string text);
            [DllImport(Dll, CharSet = CharSet.Unicode)] public static extern int nvdaController_brailleMessage(string message);
        }

        private static class Nvda32
        {
            public const string Dll = "nvdaControllerClient32.dll";
            [DllImport(Dll)] public static extern int nvdaController_testIfRunning();
            [DllImport(Dll, CharSet = CharSet.Unicode)] public static extern int nvdaController_speakText(string text);
            [DllImport(Dll, CharSet = CharSet.Unicode)] public static extern int nvdaController_brailleMessage(string message);
        }
    }
}
