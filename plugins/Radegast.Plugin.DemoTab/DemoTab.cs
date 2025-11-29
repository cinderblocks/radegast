#region Copyright
// 
// Radegast Metaverse Client
// Copyright (c) 2009-2014, Radegast Development Team
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
// 
//     * Redistributions of source code must retain the above copyright notice,
//       this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above copyright
//       notice, this list of conditions and the following disclaimer in the
//       documentation and/or other materials provided with the distribution.
//     * Neither the name of the application "Radegast", nor the names of its
//       contributors may be used to endorse or promote products derived from
//       this software without specific prior written permission.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
// $Id$
//
#endregion

#region Usings
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Radegast;
using OpenMetaverse;
using Radegast.Threading;
#endregion

namespace DemoTab
{
    /// <summary>
    /// Example implementation of a control that can be used
    /// as Radegast tab and loeaded as a plugin
    /// </summary>
    [Radegast.Plugin(Name = "Demo Tab", Description = "Demonstration of how to add a new tab via plugin", Version = "1.0")]
    public partial class DemoTab : RadegastTabControl, IRadegastPlugin
    {
        // Methods needed for proper registration of a GUI tab
        #region Template for GUI radegast tab
        /// <summary>String for internal identification of the tab (change this!)</summary>
        static string tabID = "demo_tab";
        /// <summary>Text displayed in the plugins menu and the tab label (change this!)</summary>
        static string tabLabel = "Demo Tab";

        /// <summary>Menu item that gets added to the Plugins menu</summary>
        ToolStripMenuItem ActivateTabButton;

        /// <summary>Default constructor. Never used. Needed for VS designer</summary>
        public DemoTab()
        {
        }

        /// <summary>
        /// Main constructor used when actually creating the tab control for display
        /// Register client and instance events
        /// </summary>
        /// <param name="instance">RadegastInstance</param>
        /// <param name="unused">This param is not used, but needs to be there to keep the constructor signature</param>
        public DemoTab(RadegastInstance instance, bool unused)
            : base(instance)
        {
            InitializeComponent();
            Disposed += new EventHandler(DemoTab_Disposed);
            instance.ClientChanged += new EventHandler<ClientChangedEventArgs>(instance_ClientChanged);
            RegisterClientEvents(client);
        }

        /// <summary>
        /// Cleanup after the tab is closed
        /// Unregister event handler hooks we have installed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void DemoTab_Disposed(object sender, EventArgs e)
        {
            UnregisterClientEvents(client);
            instance.ClientChanged -= new EventHandler<ClientChangedEventArgs>(instance_ClientChanged);
        }

        /// <summary>
        /// Plugin loader calls this at the time plugin gets created
        /// We add a button to the Plugins menu on the main window
        /// for this tab
        /// </summary>
        /// <param name="inst">Main RadegastInstance</param>
        public void StartPlugin(RadegastInstance inst)
        {
            this.instance = inst;
            ActivateTabButton = new ToolStripMenuItem(tabLabel, null, MenuButtonClicked);
            instance.MainForm.PluginsMenu.DropDownItems.Add(ActivateTabButton);
        }

        /// <summary>
        /// Called when the plugin manager unloads our plugin. 
        /// Close the tab if it's active and remove the menu button
        /// </summary>
        /// <param name="inst"></param>
        public void StopPlugin(RadegastInstance inst)
        {
            ActivateTabButton.Dispose();
            instance.TabConsole.Tabs[tabID].Close();
        }

        /// <summary>
        /// Hadle case when GridClient is changed (relog haa occured without
        /// quiting Radegast). We need to unregister events from the old client
        /// and re-register them with the new
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            UnregisterClientEvents(e.OldClient);
            RegisterClientEvents(e.Client);
        }

        /// <summary>
        /// Registration of all GridClient (libomv) events go here
        /// </summary>
        /// <param name="client"></param>
        void RegisterClientEvents(GridClient client)
        {
            client.Self.ChatFromSimulator += new EventHandler<ChatEventArgs>(Self_ChatFromSimulator);
        }

        /// <summary>
        /// Unregistration of GridClient (libomv) events.
        /// Important that this be symetric to RegisterClientEvents() calls
        /// </summary>
        /// <param name="client"></param>
        void UnregisterClientEvents(GridClient client)
        {
            if (client == null) return;
            client.Self.ChatFromSimulator -= new EventHandler<ChatEventArgs>(Self_ChatFromSimulator);
        }

        /// <summary>
        /// Handling the click on Plugins -> Demo Tab button
        /// Check if we already have a tab. If we do make it active tab.
        /// If not, create a new tab and make it active.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void MenuButtonClicked(object sender, EventArgs e)
        {
            if (instance.TabConsole.TabExists(tabID))
            {
                instance.TabConsole.Tabs[tabID].Select();
            }
            else
            {
                instance.TabConsole.AddTab(tabID, tabLabel, new DemoTab(instance, true));
                instance.TabConsole.Tabs[tabID].Select();
            }
        }
        #endregion Template for GUI radegast tab

        #region Implementation of the custom tab functionality
        void Self_ChatFromSimulator(object sender, ChatEventArgs e)
        {
            // Boilerplate, make sure to be on the GUI thread
            if (InvokeRequired)
            {
                ThreadingHelper.SafeInvoke(this, new Action(() => Self_ChatFromSimulator(sender, e)), instance.MonoRuntime);
                return;
            }

            txtChat.Text = e.Message;
        }

        private void btnSaySomething_Click(object sender, EventArgs e)
        {
            client.Self.Chat("Something", 0, ChatType.Normal);
        }
        #endregion Implementation of the custom tab functionality
    }
}
