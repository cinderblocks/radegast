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
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Path = System.IO.Path;

namespace Radegast
{
    public sealed class RadegastInstanceForms : RadegastInstance
    {
        private static readonly Lazy<RadegastInstanceForms> instance =
            new Lazy<RadegastInstanceForms>(() => new RadegastInstanceForms(Properties.Resources.ProgramName, new GridClient()));

        public static RadegastInstanceForms Instance => instance.Value;
        public static bool Initialized => instance.IsValueCreated;


        /// <summary>ContextAction manager for context-sensitive actions</summary>
        public ContextActionsManager ContextActionManager { get; private set; }

        /// <summary>Keyboard handling manager (used in 3D scene viewer)</summary>
        public Keyboard Keyboard;

        /// <summary> Handles loading plugins and scripts</summary>
        public PluginManager PluginManager { get; private set; }

        /// <summary>
        /// Is system using plain color theme, with white background and dark text
        /// </summary>
        public bool PlainColors
        {
            get
            {
                // If windows background is whiteish, declare as standard color scheme
                var c = System.Drawing.SystemColors.Window;
                return c.R > 240 && c.G > 240 && c.B > 240;
            }
        }

        private RadegastInstanceForms(string appName, GridClient client0) : base(appName, client0, new NetComForms(client0))
        {
            Keyboard = new Keyboard();
            Application.AddMessageFilter(Keyboard);

            ContextActionManager = new ContextActionsManager(this);
            RegisterContextActions();

            PluginManager = new PluginManager(this);

            MainForm = new frmMain(this);
            MainForm.InitializeControls();
            MainForm.Load += mainForm_Load;
        }

        public override void CleanUp()
        {
            if (PluginManager != null)
            {
                PluginManager.Dispose();
                PluginManager = null;
            }
            if (ContextActionManager != null)
            {
                ContextActionManager.Dispose();
                ContextActionManager = null;
            }
            if (MainForm != null)
            {
                MainForm.Load -= mainForm_Load;
            }
            base.CleanUp();
        }

        private void mainForm_Load(object sender, EventArgs e)
        {
            try
            {
                var pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                PluginManager.LoadPluginsInDirectory(pluginDirectory);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error scanning and loading plugins", ex);
            }
        }

        public override void ShowNotificationInChat(string message, ChatBufferTextStyle style = ChatBufferTextStyle.ObjectChat, bool highlight = false)
        {
            TabConsole.DisplayNotificationInChat(message, style, highlight);
        }

        public override void AddNotification(INotification notification)
        {
            MainForm.AddNotification(notification);
        }

        public override void RemoveNotification(INotification notification)
        {
            MainForm.RemoveNotification(notification);
        }

        public override void ShowAgentProfile(string agentName, UUID agentID)
        {
            MainForm.ShowAgentProfile(agentName, agentID);
        }

        public override void ShowGroupProfile(UUID groupId)
        {
            MainForm.ShowGroupProfile(groupId);
        }

        public override void ShowLocation(string region, int x, int y, int z)
        {
            MainForm.MapTab.Select();
            MainForm.WorldMap.DisplayLocation(region, x, y, z);
        }

        public override void RegisterContextAction(Type omvType, string label, EventHandler handler)
        {
            ContextActionManager.RegisterContextAction(omvType, label, handler);
        }

        public override void DeregisterContextAction(Type omvType, string label)
        {
            ContextActionManager.DeregisterContextAction(omvType, label);
        }

        protected override void InitializeAppData()
        {
            try
            {
                UserDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
                if (!Directory.Exists(UserDir))
                {
                    Directory.CreateDirectory(UserDir);
                }
            }
            catch (Exception)
            {
                UserDir = Environment.CurrentDirectory;
            }
            GlobalLogFile = Path.Combine(UserDir, $"{AppName}.log");
            GlobalSettings = new SettingsForms(Path.Combine(UserDir, "settings.xml"));
            frmSettings.InitSettings(GlobalSettings);
        }

        public frmMain MainForm { get; }
        public TabsConsole TabConsole => MainForm.TabConsole;

        #region OnRadegastFormCreated

        public event Action<RadegastForm> RadegastFormCreated;

        /// <summary>
        /// Triggers the RadegastFormCreated event.
        /// </summary>
        public void OnRadegastFormCreated(RadegastForm radForm)
        {
            RadegastFormCreated?.Invoke(radForm);
        }

        #endregion

        #region Context Actions

        private void RegisterContextActions()
        {
            ContextActionManager.RegisterContextAction(typeof(Primitive), "Save as DAE...", ExportDAEHander);
            ContextActionManager.RegisterContextAction(typeof(Primitive), "Copy UUID to clipboard",
                CopyObjectUUIDHandler);
        }

        private void DeregisterContextActions()
        {
            ContextActionManager.DeregisterContextAction(typeof(Primitive), "Save as DAE...");
            ContextActionManager.DeregisterContextAction(typeof(Primitive), "Copy UUID to clipboard");
        }

        private void ExportDAEHander(object sender, EventArgs e)
        {
            MainForm.DisplayColladaConsole((Primitive)sender);
        }

        private void CopyObjectUUIDHandler(object sender, EventArgs e)
        {
            if (MainForm.InvokeRequired)
            {
                ThreadingHelper.SafeInvokeSync(MainForm, new Action(() => CopyObjectUUIDHandler(sender, e)), MonoRuntime);
                return;
            }

            Clipboard.SetText(((Primitive)sender).ID.ToString());
        }

        #endregion Context Actions

    }
}
