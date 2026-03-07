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

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    public partial class MapConsole : UserControl
    {
        private readonly RadegastInstanceForms instance;
        private GridClient client => instance.Client;
        private bool Active => client.Network.Connected;
        private WebBrowser map;
        private MapControl mapCtrl;
        private bool InTeleport = false;
        private bool mapCreated = false;
        private readonly Dictionary<string, ulong> regionHandles = new Dictionary<string, ulong>();

        public MapConsole(RadegastInstanceForms inst)
        {
            InitializeComponent();
            Disposed += frmMap_Disposed;

            instance = inst;
            instance.ClientChanged += instance_ClientChanged;

            Visible = false;
            VisibleChanged += MapConsole_VisibleChanged;

            // Register callbacks
            RegisterClientEvents(client);

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void RegisterClientEvents(GridClient gridClient)
        {
            gridClient.Grid.GridRegion += Grid_GridRegion;
            gridClient.Self.TeleportProgress += Self_TeleportProgress;
            gridClient.Network.SimChanged += Network_SimChanged;
            gridClient.Friends.FriendFoundReply += Friends_FriendFoundReply;
        }

        private void UnregisterClientEvents(GridClient gridClient)
        {
            gridClient.Grid.GridRegion -= Grid_GridRegion;
            gridClient.Self.TeleportProgress -= Self_TeleportProgress;
            gridClient.Network.SimChanged -= Network_SimChanged;
            gridClient.Friends.FriendFoundReply -= Friends_FriendFoundReply;
        }

        private void instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            UnregisterClientEvents(e.OldClient);
            RegisterClientEvents(client);
        }

        private void createMap()
        {
            if (map == null)
            {
                mapCtrl = new MapControl(instance);
                mapCtrl.MapTargetChanged += (sender, e) =>
                {
                    txtRegion.Text = e.Region.Name;
                    nudX.Value = e.LocalX;
                    nudY.Value = e.LocalY;
                    lblStatus.Text = $"Ready for {e.Region.Name}";
                };

                mapCtrl.ZoomChanged += MapCtrlZoomChanged;

                if (instance.NetCom.Grid.ID == "agni")
                {
                    mapCtrl.UseExternalTiles = true;
                }
                mapCtrl.Dock = DockStyle.Fill;
                pnlMap.Controls.Add(mapCtrl);
                MapCtrlZoomChanged(null, null);
                zoomTracker.Visible = true;
            }

        }

        private void MapCtrlZoomChanged(object sender, EventArgs e)
        {
            int newval = (int)(100f * (mapCtrl.Zoom - mapCtrl.MinZoom) / (mapCtrl.MaxZoom - mapCtrl.MinZoom));
            if (newval >= zoomTracker.Minimum && newval <= zoomTracker.Maximum)
                zoomTracker.Value = newval;
        }

        private void map_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            map.DocumentCompleted -= map_DocumentCompleted;
            map.AllowWebBrowserDrop = false;
            map.WebBrowserShortcutsEnabled = false;
            map.ScriptErrorsSuppressed = true;
            map.ObjectForScripting = this;
            map.AllowNavigation = false;

            if (instance.MonoRuntime)
            {
                map.Navigating += map_Navigating;
            }

            ThreadPool.QueueUserWorkItem(sync =>
                {
                    Thread.Sleep(1000);
                    if (InvokeRequired && (!instance.MonoRuntime || IsHandleCreated))
                        BeginInvoke(new MethodInvoker(() =>
                            {
                                if (savedRegion != null)
                                {
                                    _ = gotoRegion(savedRegion, savedX, savedY);
                                }
                                else if (Active)
                                {
                                    _ = gotoRegion(client.Network.CurrentSim.Name, 128, 128);
                                }
                            }
                    ));
                }
            );
        }

        private void frmMap_Disposed(object sender, EventArgs e)
        {
            // Unregister callbacks
            UnregisterClientEvents(client);
            instance.ClientChanged -= instance_ClientChanged;

            if (map != null)
            {
                if (instance.MonoRuntime)
                {
                    map.Navigating -= map_Navigating;
                }
                else
                {
                    map.Dispose();
                }
                map = null;
            }

            if (mapCtrl != null)
            {
                mapCtrl.Dispose();
                mapCtrl = null;
            }
        }

        #region PublicMethods
        public void GoHome()
        {
            btnGoHome_Click(this, EventArgs.Empty);
        }

        public void DoNavigate(string region, string x, string y)
        {
            DisplayLocation(region, int.Parse(x), int.Parse(y), 0);
        }

        public void DisplayLocation(string region, int x, int y, int z)
        {
            txtRegion.Text = region;
            
            try
            {
                nudX.Value = x;
                nudY.Value = y;
                nudZ.Value = z;
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed setting map position controls", ex, instance.Client);
            }

            _ = gotoRegion(region, x, y);
            btnTeleport.Enabled = true;
            btnTeleport.Focus();
            lblStatus.Text = $"Ready for {region}";
        }

        public void SetStatus(string msg)
        {
            lblStatus.Text = msg;
            btnTeleport.Enabled = false;
        }

        public void CenterOnGlobalPos(float gx, float gy, float z)
        {
            txtRegion.Text = string.Empty;

            nudX.Value = (int)gx % 256;
            nudY.Value = (int)gy % 256;
            nudZ.Value = (int)z;

            uint rx = (uint)(gx / 256);
            uint ry = (uint)(gy / 256);

            ulong hndle = Utils.UIntsToLong(rx * 256, ry * 256);
            targetRegionHandle = hndle;

            foreach (KeyValuePair<string, ulong> kvp in regionHandles)
            {
                if (kvp.Value == hndle)
                {
                    txtRegion.Text = kvp.Key;
                    btnTeleport.Enabled = true;
                }
            }
            mapCtrl.CenterMap(rx, ry, (uint)gx % 256, (uint)gy % 256, true);
        }

        #endregion

        #region NetworkEvents

        private void Network_SimChanged(object sender, SimChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Network_SimChanged(sender, e)));
                return;
            }

            _ = gotoRegion(client.Network.CurrentSim.Name, (int)client.Self.SimPosition.X, (int)client.Self.SimPosition.Y);
            lblStatus.Text = $"Now in {client.Network.CurrentSim.Name}";
        }

        private int lastTick = 0;

        private void Self_TeleportProgress(object sender, TeleportEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Self_TeleportProgress(sender, e)));
                return;
            }

            switch (e.Status)
            {
                case TeleportStatus.Progress:
                    lblStatus.Text = "Progress: " + e.Message;
                    InTeleport = true;
                    break;

                case TeleportStatus.Start:
                    lblStatus.Text = "Teleporting to " + txtRegion.Text;
                    InTeleport = true;
                    break;

                case TeleportStatus.Cancelled:
                case TeleportStatus.Failed:
                    InTeleport = false;
                    lblStatus.Text = "Failed: " + e.Message;
                    if (Environment.TickCount - lastTick > 500)
                        instance.ShowNotificationInChat("Teleport failed");
                    lastTick = Environment.TickCount;
                    break;

                case TeleportStatus.Finished:
                    lblStatus.Text = "Teleport complete";
                    if (Environment.TickCount - lastTick > 500)
                        instance.ShowNotificationInChat("Teleport complete");
                    lastTick = Environment.TickCount;
                    InTeleport = false;
                    Network_SimChanged(null, null);
                    mapCtrl?.ClearTarget();
                    break;

                default:
                    InTeleport = false;
                    break;
            }

            if (!InTeleport)
            {
                prgTeleport.Style = ProgressBarStyle.Blocks;
            }
        }

        private void Grid_GridRegion(object sender, GridRegionEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Grid_GridRegion(sender, e)));
                return;
            }

            if (e.Region.RegionHandle == targetRegionHandle)
            {
                txtRegion.Text = e.Region.Name;
                btnTeleport.Enabled = true;
                targetRegionHandle = 0;
            }

            if (!string.IsNullOrEmpty(txtRegion.Text)
                && e.Region.Name.ToLower().Contains(txtRegion.Text.ToLower())
                && !lstRegions.Items.ContainsKey(e.Region.Name))
            {
                ListViewItem item = new ListViewItem(e.Region.Name) {Tag = e.Region, Name = e.Region.Name};
                lstRegions.Items.Add(item);
            }

            regionHandles[e.Region.Name] = Utils.UIntsToLong((uint)e.Region.X, (uint)e.Region.Y);

        }
        #endregion NetworkEvents

        private void map_Navigating(object sender, WebBrowserNavigatingEventArgs e)
        {
            e.Cancel = true;
            Regex r = new Regex(@"^(http://slurl.com/secondlife/|secondlife://)([^/]+)/(\d+)/(\d+)(/(\d+))?");
            Match m = r.Match(e.Url.ToString());

            if (m.Groups.Count > 3)
            {
                txtRegion.Text = m.Groups[2].Value;
                try
                {
                    nudX.Value = int.Parse(m.Groups[3].Value);
                    nudY.Value = int.Parse(m.Groups[4].Value);
                    nudZ.Value = 0;
                }
                catch (Exception ex)
                {
                    Logger.Warn("Failed setting map position controls", ex, instance.Client);
                }

                if (m.Groups.Count > 5 && m.Groups[6].Value != string.Empty)
                {
                    nudZ.Value = int.Parse(m.Groups[6].Value);
                }
                BeginInvoke(new MethodInvoker(DoTeleport));
            }
        }

        private void DoSearch()
        {
            if (!Active || txtRegion.Text.Length < 2) return;
            lstRegions.Clear();
            client.Grid.RequestMapRegion(txtRegion.Text, GridLayerType.Objects);

        }

        public void DoTeleport()
        {
            if (!Active) return;

            if (instance.MonoRuntime)
            {
                map?.Navigate(Path.GetDirectoryName(Application.ExecutablePath) + @"/worldmap.html");
            }

            lblStatus.Text = $"Teleporting to {txtRegion.Text}";
            prgTeleport.Style = ProgressBarStyle.Marquee;

            ThreadPool.QueueUserWorkItem(state =>
                {
                    if (!client.Self.Teleport(txtRegion.Text, new Vector3((int)nudX.Value, (int)nudY.Value, (int)nudZ.Value)))
                    {
                        Self_TeleportProgress(this, new TeleportEventArgs(string.Empty, TeleportStatus.Failed, TeleportFlags.Default));
                    }
                    InTeleport = false;
                }
            );
        }

        #region JavascriptHooks

        private string savedRegion = null;
        private int savedX, savedY;

        private async Task gotoRegion(string regionName, int simX, int simY)
        {
            savedRegion = regionName;
            savedX = simX;
            savedY = simY;

            if (!Visible) return;

            if (mapCtrl != null)
            {
                bool hasHandle;
                ulong handleValue = 0;
                lock (regionHandles)
                {
                    hasHandle = regionHandles.TryGetValue(regionName, out handleValue);
                }

                if (!hasHandle)
                {
                    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                    EventHandler<GridRegionEventArgs> handler = (sender, e) =>
                    {
                        lock (regionHandles)
                        {
                            regionHandles[e.Region.Name] = Utils.UIntsToLong((uint)e.Region.X, (uint)e.Region.Y);
                        }
                        if (e.Region.Name == regionName)
                        {
                            tcs.TrySetResult(true);
                        }
                    };

                    client.Grid.GridRegion += handler;
                    CancellationTokenSource cts = new CancellationTokenSource();
                    try
                    {
                        client.Grid.RequestMapRegion(regionName, GridLayerType.Objects);

                        // Wait for either the handler to signal or the timeout
                        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30), cts.Token)).ConfigureAwait(false);
                        if (completed == tcs.Task)
                        {
                            // Successful: get the handle and center map on UI thread
                            lock (regionHandles)
                            {
                                if (regionHandles.TryGetValue(regionName, out handleValue))
                                {
                                    if (InvokeRequired)
                                        BeginInvoke(new MethodInvoker(() => mapCtrl.CenterMap(handleValue, (uint)simX, (uint)simY, true)));
                                    else
                                        mapCtrl.CenterMap(handleValue, (uint)simX, (uint)simY, true);
                                }
                            }
                        }
                        else
                        {
                            // Timeout
                            Logger.Warn($"Map Console timed out waiting for region handle for '{regionName}'", instance.Client);
                            try
                            {
                                if (InvokeRequired)
                                    BeginInvoke(new MethodInvoker(() 
                                        => instance.ShowNotificationInChat($"Timed out looking up region {regionName}")));
                                else
                                    instance.ShowNotificationInChat($"Timed out looking up region {regionName}");
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Exception while requesting map region {regionName}", ex, instance.Client);
                    }
                    finally
                    {
                        client.Grid.GridRegion -= handler;
                        cts.Cancel();
                        cts.Dispose();
                    }

                    return;
                }

                mapCtrl.CenterMap(handleValue, (uint)simX, (uint)simY, true);
                return;
            }

            if (map == null || map.Document == null) return;

            if (instance.MonoRuntime)
            {
                map.Document.InvokeScript($"gReg = \"{regionName}\"; gSimX = {simX}; gSimY = {simY}; monosucks");
            }
            else
            {
                map.Document.InvokeScript("gotoRegion", new object[] { regionName, simX, simY });
            }
        }

        #endregion

        #region GUIEvents

        private void txtRegion_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                txtRegion.Text = txtRegion.Text.Trim();
                e.SuppressKeyPress = true;
                DoSearch();
            }
        }

        private void lstRegions_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstRegions.SelectedItems.Count != 1)
            {
                btnTeleport.Enabled = false;
                return;
            }
            btnTeleport.Enabled = true;
            txtRegion.Text = lstRegions.SelectedItems[0].Text;
            lblStatus.Text = "Ready for " + txtRegion.Text;
            nudX.Value = 128;
            nudY.Value = 128;
            _ = gotoRegion(txtRegion.Text, (int)nudX.Value, (int)nudY.Value);
        }

        private void lstRegions_Enter(object sender, EventArgs e)
        {
            lstRegions_SelectedIndexChanged(sender, e);
        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            DoSearch();
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            Hide();
        }

        private void btnTeleport_Click(object sender, EventArgs e)
        {
            DoTeleport();
        }

        private void btnGoHome_Click(object sender, EventArgs e)
        {
            if (!Active) return;
            InTeleport = true;

            prgTeleport.Style = ProgressBarStyle.Marquee;
            lblStatus.Text = "Teleporting home...";

            client.Self.RequestTeleport(UUID.Zero);
        }

        private void btnMyPos_Click(object sender, EventArgs e)
        {
            _ = gotoRegion(client.Network.CurrentSim.Name, (int)client.Self.SimPosition.X, (int)client.Self.SimPosition.Y);
        }

        private void btnDestination_Click(object sender, EventArgs e)
        {
            if (txtRegion.Text != string.Empty)
            {
                _ = gotoRegion(txtRegion.Text, (int)nudX.Value, (int)nudY.Value);
            }
        }

        private void MapConsole_VisibleChanged(object sender, EventArgs e)
        {
            if (Visible)
            {
                ddOnlineFriends.Items.Clear();
                ddOnlineFriends.Items.Add("Online Friends");
                ddOnlineFriends.SelectedIndex = 0;

                var friends = (from friend in client.Friends.FriendList 
                    where friend.Value != null && friend.Value.CanSeeMeOnMap && friend.Value.IsOnline 
                    select friend.Value).ToList();

                foreach (var f in friends.Where(f => f.Name != null))
                {
                    ddOnlineFriends.Items.Add(f.Name);
                }
            }

            if (!mapCreated && Visible)
            {
                createMap();
                mapCreated = true;
            }
            else if (Visible && instance.MonoRuntime && savedRegion != null)
            {
                _ =gotoRegion(savedRegion, savedX, savedY);
            }
        }

        private void zoomTracker_Scroll(object sender, EventArgs e)
        {
            if (mapCtrl != null)
            {
                mapCtrl.Zoom = mapCtrl.MinZoom + (mapCtrl.MaxZoom - mapCtrl.MinZoom) * ((float)zoomTracker.Value / 100f);
            }
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            mapCtrl?.RefreshRegionAgents();
        }

        #endregion GUIEvents

        #region Map friends

        private FriendInfo mapFriend = null;
        private ulong targetRegionHandle = 0;

        private void Friends_FriendFoundReply(object sender, FriendFoundReplyEventArgs e)
        {
            if (mapFriend == null || mapFriend.UUID != e.AgentID) return;

            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                {
                    BeginInvoke(new MethodInvoker(() => Friends_FriendFoundReply(sender, e)));
                }
                return;
            }

            txtRegion.Text = string.Empty;
            nudX.Value = (int)e.Location.X;
            nudY.Value = (int)e.Location.Y;
            nudZ.Value = (int)e.Location.Z;
            targetRegionHandle = e.RegionHandle;
            uint x, y;
            Utils.LongToUInts(e.RegionHandle, out x, out y);
            x /= 256;
            y /= 256;
            ulong hndle = Utils.UIntsToLong(x, y);
            foreach (var kvp in regionHandles.Where(kvp => kvp.Value == hndle))
            {
                txtRegion.Text = kvp.Key;
                btnTeleport.Enabled = true;
            }
            mapCtrl.CenterMap(x, y, (uint)e.Location.X, (uint)e.Location.Y, true);
        }

        private void ddOnlineFriends_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ddOnlineFriends.SelectedIndex < 1) { return; }

            foreach (var friend in client.Friends.FriendList)
            {
                if (string.Equals(friend.Value?.Name, ddOnlineFriends.SelectedItem.ToString(), StringComparison.InvariantCulture))
                {
                    mapFriend = friend.Value;
                    break;
                }
            }
            if (mapFriend != null)
            {
                targetRegionHandle = 0;
                client.Friends.MapFriend(mapFriend.UUID);
            }
        }
        #endregion Map friends
    }
}
