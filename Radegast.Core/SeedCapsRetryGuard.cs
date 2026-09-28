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
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.Logging;
using LibreMetaverse;

namespace Radegast
{
    /// <summary>
    /// Workaround for a LibreMetaverse (3.1.5) crash. When a seed capability request fails,
    /// Caps.SeedRequestCompleteHandler cancels and disposes its readonly _HttpCts, then retries.
    /// The retry reads _HttpCts.Token, which throws ObjectDisposedException synchronously, so the
    /// handler retries again on the same stack until the process dies of a StackOverflowException.
    ///
    /// On the second pass the handler's SafeCancelAndDispose logs "Error cancelling
    /// CancellationTokenSource" (Cancel on the disposed source throws) right before it calls
    /// MakeSeedRequestAsync again. That log call runs synchronously on the same thread, so this
    /// provider uses it to give every live simulator's Caps a fresh CancellationTokenSource,
    /// and the retry then goes out as a real asynchronous request.
    /// Remove once LibreMetaverse creates a new CancellationTokenSource per seed request.
    /// </summary>
    public sealed class SeedCapsRetryGuard : ILoggerProvider, ILogger
    {
        private static readonly FieldInfo? HttpCtsField =
            typeof(Caps).GetField("_HttpCts", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly ConcurrentDictionary<GridClient, byte> Clients = new();

        public static void Register(GridClient client) => Clients[client] = 0;
        public static void Unregister(GridClient client) => Clients.TryRemove(client, out _);

        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose()
        {
            // Nothing to release: the provider holds no resources, only static state shared across clients.
        }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (HttpCtsField == null || exception is not ObjectDisposedException)
            {
                return;
            }
            if (formatter(state, exception) != "Error cancelling CancellationTokenSource")
            {
                return;
            }

            foreach (var client in Clients.Keys)
            {
                Repair(client);
            }
        }

        private static void Repair(GridClient client)
        {
            // ponytail: only sims still in Network.Simulators and connected get repaired. A Caps whose
            // simulator was already dropped (disconnected mid seed request) is unreachable here and
            // still hits the upstream bug. Retries also have no backoff; a seed cap that keeps
            // failing gets hammered asynchronously instead of overflowing the stack.
            Simulator[] sims;
            lock (client.Network.Simulators)
            {
                sims = client.Network.Simulators.ToArray();
            }

            foreach (var sim in sims)
            {
                var caps = sim.Caps;
                if (caps == null || !sim.Connected)
                {
                    continue;
                }
                if (HttpCtsField!.GetValue(caps) is not CancellationTokenSource cts || !IsDisposed(cts))
                {
                    continue;
                }

                HttpCtsField.SetValue(caps, new CancellationTokenSource());
            }
        }

        private static bool IsDisposed(CancellationTokenSource cts)
        {
            try
            {
                _ = cts.Token;
                return false;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }
    }
}
