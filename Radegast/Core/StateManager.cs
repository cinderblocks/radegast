/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2020, Sjofn, LLC
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
using System.Timers;
using System.Threading;

using OpenMetaverse;

using Radegast.Automation;
using Radegast;

namespace Radegast
{
    public class KnownHeading
    {
        public string ID { get; set; }
        public string Name { get; set; }
        public Quaternion Heading { get; set; }

        public KnownHeading(string id, string name, Quaternion heading)
        {
            ID = id;
            Name = name;
            Heading = heading;
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public class StateManager : IDisposable
    {
        public Parcel Parcel { get; set; }

        private RadegastInstance instance;
        private GridClient Client => instance.Client;
        private Radegast.Netcom Netcom => instance.Netcom;

        private bool away = false;
        private bool flying = false;
        private bool alwaysrun = false;
        private bool sitting = false;

        private UUID followID;
        private bool displayEndWalk = false;

        internal static Random rnd = new Random();
        private System.Threading.Timer lookAtTimer;

        public float FOVVerticalAngle = Utils.TWO_PI - 0.05f;

        /// <summary>
        /// Passes walk state
        /// </summary>
        /// <param name="walking">True if we are walking towards a targer</param>
        public delegate void WalkStateCanged(bool walking);

        /// <summary>
        /// Fires when we start or stop walking towards a target
        /// </summary>
        public event WalkStateCanged OnWalkStateCanged;

        /// <summary>
        /// Fires when avatar stands
        /// </summary>
        public event EventHandler<SitEventArgs> SitStateChanged;

        static List<KnownHeading> m_Headings;
        public static List<KnownHeading> KnownHeadings => m_Headings ?? (m_Headings = new List<KnownHeading>(16)
        {
            new KnownHeading("E", "East", new Quaternion(0.00000f, 0.00000f, 0.00000f, 1.00000f)),
            new KnownHeading("ENE", "East by Northeast",
                new Quaternion(0.00000f, 0.00000f, 0.19509f, 0.98079f)),
            new KnownHeading("NE", "Northeast", new Quaternion(0.00000f, 0.00000f, 0.38268f, 0.92388f)),
            new KnownHeading("NNE", "North by Northeast",
                new Quaternion(0.00000f, 0.00000f, 0.55557f, 0.83147f)),
            new KnownHeading("N", "North", new Quaternion(0.00000f, 0.00000f, 0.70711f, 0.70711f)),
            new KnownHeading("NNW", "North by Northwest",
                new Quaternion(0.00000f, 0.00000f, 0.83147f, 0.55557f)),
            new KnownHeading("NW", "Nortwest", new Quaternion(0.00000f, 0.00000f, 0.92388f, 0.38268f)),
            new KnownHeading("WNW", "West by Northwest",
                new Quaternion(0.00000f, 0.00000f, 0.98079f, 0.19509f)),
            new KnownHeading("W", "West", new Quaternion(0.00000f, 0.00000f, 1.00000f, -0.00000f)),
            new KnownHeading("WSW", "West by Southwest",
                new Quaternion(0.00000f, 0.00000f, 0.98078f, -0.19509f)),
            new KnownHeading("SW", "Southwest", new Quaternion(0.00000f, 0.00000f, 0.92388f, -0.38268f)),
            new KnownHeading("SSW", "South by Southwest",
                new Quaternion(0.00000f, 0.00000f, 0.83147f, -0.55557f)),
            new KnownHeading("S", "South", new Quaternion(0.00000f, 0.00000f, 0.70711f, -0.70711f)),
            new KnownHeading("SSE", "South by Southeast",
                new Quaternion(0.00000f, 0.00000f, 0.55557f, -0.83147f)),
            new KnownHeading("SE", "Southeast", new Quaternion(0.00000f, 0.00000f, 0.38268f, -0.92388f)),
            new KnownHeading("ESE", "East by Southeast",
                new Quaternion(0.00000f, 0.00000f, 0.19509f, -0.98078f))
        });

        public static Vector3 RotToEuler(Quaternion r)
        {
            Quaternion t = new Quaternion(r.X * r.X, r.Y * r.Y, r.Z * r.Z, r.W * r.W);

            float m = (t.X + t.Y + t.Z + t.W);
            if (Math.Abs(m) < 0.001) return Vector3.Zero;
            float n = 2 * (r.Y * r.W + r.X * r.Z);
            float p = m * m - n * n;

            if (p > 0)
                return new Vector3(
                    (float)Math.Atan2(2.0 * (r.X * r.W - r.Y * r.Z), (-t.X - t.Y + t.Z + t.W)),
                    (float)Math.Atan2(n, Math.Sqrt(p)),
                    (float)Math.Atan2(2.0 * (r.Z * r.W - r.X * r.Y), t.X - t.Y - t.Z + t.W)
                    );
            else if (n > 0)
                return new Vector3(
                    0f,
                    (float)(Math.PI / 2d),
                    (float)Math.Atan2((r.Z * r.W + r.X * r.Y), 0.5 - t.X - t.Y)
                    );
            else
                return new Vector3(
                    0f,
                    -(float)(Math.PI / 2d),
                    (float)Math.Atan2((r.Z * r.W + r.X * r.Y), 0.5 - t.X - t.Z)
                    );
        }

        public static KnownHeading ClosestKnownHeading(int degrees)
        {
            KnownHeading ret = KnownHeadings[0];
            int facing = (int)(57.2957795d * RotToEuler(KnownHeadings[0].Heading).Z);
            if (facing < 0) facing += 360;
            int minDistance = Math.Abs(degrees - facing);

            for (int i = 1; i < KnownHeadings.Count; i++)
            {
                facing = (int)(57.2957795d * RotToEuler(KnownHeadings[i].Heading).Z);
                if (facing < 0) facing += 360;

                int distance = Math.Abs(degrees - facing);
                if (distance < minDistance)
                {
                    ret = KnownHeadings[i];
                    minDistance = distance;
                }
            }

            return ret;
        }

        public Dictionary<UUID, string> KnownAnimations;
        public bool CameraTracksOwnAvatar = true;
        public Vector3 DefaultCameraOffset = new Vector3(-5, 0, 0);

        public StateManager(RadegastInstance instance)
        {
            this.instance = instance;
            this.instance.ClientChanged += Instance_ClientChanged;
            KnownAnimations = Animations.ToDictionary();
            AutoSit = new AutoSit(this.instance);
            PseudoHome = new PseudoHome(this.instance);
            LSLHelper = new LSLHelper(this.instance);

            beamTimer = new System.Timers.Timer {Enabled = false};
            beamTimer.Elapsed += BeamTimer_Elapsed;

            // Callbacks
            Netcom.ClientConnected += Netcom_ClientConnected;
            Netcom.ClientDisconnected += Netcom_ClientDisconnected;
            Netcom.ChatReceived += Netcom_ChatReceived;
            RegisterClientEvents(Client);
        }


        private void RegisterClientEvents(GridClient client)
        {
            client.Objects.AvatarUpdate += Objects_AvatarUpdate;
            client.Objects.TerseObjectUpdate += Objects_TerseObjectUpdate;
            client.Objects.AvatarSitChanged += Objects_AvatarSitChanged;
            client.Self.AlertMessage += Self_AlertMessage;
            client.Self.TeleportProgress += Self_TeleportProgress;
            client.Network.EventQueueRunning += Network_EventQueueRunning;
            client.Network.SimChanged += Network_SimChanged;
        }

        private void UnregisterClientEvents(GridClient client)
        {
            client.Objects.AvatarUpdate -= Objects_AvatarUpdate;
            client.Objects.TerseObjectUpdate -= Objects_TerseObjectUpdate;
            client.Objects.AvatarSitChanged -= Objects_AvatarSitChanged;
            client.Self.AlertMessage -= Self_AlertMessage;
            client.Self.TeleportProgress -= Self_TeleportProgress;
            client.Network.EventQueueRunning -= Network_EventQueueRunning;
            client.Network.SimChanged -= Network_SimChanged;
        }

        public void Dispose()
        {
            Netcom.ClientConnected -= Netcom_ClientConnected;
            Netcom.ClientDisconnected -= Netcom_ClientDisconnected;
            Netcom.ChatReceived -= Netcom_ChatReceived;
            UnregisterClientEvents(Client);
            beamTimer.Dispose();
            beamTimer = null;

            if (lookAtTimer != null)
            {
                lookAtTimer.Dispose();
                lookAtTimer = null;
            }

            if (walkTimer != null)
            {
                walkTimer.Dispose();
                walkTimer = null;
            }

            if (AutoSit != null)
            {
                AutoSit.Dispose();
                AutoSit = null;
            }

            if (LSLHelper != null)
            {
                LSLHelper.Dispose();
                LSLHelper = null;
            }
        }

        void Instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            UnregisterClientEvents(e.OldClient);
            RegisterClientEvents(Client);
        }

        void Objects_AvatarSitChanged(object sender, AvatarSitChangedEventArgs e)
        {
            if (e.Avatar.LocalID != Client.Self.LocalID) return;

            sitting = e.SittingOn != 0;

            if (Client.Self.SittingOn != 0 && !Client.Network.CurrentSim.ObjectsPrimitives.ContainsKey(Client.Self.SittingOn))
            {
                Client.Objects.RequestObject(Client.Network.CurrentSim, Client.Self.SittingOn);
            }

            SitStateChanged?.Invoke(this, new SitEventArgs(sitting));
        }

        /// <summary>
        /// Locates avatar in the current sim, or adjacents sims
        /// </summary>
        /// <param name="person">Avatar UUID</param>
        /// <param name="position">Position within sim</param>
        /// <returns>True if managed to find the avatar</returns>
        public bool TryFindAvatar(UUID person, out Vector3 position)
        {
            Simulator sim;
            if (!TryFindAvatar(person, out sim, out position)) return false;
            // same sim?
            if (sim == Client.Network.CurrentSim) return true;
            position = ToLocalPosition(sim.Handle, position);
            return true;
        }

        public Vector3 ToLocalPosition(ulong handle, Vector3 position)
        {
            Vector3d diff = ToVector3D(handle, position) - Client.Self.GlobalPosition;
            position = new Vector3((float) diff.X, (float) diff.Y, (float) diff.Z) - position;
            return position;
        }

        public static Vector3d ToVector3D(ulong handle, Vector3 pos)
        {
            uint globalX, globalY;
            Utils.LongToUInts(handle, out globalX, out globalY);

            return new Vector3d(
                (double)globalX + (double)pos.X,
                (double)globalY + (double)pos.Y,
                (double)pos.Z);
        }

        /// <summary>
        /// Locates avatar in the current sim, or adjacents sims
        /// </summary>
        /// <param name="person">Avatar UUID</param>
        /// <param name="sim">Simulator avatar is in</param>
        /// <param name="position">Position within sim</param>
        /// <returns>True if managed to find the avatar</returns>
        public bool TryFindAvatar(UUID person, out Simulator sim, out Vector3 position)
        {
            return TryFindPrim(person, out sim, out position, true);
        }

        public bool TryFindPrim(UUID person, out Simulator sim, out Vector3 position, bool onlyAvatars)
        {
            Simulator[] Simulators = null;
            lock (Client.Network.Simulators)
            {
                Simulators = Client.Network.Simulators.ToArray();
            }
            sim = null;
            position = Vector3.Zero;

            Primitive avi = null;
            // First try the object tracker
            foreach (var s in Simulators)
            {
                var kvp = s.ObjectsAvatars.FirstOrDefault(av => av.Value.ID == person);
                if (kvp.Value != null)
                {
                    avi = kvp.Value;
                    sim = s;
                    break;
                }
            }
            if (avi == null && !onlyAvatars)
            {
                foreach (var s in Simulators)
                {
                    var kvp = s.ObjectsPrimitives.FirstOrDefault(av => av.Value.ID == person);
                    if (kvp.Value != null)
                    {
                        avi = kvp.Value;
                        sim = s;
                        break;
                    }
                }
            }
            if (avi != null)
            {
                if (avi.ParentID == 0)
                {
                    position = avi.Position;
                }
                else
                {
                    if (sim.ObjectsPrimitives.TryGetValue(avi.ParentID, out var seat))
                    {
                        position = seat.Position + avi.Position * seat.Rotation;
                    }
                }
            }
            else
            {
                foreach (var s in Simulators)
                {
                    if (s.AvatarPositions.TryGetValue(person, out var avatarPosition))
                    {
                        position = avatarPosition;
                        sim = s;
                        break;
                    }
                }
            }

            return position.Z > 0.1f;
        }

        public bool TryLocatePrim(Primitive avi, out Simulator sim, out Vector3 position)
        {
            Simulator[] Simulators = null;
            lock (Client.Network.Simulators)
            {
                Simulators = Client.Network.Simulators.ToArray();
            }

            sim = Client.Network.CurrentSim;
            position = Vector3.Zero;
            {
                foreach (var s in Simulators)
                {
                    if (s.Handle == avi.RegionHandle)
                    {
                        sim = s;
                        break;
                    }
                }
            }
            if (avi != null)
            {
                if (avi.ParentID == 0)
                {
                    position = avi.Position;
                }
                else
                {
                    Primitive seat;
                    if (sim.ObjectsPrimitives.TryGetValue(avi.ParentID, out seat))
                    {
                        position = seat.Position + avi.Position*seat.Rotation;
                    }
                }
            }
            return position.Z > 0.1f;
        }

        /// <summary>
        /// Move to target position either by walking or by teleporting
        /// </summary>
        /// <param name="target">Sim local position of the target</param>
        /// <param name="useTP">Move using teleport</param>
        public void MoveTo(Vector3 target, bool useTP)
        {
            MoveTo(Client.Network.CurrentSim, target, useTP);
        }

        /// <summary>
        /// Move to target position either by walking or by teleporting
        /// </summary>
        /// <param name="sim">Simulator in which the target is</param>
        /// <param name="target">Sim local position of the target</param>
        /// <param name="useTP">Move using teleport</param>
        public void MoveTo(Simulator sim, Vector3 target, bool useTP)
        {
            SetSitting(false, UUID.Zero);

            if (useTP)
            {
                Client.Self.RequestTeleport(sim.Handle, target);
            }
            else
            {
                displayEndWalk = true;
                Client.Self.Movement.TurnToward(target);
                WalkTo(GlobalPosition(sim, target));
            }
        }


        public void SetRandomHeading()
        {
            Client.Self.Movement.UpdateFromHeading(Utils.TWO_PI * rnd.NextDouble(), true);
            LookInFront();
        }

        void Network_EventQueueRunning(object sender, EventQueueRunningEventArgs e)
        {
            if (e.Simulator == Client.Network.CurrentSim)
            {
                SetRandomHeading();
            }
        }

        void Network_SimChanged(object sender, SimChangedEventArgs e)
        {
            ThreadPool.QueueUserWorkItem(sync =>
            {
                Thread.Sleep(15 * 1000);
                AutoSit.TrySit();
                PseudoHome.ETGoHome();
            });
            Client.Self.Movement.SetFOVVerticalAngle(FOVVerticalAngle);
        }

        private UUID teleportEffect = UUID.Random();

        void Self_TeleportProgress(object sender, TeleportEventArgs e)
        {
            if (!Client.Network.Connected) return;

            switch (e.Status)
            {
                case TeleportStatus.Progress:
                    instance.MediaManager.PlayUISound(UISounds.Teleport);
                    Client.Self.SphereEffect(Client.Self.GlobalPosition, Color4.White, 4f, teleportEffect);
                    break;
                case TeleportStatus.Finished:
                    Client.Self.SphereEffect(Vector3d.Zero, Color4.White, 0f, teleportEffect);
                    SetRandomHeading();
                    break;
                case TeleportStatus.Failed:
                    instance.MediaManager.PlayUISound(UISounds.Error);
                    Client.Self.SphereEffect(Vector3d.Zero, Color4.White, 0f, teleportEffect);
                    break;
            }
        }

        void Netcom_ClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            IsTyping = away = IsBusy = IsWalking = false;

            if (lookAtTimer != null)
            {
                lookAtTimer.Dispose();
                lookAtTimer = null;
            }

        }

        void Netcom_ClientConnected(object sender, EventArgs e)
        {
            if (!instance.GlobalSettings.ContainsKey("draw_distance"))
            {
                instance.GlobalSettings["draw_distance"] = 48;
            }

            Client.Self.Movement.Camera.Far = instance.GlobalSettings["draw_distance"];

            if (lookAtTimer == null)
            {
                lookAtTimer = new System.Threading.Timer(LookAtTimerTick, null, Timeout.Infinite, Timeout.Infinite);
            }
        }

        void Objects_AvatarUpdate(object sender, AvatarUpdateEventArgs e)
        {
            if (e.Avatar.LocalID == Client.Self.LocalID)
            {
                SetDefaultCamera();
            }
        }

        void Objects_TerseObjectUpdate(object sender, TerseObjectUpdateEventArgs e)
        {
            if (!e.Update.Avatar) return;
            
            if (e.Prim.LocalID == Client.Self.LocalID)
            {
                SetDefaultCamera();
            }

            if (!IsFollowing) return;

            Avatar av;
            Client.Network.CurrentSim.ObjectsAvatars.TryGetValue(e.Update.LocalID, out av);
            if (av == null) return;

            if (av.ID == followID)
            {
                Vector3 pos = AvatarPosition(Client.Network.CurrentSim, av);

                FollowUpdate(pos);
            }
        }

        void FollowUpdate(Vector3 pos)
        {
            if (Vector3.Distance(pos, Client.Self.SimPosition) > FollowDistance)
            {
                Vector3 target = pos + Vector3.Normalize(Client.Self.SimPosition - pos) * (FollowDistance - 1f);
                Client.Self.AutoPilotCancel();
                Vector3d glb = GlobalPosition(Client.Network.CurrentSim, target);
                Client.Self.AutoPilot(glb.X, glb.Y, glb.Z);
            }
            else
            {
                Client.Self.AutoPilotCancel();
                Client.Self.Movement.TurnToward(pos);
            }
        }

        public void SetDefaultCamera()
        {
            if (CameraTracksOwnAvatar)
            {
                if (Client.Self.SittingOn != 0 && !Client.Network.CurrentSim.ObjectsPrimitives.ContainsKey(Client.Self.SittingOn))
                {
                    // We are sitting but don't have the information about the object we are sitting on
                    // Sim seems to ignore RequestMutlipleObjects message
                    // client.Objects.RequestObject(client.Network.CurrentSim, client.Self.SittingOn);
                }
                else
                {
                    Vector3 pos = Client.Self.SimPosition + DefaultCameraOffset * Client.Self.Movement.BodyRotation;
                    //Logger.Log("Setting camera position to " + pos.ToString(), Helpers.LogLevel.Debug);
                    Client.Self.Movement.Camera.LookAt(
                        pos, Client.Self.SimPosition
                    );
                }
            }
        }

        public Quaternion AvatarRotation(Simulator sim, UUID avID)
        {
            Quaternion rot = Quaternion.Identity;
            var kvp = sim.ObjectsAvatars.FirstOrDefault(a => a.Value.ID == avID);

            if (kvp.Value == null)
            {
                return rot;
            }

            var av = kvp.Value;
            if (av.ParentID == 0)
            {
                rot = av.Rotation;
            }
            else
            {
                if (sim.ObjectsPrimitives.TryGetValue(av.ParentID, out var prim))
                {
                    rot = prim.Rotation + av.Rotation;
                }
            }

            return rot;
        }


        public Vector3 AvatarPosition(Simulator sim, UUID avID)
        {
            var pos = Vector3.Zero;
            var av = sim.ObjectsAvatars.FirstOrDefault(a => a.Value.ID == avID);
            if (av.Value != null)
            {
                return AvatarPosition(sim, av.Value);
            }

            if (!sim.AvatarPositions.TryGetValue(avID, out var coarse)) { return pos; }
            return coarse.Z > 0.01 ? coarse : pos;
        }

        public Vector3 AvatarPosition(Simulator sim, Avatar av)
        {
            var pos = Vector3.Zero;

            if (av.ParentID == 0)
            {
                pos = av.Position;
            }
            else
            {
                if (sim.ObjectsPrimitives.TryGetValue(av.ParentID, out var prim))
                {
                    pos = prim.Position + av.Position;
                }
            }

            return pos;
        }

        public void Follow(string name, UUID id)
        {
            FollowName = name;
            followID = id;
            IsFollowing = followID != UUID.Zero;

            if (IsFollowing)
            {
                IsWalking = false;

                Vector3 target = AvatarPosition(Client.Network.CurrentSim, id);
                if (Vector3.Zero != target)
                {
                    Client.Self.Movement.TurnToward(target);
                    FollowUpdate(target);
                }

            }
        }

        public void StopFollowing()
        {
            IsFollowing = false;
            FollowName = string.Empty;
            followID = UUID.Zero;
        }

        #region Look at effect
        private int lastLookAtEffect = 0;
        private UUID lookAtEffect = UUID.Random();

        /// <summary>
        /// Set eye focus 3m in front of us
        /// </summary>
        public void LookInFront()
        {
            if (!Client.Network.Connected || instance.GlobalSettings["disable_look_at"]) return;

            Client.Self.LookAtEffect(Client.Self.AgentID, Client.Self.AgentID,
                new Vector3d(new Vector3(3, 0, 0) * Quaternion.Identity),
                LookAtType.Idle, lookAtEffect);
        }

        void LookAtTimerTick(object state)
        {
            LookInFront();
        }

        void Netcom_ChatReceived(object sender, ChatEventArgs e)
        {
            //somehow it can be too early (when Radegast is loaded from running bot)
            if (instance.GlobalSettings==null) return;
            if (!instance.GlobalSettings["disable_look_at"]
                && e.SourceID != Client.Self.AgentID
                && (e.SourceType == ChatSourceType.Agent || e.Type == ChatType.StartTyping))
            {
                // change focus max every 4 seconds
                if (Environment.TickCount - lastLookAtEffect > 4000)
                {
                    lastLookAtEffect = Environment.TickCount;
                    Client.Self.LookAtEffect(Client.Self.AgentID, e.SourceID, Vector3d.Zero, LookAtType.Respond, lookAtEffect);
                    // keep looking at the speaker for 10 seconds
                    lookAtTimer?.Change(10000, Timeout.Infinite);
                }
            }
        }
        #endregion Look at effect

        #region Walking (move to)

        private System.Threading.Timer walkTimer;
        private int walkChekInterval = 500;
        private Vector3d walkToTarget;
        int lastDistance = 0;
        int lastDistanceChanged = 0;

        public void WalkTo(Primitive prim)
        {
            WalkTo(GlobalPosition(prim));
        }
        public double WaitUntilPosition(Vector3d pos, TimeSpan maxWait, double howClose)
        {
             
            DateTime until = DateTime.Now + maxWait;
            while (until > DateTime.Now)
            {
                double dist = Vector3d.Distance(Client.Self.GlobalPosition, pos);
                if (howClose >= dist) return dist;
                Thread.Sleep(250);
            }
            return Vector3d.Distance(Client.Self.GlobalPosition, pos);
            
        }

        public void WalkTo(Vector3d globalPos)
        {
            walkToTarget = globalPos;

            if (IsFollowing)
            {
                IsFollowing = false;
                FollowName = string.Empty;
            }

            if (walkTimer == null)
            {
                walkTimer = new System.Threading.Timer(WalkTimerElapsed, null, walkChekInterval, Timeout.Infinite);
            }

            lastDistanceChanged = Environment.TickCount;
            Client.Self.AutoPilotCancel();
            IsWalking = true;
            Client.Self.AutoPilot(walkToTarget.X, walkToTarget.Y, walkToTarget.Z);
            FireWalkStateCanged();
        }

        void WalkTimerElapsed(object sender)
        {

            double distance = Vector3d.Distance(Client.Self.GlobalPosition, walkToTarget);

            if (distance < 2d)
            {
                // We're there
                EndWalking();
            }
            else
            {
                if (lastDistance != (int)distance)
                {
                    lastDistanceChanged = Environment.TickCount;
                    lastDistance = (int)distance;
                }
                else if ((Environment.TickCount - lastDistanceChanged) > 10000)
                {
                    // Our distance to the target has not changed in 10s, give up
                    EndWalking();
                    return;
                }
                walkTimer?.Change(walkChekInterval, Timeout.Infinite);
            }
        }

        void Self_AlertMessage(object sender, AlertMessageEventArgs e)
        {
            if (e.NotificationId == "AutopilotCanceled")
            {
                if (IsWalking)
                {
                    EndWalking();
                }
            }
        }

        void FireWalkStateCanged()
        {
            if (OnWalkStateCanged != null)
            {
                try { OnWalkStateCanged(IsWalking); }
                catch (Exception) { }
            }
        }

        public void EndWalking()
        {
            if (IsWalking)
            {
                IsWalking = false;
                Logger.Log("Finished walking.", Helpers.LogLevel.Debug, Client);
                walkTimer.Dispose();
                walkTimer = null;
                Client.Self.AutoPilotCancel();
                
                if (displayEndWalk)
                {
                    displayEndWalk = false;
                    string msg = "Finished walking";

                    if (walkToTarget != Vector3d.Zero)
                    {
                        Thread.Sleep(1000);
                        msg += $" {Vector3d.Distance(Client.Self.GlobalPosition, walkToTarget):0} meters from destination";
                        walkToTarget = Vector3d.Zero;
                    }

                    instance.TabConsole.DisplayNotificationInChat(msg);
                }

                FireWalkStateCanged();
            }
        }
        #endregion

        public void SetTyping(bool typing)
        {
            if (!Client.Network.Connected) return;
            var typingAnim = new Dictionary<UUID, bool> {{TypingAnimationID, typing}};
            Client.Self.Animate(typingAnim, false);
            Client.Self.Chat(string.Empty, 0, typing ? ChatType.StartTyping : ChatType.StopTyping);
            IsTyping = typing;
        }

        public void SetAway(bool away)
        {
            var awayAnim = new Dictionary<UUID, bool> {{AwayAnimationID, away}};
            Client.Self.Animate(awayAnim, true);
            if (UseMoveControl) Client.Self.Movement.Away = away;
            this.away = away;
        }

        public void SetBusy(bool busy)
        {
            var busyAnim = new Dictionary<UUID, bool> {{BusyAnimationID, busy}};
            Client.Self.Animate(busyAnim, true);
            IsBusy = busy;
        }

        public void SetFlying(bool fly)
        {
            flying = Client.Self.Movement.Fly = fly;
        }

        public void SetAlwaysRun(bool always_run)
        {
            alwaysrun = Client.Self.Movement.AlwaysRun = always_run;
        }

        public void SetSitting(bool sit, UUID target)
        {
            sitting = sit;

            if (!instance.RLV.RestictionActive("unsit"))
            {
                if (sitting)
                {
                    Client.Self.RequestSit(target, Vector3.Zero);
                    Client.Self.Sit();
                }
                else
                {
                    Client.Self.Stand();
                }
            }
            else
            {
                instance.TabConsole.DisplayNotificationInChat("Unsit prevented by RLV");
                sitting = true;
                return;
            }

            SitStateChanged?.Invoke(this, new SitEventArgs(sitting));

            if (!sitting)
            {
                StopAllAnimations();
            }
        }

        public void StopAllAnimations()
        {
            var stop = new Dictionary<UUID, bool>();

            Client.Self.SignaledAnimations.ForEach(anim =>
            {
                if (!KnownAnimations.ContainsKey(anim))
                {
                    stop.Add(anim, false);
                }
            });

            if (stop.Count > 0)
            {
                Client.Self.Animate(stop, true);
            }
        }

        public static Vector3d GlobalPosition(Simulator sim, Vector3 pos)
        {
            uint globalX, globalY;
            Utils.LongToUInts(sim.Handle, out globalX, out globalY);

            return new Vector3d(
                (double)globalX + (double)pos.X,
                (double)globalY + (double)pos.Y,
                (double)pos.Z);
        }

        public Vector3d GlobalPosition(Primitive prim)
        {
            return GlobalPosition(Client.Network.CurrentSim, prim.Position);
        }

        private System.Timers.Timer beamTimer;
        private List<Vector3d> beamTarget;
        private Random beamRandom = new Random();
        private UUID pointID;
        private UUID sphereID;
        private List<UUID> beamID;
        private int numBeans;
        private Color4[] beamColors = new Color4[] { new Color4(0, 255, 0, 255), new Color4(255, 0, 0, 255), new Color4(0, 0, 255, 255) };
        private Primitive targetPrim;

        public void UnSetPointing()
        {
            beamTimer.Enabled = false;
            if (pointID != UUID.Zero)
            {
                Client.Self.PointAtEffect(Client.Self.AgentID, UUID.Zero, Vector3d.Zero, PointAtType.None, pointID);
                pointID = UUID.Zero;
            }

            if (beamID != null)
            {
                foreach (UUID id in beamID)
                {
                    Client.Self.BeamEffect(UUID.Zero, UUID.Zero, Vector3d.Zero, new Color4(255, 255, 255, 255), 0, id);
                }
                beamID = null;
            }

            if (sphereID != UUID.Zero)
            {
                Client.Self.SphereEffect(Vector3d.Zero, Color4.White, 0, sphereID);
                sphereID = UUID.Zero;
            }

        }

        void BeamTimer_Elapsed(object sender, EventArgs e)
        {
            if (beamID == null) return;

            try
            {
                Client.Self.SphereEffect(GlobalPosition(targetPrim), beamColors[beamRandom.Next(0, 3)], 0.85f, sphereID);
                int i = 0;
                for (i = 0; i < numBeans; i++)
                {
                    Vector3d scatter;

                    if (i == 0)
                    {
                        scatter = GlobalPosition(targetPrim);
                    }
                    else
                    {
                        Vector3d direction = Client.Self.GlobalPosition - GlobalPosition(targetPrim);
                        Vector3d cross = direction % new Vector3d(0, 0, 1);
                        cross.Normalize();
                        scatter = GlobalPosition(targetPrim) + cross * (i * 0.2d) * (i % 2 == 0 ? 1 : -1);
                    }
                    Client.Self.BeamEffect(Client.Self.AgentID, UUID.Zero, scatter, beamColors[beamRandom.Next(0, 3)], 1.0f, beamID[i]);
                }

                for (int j = 1; j < numBeans; j++)
                {
                    Vector3d cross = new Vector3d(0, 0, 1);
                    cross.Normalize();
                    var scatter = GlobalPosition(targetPrim) + cross * (j * 0.2d) * (j % 2 == 0 ? 1 : -1);

                    Client.Self.BeamEffect(Client.Self.AgentID, UUID.Zero, scatter, beamColors[beamRandom.Next(0, 3)], 1.0f, beamID[j + i - 1]);
                }
            }
            catch (Exception) { }

        }

        public void SetPointing(Primitive prim, int num_beans)
        {
            UnSetPointing();
            Client.Self.Movement.TurnToward(prim.Position);
            pointID = UUID.Random();
            sphereID = UUID.Random();
            beamID = new List<UUID>();
            beamTarget = new List<Vector3d>();
            targetPrim = prim;
            numBeans = num_beans;

            Client.Self.PointAtEffect(Client.Self.AgentID, prim.ID, Vector3d.Zero, PointAtType.Select, pointID);

            for (int i = 0; i < numBeans; i++)
            {
                UUID newBeam = UUID.Random();
                beamID.Add(newBeam);
                beamTarget.Add(Vector3d.Zero);
            }

            for (int i = 1; i < numBeans; i++)
            {
                UUID newBeam = UUID.Random();
                beamID.Add(newBeam);
                beamTarget.Add(Vector3d.Zero);
            }

            beamTimer.Interval = 1000;
            beamTimer.Enabled = true;
        }

        public UUID TypingAnimationID { get; set; } = new UUID("c541c47f-e0c0-058b-ad1a-d6ae3a4584d9");
        public UUID AwayAnimationID { get; set; } = new UUID("fd037134-85d4-f241-72c6-4f42164fedee");
        public UUID BusyAnimationID { get; set; } = new UUID("efcf670c2d188128973a034ebc806b67");
        public bool IsTyping { get; private set; } = false;
        public bool IsAway => UseMoveControl ? Client.Self.Movement.Away : away;
        public bool IsBusy { get; private set; } = false;
        public bool IsFlying => Client.Self.Movement.Fly;
        public bool IsSitting
        {
            get
            {
                if (Client.Self.Movement.SitOnGround || Client.Self.SittingOn != 0) return true;
                if (sitting) {
                    Logger.Log("out of sync sitting", Helpers.LogLevel.Debug);
                    sitting = false;
                }
                return false;
            }
        }

        public bool IsPointing => pointID != UUID.Zero;
        public bool IsFollowing { get; private set; } = false;
        public string FollowName { get; set; } = string.Empty;
        public float FollowDistance { get; set; } = 3.0f;
        public bool IsWalking { get; private set; } = false;
        public AutoSit AutoSit { get; private set; }
        public LSLHelper LSLHelper { get; private set; }
        public PseudoHome PseudoHome { get; }

        /// <summary>
        /// Experimental Option that sometimes the Client has more authority than state mananger
        /// </summary>
        public static bool UseMoveControl;
    }

    public class SitEventArgs : EventArgs
    {
        public bool Sitting;

        public SitEventArgs(bool sitting)
        {
            Sitting = sitting;
        }
    }
}
