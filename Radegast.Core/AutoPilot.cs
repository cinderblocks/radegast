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

using System.Collections.Generic;
using OpenMetaverse;
using System.Threading;
using System.Threading.Tasks;

namespace Radegast
{
    public class AutoPilot
    {
        private readonly GridClient Client;
        private readonly List<Vector3> Waypoints = new List<Vector3>();
        private CancellationTokenSource tickerCts;
        private Task tickerTask;

        private Vector3 AgentPosition;
        private int nwp = 0;

        // Cancellation source for the delayed AutoPilotLocal invocation
        private CancellationTokenSource moveDelayCts;

        private int NextWaypoint
        {
            set
            {
                nwp = value == Waypoints.Count ? 0 : value;
                System.Console.WriteLine($"Way point {nwp} {Waypoints[nwp]}");
                Client.Self.AutoPilotCancel();
                Client.Self.Movement.TurnToward(Waypoints[nwp]);

                // Cancel any pending delayed move and start a new one
                try
                {
                    moveDelayCts?.Cancel();
                    moveDelayCts?.Dispose();
                }
                catch { }

                moveDelayCts = new CancellationTokenSource();
                var token = moveDelayCts.Token;

                // Fire-and-forget task to wait without blocking the caller thread
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(500, token);
                        if (token.IsCancellationRequested) return;

                        Client.Self.AutoPilotLocal((int)Waypoints[nwp].X, (int)Waypoints[nwp].Y, (int)Waypoints[nwp].Z);
                    }
                    catch (TaskCanceledException) { }
                    catch { }
                }, token);
            }
            get => nwp;
        }

        public AutoPilot(GridClient client)
        {
            Client = client;
            Client.Objects.TerseObjectUpdate += Objects_TerseObjectUpdate;
        }

        private void Objects_TerseObjectUpdate(object sender, TerseObjectUpdateEventArgs e)
        {
            if (e.Update.Avatar && e.Update.LocalID == Client.Self.LocalID) {
                AgentPosition = e.Update.Position;
                if (Waypoints.Count == 0) { return; }

                if (Vector3.Distance(AgentPosition, Waypoints[NextWaypoint]) < 2f) {
                    NextWaypoint++;
                }
            }
        }

        private void Ticker_Elapsed()
        {
            // Placeholder for periodic work. Keep minimal to preserve previous behavior.
        }

        private async Task TickerLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(500, token);
                    if (token.IsCancellationRequested) break;
                    try { Ticker_Elapsed(); } catch { }
                }
            }
            catch (TaskCanceledException) { }
            catch { }
        }

        public int InsertWaypoint(Vector3 wp)
        {
            Waypoints.Add(wp);
            return Waypoints.Count;
        }

        public void Start()
        {
            if (Waypoints.Count < 2) {
                return;
            }

            //Client.Self.Teleport(Client.Network.CurrentSim.Handle, Waypoints[0]);
            NextWaypoint++;

            if (tickerTask == null || tickerTask.IsCompleted)
            {
                try
                {
                    tickerCts?.Cancel();
                    tickerCts?.Dispose();
                }
                catch { }

                tickerCts = new CancellationTokenSource();
                tickerTask = Task.Run(() => TickerLoop(tickerCts.Token), tickerCts.Token);
            }
        }

        public void Stop()
        {
            // Cancel any pending delayed move and stop autopilot
            try
            {
                moveDelayCts?.Cancel();
                moveDelayCts?.Dispose();
                moveDelayCts = null;
            }
            catch { }

            try
            {
                tickerCts?.Cancel();
                tickerCts?.Dispose();
                tickerCts = null;
                tickerTask = null;
            }
            catch { }

            Client.Self.AutoPilotCancel();
        }
    }
}
