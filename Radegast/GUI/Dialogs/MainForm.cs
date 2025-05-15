/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2021, Sjofn, LLC
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
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Timers;
using System.Threading;

using System.Windows.Forms;
using System.Resources;
using System.IO;
using System.Web;
using Radegast;
using OpenMetaverse;
using NetSparkleUpdater.SignatureVerifiers;

namespace Radegast
{
    public partial class frmMain : RadegastForm
    {
        #region Public members
        public static ImageList ResourceImages = new ImageList();
        public static List<string> ImageNames = new List<string>();
        public bool PreventParcelUpdate = false;
        public delegate void ProfileHandlerDelegate(string agentName, UUID agentID);
        public ProfileHandlerDelegate ShowAgentProfile;

        public TabsConsole TabConsole { get; private set; }

        public MapConsole WorldMap => (MapConsole) MapTab?.Control;

        public RadegastTab MapTab =>
            TabConsole.TabExists("map") 
                ? TabConsole.Tabs["map"] : null;

        public MediaConsole MediaConsole { get; private set; }

        /// <summary>
        /// Drop down that contains the tools menu
        /// </summary>
        public ToolStripDropDownButton ToolsMenu => tbnTools;

        /// <summary>
        /// Dropdown that contains the heelp menu
        /// </summary>
        public ToolStripDropDownButton HelpMenu => tbtnHelp;

        /// <summary>
        /// Drop down that contants the plugins menu. Make sure to set it Visible if
        /// you add items to this menu, it's hidden by default
        /// </summary>
        public ToolStripDropDownButton PluginsMenu => tbnPlugins;

        #endregion

        #region Private members
        private RadegastInstance instance;
        private GridClient client => instance.Client;
        private Radegast.Netcom netcom => instance.Netcom;
        private System.Timers.Timer statusTimer;
        private AutoPilot ap;
        private bool AutoPilotActive = false;
        private TransparentButton btnDialogNextControl;
        private SlUriParser uriParser;
        private NetSparkleUpdater.SparkleUpdater SparkleUpdater;

        #endregion

        #region Constructor and disposal
        public frmMain(RadegastInstance instance)
            : base(instance)
        {
            InitializeComponent();
            Disposed += frmMain_Disposed;

            this.instance = instance;
            this.instance.ClientChanged += instance_ClientChanged;

            netcom.NetcomSync = this;
            ShowAgentProfile = ShowAgentProfileInternal;

            pnlDialog.Visible = false;
            btnDialogNextControl = new TransparentButton();
            pnlDialog.Controls.Add(btnDialogNextControl);
            pnlDialog.Top = 0;

            btnDialogNextControl.Size = new Size(35, 20);
            btnDialogNextControl.BackColor = Color.Transparent;
            btnDialogNextControl.ForeColor = Color.Gold;
            btnDialogNextControl.FlatAppearance.BorderSize = 0;
            btnDialogNextControl.FlatStyle = FlatStyle.Flat;
            btnDialogNextControl.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnDialogNextControl.Text = ">>";
            btnDialogNextControl.Font = new Font(btnDialogNextControl.Font, FontStyle.Bold);
            btnDialogNextControl.Margin = new Padding(0);
            btnDialogNextControl.Padding = new Padding(0);
            btnDialogNextControl.UseVisualStyleBackColor = false;
            btnDialogNextControl.Top = btnDialogNextControl.Parent.ClientSize.Height - btnDialogNextControl.Size.Height;
            btnDialogNextControl.Left = btnDialogNextControl.Parent.ClientSize.Width - btnDialogNextControl.Size.Width;
            btnDialogNextControl.Click += btnDialogNextControl_Click;

            if (instance.MonoRuntime)
            {
                statusStrip1.LayoutStyle = ToolStripLayoutStyle.Table;
            }

            // Callbacks
            netcom.ClientLoginStatus += netcom_ClientLoginStatus;
            netcom.ClientLoggedOut += netcom_ClientLoggedOut;
            netcom.ClientDisconnected += netcom_ClientDisconnected;
            instance.Names.NameUpdated += Names_NameUpdated;
            client.Network.SimChanged += Network_SimChanged;

            RegisterClientEvents(client);

            InitializeStatusTimer();
            RefreshWindowTitle();

            GUI.GuiHelpers.ApplyGuiFixes(this);

            InitUpdater();
        }

        private void Network_SimChanged(object sender, SimChangedEventArgs e)
        {
            SetHoverHeightFromSettings();
            client.Network.CurrentSim.Caps.CapabilitiesReceived += Caps_CapabilitiesReceived;
        }

        private void RegisterClientEvents(GridClient client)
        {
            client.Parcels.ParcelProperties += Parcels_ParcelProperties;
            client.Self.MoneyBalanceReply += Self_MoneyBalanceReply;
            client.Self.MoneyBalance += Self_MoneyBalance;
        }

        private void UnregisterClientEvents(GridClient client)
        {
            client.Parcels.ParcelProperties -= Parcels_ParcelProperties;
            client.Self.MoneyBalanceReply -= Self_MoneyBalanceReply;
            client.Self.MoneyBalance -= Self_MoneyBalance;
        }

        void instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            UnregisterClientEvents(e.OldClient);
            RegisterClientEvents(client);
        }

        void frmMain_Disposed(object sender, EventArgs e)
        {
            if (netcom != null)
            {
                netcom.NetcomSync = null;
                netcom.ClientLoginStatus -= netcom_ClientLoginStatus;
                netcom.ClientLoggedOut -= netcom_ClientLoggedOut;
                netcom.ClientDisconnected -= netcom_ClientDisconnected;
            }

            if (client != null)
            {
                UnregisterClientEvents(client);
            }

            if (instance?.Names != null)
            {
                instance.Names.NameUpdated -= Names_NameUpdated;
            }

            if (instance != null) instance.CleanUp();
        }
        #endregion

        #region Event handlers
        bool firstMoneyNotification = true;
        void Self_MoneyBalance(object sender, BalanceEventArgs e)
        {
            int oldBalance = 0;
            int.TryParse(tlblMoneyBalance.Text, out oldBalance);
            int delta = Math.Abs(oldBalance - e.Balance);

            if (firstMoneyNotification)
            {
                firstMoneyNotification = false;
            }
            else
            {
                if (delta <= 50) return;

                instance.MediaManager.PlayUISound(oldBalance > e.Balance 
                    ? UISounds.MoneyIn : UISounds.MoneyOut);
            }
        }

        void Names_NameUpdated(object sender, UUIDNameReplyEventArgs e)
        {
            if (!e.Names.ContainsKey(client.Self.AgentID)) return;

            if (InvokeRequired)
            {
                if (IsHandleCreated || !instance.MonoRuntime)
                {
                    BeginInvoke(new MethodInvoker(() => Names_NameUpdated(sender, e)));
                }
                return;
            }

            RefreshWindowTitle();
            RefreshStatusBar();
        }

        void Self_MoneyBalanceReply(object sender, MoneyBalanceReplyEventArgs e)
        {
            if (String.IsNullOrEmpty(e.Description)) return;

            if (instance.GlobalSettings["transaction_notification_dialog"].AsBoolean())
                AddNotification(new ntfGeneric(instance, e.Description));
            if (instance.GlobalSettings["transaction_notification_chat"].AsBoolean())
                TabConsole.DisplayNotificationInChat(e.Description);
        }

        public void InitializeControls()
        {
            InitializeTabsConsole();
            uriParser = new SlUriParser(instance);

            if (!instance.MediaManager.SoundSystemAvailable) return;

            MediaConsole = new MediaConsole(instance);
            tbtnMedia.Visible = true;
        }

        public bool InAutoReconnect { get; set; }

        private void DisplayAutoReconnectForm()
        {
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(DisplayAutoReconnectForm));
                return;
            }

            InAutoReconnect = true;
            frmReconnect dialog = new frmReconnect(instance, instance.GlobalSettings["reconnect_time"]);
            dialog.ShowDialog(this);
            dialog.Dispose();
            dialog = null;
        }

        public void BeginAutoReconnect()
        {
            // Sleep for 3 seconds on a separate thread while things unwind on
            // disconnect, since ShowDialog() blocks GUI thread
            (new Thread(() =>
                    {
                        Thread.Sleep(3000);
                        DisplayAutoReconnectForm();
                    })
                {
                    Name = "Reconnect Delay Thread",
                    IsBackground = true
                }
            ).Start();
        }

        private void netcom_ClientLoginStatus(object sender, LoginProgressEventArgs e)
        {
            if (e.Status == LoginStatus.Failed)
            {
                if (!InAutoReconnect) return;

                if (instance.GlobalSettings["auto_reconnect"].AsBoolean() && e.FailReason != "tos")
                    BeginAutoReconnect();
                else
                    InAutoReconnect = false;
            }
            else if (e.Status == LoginStatus.Success)
            {
                InAutoReconnect = false;
                reconnectToolStripMenuItem.Enabled = false;
                loginToolStripMenuItem.Enabled = false;
                tsb3D.Enabled = tbtnVoice.Enabled = disconnectToolStripMenuItem.Enabled =
                tbtnGroups.Enabled = tbnObjects.Enabled = tbtnWorld.Enabled = tbnTools.Enabled = tmnuImport.Enabled =
                    tbtnFriends.Enabled = tbtnInventory.Enabled = tbtnSearch.Enabled = tbtnMap.Enabled = true;

                statusTimer.Start();
                RefreshWindowTitle();

                if (instance.GlobalSettings.ContainsKey("AvatarHoverOffsetZ"))
                {
                    var hoverHeight = instance.GlobalSettings["AvatarHoverOffsetZ"];
                    Client.Self.SetHoverHeight(hoverHeight);
                }
            }
        }

        private void netcom_ClientLoggedOut(object sender, EventArgs e)
        {
            tsb3D.Enabled = tbtnVoice.Enabled = disconnectToolStripMenuItem.Enabled =
            tbtnGroups.Enabled = tbnObjects.Enabled = tbtnWorld.Enabled = tbnTools.Enabled = tmnuImport.Enabled =
                tbtnFriends.Enabled = tbtnInventory.Enabled = tbtnSearch.Enabled = tbtnMap.Enabled = false;

            reconnectToolStripMenuItem.Enabled = true;
            loginToolStripMenuItem.Enabled = true;
            InAutoReconnect = false;

            statusTimer?.Stop();

            RefreshStatusBar();
            RefreshWindowTitle();
        }

        private void netcom_ClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            firstMoneyNotification = true;

            if (e.Reason == NetworkManager.DisconnectType.ClientInitiated) return;
            netcom_ClientLoggedOut(sender, EventArgs.Empty);

            if (instance.GlobalSettings["auto_reconnect"].AsBoolean())
            {
                BeginAutoReconnect();
            }
        }

        private void frmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (instance.GlobalSettings["confirm_exit"].AsBoolean())
            {
                if (MessageBox.Show("Are you sure you want to exit Radegast?", "Confirm Exit",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            if (statusTimer != null)
            {
                statusTimer.Stop();
                statusTimer.Dispose();
                statusTimer = null;
            }

            if (MediaConsole != null)
            {
                if (TabConsole.TabExists("media"))
                {
                    TabConsole.Tabs["media"].AllowClose = true;
                    TabConsole.Tabs["media"].Close();
                }
                else
                {
                    MediaConsole.Dispose();
                }
                MediaConsole = null;
            }

            if (!netcom.IsLoggedIn) return;

            Thread saveInvToDisk = new Thread(delegate()
            {
                client.Inventory.Store.SaveToDisk(instance.InventoryCacheFileName);
            })
            {
                Name = "Save inventory to disk"
            };
            saveInvToDisk.Start();

            netcom.Logout();
        }
        #endregion

        # region Update status

        void Parcels_ParcelProperties(object sender, ParcelPropertiesEventArgs e)
        {
            if (PreventParcelUpdate || e.Result != ParcelResult.Single) return;
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Parcels_ParcelProperties(sender, e)));
                return;
            }

            Parcel parcel = instance.State.Parcel = e.Parcel;

            tlblParcel.Text = parcel.Name;
            tlblParcel.ToolTipText = parcel.Desc;

            icoNoFly.Visible = (parcel.Flags & ParcelFlags.AllowFly) != ParcelFlags.AllowFly;
            icoNoBuild.Visible = (parcel.Flags & ParcelFlags.CreateObjects) != ParcelFlags.CreateObjects;
            icoNoScript.Visible = (parcel.Flags & ParcelFlags.AllowOtherScripts) != ParcelFlags.AllowOtherScripts;
            icoNoPush.Visible = (parcel.Flags & ParcelFlags.RestrictPushObject) == ParcelFlags.RestrictPushObject;
            icoHealth.Visible = (parcel.Flags & ParcelFlags.AllowDamage) == ParcelFlags.AllowDamage;
            icoNoVoice.Visible = (parcel.Flags & ParcelFlags.AllowVoiceChat) != ParcelFlags.AllowVoiceChat;
        }

        private void RefreshStatusBar()
        {
            if (netcom.IsLoggedIn)
            {
                tlblLoginName.Text = instance.Names.Get(client.Self.AgentID, client.Self.Name);
                tlblMoneyBalance.Text = client.Self.Balance.ToString();
                icoHealth.Text = client.Self.Health.ToString(CultureInfo.CurrentCulture) + @"%";

                var cs = client.Network.CurrentSim;
                tlblRegionInfo.Text =
                    (cs == null ? "No region" : cs.Name) +
                    @" (" + Math.Floor(client.Self.SimPosition.X).ToString(CultureInfo.CurrentCulture) + @", " +
                    Math.Floor(client.Self.SimPosition.Y).ToString(CultureInfo.CurrentCulture) + @", " +
                    Math.Floor(client.Self.SimPosition.Z).ToString(CultureInfo.CurrentCulture) + @")";
            }
            else
            {
                tlblLoginName.Text = "Offline";
                tlblMoneyBalance.Text = @"0";
                icoHealth.Text = @"0%";
                tlblRegionInfo.Text = "No Region";
                tlblParcel.Text = "No Parcel";

                icoHealth.Visible = false;
                icoNoBuild.Visible = false;
                icoNoFly.Visible = false;
                icoNoPush.Visible = false;
                icoNoScript.Visible = false;
                icoNoVoice.Visible = false;
            }
        }

        private void RefreshWindowTitle()
        {
            string name = instance.Names.Get(client.Self.AgentID, client.Self.Name);
            StringBuilder sb = new StringBuilder();
            sb.Append("Radegast - ");

            if (netcom.IsLoggedIn)
            {
                sb.Append("[" + name + "]");

                if (instance.State.IsAway)
                {
                    sb.Append(" - Away");
                    if (instance.State.IsBusy) sb.Append(", Busy");
                }
                else if (instance.State.IsBusy)
                {
                    sb.Append(" - Busy");
                }

                if (instance.State.IsFollowing)
                {
                    sb.Append(" - Following ");
                    sb.Append(instance.State.FollowName);
                }
            }
            else
            {
                sb.Append("Logged Out");
            }

            Text = sb.ToString();

            // When minimized to tray, update tray tool tip also
            if (WindowState == FormWindowState.Minimized && instance.GlobalSettings["minimize_to_tray"])
            {
                trayIcon.Text = sb.ToString();
                ctxTrayMenuLabel.Text = sb.ToString();
            }

            sb = null;
        }

        private void InitializeStatusTimer()
        {
            statusTimer = new System.Timers.Timer(250) {SynchronizingObject = this};
            statusTimer.Elapsed += statusTimer_Elapsed;
        }

        private void statusTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            // Mono sometimes fires timer after is's disposed
            try
            {
                RefreshWindowTitle();
                RefreshStatusBar();
            }
            catch { }
        }
        #endregion

        #region Initialization, configuration, and key shortcuts
        private void InitializeTabsConsole()
        {
            TabConsole = new TabsConsole(instance) {Dock = DockStyle.Fill};
            toolStripContainer1.ContentPanel.Controls.Add(TabConsole);
        }

        private void frmMain_KeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl-Alt-Shift-H Say "Hippos!" in chat
            if (e.Modifiers == (Keys.Control | Keys.Shift | Keys.Alt) && e.KeyCode == Keys.H)
            {
                e.Handled = e.SuppressKeyPress = true;
                netcom.ChatOut("Hippos!", ChatType.Normal, 0);
                return;
            }

            // Ctrl-Shift-1 (sim/parcel info)
            if (e.Modifiers == (Keys.Control | Keys.Shift) && e.KeyCode == Keys.D1)
            {
                e.Handled = e.SuppressKeyPress = true;
                DisplayRegionParcelConsole();
                return;
            }

            // Ctrl-W: Close tab
            if (e.Modifiers == Keys.Control && e.KeyCode == Keys.W)
            {
                e.Handled = e.SuppressKeyPress = true;
                RadegastTab tab = TabConsole.SelectedTab;

                if (tab.AllowClose)
                {
                    tab.Close();
                }
                else if (tab.AllowHide)
                {
                    tab.Hide();
                }

                return;
            }

            // Ctl-Shift-H: Teleport Home
            if (e.Modifiers == (Keys.Control | Keys.Shift) && e.KeyCode == Keys.H)
            {
                e.Handled = e.SuppressKeyPress = true;
                tmnuTeleportHome.PerformClick();
                return;
            }

            // Alt-Ctrl-D Open debug console
            if (e.Modifiers == (Keys.Control | Keys.Alt) && e.KeyCode == Keys.D)
            {
                e.Handled = e.SuppressKeyPress = true;
                debugConsoleToolStripMenuItem.PerformClick();
                return;
            }

            // Alt 1-8: Toggle various tabs
            if (e.Modifiers == Keys.Alt)
            {
                switch (e.KeyCode)
                {
                    case Keys.D1:
                        e.Handled = e.SuppressKeyPress = true;
                        TabConsole.Tabs["chat"].Select();
                        return;

                    case Keys.D2:
                        e.Handled = e.SuppressKeyPress = true;
                        tbtnFriends.PerformClick();
                        return;

                    case Keys.D3:
                        e.Handled = e.SuppressKeyPress = true;
                        tbtnGroups.PerformClick();
                        return;

                    case Keys.D4:
                        e.Handled = e.SuppressKeyPress = true;
                        tbtnInventory.PerformClick();
                        return;

                    case Keys.D5:
                        e.Handled = e.SuppressKeyPress = true;
                        tbtnSearch.PerformClick();
                        return;

                    case Keys.D6:
                        e.Handled = e.SuppressKeyPress = true;
                        tbtnMap.PerformClick();
                        return;

                    case Keys.D7:
                        e.Handled = e.SuppressKeyPress = true;
                        tbnObjects.PerformClick();
                        return;

                    case Keys.D8:
                        e.Handled = e.SuppressKeyPress = true;
                        tbtnMedia.PerformClick();
                        return;

                    case Keys.D9:
                        e.Handled = e.SuppressKeyPress = true;
                        tbtnVoice.PerformClick();
                        return;
                }
            }

            // ctrl-g, goto slurl
            if (e.Control && e.KeyCode == Keys.G)
            {
                if (!ProcessLink(Clipboard.GetText(), true))
                    MapToCurrentLocation();

                e.Handled = e.SuppressKeyPress = true;
                return;
            }

            // ctrl-(shift)-tab for next/previous tab
            if (e.Control && e.KeyCode == Keys.Tab)
            {
                if (e.Shift)
                {
                    TabConsole.SelectPreviousTab();
                }
                else
                {
                    TabConsole.SelectNextTab();
                }
                e.Handled = e.SuppressKeyPress = true;
            }
        }

        bool firstLoad = true;

        private void frmMain_Load(object sender, EventArgs e)
        {
            if (!firstLoad) return;

            firstLoad = false;
            TabConsole.SelectTab("login");
            ResourceManager rm = Properties.Resources.ResourceManager;
            ResourceSet set = rm.GetResourceSet(CultureInfo.CurrentCulture, true, true);
            System.Collections.IDictionaryEnumerator de = set.GetEnumerator();
            while (de.MoveNext())
            {
                if (de.Entry.Value is Image)
                {
                    Bitmap bitMap = de.Entry.Value as Bitmap;
                    ResourceImages.Images.Add(bitMap);
                    ImageNames.Add(de.Entry.Key.ToString());
                }
            }

            if (!instance.GlobalSettings["theme_compatibility_mode"] && instance.PlainColors)
            {
                pnlDialog.BackColor = Color.FromArgb(120, 220, 255);
            }
        }
        #endregion

        #region Public methods

        private Dictionary<UUID, frmProfile> shownProfiles = new Dictionary<UUID, frmProfile>();

        void ShowAgentProfileInternal(string name, UUID agentID)
        {
            lock (shownProfiles)
            {
                frmProfile profile = null;
                if (shownProfiles.TryGetValue(agentID, out profile))
                {
                    profile.WindowState = FormWindowState.Normal;
                    profile.Focus();
                }
                else
                {
                    profile = new frmProfile(instance, name, agentID);

                    profile.Disposed += (sender, e) =>
                        {
                            lock (shownProfiles)
                            {
                                frmProfile agentProfile = (frmProfile)sender;
                                if (shownProfiles.ContainsKey(agentProfile.AgentID))
                                    shownProfiles.Remove(agentProfile.AgentID);
                            }
                        };

                    profile.Show();
                    profile.Focus();
                    shownProfiles.Add(agentID, profile);
                }
            }
        }

        private Dictionary<UUID, frmGroupInfo> shownGroupProfiles = new Dictionary<UUID, frmGroupInfo>();

        public void ShowGroupProfile(UUID id)
        {
            ShowGroupProfile(new OpenMetaverse.Group()
            {
                ID = id,
            });
        }

        public void ShowGroupProfile(AvatarGroup group)
        {
            ShowGroupProfile(new OpenMetaverse.Group()
            {
                ID = group.GroupID,
                InsigniaID = group.GroupInsigniaID,
                Name = group.GroupName
            }
            );
        }

        public void ShowGroupProfile(OpenMetaverse.Group group)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => ShowGroupProfile(group)));
                return;
            }

            lock (shownGroupProfiles)
            {
                frmGroupInfo profile = null;
                if (shownGroupProfiles.TryGetValue(group.ID, out profile))
                {
                    profile.WindowState = FormWindowState.Normal;
                    profile.Focus();
                }
                else
                {
                    profile = new frmGroupInfo(instance, group);

                    profile.Disposed += (sender, e) =>
                        {
                            lock (shownGroupProfiles)
                            {
                                frmGroupInfo groupProfile = (frmGroupInfo)sender;
                                if (shownGroupProfiles.ContainsKey(groupProfile.Group.ID))
                                    shownGroupProfiles.Remove(groupProfile.Group.ID);
                            }
                        };

                    profile.Show();
                    profile.Focus();
                    shownGroupProfiles.Add(group.ID, profile);
                }
            }
        }

        public bool ProcessSecondlifeURI(string link)
        {
            uriParser.ExecuteLink(link);
            return true;
        }

        public void ProcessLink(string link)
        {
            ProcessLink(link, false);
        }

        public bool ProcessLink(string link, bool onlyMap)
        {
            var pos = link.IndexOf(RRichTextBox.LinkSeparator);
            if (pos > 0)
            {
                link = link.Substring(pos + 1);
            }

            if (link.StartsWith("secondlife://") || link.StartsWith("[secondlife://"))
            {
                return ProcessSecondlifeURI(link);
            }

            if (!link.Contains("://"))
            {
                link = "http://" + link;
            }

            Regex r = new Regex(@"^(http://(slurl\.com|maps\.secondlife\.com)/secondlife/|secondlife://)(?<region>[^/]+)/(?<x>\d+)/(?<y>\d+)(/(?<z>\d+))?",
                RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase
                );
            Match m = r.Match(link);

            if (m.Success)
            {
                string region = HttpUtility.UrlDecode(m.Groups["region"].Value);
                int x = int.Parse(m.Groups["x"].Value);
                int y = int.Parse(m.Groups["y"].Value);
                int z = 0;

                if (!string.IsNullOrEmpty(m.Groups["z"].Value))
                {
                    z = int.Parse(m.Groups["z"].Value);
                }

                MapTab.Select();
                WorldMap.DisplayLocation(region, x, y, z);
                return true;
            }
            else if (!onlyMap)
            {
                System.Diagnostics.Process.Start(link);
            }
            return false;
        }
        #endregion

        #region Notifications
        CircularList<Control> notifications = new CircularList<Control>();

        public Color NotificationBackground => pnlDialog.BackColor;

        void ResizeNotificationByControl(Control active)
        {
            int width = active.Size.Width + 6;
            int height = notifications.Count > 1 ? active.Size.Height + 3 + btnDialogNextControl.Size.Height : active.Size.Height + 3;
            pnlDialog.Size = new Size(width, height);
            pnlDialog.Top = 0;
            pnlDialog.Left = pnlDialog.Parent.ClientSize.Width - width;

            btnDialogNextControl.Top = btnDialogNextControl.Parent.ClientSize.Height - btnDialogNextControl.Size.Height;
            btnDialogNextControl.Left = btnDialogNextControl.Parent.ClientSize.Width - btnDialogNextControl.Size.Width;

            btnDialogNextControl.BringToFront();
        }

        public void AddNotification(Control control)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(delegate()
                {
                    AddNotification(control);
                }
                ));
                return;
            }

            Control active = TabsConsole.FindFocusedControl(this);

            FormFlash.StartFlash(this);
            pnlDialog.Visible = true;
            pnlDialog.BringToFront();

            foreach (Control existing in notifications)
            {
                existing.Visible = false;
            }

            instance.MediaManager.PlayUISound(UISounds.WindowOpen);

            notifications.Add(control);
            control.Visible = true;
            control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            control.Top = 3;
            control.Left = 3;
            pnlDialog.Controls.Add(control);
            ResizeNotificationByControl(control);

            btnDialogNextControl.Visible = notifications.Count > 1;

            active?.Focus();
        }

        public void RemoveNotification(Control control)
        {
            pnlDialog.Controls.Remove(control);
            notifications.Remove(control);
            control.Dispose();

            if (notifications.HasNext)
            {
                pnlDialog.Visible = true;
                Control active = notifications.Next;
                active.Visible = true;
                ResizeNotificationByControl(active);
            }
            else
            {
                pnlDialog.Visible = false;
            }

            btnDialogNextControl.Visible = notifications.Count > 1;
        }

        private void btnDialogNextControl_Click(object sender, EventArgs e)
        {
            foreach (Control existing in notifications)
            {
                existing.Visible = false;
            }

            if (notifications.HasNext)
            {
                pnlDialog.Visible = true;
                Control active = notifications.Next;
                active.Visible = true;
                ResizeNotificationByControl(active);
            }
            else
            {
                pnlDialog.Visible = false;
            }

        }
        #endregion Notifications

        #region Menu click handlers

        private void tmnuStatusAway_Click(object sender, EventArgs e)
        {
            instance.State.SetAway(tmnuStatusAway.Checked);
        }

        private void tmnuHelpReadme_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start(Application.StartupPath + @"\Readme.txt");
        }

        private void tmnuStatusBusy_Click(object sender, EventArgs e)
        {
            instance.State.SetBusy(tmnuStatusBusy.Checked);
        }

        private void tmnuControlFly_Click(object sender, EventArgs e)
        {
            instance.State.SetFlying(tmnuControlFly.Checked);
        }

        private void tmnuControlAlwaysRun_Click(object sender, EventArgs e)
        {
            instance.State.SetAlwaysRun(tmnuControlAlwaysRun.Checked);
        }

        private void tmnuPrefs_Click(object sender, EventArgs e)
        {
            (new frmSettings(instance)).ShowDialog();
        }

        private void tbtnAppearance_Click(object sender, EventArgs e)
        {
            client.Appearance.RequestSetAppearance(false);
        }

        private void importObjectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //PrimDeserializer.ImportFromFile(client);
            DisplayImportConsole();
        }

        private void autopilotToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ap == null)
            {
                ap = new AutoPilot(client);
                /*
                ap.InsertWaypoint(new Vector3(66, 163, 21));
                ap.InsertWaypoint(new Vector3(66, 98, 21));

                ap.InsertWaypoint(new Vector3(101, 98, 21));
                ap.InsertWaypoint(new Vector3(101, 45, 21));
                ap.InsertWaypoint(new Vector3(93, 27, 21));
                ap.InsertWaypoint(new Vector3(106, 12, 21));
                ap.InsertWaypoint(new Vector3(123, 24, 21));
                ap.InsertWaypoint(new Vector3(114, 45, 21));
                ap.InsertWaypoint(new Vector3(114, 98, 21));

                ap.InsertWaypoint(new Vector3(130, 98, 21));
                ap.InsertWaypoint(new Vector3(130, 163, 21));
                 **/
                ap.InsertWaypoint(new Vector3(64, 68, 21));
                ap.InsertWaypoint(new Vector3(65, 20, 21));
                ap.InsertWaypoint(new Vector3(33, 23, 21));
                ap.InsertWaypoint(new Vector3(17, 39, 21));
                ap.InsertWaypoint(new Vector3(17, 62, 21));


            }
            if (AutoPilotActive)
            {
                AutoPilotActive = false;
                ap.Stop();
            }
            else
            {
                AutoPilotActive = true;
                ap.Start();
            }

        }

        int filesDeleted;

        private void deleteFolder(DirectoryInfo dir)
        {
            foreach (var file in dir.GetFiles())
            {
                try 
                {
                    file.Delete();
                    filesDeleted++;
                }
                catch { }
            }

            foreach (var subDir in dir.GetDirectories())
            {
                deleteFolder(subDir);
            }

            try { dir.Delete(); }
            catch { }
        }

        private void cleanCacheToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ThreadPool.QueueUserWorkItem(sync =>
            {
                filesDeleted = 0;
                try { deleteFolder(new DirectoryInfo(client.Settings.ASSET_CACHE_DIR)); }
                catch { }
                Logger.DebugLog("Wiped out " + filesDeleted + " files from the cache directory.");
            });
            instance.Names.CleanCache();
        }

        private void rebakeTexturesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            instance.COF.RebakeTextures();
        }

        public void MapToCurrentLocation()
        {
            if (MapTab == null || !client.Network.Connected) return;

            MapTab.Select();
            WorldMap.DisplayLocation(client.Network.CurrentSim.Name,
                (int)client.Self.SimPosition.X,
                (int)client.Self.SimPosition.Y,
                (int)client.Self.SimPosition.Z);
        }

        private void standToolStripMenuItem_Click(object sender, EventArgs e)
        {
            instance.State.SetSitting(false, UUID.Zero);
        }

        private void groundSitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            client.Self.SitOnGround();
        }

        private void newWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try { System.Diagnostics.Process.Start(Application.ExecutablePath); }
            catch (Exception) { }
        }

        private void tmnuExit_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void tlblRegionInfo_Click(object sender, EventArgs e)
        {
            if (WorldMap != null && client.Network.Connected)
            {
                MapTab.Select();
            }
        }

        private void scriptEditorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ScriptEditor se = new ScriptEditor(instance) {Dock = DockStyle.Fill};
            se.ShowDetached();
        }

        private void tmnuSetHome_Click(object sender, EventArgs e)
        {
            client.Self.SetHome();
        }

        private void tmnuCreateLandmark_Click(object sender, EventArgs e)
        {
            string location = string.Format(", {0} ({1}, {2}, {3})",
                client.Network.CurrentSim.Name,
                (int)client.Self.SimPosition.X,
                (int)client.Self.SimPosition.Y,
                (int)client.Self.SimPosition.Z);

            string name = tlblParcel.Text;
            int maxLen = 63 - location.Length;

            if (name.Length > maxLen)
                name = name.Substring(0, maxLen);

            name += location;

            client.Inventory.RequestCreateItem(
                client.Inventory.FindFolderForType(AssetType.Landmark),
                name,
                tlblParcel.ToolTipText,
                AssetType.Landmark,
                UUID.Random(),
                InventoryType.Landmark,
                PermissionMask.All,
                (success, item) =>
                {
                    if (success)
                    {
                        BeginInvoke(new MethodInvoker(() =>
                            {
                                Landmark ln = new Landmark(instance, (InventoryLandmark) item)
                                {
                                    Dock = DockStyle.Fill, Detached = true
                                };
                            }));
                    }
                }
            );
        }


        private void timerWorldClock_Tick(object sender, EventArgs e)
        {
            lblTime.Text = instance.GetWorldTime().ToString("h:mm tt", CultureInfo.InvariantCulture);
        }

        private void reportBugsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProcessLink("https://radegast.life/bugs/issue-entry/");
        }

        private void accessibilityGuideToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProcessLink("https://radegast.life/documentation/help/");
        }

        private void aboutRadegastToolStripMenuItem_Click(object sender, EventArgs e)
        {
            (new frmAbout(instance)).ShowDialog();
        }

        private void ToggleHidden(string tabName)
        {
            if (!TabConsole.TabExists(tabName)) return;

            RadegastTab tab = TabConsole.Tabs[tabName];

            if (tab.Hidden)
            {
                tab.Show();
            }
            else
            {
                if (!tab.Selected)
                {
                    tab.Select();
                }
                else
                {
                    tab.Hide();
                }
            }
        }

        private void tbtnFriends_Click(object sender, EventArgs e)
        {
            ToggleHidden("friends");
        }

        private void tbtnInventory_Click(object sender, EventArgs e)
        {
            ToggleHidden("inventory");
        }

        private void tbtnSearch_Click(object sender, EventArgs e)
        {
            ToggleHidden("search");
        }

        private void tbtnGroups_Click(object sender, EventArgs e)
        {
            ToggleHidden("groups");
        }

        private void tbtnVoice_Click(object sender, EventArgs e)
        {
            ToggleHidden("voice");
        }

        private void tbtnMedia_Click(object sender, EventArgs e)
        {
            if (TabConsole.TabExists("media"))
            {
                ToggleHidden("media");
            }
            else
            {
                RadegastTab tab = TabConsole.AddTab("media", "Media", MediaConsole);
                tab.AllowClose = false;
                tab.AllowHide = true;
                tab.Select();
            }
        }

        private void debugConsoleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (TabConsole.TabExists("debug"))
            {
                ToggleHidden("debug");
            }
            else
            {
                RadegastTab tab = TabConsole.AddTab("debug", "Debug", new DebugConsole(instance));
                tab.AllowClose = false;
                tab.AllowHide = true;
                tab.Select();
            }
        }

        private void tbnObjects_Click(object sender, EventArgs e)
        {
            if (TabConsole.TabExists("objects"))
            {
                RadegastTab tab = TabConsole.Tabs["objects"];
                if (!tab.Selected)
                {
                    tab.Select();
                    ((ObjectsConsole)tab.Control).RefreshObjectList();
                }
                else
                {
                    tab.Close();
                }
            }
            else
            {
                RadegastTab tab = TabConsole.AddTab("objects", "Objects", new ObjectsConsole(instance));
                tab.AllowClose = true;
                tab.AllowDetach = true;
                tab.Visible = true;
                tab.AllowHide = false;
                tab.Select();
                ((ObjectsConsole)tab.Control).RefreshObjectList();
            }
        }

        private void tbtnMap_Click(object sender, EventArgs e)
        {
            if (MapTab == null) return; // too soon!

            ToggleHidden("map");
            if (!MapTab.Hidden)
                MapToCurrentLocation();
        }

        private void disconnectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (client.Network.Connected)
                client.Network.RequestLogout();
        }

        private void reconnectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!client.Network.Connected)
            {
                instance.Reconnect();
            }
        }

        private frmKeyboardShortcuts keyboardShortcutsForm = null;
        private void keyboardShortcutsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (keyboardShortcutsForm != null)
            {
                keyboardShortcutsForm.Focus();
            }
            else
            {
                keyboardShortcutsForm = new frmKeyboardShortcuts(instance);

                keyboardShortcutsForm.Disposed += (senderx, ex) =>
                    {
                        components?.Remove(keyboardShortcutsForm);
                        keyboardShortcutsForm = null;
                    };

                keyboardShortcutsForm.Show(this);
                keyboardShortcutsForm.Top = Top + 100;
                keyboardShortcutsForm.Left = Left + 100;

                components?.Add(keyboardShortcutsForm);
            }
        }

        // Menu item for testing out stuff
        private void testToolStripMenuItem_Click(object sender, EventArgs e)
        {
        }

        private void reloadInventoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!TabConsole.TabExists("inventory")) return;

            ((InventoryConsole)TabConsole.Tabs["inventory"].Control).ReloadInventory();
            TabConsole.Tabs["inventory"].Select();
        }

        private void btnLoadScript_Click(object sender, EventArgs e)
        {
            if (!TabConsole.TabExists("plugin_manager"))
            {
                TabConsole.AddTab("plugin_manager", "Plugins", new PluginsTab(instance));
            }
            TabConsole.Tabs["plugin_manager"].Select();
        }

        private void frmMain_Resize(object sender, EventArgs e)
        {
            if (WindowState != FormWindowState.Minimized ||
                !instance.GlobalSettings["minimize_to_tray"].AsBoolean()) return;

            if (TabConsole.TabExists("scene_window") && !TabConsole.Tabs["scene_window"].Detached)
            {
                TabConsole.Tabs["scene_window"].Close();
            }
            ShowInTaskbar = false;
            trayIcon.Visible = true;
            trayIcon.BalloonTipText = "Radegast is running in the background";
            trayIcon.ShowBalloonTip(2000);
        }

        private void treyIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            trayIcon.Visible = false;
        }

        private void ctxTreyRestore_Click(object sender, EventArgs e)
        {
            treyIcon_MouseDoubleClick(this, null);
        }

        private void ctxTreyExit_Click(object sender, EventArgs e)
        {
            tmnuExit_Click(this, EventArgs.Empty);
        }

        private void tmnuTeleportHome_Click(object sender, EventArgs e)
        {
            TabConsole.DisplayNotificationInChat("Teleporting home...");
            client.Self.RequestTeleport(UUID.Zero);
        }

        private void stopAllAnimationsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            instance.State.StopAllAnimations();
        }

        public void DisplayRegionParcelConsole()
        {
            if (TabConsole.TabExists("current region info"))
            {
                TabConsole.Tabs["current region info"].Select();
                (TabConsole.Tabs["current region info"].Control as RegionInfo)?.UpdateDisplay();
            }
            else
            {
                TabConsole.AddTab("current region info", "Region info", new RegionInfo(instance));
                TabConsole.Tabs["current region info"].Select();
            }
        }

        public void DisplayExportConsole(uint localID)
        {
            if (InvokeRequired)
            {
                if (IsHandleCreated || !instance.MonoRuntime)
                    BeginInvoke(new MethodInvoker(() => DisplayExportConsole(localID)));
                return;
            }

            if (TabConsole.TabExists("export console"))
            {
                TabConsole.Tabs["export console"].Close();
            }
            RadegastTab tab = TabConsole.AddTab("export console", "Export Object", new ExportConsole(client, localID));
            tab.Select();
        }

        public void DisplayImportConsole()
        {
            if (TabConsole.TabExists("import console"))
            {
                TabConsole.Tabs["import console"].Select();
            }
            else
            {
                RadegastTab tab = TabConsole.AddTab("import console", "Import Object", new ImportConsole(client));
                tab.AllowClose = false;
                tab.AllowHide = true;
                tab.Select();
            }
        }

        public void DisplayColladaConsole(Primitive prim)
        {
            if (InvokeRequired)
            {
                if (IsHandleCreated || !instance.MonoRuntime)
                    BeginInvoke(new MethodInvoker(() => DisplayColladaConsole(prim)));
                return;
            }

            if (TabConsole.TabExists("collada console"))
            {
                TabConsole.Tabs["collada console"].Close();
            }
            RadegastTab tab = TabConsole.AddTab("collada console", "Export Collada", new ExportCollada(instance, prim));
            tab.Select();
        }

        private void regionParcelToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DisplayRegionParcelConsole();
        }

        private void tlblParcel_Click(object sender, EventArgs e)
        {
            if (!client.Network.Connected) return;
            DisplayRegionParcelConsole();
        }

        private void changeMyDisplayNameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!client.Avatars.DisplayNamesAvailable())
            {
                TabConsole.DisplayNotificationInChat("This grid does not support display names.", ChatBufferTextStyle.Error);
                return;
            }

            var dlg = new DisplayNameChange(instance);
            dlg.ShowDialog();
        }

        private void muteListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!TabConsole.TabExists("mute list console"))
            {
                TabConsole.AddTab("mute list console", "Mute list", new MuteList(instance));
            }
            TabConsole.Tabs["mute list console"].Select();
        }

        private void uploadImageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!TabConsole.TabExists("image upload console"))
            {
                TabConsole.AddTab("image upload console", "Upload image", new ImageUploadConsole(instance));
            }
            TabConsole.Tabs["image upload console"].Select();
        }
        #endregion

        private void myAttachmentsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Avatar av = client.Network.CurrentSim.ObjectsAvatars.Find(a => a.ID == client.Self.AgentID);

            if (av == null)
            {
                TabConsole.DisplayNotificationInChat("Unable to find my avatar!", ChatBufferTextStyle.Error);
                return;
            }

            if (!instance.TabConsole.TabExists("AT: " + av.ID))
            {
                instance.TabConsole.AddTab("AT: " + av.ID, "My Attachments", new AttachmentTab(instance, av));
            }
            instance.TabConsole.SelectTab("AT: " + av.ID);

        }

        private void tsb3D_Click(object sender, EventArgs e)
        {
            if (instance.TabConsole.TabExists("scene_window"))
            {
                instance.TabConsole.Tabs["scene_window"].Select();
            }
            else
            {
                var control = new Rendering.SceneWindow(instance) {Dock = DockStyle.Fill};
                instance.TabConsole.AddTab("scene_window", "Scene Viewer", control);
                instance.TabConsole.Tabs["scene_window"].Floater = false;
                instance.TabConsole.Tabs["scene_window"].CloseOnDetachedClose = true;
                control.RegisterTabEvents();

                if (instance.GlobalSettings["scene_window_docked"])
                {
                    instance.TabConsole.Tabs["scene_window"].Select();
                }
                else
                {
                    instance.TabConsole.Tabs["scene_window"].Detach(instance);
                }
            }
        }

        private void loginToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // We are logging in without exiting the client
            // Mark last run as successful
            instance.MarkEndExecution();
            TabConsole.InitializeMainTab();
            TabConsole.Tabs["login"].Select();
        }

        private void setMaturityLevel(string level)
        {
            client.Self.SetAgentAccess(level, res =>
            {
                if (res.Success)
                {
                    TabConsole.DisplayNotificationInChat("Successfully changed maturity access level to " + res.NewLevel);
                }
                else
                {
                    TabConsole.DisplayNotificationInChat("Failed to change maturity access level.", ChatBufferTextStyle.Error);
                }
            });
        }

        private void pGToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMaturityLevel("PG");
        }

        private void matureToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMaturityLevel("M");
        }

        private void adultToolStripMenuItem_Click(object sender, EventArgs e)
        {
            setMaturityLevel("A");
        }

        private void uploadmeshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!TabConsole.TabExists("mesh upload console"))
            {
                TabConsole.AddTab("mesh upload console", "Upload mesh", new MeshUploadConsole(instance));
            }
            TabConsole.Tabs["mesh upload console"].Select();
        }

        private void setHoverHeightToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var hoverHeight = 0.0;
            if (instance.GlobalSettings.ContainsKey("AvatarHoverOffsetZ"))
            {
                hoverHeight = instance.GlobalSettings["AvatarHoverOffsetZ"];
            }

            var hoverHeightControl = new frmHoverHeight(hoverHeight, Instance.MonoRuntime);
            hoverHeightControl.HoverHeightChanged += HoverHeightControl_HoverHeightChanged;
            hoverHeightControl.Show();
        }

        private void HoverHeightControl_HoverHeightChanged(object sender, HoverHeightChangedEventArgs e)
        {
            instance.GlobalSettings["AvatarHoverOffsetZ"] = e.HoverHeight;
            Client.Self.SetHoverHeight(e.HoverHeight);
        }

        private void Caps_CapabilitiesReceived(object sender, CapabilitiesReceivedEventArgs e)
        {
            e.Simulator.Caps.CapabilitiesReceived -= Caps_CapabilitiesReceived;

            if (e.Simulator == client.Network.CurrentSim)
            {
                SetHoverHeightFromSettings();
            }
        }

        private void SetHoverHeightFromSettings()
        {
            if (!instance.GlobalSettings.ContainsKey("AvatarHoverOffsetZ")) return;

            var hoverHeight = instance.GlobalSettings["AvatarHoverOffsetZ"];
            Client.Self.SetHoverHeight(hoverHeight);
        }

        private void openMiniMapToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var miniMapControl = new GUI.Dialogs.MiniMapForm(client);
            miniMapControl.Show();
        }

        private void InitUpdater()
        {
            var appcastUrl = "https://update.radegast.life/appcast.json";
            var manifestModuleName = System.Reflection.Assembly.GetEntryAssembly()?.ManifestModule.FullyQualifiedName;
            var icon = Icon.ExtractAssociatedIcon(manifestModuleName);
            SparkleUpdater = new NetSparkleUpdater.SparkleUpdater(appcastUrl,
                new Ed25519Checker(NetSparkleUpdater.Enums.SecurityMode.Strict, "euvj+Uut3Nt3BVIu+aqJ02++Jflh8VjzBUzMgb7EnP8="))
            {
                UIFactory = new NetSparkleUpdater.UI.WinForms.UIFactory(icon),
                RelaunchAfterUpdate = true,
                UseNotificationToast = true
            };
            SparkleUpdater.StartLoop(true);
        }

        private void ctxCheckForUpdates_Click(object sender, EventArgs e)
        {
            SparkleUpdater.CheckForUpdatesAtUserRequest();
        }
    }
}