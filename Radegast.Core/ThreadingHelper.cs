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
using System.Threading;

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
            return SafeInvokeAsync(invoker, action, CancellationToken.None);
        }

        /// <summary>
        /// Safely invoke an action asynchronously with cancellation support
        /// </summary>
        public static Task SafeInvokeAsync(ISynchronizeInvoke invoker, Action action, CancellationToken cancellationToken)
        {
            if (invoker == null || action == null) return Task.CompletedTask;

            var tcs = new TaskCompletionSource<bool>();

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

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

                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(state => ((TaskCompletionSource<bool>)state).TrySetCanceled(), tcs, useSynchronizationContext: false);
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

        /// <summary>
        /// Safely invoke an async function on the control's thread and await its completion
        /// </summary>
        public static Task SafeInvokeAsync(ISynchronizeInvoke invoker, Func<Task> asyncAction, CancellationToken cancellationToken = default)
        {
            if (invoker == null || asyncAction == null) return Task.CompletedTask;

            var tcs = new TaskCompletionSource<bool>();

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            if (invoker.InvokeRequired)
            {
                try
                {
                    invoker.BeginInvoke(new Action(async () =>
                    {
                        try
                        {
                            await asyncAction().ConfigureAwait(false);
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

                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(state => ((TaskCompletionSource<bool>)state).TrySetCanceled(), tcs, useSynchronizationContext: false);
                }
            }
            else
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await asyncAction().ConfigureAwait(false);
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                });
            }

            return tcs.Task;
        }
    }
}
