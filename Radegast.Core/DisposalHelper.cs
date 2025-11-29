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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Radegast
{
    /// <summary>
    /// Helper class for safely disposing resources
    /// </summary>
    public static class DisposalHelper
    {
        /// <summary>
        /// Safely dispose an object, catching and logging any exceptions
        /// </summary>
        public static void SafeDispose(IDisposable resource, string resourceName = null, Action<string, Exception> logger = null)
        {
            if (resource == null) return;

            try
            {
                resource.Dispose();
            }
            catch (Exception ex)
            {
                var message = string.IsNullOrEmpty(resourceName)
                    ? "Error disposing resource"
                    : $"Error disposing {resourceName}";

                logger?.Invoke(message, ex);
            }
        }

        /// <summary>
        /// Safely dispose multiple resources
        /// </summary>
        public static void SafeDisposeAll(IEnumerable<IDisposable> resources, Action<string, Exception> logger = null)
        {
            if (resources == null) return;

            foreach (var resource in resources.Where(r => r != null))
            {
                SafeDispose(resource, logger: logger);
            }
        }

        /// <summary>
        /// Safely dispose all items in a collection and clear it
        /// </summary>
        public static void SafeDisposeClear<T>(ICollection<T> collection, Action<string, Exception> logger = null)
            where T : IDisposable
        {
            if (collection == null) return;

            try
            {
                SafeDisposeAll(collection.Cast<IDisposable>(), logger);
                collection.Clear();
            }
            catch (Exception ex)
            {
                logger?.Invoke("Error clearing collection", ex);
            }
        }

        /// <summary>
        /// Safely wait for and dispose a thread
        /// </summary>
        public static bool SafeJoinThread(Thread thread, TimeSpan timeout, Action<string, Exception> logger = null)
        {
            if (thread == null || !thread.IsAlive) return true;

            try
            {
                if (!thread.Join(timeout))
                {
                    logger?.Invoke($"Thread {thread.Name ?? "unnamed"} did not exit in time", null);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                logger?.Invoke($"Error waiting for thread {thread.Name ?? "unnamed"}", ex);
                return false;
            }
        }

        /// <summary>
        /// Safely cancel and wait for a cancellation token source
        /// </summary>
        public static void SafeCancelAndDispose(CancellationTokenSource cts, Action<string, Exception> logger = null)
        {
            if (cts == null) return;

            try
            {
                cts.Cancel();
            }
            catch (Exception ex)
            {
                logger?.Invoke("Error cancelling CancellationTokenSource", ex);
            }

            SafeDispose(cts, "CancellationTokenSource", logger);
        }

        /// <summary>
        /// Execute an action with automatic resource disposal
        /// </summary>
        public static TResult Using<TDisposable, TResult>(
            Func<TDisposable> factory,
            Func<TDisposable, TResult> action)
            where TDisposable : IDisposable
        {
            using (var resource = factory())
            {
                return action(resource);
            }
        }

        /// <summary>
        /// Execute an async action with automatic resource disposal
        /// </summary>
        public static async Task<TResult> UsingAsync<TDisposable, TResult>(
            Func<TDisposable> factory,
            Func<TDisposable, Task<TResult>> action)
            where TDisposable : IDisposable
        {
            using (var resource = factory())
            {
                return await action(resource);
            }
        }

        /// <summary>
        /// Guard for ensuring disposal even with exceptions
        /// </summary>
        public sealed class DisposalGuard : IDisposable
        {
            private readonly Action onDispose;
            private bool disposed;

            public DisposalGuard(Action onDispose)
            {
                this.onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
            }

            public void Dispose()
            {
                if (!disposed)
                {
                    disposed = true;
                    onDispose();
                }
            }
        }
    }
}
