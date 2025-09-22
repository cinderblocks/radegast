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
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Radegast
{
    public class NameManager : IDisposable
    {
        #region public fields and properties
        public event EventHandler<UUIDNameReplyEventArgs> NameUpdated;

        public NameMode Mode
        {
            get => !Client.Avatars.DisplayNamesAvailable()
                ? NameMode.Standard
                : (NameMode)instance.GlobalSettings["display_name_mode"].AsInteger();

            set => instance.GlobalSettings["display_name_mode"] = (int)value;
        }
        #endregion public fields and properties


        #region private fields and properties
        private GridClient Client => instance.Client;

        private readonly RadegastInstance instance;
        private readonly string cacheFileName;

        private const int MaxNameRequests = 80;

        private readonly DateTime UUIDNameOnly = new DateTime(1970, 9, 4, 10, 0, 0, DateTimeKind.Utc);
        private readonly ConcurrentDictionary<UUID, AgentDisplayName> names = new ConcurrentDictionary<UUID, AgentDisplayName>();

        private readonly Channel<UUID> backlog;
        private readonly Task backlogTask;
        private readonly CancellationTokenSource backlogCts = new CancellationTokenSource();


        #endregion private fields and properties

        public NameManager(RadegastInstance instance)
        {
            this.instance = instance;
            backlog = Channel.CreateUnbounded<UUID>();
            backlogTask = Task.Run(() => ResolveNames(backlogCts.Token), backlogCts.Token);

            instance.ClientChanged += instance_ClientChanged;
            RegisterEvents(Client);
        }

        private async Task ResolveNames(CancellationToken cancellationToken)
        {
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                HashSet<UUID> batchedNames = new HashSet<UUID>();
                UUID avatarId = await backlog.Reader.ReadAsync(cancellationToken);
                batchedNames.Add(avatarId);

                Stopwatch stopwatch = Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < 100 && batchedNames.Count < 100)
                {
                    if (!backlog.Reader.TryRead(out UUID nextAvatarId))
                    {
                        await Task.Delay(5);
                        continue;
                    }
                    
                    batchedNames.Add(nextAvatarId);
                }
                stopwatch.Stop();

                var batchedList = batchedNames.ToList();
                if (Mode == NameMode.Standard || (!Client.Avatars.DisplayNamesAvailable()))
                {
                    Client.Avatars.RequestAvatarNames(batchedList);
                }
                else
                {
                    // use display names
                    _ =  Client.Avatars.GetDisplayNames(batchedList, (success, names, badIDs) =>
                    {
                        if (success)
                        {
                            ProcessDisplayNames(names);
                        }
                        else
                        {
                            Logger.Log("Failed fetching display names", Helpers.LogLevel.Warning, Client);
                        }
                    });
                }
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
            catch { }
        }

        public void Dispose()
        {
        }

        /// <summary>
        /// Get avatar display name, or queue fetching of the name
        /// </summary>
        /// <param name="agentID">UUID of avatar to lookup</param>
        /// <returns>Avatar display name or "Loading..." if not in cache</returns>
        public string Get(UUID agentID)
        {
            if (agentID == UUID.Zero) { return "(???) (???)"; }

            string name = null;
            bool requestName = true;

            if (names.TryGetValue(agentID, out AgentDisplayName displayName))
            {
                if (Mode == NameMode.Standard || names[agentID].NextUpdate != UUIDNameOnly)
                {
                    requestName = false;
                }

                name = FormatName(names[agentID]);
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


        public async Task<string> GetAsync(UUID agentID, CancellationToken cancellationToken = default)
        {
            if (names.TryGetValue(agentID, out var displayName))
            {
                return FormatName(displayName);
            }

            TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();

            void NameReplyHandler(object sender, UUIDNameReplyEventArgs e)
            {
                if (e.Names.TryGetValue(agentID, out var found))
                {
                    tcs.SetResult(found);
                }
            }

            using (cancellationToken.Register(tcs.SetCanceled))
            {
                NameUpdated += NameReplyHandler;

                try
                {
                    Task completedTask = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
                    if (completedTask == tcs.Task)
                    {
                        return await tcs.Task;
                    }

                    return RadegastInstance.INCOMPLETE_NAME;
                }
                finally
                {
                    NameUpdated -= NameReplyHandler;
                }
            }
        }

        /// <summary>
        /// Get avatar display name, or queue fetching of the name
        /// </summary>
        /// <param name="agentID">UUID of avatar to lookup</param>
        /// <param name="blocking">If true, wait until name is received, otherwise return immediately</param>
        /// <returns>Avatar display name or "Loading..." if not in cache</returns>
        [Obsolete("Deprecated. Use async version instead.")]
        public string Get(UUID agentID, bool blocking)
        {
            if (!blocking)
            {
                Get(agentID);
            }

            string name = null;

            using (ManualResetEvent gotName = new ManualResetEvent(false))
            {

                EventHandler<UUIDNameReplyEventArgs> handler = (sender, e) =>
                {
                    if (e.Names.TryGetValue(agentID, out var found))
                    {
                        name = found;
                        gotName.Set();
                    }
                };

                NameUpdated += handler;
                name = Get(agentID);

                if (name == RadegastInstance.INCOMPLETE_NAME)
                {
                    gotName.WaitOne(20 * 1000, false);
                }

                NameUpdated -= handler;
            }
            return name;
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
            if (agentID == UUID.Zero) { return "(???) (???)"; }

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
            if (agentID == UUID.Zero) { return "(???) (???)"; }

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
            if (agentID == UUID.Zero) { return "(???) (???)"; }

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

        private void ProcessDisplayNames(IEnumerable<AgentDisplayName> names)
        {
            Dictionary<UUID, string> updatedNames = new Dictionary<UUID, string>();

            foreach (var name in names)
            {
                if (!IsValidName(name.DisplayName))
                {
                    continue;
                }

                updatedNames.Add(name.ID, FormatName(name));
                name.Updated = DateTime.Now;

                this.names.AddOrUpdate(name.ID, name, (id, old) => name);
            }

            TriggerEvent(updatedNames);
            //TriggerCacheSave();
        }

        private void TriggerEvent(Dictionary<UUID, string> ret)
        {
            if (NameUpdated == null || ret.Count == 0)
            {
                return;
            }

            try
            {
                NameUpdated(this, new UUIDNameReplyEventArgs(ret));
            }
            catch (Exception ex)
            {
                Logger.Log("Failure in event handler: " + ex.Message, Helpers.LogLevel.Warning, Client, ex);
            }
        }

        private void QueueNameRequest(UUID agentID)
        {
            if (names.TryGetValue(agentID, out AgentDisplayName name) && !IsValidName(name.DisplayName))
            {
                return;
            }

            if (!backlog.Writer.TryWrite(agentID))
            {
                Logger.Log("Failed to queue avatar name resolving.", Helpers.LogLevel.Warning);
            }
        }

        private async ValueTask QueueNameRequestAsync(UUID agentID, CancellationToken cancellationToken = default)
        {
            if (names.TryGetValue(agentID, out AgentDisplayName name) && !IsValidName(name.DisplayName))
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

        private void instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            DeregisterEvents(e.OldClient);
            RegisterEvents(e.Client);
        }

        private void Avatars_DisplayNameUpdate(object sender, DisplayNameUpdateEventArgs e)
        {
            e.DisplayName.Updated = DateTime.Now;
            names[e.DisplayName.ID] = e.DisplayName;

            var results = new Dictionary<UUID, string>
                { {e.DisplayName.ID, FormatName(e.DisplayName) }
            };
            TriggerEvent(results);
        }

        private void Avatars_UUIDNameReply(object sender, UUIDNameReplyEventArgs e)
        {
            var ret = new Dictionary<UUID, string>();

            foreach (var kvp in e.Names)
            {
                if (!names.ContainsKey(kvp.Key))
                {
                    names[kvp.Key] = new AgentDisplayName
                    {
                        ID = kvp.Key,
                        NextUpdate = UUIDNameOnly,
                        IsDefaultDisplayName = true
                    };
                }

                names[kvp.Key].Updated = DateTime.Now;

                var parts = kvp.Value.Trim().Split(' ');
                if (parts.Length != 2) continue;
                if (IsValidName(names[kvp.Key].DisplayName))
                {
                    names[kvp.Key].DisplayName = $"{parts[0]} {parts[1]}";
                }

                names[kvp.Key].LegacyFirstName = parts[0];
                names[kvp.Key].LegacyLastName = parts[1];
                names[kvp.Key].UserName = names[kvp.Key].LegacyLastName == "Resident"
                    ? names[kvp.Key].LegacyFirstName.ToLower()
                    : $"{parts[0]}.{parts[1]}".ToLower();

                ret.Add(kvp.Key, FormatName(names[kvp.Key]));
            }

            TriggerEvent(ret);
            //TriggerCacheSave();
        }
    }
}
