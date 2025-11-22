using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenMetaverse;

namespace Radegast
{
    public class IMSessionInfo
    {
        public UUID SessionID { get; }
        public HashSet<UUID> Participants { get; } = new HashSet<UUID>();
        public DateTime LastActivityUtc { get; set; }
        public Dictionary<UUID, DateTime> TypingSinceUtc { get; } = new Dictionary<UUID, DateTime>();

        public IMSessionInfo(UUID sessionId)
        {
            SessionID = sessionId;
            LastActivityUtc = DateTime.UtcNow;
        }
    }

    public class IMSessionEventArgs : EventArgs
    {
        public IMSessionInfo Session { get; }
        public IMSessionEventArgs(IMSessionInfo s) { Session = s; }
    }

    public class IMTypingEventArgs : EventArgs
    {
        public UUID SessionID { get; }
        public UUID AgentID { get; }
        public IMTypingEventArgs(UUID sessionId, UUID agentId)
        {
            SessionID = sessionId;
            AgentID = agentId;
        }
    }

    /// <summary>
    /// Tracks active IM sessions, typing notifications and session timeouts.
    /// Subscribes to NetCom events.
    /// </summary>
    public class IMSessionManager : IDisposable
    {
        private readonly RadegastInstance instance;
        private readonly Dictionary<UUID, IMSessionInfo> sessions = new Dictionary<UUID, IMSessionInfo>();
        private readonly object sync = new object();
        private readonly Timer cleanupTimer;

        // defaults
        public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan TypingClearTimeout { get; set; } = TimeSpan.FromSeconds(8);
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromSeconds(30);

        public event EventHandler<IMSessionEventArgs> SessionOpened;
        public event EventHandler<IMSessionEventArgs> SessionClosed;
        public event EventHandler<IMTypingEventArgs> TypingStarted;
        public event EventHandler<IMTypingEventArgs> TypingStopped;

        public IMSessionManager(RadegastInstance instance)
        {
            this.instance = instance ?? throw new ArgumentNullException(nameof(instance));

            instance.NetCom.InstantMessageReceived += NetCom_InstantMessageReceived;
            instance.NetCom.InstantMessageSent += NetCom_InstantMessageSent;

            cleanupTimer = new Timer(CleanupCallback, null, (int)CleanupInterval.TotalMilliseconds, (int)CleanupInterval.TotalMilliseconds);
        }

        private void NetCom_InstantMessageSent(object sender, InstantMessageSentEventArgs e)
        {
            if (e == null) return;
            UpdateSessionActivity(e.SessionID, e.FromAgentID == UUID.Zero ? instance.Client.Self.AgentID : e.FromAgentID);
        }

        private void NetCom_InstantMessageReceived(object sender, InstantMessageEventArgs e)
        {
            if (e == null) return;

            var im = e.IM;

            // typing dialogs are handled separately
            if (im.Dialog == InstantMessageDialog.StartTyping)
            {
                OnTypingStarted(im.IMSessionID, im.FromAgentID);
                return;
            }
            if (im.Dialog == InstantMessageDialog.StopTyping)
            {
                OnTypingStopped(im.IMSessionID, im.FromAgentID);
                return;
            }

            UpdateSessionActivity(im.IMSessionID, im.FromAgentID);
        }

        private void UpdateSessionActivity(UUID sessionId, UUID participant)
        {
            if (sessionId == UUID.Zero) return;

            IMSessionInfo info;
            bool isNew = false;
            lock (sync)
            {
                if (!sessions.TryGetValue(sessionId, out info))
                {
                    info = new IMSessionInfo(sessionId);
                    sessions[sessionId] = info;
                    isNew = true;
                }
                info.LastActivityUtc = DateTime.UtcNow;
                if (participant != UUID.Zero)
                    info.Participants.Add(participant);
            }

            if (isNew)
            {
                try { SessionOpened?.Invoke(this, new IMSessionEventArgs(info)); } catch { }
            }
        }

        private void OnTypingStarted(UUID sessionId, UUID agentId)
        {
            if (sessionId == UUID.Zero) return;
            lock (sync)
            {
                if (!sessions.TryGetValue(sessionId, out var info))
                {
                    info = new IMSessionInfo(sessionId);
                    sessions[sessionId] = info;
                    try { SessionOpened?.Invoke(this, new IMSessionEventArgs(info)); } catch { }
                }
                info.TypingSinceUtc[agentId] = DateTime.UtcNow;
                info.LastActivityUtc = DateTime.UtcNow;
            }

            try { TypingStarted?.Invoke(this, new IMTypingEventArgs(sessionId, agentId)); } catch { }

            // schedule a clear after TypingClearTimeout
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep((int)TypingClearTimeout.TotalMilliseconds);
                lock (sync)
                {
                    if (sessions.TryGetValue(sessionId, out var info2))
                    {
                        if (info2.TypingSinceUtc.TryGetValue(agentId, out var since) && DateTime.UtcNow - since >= TypingClearTimeout)
                        {
                            info2.TypingSinceUtc.Remove(agentId);
                            try { TypingStopped?.Invoke(this, new IMTypingEventArgs(sessionId, agentId)); } catch { }
                        }
                    }
                }
            });
        }

        private void OnTypingStopped(UUID sessionId, UUID agentId)
        {
            lock (sync)
            {
                if (sessions.TryGetValue(sessionId, out var info))
                {
                    info.TypingSinceUtc.Remove(agentId);
                    info.LastActivityUtc = DateTime.UtcNow;
                }
            }

            try { TypingStopped?.Invoke(this, new IMTypingEventArgs(sessionId, agentId)); } catch { }
        }

        private void CleanupCallback(object state)
        {
            List<IMSessionInfo> toClose = new List<IMSessionInfo>();
            lock (sync)
            {
                var cutoff = DateTime.UtcNow - SessionTimeout;
                foreach (var kv in sessions.ToList())
                {
                    if (kv.Value.LastActivityUtc < cutoff)
                    {
                        sessions.Remove(kv.Key);
                        toClose.Add(kv.Value);
                    }
                    else
                    {
                        // also clear stale typing entries
                        var stale = kv.Value.TypingSinceUtc.Where(t => DateTime.UtcNow - t.Value >= TypingClearTimeout).Select(t => t.Key).ToList();
                        foreach (var a in stale) kv.Value.TypingSinceUtc.Remove(a);
                    }
                }
            }

            foreach (var s in toClose)
            {
                try { SessionClosed?.Invoke(this, new IMSessionEventArgs(s)); } catch { }
            }
        }

        public void CloseSession(UUID sessionId)
        {
            IMSessionInfo info = null;
            lock (sync)
            {
                if (sessions.TryGetValue(sessionId, out info))
                {
                    sessions.Remove(sessionId);
                }
            }

            if (info != null)
            {
                try { SessionClosed?.Invoke(this, new IMSessionEventArgs(info)); } catch { }
            }
        }

        // IMSessionManager.Dispose()
        public void Dispose()
        {
            cleanupTimer?.Dispose();

            var netCom = instance?.NetCom;
            if (netCom != null)
            {
                try
                {
                    netCom.InstantMessageReceived -= NetCom_InstantMessageReceived;
                    netCom.InstantMessageSent -= NetCom_InstantMessageSent;
                }
                catch { }
            }

            lock (sync)
            {
                sessions.Clear();
            }
        }
    }
}
