/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2025, Sjofn, LLC
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
using System.ComponentModel;
using System.Threading.Tasks;

namespace Radegast
{
    /// <summary>
    /// Helper class for cross-thread invocation patterns
    /// </summary>
    public static class ThreadingHelper
    {
        /// <summary>
        /// Safely invoke an action on the control's thread if required
        /// </summary>
        /// <param name="invoker">Control or component that supports ISynchronizeInvoke</param>
        /// <param name="action">Action to execute</param>
        /// <param name="isMonoRuntime">Whether running on Mono</param>
        public static void SafeInvoke(ISynchronizeInvoke invoker, Action action, bool isMonoRuntime = false)
        {
            if (invoker == null || action == null) return;

            if (invoker.InvokeRequired)
            {
                bool hasHandle = true;
                if (invoker is IDisposable disposable)
                {
                    try
                    {
                        // Try to check if handle is created for Windows Forms controls
                        var handleProp = invoker.GetType().GetProperty("IsHandleCreated");
                        if (handleProp != null)
                        {
                            hasHandle = (bool)handleProp.GetValue(invoker);
                        }
                    }
                    catch { }
                }

                if (!isMonoRuntime || hasHandle)
                {
                    try
                    {
                        invoker.BeginInvoke(action, null);
                    }
                    catch (ObjectDisposedException) { }
                    catch (InvalidOperationException) { }
                }
                return;
            }

            action();
        }

        /// <summary>
        /// Safely invoke an action synchronously on the control's thread if required
        /// </summary>
        public static void SafeInvokeSync(ISynchronizeInvoke invoker, Action action, bool isMonoRuntime = false)
        {
            if (invoker == null || action == null) return;

            if (invoker.InvokeRequired)
            {
                bool hasHandle = true;
                if (invoker is IDisposable)
                {
                    try
                    {
                        var handleProp = invoker.GetType().GetProperty("IsHandleCreated");
                        if (handleProp != null)
                        {
                            hasHandle = (bool)handleProp.GetValue(invoker);
                        }
                    }
                    catch { }
                }

                if (!isMonoRuntime || hasHandle)
                {
                    try
                    {
                        invoker.Invoke(action, null);
                    }
                    catch (ObjectDisposedException) { }
                    catch (InvalidOperationException) { }
                }
                return;
            }

            action();
        }

        /// <summary>
        /// Safely invoke an action asynchronously
        /// </summary>
        public static Task SafeInvokeAsync(ISynchronizeInvoke invoker, Action action)
        {
            if (invoker == null || action == null) return Task.CompletedTask;

            var tcs = new TaskCompletionSource<bool>();

            if (invoker.InvokeRequired)
            {
                try
                {
                    invoker.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            action();
                            tcs.TrySetResult(true);
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                    }), null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }
            else
            {
                try
                {
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }

            return tcs.Task;
        }
    }
}
