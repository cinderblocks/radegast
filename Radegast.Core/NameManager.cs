/*
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

using OpenMetaverse;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Channels;
using System.Threading.Tasks;
using LibreMetaverse;
using OpenMetaverse.StructuredData;

namespace Radegast
{
    public class NameManager : IDisposable, IAsyncDisposable
    {
        private const string UNKNOWN = "(???) (???)";

        public event EventHandler<UUIDNameReplyEventArgs> NameUpdated;

        public NameMode Mode
        {
            get => Client.Avatars.DisplayNamesAvailable()
                ? (NameMode)instance.GlobalSettings["display_name_mode"].AsInteger()
                : NameMode.Standard;

            set => instance.GlobalSettings["display_name_mode"] = (int)value;
        }

        private GridClient Client => instance.Client;
        private bool hasUpdates;
        private DateTime lastUpdate = DateTime.Now;
        private readonly TimeSpan cacheInterval = TimeSpan.FromSeconds(30);

        private readonly RadegastInstance instance;
        private readonly string cacheFileName;

        private readonly DateTime UUIDNameOnly = new DateTime(1970, 9, 4, 10, 0, 0, DateTimeKind.Utc);
        private readonly ConcurrentDictionary<UUID, AgentDisplayName> names = new ConcurrentDictionary<UUID, AgentDisplayName>();

        private readonly Channel<UUID> backlog;
        private readonly Task backlogTask;
        private readonly Task cacheUpdateTask;
        private readonly CancellationTokenSource backlogCts = new CancellationTokenSource();

        private readonly TokenBucketRateLimiter rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions()
        {
            AutoReplenishment = true,
            // Queue Limit shouldn't matter, since it's only used in NameManager and from a single background task, a queue of 1 should be sufficient
            QueueLimit = 1,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokenLimit = 20,
            TokensPerPeriod = 5
        });

        private readonly HashSet<UUID> batchedNamesBuffer = new HashSet<UUID>();
        private readonly List<UUID> batchedListBuffer = new List<UUID>(128);

        public NameManager(RadegastInstance instance)
        {
            this.instance = instance;
            backlog = Channel.CreateUnbounded<UUID>();
            cacheFileName = Path.Combine(instance.UserDir, "name.cache");

            instance.ClientChanged += Instance_ClientChanged;
            RegisterEvents(Client);
            backlogTask = Task.Run(() => ResolveNames(backlogCts.Token), backlogCts.Token);
            cacheUpdateTask = Task.Run(() => UpdateCache(backlogCts.Token), backlogCts.Token);
            LoadCachedNames();
        }

        private async Task ResolveNames(CancellationToken cancellationToken)
        {
            ChannelReader<UUID> reader = backlog.Reader;
            while (!cancellationToken.IsCancellationRequested)
            {
                await reader.WaitToReadAsync(cancellationToken);

                using (RateLimitLease lease = await rateLimiter.AcquireAsync(1, cancellationToken))
                {
                    if (!lease.IsAcquired)
                    {
                        Logger.Warn("Unable to require rate limit lease in name manager.", Client);
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                        continue;
                    }

                    await ProcessNameRequests(reader, cancellationToken);
                }
            }
        }

        private async Task ProcessNameRequests(ChannelReader<UUID> reader, CancellationToken cancellationToken = default)
        {
            batchedNamesBuffer.Clear();
            Stopwatch stopwatch = Stopwatch.StartNew();

            // Wait for the first item (blocking asynchronously)
            UUID firstAvatar;
            try
            {
                firstAvatar = await reader.ReadAsync(cancellationToken);
            }
            catch (OperationCanceledException) { return; }
            catch (ChannelClosedException) { return; }

            batchedNamesBuffer.Add(firstAvatar);

            // Continue to collect items until timeout (100ms) or max count (100)
            while (stopwatch.ElapsedMilliseconds < 100 && batchedNamesBuffer.Count < 100)
            {
                // Drain any immediately-available items without awaiting
                while (reader.TryRead(out UUID next) && batchedNamesBuffer.Count < 100)
                {
                    batchedNamesBuffer.Add(next);
                }

                if (batchedNamesBuffer.Count >= 100) break;

                int remaining = (int)Math.Max(1, 100 - stopwatch.ElapsedMilliseconds);

                // Wait either for a new item or timeout
                var readTask = reader.ReadAsync(cancellationToken).AsTask();
                var delayTask = Task.Delay(remaining, cancellationToken);

                var completed = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);

                if (completed == readTask)
                {
                    try
                    {
                        UUID got = await readTask; // propagate exceptions/cancellation
                        batchedNamesBuffer.Add(got);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (ChannelClosedException) { break; }
                    catch { break; }
                }
                else
                {
                    // timeout elapsed
                    break;
                }
            }

            stopwatch.Stop();

            if (batchedNamesBuffer.Count == 0) return;

            batchedListBuffer.Clear();
            batchedListBuffer.AddRange(batchedNamesBuffer);

            if (Mode == NameMode.Standard || (!Client.Avatars.DisplayNamesAvailable()))
            {
                Client.Avatars.RequestAvatarNames(batchedListBuffer);
            }
            else
            {
                // use display names
                _ = Client.Avatars.GetDisplayNames(batchedListBuffer, (success, names, badIDs) =>
                {
                    if (success)
                    {
                        ProcessDisplayNames(names);
                    }
                    else
                    {
                        Logger.Warn("Failed fetching display names", Client);
                    }
                }, cancellationToken);
            }

            batchedNamesBuffer.Clear();
            batchedListBuffer.Clear();
        }
        private void LoadCachedNames()
        {
            ThreadPool.QueueUserWorkItem(syncx =>
            {
                try
                {
                    byte[] data = File.ReadAllBytes(cacheFileName);
                    OSDMap cache = (OSDMap)OSDParser.DeserializeLLSDBinary(data);
                    OSDArray namesOSD = (OSDArray)cache["names"];
                    DateTime now = DateTime.Now;
                    TimeSpan maxAge = new TimeSpan(48, 0, 0);
                    NameMode mode = (NameMode)(int)instance.GlobalSettings["display_name_mode"];

                    foreach (var osdname in namesOSD)
                    {
                        AgentDisplayName name = AgentDisplayName.FromOSD(osdname);
                        if (mode == NameMode.Standard || ((now - name.Updated) < maxAge))
                        {
                            names.AddOrUpdate(name.ID, name, (key, _) => name);
                        }
                    }

                    Logger.DebugLog($"Restored {names.Count} names from the avatar name cache");
                }
                catch (Exception ex)
                {
                    Logger.Warn("Failed to load avatar name cache", ex, Client);
                }
            });
        }

        private async Task UpdateCache(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TimeSpan sinceLastUpdate = DateTime.Now - lastUpdate;
                if (hasUpdates && sinceLastUpdate >= cacheInterval)
                {
                    SaveToCache();
                }

                await Task.Delay(cacheInterval, cancellationToken);
            }
        }

        /// <summary>
        /// Saves name list to names.cache file.
        /// </summary>
        public void SaveToCache()
        {
            OSDArray namesOSD = new OSDArray(names.Count);
            foreach (var name in names)
            {
                namesOSD.Add(name.Value.GetOSD());
            }

            OSDMap cache = new OSDMap(1) { ["names"] = namesOSD };
            byte[] data = OSDParser.SerializeLLSDBinary(cache, false);
            Logger.DebugLog($"Caching {namesOSD.Count} avatar names to {cacheFileName}");

            try
            {
                hasUpdates = false;
                File.WriteAllBytes(cacheFileName, data);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save avatar name cache", ex, Client);
            }
            finally
            {
                lastUpdate = DateTime.Now;
            }
        }

        /// <summary>
        /// Cleans avatar name cache
        /// </summary>
        public void CleanCache()
        {
            try
            {
                names.Clear();
                File.Delete(cacheFileName);
            }
            catch (Exception ex)
            {
                Logger.Error("Error cleaning name cache.", ex);
            }
        }

        /// <summary>
        /// Disposes managed resources
        /// </summary>
        /// <returns></returns>
        public void Dispose()
        {
            backlogCts.Cancel();
            instance.ClientChanged -= Instance_ClientChanged;
            DeregisterEvents(Client);
            rateLimiter.Dispose();
        }

        /// <summary>
        /// Disposes managed resources
        /// </summary>
        /// <returns></returns>
        public async ValueTask DisposeAsync()
        {
            backlogCts.Cancel();
            await Task.WhenAll(backlogTask, cacheUpdateTask);
            instance.ClientChanged -= Instance_ClientChanged;
            DeregisterEvents(Client);
            await rateLimiter.DisposeAsync();
        }

        /// <summary>
        /// Get avatar display name, or queue fetching of the name
        /// </summary>
        /// <param name="agentID">UUID of avatar to lookup</param>
        /// <returns>Avatar display name or "Loading..." if not in cache</returns>
        public string Get(UUID agentID)
        {
            if (agentID == UUID.Zero)
            {
                return UNKNOWN;
            }

            string name = null;
            bool requestName = true;

            if (names.TryGetValue(agentID, out AgentDisplayName displayName))
            {
                if (Mode == NameMode.Standard || displayName.NextUpdate != UUIDNameOnly)
                {
                    requestName = false;
                }

                name = FormatName(displayName);
            }

            if (requestName)
            {
                QueueNameRequest(agentID);
            }
            return string.IsNullOrEmpty(name) ? RadegastInstance.INCOMPLETE_NAME : name;
        }

        private string FormatName(AgentDisplayName name)
        {
            switch (Mode)
            {
                case NameMode.OnlyDisplayName:
                    return name.DisplayName;

                case NameMode.Smart:
                    return name.IsDefaultDisplayName ? name.DisplayName : $"{name.DisplayName} ({name.UserName})";

                case NameMode.DisplayNameAndUserName:
                    return $"{name.DisplayName} ({name.UserName})";

                case NameMode.Standard:
                default:
                    return name.LegacyFullName;
            }
        }

        /// <summary>
        /// Get avatar display name, or queue fetching of the name and waiting for the response in async non-blocking manner.
        /// </summary>
        /// <param name="agentID">UUID of avatar to lookup</param>
        /// <param name="cancellationToken"></param>
        /// <returns>Avatar display name or "Loading..." if not in cache or times out</returns>
        public async Task<string> GetAsync(UUID agentID, CancellationToken cancellationToken = default)
        {
            if (names.TryGetValue(agentID, out var displayName))
            {
                return FormatName(displayName);
            }

            // Use EventSubscriptionHelper to wait for NameUpdated without manual TaskCompletionSource
            await QueueNameRequestAsync(agentID, cancellationToken);

            var result = await EventSubscriptionHelper.WaitForEventAsync<UUIDNameReplyEventArgs, string>(
                h => NameUpdated += h,
                h => NameUpdated -= h,
                e => e.Names != null && e.Names.ContainsKey(agentID),
                e => e.Names[agentID],
                5000,
                cancellationToken,
                RadegastInstance.INCOMPLETE_NAME).ConfigureAwait(false);

            if (result != RadegastInstance.INCOMPLETE_NAME)
            {
                return result;
            }

            // Fallback: maybe the name was filled into the cache while we awaited
            if (names.TryGetValue(agentID, out var avatarDisplayName))
            {
                return FormatName(avatarDisplayName);
            }

            return RadegastInstance.INCOMPLETE_NAME;
        }

        /// <summary>
        /// Get avatar display name, or queue fetching of the name
        /// </summary>
        /// <param name="agentID">UUID of avatar to lookup</param>
        /// <param name="defaultValue">If name failed to retrieve, use this</param>
        /// <returns>Avatar display name or the default value if not in cache</returns>
        public string Get(UUID agentID, string defaultValue)
        {
            if (Mode == NameMode.Standard)
            {
                return defaultValue;
            }

            string name = Get(agentID);
            return name == RadegastInstance.INCOMPLETE_NAME ? defaultValue : name;
        }

        /// <summary>
        /// Gets legacy First Last name
        /// </summary>
        /// <param name="agentID">UUID of the agent</param>
        /// <returns></returns>
        public string GetLegacyName(UUID agentID)
        {
            if (agentID == UUID.Zero)
            {
                return UNKNOWN;
            }

            if (names.TryGetValue(agentID, out var name))
            {
                return name.LegacyFullName;
            }

            QueueNameRequest(agentID);
            return RadegastInstance.INCOMPLETE_NAME;
        }

        /// <summary>
        /// Gets UserName
        /// </summary>
        /// <param name="agentID">UUID of the agent</param>
        /// <returns></returns>
        public string GetUserName(UUID agentID)
        {
            if (agentID == UUID.Zero)
            {
                return UNKNOWN;
            }

            if (names.TryGetValue(agentID, out var name))
            {
                return name.UserName;
            }

            QueueNameRequest(agentID);
            return RadegastInstance.INCOMPLETE_NAME;
        }

        /// <summary>
        /// Gets DisplayName
        /// </summary>
        /// <param name="agentID">UUID of the agent</param>
        /// <returns></returns>
        public string GetDisplayName(UUID agentID)
        {
            if (agentID == UUID.Zero)
            {
                return UNKNOWN;
            }

            if (names.TryGetValue(agentID, out var name))
            {
                return name.DisplayName;
            }

            QueueNameRequest(agentID);
            return RadegastInstance.INCOMPLETE_NAME;
        }

        private bool IsValidName(string displayName)
        {
            return !string.IsNullOrEmpty(displayName) &&
                   displayName != "???" &&
                   displayName != RadegastInstance.INCOMPLETE_NAME;
        }

        private void ProcessDisplayNames(IEnumerable<AgentDisplayName> displayNames)
        {
            Dictionary<UUID, string> updatedNames = new Dictionary<UUID, string>();

            foreach (AgentDisplayName name in displayNames)
            {
                if (!IsValidName(name.DisplayName))
                {
                    continue;
                }

                updatedNames.Add(name.ID, FormatName(name));
                name.Updated = DateTime.Now;

                names.AddOrUpdate(name.ID, name, (id, old) => name);
                hasUpdates = true;
            }

            OnDisplayNamesChanged(updatedNames);
        }

        private void OnDisplayNamesChanged(Dictionary<UUID, string> updatedNames)
        {
            if (NameUpdated == null || updatedNames.Count == 0)
            {
                return;
            }

            try
            {
                NameUpdated(this, new UUIDNameReplyEventArgs(updatedNames));
            }
            catch (Exception ex)
            {
                Logger.Warn("Failure in event handler", ex, Client);
            }
        }

        private void QueueNameRequest(UUID agentID)
        {
            if (names.TryGetValue(agentID, out AgentDisplayName name) && IsValidName(name.DisplayName))
            {
                return;
            }

            if (!backlog.Writer.TryWrite(agentID))
            {
                Logger.Warn("Failed to queue avatar name resolving.");
            }
        }

        private async ValueTask QueueNameRequestAsync(UUID agentID, CancellationToken cancellationToken = default)
        {
            if (names.TryGetValue(agentID, out AgentDisplayName name) && IsValidName(name.DisplayName))
            {
                return;
            }

            await backlog.Writer.WriteAsync(agentID, cancellationToken);
        }

        private void RegisterEvents(GridClient c)
        {
            c.Avatars.UUIDNameReply += Avatars_UUIDNameReply;
            c.Avatars.DisplayNameUpdate += Avatars_DisplayNameUpdate;
        }

        private void DeregisterEvents(GridClient c)
        {
            c.Avatars.UUIDNameReply -= Avatars_UUIDNameReply;
            c.Avatars.DisplayNameUpdate -= Avatars_DisplayNameUpdate;
        }

        private void Instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            DeregisterEvents(e.OldClient);
            RegisterEvents(e.Client);
        }

        private void Avatars_DisplayNameUpdate(object sender, DisplayNameUpdateEventArgs e)
        {
            AgentDisplayName name = e.DisplayName;
            name.Updated = DateTime.Now;

            names.AddOrUpdate(name.ID, name, (key, _) => name);
            hasUpdates = true;

            Dictionary<UUID, string> results = new Dictionary<UUID, string>
            {
                { e.DisplayName.ID, FormatName(e.DisplayName) }
            };
            OnDisplayNamesChanged(results);
        }

        private void Avatars_UUIDNameReply(object sender, UUIDNameReplyEventArgs e)
        {
            Dictionary<UUID, string> results = new Dictionary<UUID, string>();

            foreach (var kvp in e.Names)
            {
                string name = UpdateAvatarDisplayName(kvp.Key, kvp.Value);
                if (!string.IsNullOrEmpty(name))
                {
                    results.Add(kvp.Key, name);
                }
            }

            OnDisplayNamesChanged(results);
        }

        private string UpdateAvatarDisplayName(UUID avatarId, string name)
        {
            AgentDisplayName agentDisplayName = names.GetOrAdd(avatarId, (id) => new AgentDisplayName()
            {
                ID = id,
                NextUpdate = UUIDNameOnly,
                IsDefaultDisplayName = true,
                Updated = DateTime.Now
            });

            if (!Utilities.TryParseTwoNames(name, out string first, out string last))
            {
                return null;
            }

            if (IsValidName(agentDisplayName.DisplayName))
            {
                agentDisplayName.DisplayName = first + " " + last;
            }

            agentDisplayName.LegacyFirstName = first;
            agentDisplayName.LegacyLastName = last;
            agentDisplayName.UserName = agentDisplayName.LegacyLastName == "Resident"
                ? agentDisplayName.LegacyFirstName.ToLower()
                : (first + "." + last).ToLower();

            hasUpdates = true;
            return FormatName(agentDisplayName);
        }
    }

    /// <summary>
    /// Enum representing different modes of handling display names
    /// </summary>
    public enum NameMode
    {
        /// <summary> No display names </summary>
        Standard,
        /// <summary> Display name followed by (username) if display name is not default  </summary>
        Smart,
        /// <summary> Display name followed by (username) </summary>
        OnlyDisplayName,
        /// <summary> Only display </summary>
        DisplayNameAndUserName,
    }
}
