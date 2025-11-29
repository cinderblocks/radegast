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
using System.Threading;
using System.Threading.Tasks;

namespace Radegast
{
    /// <summary>
    /// Helper for managing event subscriptions with timeout and cancellation support
    /// </summary>
    /// <remarks>This may end up in LibreMetaverse</remarks>
    public static class EventSubscriptionHelper
    {
        /// <summary>
        /// Wait for an event to fire with timeout support using ManualResetEvent
        /// </summary>
        public static TResult WaitForEvent<TEventArgs, TResult>(
            Action<EventHandler<TEventArgs>> subscribe,
            Action<EventHandler<TEventArgs>> unsubscribe,
            Func<TEventArgs, bool> filter,
            Func<TEventArgs, TResult> resultSelector,
            int timeoutMs,
            TResult defaultValue = default)
        {
            var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            EventHandler<TEventArgs> handler = (sender, e) =>
            {
                if (filter == null || filter(e))
                {
                    tcs.TrySetResult(resultSelector(e));
                }
            };

            subscribe(handler);
            try
            {
                if (tcs.Task.Wait(timeoutMs))
                {
                    return tcs.Task.Result;
                }

                return defaultValue;
            }
            finally
            {
                unsubscribe(handler);
            }
        }

        /// <summary>
        /// Wait for an event to fire with timeout support using async/await
        /// </summary>
        public static async Task<TResult> WaitForEventAsync<TEventArgs, TResult>(
            Action<EventHandler<TEventArgs>> subscribe,
            Action<EventHandler<TEventArgs>> unsubscribe,
            Func<TEventArgs, bool> filter,
            Func<TEventArgs, TResult> resultSelector,
            int timeoutMs,
            CancellationToken cancellationToken = default,
            TResult defaultValue = default)
        {
            var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            EventHandler<TEventArgs> handler = (sender, e) =>
            {
                if (filter == null || filter(e))
                {
                    tcs.TrySetResult(resultSelector(e));
                }
            };

            using (cancellationToken.Register(() => tcs.TrySetCanceled()))
            {
                subscribe(handler);
                try
                {
                    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, cancellationToken));
                    if (completedTask == tcs.Task)
                    {
                        return await tcs.Task;
                    }

                    return defaultValue;
                }
                finally
                {
                    unsubscribe(handler);
                }
            }
        }

        /// <summary>
        /// Subscribe to an event temporarily to wait for a condition
        /// </summary>
        public static void WaitForCondition<TEventArgs>(
            Action<EventHandler<TEventArgs>> subscribe,
            Action<EventHandler<TEventArgs>> unsubscribe,
            Func<TEventArgs, bool> condition,
            int timeoutMs)
         {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            EventHandler<TEventArgs> handler = (sender, e) =>
            {
                if (condition(e))
                {
                    tcs.TrySetResult(true);
                }
            };

            subscribe(handler);
            try
            {
                tcs.Task.Wait(timeoutMs);
            }
            finally
            {
                unsubscribe(handler);
            }
         }
    }

    /// <summary>
    /// Disposable event subscription that automatically unsubscribes
    /// </summary>
    public class EventSubscription<TEventArgs> : IDisposable
    {
        private readonly Action<EventHandler<TEventArgs>> unsubscribe;
        private readonly EventHandler<TEventArgs> handler;
        private bool disposed;

        public EventSubscription(
            Action<EventHandler<TEventArgs>> subscribe,
            Action<EventHandler<TEventArgs>> unsubscribe,
            EventHandler<TEventArgs> handler)
        {
            this.unsubscribe = unsubscribe;
            this.handler = handler;
            subscribe(handler);
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                unsubscribe(handler);
            }
        }
    }
}
