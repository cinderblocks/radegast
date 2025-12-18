#region Copyright
// 
// Radegast SimpleBuilder plugin extension
//
// Copyright (c) 2014, Ano Nymous <anonymously@hotmail.de> | SecondLife-IM: anno1986 Resident
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
using System.Text;
using System.Windows.Forms;
using Radegast;
using OpenMetaverse;
using Radegast.Core;
using System.Threading;
using System.Threading.Tasks;
using LibreMetaverse;
#endregion

namespace SimpleBuilderNamespace
{
    /// <summary>
    /// Example implementation of a control that can be used
    /// as Radegast tab and loeaded as a plugin
    /// </summary>
    [Plugin(Name = "SimpleBuilder Plugin", Description = "Allows you to build some basic prims, like boxes, cylinder, tubes, ... (requires permission!)", Version = "1.0")]
    public partial class SimpleBuilder : RadegastTabControl, IRadegastPlugin
    {
        private TaskCompletionSource<bool> primDoneTcs;

        private readonly string pluginName = "SimpleBuilder";
        // Methods needed for proper registration of a GUI tab
        #region Template for GUI radegast tab
        /// <summary>String for internal identification of the tab (change this!)</summary>
        private static readonly string tabID = "simplebuilder_tab";
        /// <summary>Text displayed in the plugins menu and the tab label (change this!)</summary>
        private static readonly string tabLabel = "Build Prims";

        /// <summary>Menu item that gets added to the Plugins menu</summary>
        private ToolStripMenuItem ActivateTabButton;

        public List<Primitive> Prims = new List<Primitive>();
        private PropertiesQueue propRequester;

        private Primitive m_selectedPrim;
        public Primitive selectedPrim {
            get => m_selectedPrim;
            set
            {
                if(value == null){
                    btnSave.Enabled = false;
                    groupTransform.Text = "Transform";
                }
                else
                {
                    btnSave.Enabled = true;
                    groupTransform.Text = "Transform: " + value.Properties.Name;
                }
                    

                m_selectedPrim = value;
            }
        }

        public string ObjectName { get; set; }

        /// <summary>Default constructor. Never used. Needed for VS designer</summary>
        public SimpleBuilder()
        {
        }

        /// <summary>
        /// Main constructor used when actually creating the tab control for display
        /// Register client and instance events
        /// </summary>
        /// <param name="instance">RadegastInstance</param>
        /// <param name="unused">This param is not used, but needs to be there to keep the constructor signature</param>
        public SimpleBuilder(RadegastInstanceForms instance, bool unused)
            : base(instance)
        {
            InitializeComponent();
            Disposed += DemoTab_Disposed;
            instance.ClientChanged += instance_ClientChanged;
            RegisterClientEvents(client);

            selectedPrim = null;

            Radegast.GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        /// <summary>
        /// Cleanup after the tab is closed
        /// Unregister event handler hooks we have installed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DemoTab_Disposed(object sender, EventArgs e)
        {
            UnregisterClientEvents(client);
            instance.ClientChanged -= instance_ClientChanged;
        }

        /// <summary>
        /// Plugin loader calls this at the time plugin gets created
        /// We add a button to the Plugins menu on the main window
        /// for this tab
        /// </summary>
        /// <param name="inst">Main RadegastInstance</param>
        public void StartPlugin(RadegastInstanceForms inst)
        {
            instance = inst;

            propRequester = new PropertiesQueue(instance);
            propRequester.OnTick += propRequester_OnTick;

            ActivateTabButton = new ToolStripMenuItem(tabLabel, null, MenuButtonClicked);
            instance.MainForm.PluginsMenu.DropDownItems.Add(ActivateTabButton);

        }

        /// <summary>
        /// Called when the plugin manager unloads our plugin. 
        /// Close the tab if it's active and remove the menu button
        /// </summary>
        /// <param name="inst"></param>
        public void StopPlugin(RadegastInstanceForms inst)
        {
            ActivateTabButton.Dispose();
            if (instance.TabConsole.Tabs.ContainsKey(tabID))
            {
                instance.TabConsole.Tabs[tabID].Close();
            }

            propRequester.OnTick -= propRequester_OnTick;
        }

        private void propRequester_OnTick(int remaining)
        {
            if (InvokeRequired)
            {
                ThreadingHelper.SafeInvoke(this, new Action(() => propRequester_OnTick(remaining)), instance.MonoRuntime);
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Tracking {0} objects", Prims.Count);

            if (remaining > 10)
            {
                sb.AppendFormat(", fetching {0} object names.", remaining);
            }
            else
            {
                sb.Append(".");
            }

            lock (Prims)
            {
                if(lstPrims != null)
                    lstPrims.VirtualListSize = Prims.Count;
            }
            lstPrims?.Invalidate();
        }

        private bool IncludePrim(Primitive prim)
        {
            if (prim.ParentID == 0 && (prim.Flags & PrimFlags.ObjectYouOwner) == PrimFlags.ObjectYouOwner)
            {
                return true;
            }
            else return false;
        }

        private string GetObjectName(Primitive prim, int distance)
        {
            string name = "Loading...";
            string ownerName = "Loading...";

            if (prim.Properties != null)
            {
                name = prim.Properties.Name;
                // prim.Properties.GroupID is the actual group when group owned, not prim.GroupID
                if (UUID.Zero == prim.Properties.OwnerID &&
                    PrimFlags.ObjectGroupOwned == (prim.Flags & PrimFlags.ObjectGroupOwned) &&
                    UUID.Zero != prim.Properties.GroupID)
                {
                    try
                    {
                        var groupName = EventSubscriptionHelper.WaitForEvent<GroupNamesEventArgs, string>(
                            h => client.Groups.GroupNamesReply += h,
                            h => client.Groups.GroupNamesReply -= h,
                            e => e.GroupNames != null && e.GroupNames.ContainsKey(prim.Properties.GroupID),
                            e => { e.GroupNames.TryGetValue(prim.Properties.GroupID, out var v); return v; },
                            5000,
                            null);

                        ownerName = string.IsNullOrEmpty(groupName) ? "Loading..." : groupName;
                    }
                    catch { ownerName = "Loading..."; }
                }
                else
                    ownerName = instance.Names.Get(prim.Properties.OwnerID);
            }

            if (prim.ParentID == client.Self.LocalID)
            {
                return $"{name} attached to {prim.PrimData.AttachmentPoint}";
            }
            else if (ownerName != "Loading...")
            {
                return $"{name} ({distance}m) owned by {ownerName}";
            }
            else
            {
                return $"{name} ({distance}m)";
            }

        }

        private string GetObjectName(Primitive prim)
        {
            int distance = (int)Vector3.Distance(client.Self.SimPosition, prim.Position);
            if (prim.ParentID == client.Self.LocalID) distance = 0;
            return GetObjectName(prim, distance);
        }

        private void lstPrims_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            Primitive prim = null;
            try
            {
                lock (Prims)
                {
                    prim = Prims[e.ItemIndex];
                }
            }
            catch
            {
                e.Item = new ListViewItem();
                return;
            }

            string name = GetObjectName(prim);
            var item = new ListViewItem(name)
            {
                Tag = prim,
                Name = prim.ID.ToString()
            };
            e.Item = item;
        }

        /// <summary>
        /// Hadle case when GridClient is changed (relog haa occured without
        /// quiting Radegast). We need to unregister events from the old client
        /// and re-register them with the new
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void instance_ClientChanged(object sender, ClientChangedEventArgs e)
        {
            UnregisterClientEvents(e.OldClient);
            RegisterClientEvents(e.Client);
        }

        /// <summary>
        /// Registration of all GridClient (libomv) events go here
        /// </summary>
        /// <param name="client"></param>
        private void RegisterClientEvents(GridClient client)
        {
            client.Self.ChatFromSimulator += Self_ChatFromSimulator;
        }

        /// <summary>
        /// Unregistration of GridClient (libomv) events.
        /// Important that this be symetric to RegisterClientEvents() calls
        /// </summary>
        /// <param name="client"></param>
        private void UnregisterClientEvents(GridClient client)
        {
            if (client == null) return;
            client.Self.ChatFromSimulator -= Self_ChatFromSimulator;
        }

        /// <summary>
        /// Handling the click on Plugins -> Demo Tab button
        /// Check if we already have a tab. If we do make it active tab.
        /// If not, create a new tab and make it active.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MenuButtonClicked(object sender, EventArgs e)
        {
            if (instance.TabConsole.TabExists(tabID))
            {
                instance.TabConsole.Tabs[tabID].Select();
            }
            else
            {
                instance.TabConsole.AddTab(tabID, tabLabel, new SimpleBuilder(instance, true));
                instance.TabConsole.Tabs[tabID].Select();
            }
        }
        #endregion Template for GUI radegast tab

        #region Implementation of the custom tab functionality

        private void Self_ChatFromSimulator(object sender, ChatEventArgs e)
        {
            // Boilerplate, make sure to be on the GUI thread
            if (InvokeRequired)
            {
                ThreadingHelper.SafeInvoke(this, new Action(() => Self_ChatFromSimulator(sender, e)), instance.MonoRuntime);
                return;
            }

            //txtChat.Text = e.Message;
        }

        private void btnBuild_Click(object sender, EventArgs e)
        {
            Button btn = sender as Button;

            if (btn == null) return;

            PrimType primType = (PrimType)Enum.Parse(typeof(PrimType), btn.Text);

            BuildAndRez(primType);
        }

        private async void BuildAndRez(PrimType primType)
        {
            float size, distance;

            size = (float)tbox_Size.Value;
            distance = (float)tbox_Distance.Value;

            Primitive.ConstructionData primData = ObjectManager.BuildBasicShape(primType);

            Vector3 rezpos = new Vector3(distance, 0, 0);
            rezpos = client.Self.SimPosition + rezpos * client.Self.Movement.BodyRotation;

            ObjectName = txt_ObjectName.Text;
            primDoneTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            client.Objects.ObjectUpdate += Objects_OnNewPrim;
            client.Objects.AddPrim(client.Network.CurrentSim, primData, UUID.Zero, rezpos, new Vector3(size), Quaternion.Identity);

            var completed = await Task.WhenAny(primDoneTcs.Task, Task.Delay(10000)).ConfigureAwait(false);
            if (completed != primDoneTcs.Task)
            {
                client.Objects.ObjectUpdate -= Objects_OnNewPrim;
                throw new Exception("Rez failed, timed out while creating the prim.");
            }

            ThreadingHelper.SafeInvoke(this, () => txt_ObjectName.Text = ObjectName, instance.MonoRuntime);
        }

        private void Objects_OnNewPrim(object sender, PrimEventArgs e)
        {
            Primitive prim = e.Prim;

            if ((prim.Flags & PrimFlags.CreateSelected) == 0)
                return; // We received an update for an object we didn't create

            if (string.IsNullOrEmpty(ObjectName))
                ObjectName = "SimpleBuilder " + DateTime.Now.ToString("hmmss");

            client.Objects.SetName(client.Network.CurrentSim, prim.LocalID, ObjectName);

            client.Objects.ObjectUpdate -= Objects_OnNewPrim;

            try { primDoneTcs?.TrySetResult(true); } catch { }

            instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + ": Object '"+ txt_ObjectName.Text +"' has been successfully built and rezzed", ChatBufferTextStyle.Normal);

            MessageBox.Show("Object '" + txt_ObjectName.Text + "' has been built and rezzed", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        #endregion Implementation of the custom tab functionality

        private void lstPrims_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstPrims.SelectedIndices.Count > 0)
            {
                selectedPrim = Prims[lstPrims.SelectedIndices[0]];
                getScaleFromSelection();
                getRotFromSelection();
                getPosFromSelection();
            }
            else
            {
                selectedPrim = null;
            }
        }

        private void getScaleFromSelection(){
            if (selectedPrim == null) return;

            scaleX.Value = (decimal)selectedPrim.Scale.X;
            scaleY.Value = (decimal)selectedPrim.Scale.Y;
            scaleZ.Value = (decimal)selectedPrim.Scale.Z;
        }

        private void getRotFromSelection()
        {
            if (selectedPrim == null) return;

            rotX.Value = (decimal)selectedPrim.Rotation.X;
            rotY.Value = (decimal)selectedPrim.Rotation.Y;
            rotZ.Value = (decimal)selectedPrim.Rotation.Z;
        }

        private void getPosFromSelection()
        {
            if (selectedPrim == null) return;

            posX.Value = (decimal)selectedPrim.Position.X;
            posY.Value = (decimal)selectedPrim.Position.Y;
            posZ.Value = (decimal)selectedPrim.Position.Z;
        }

        private void setRotToSelection()
        {
            if (selectedPrim != null)
            {
                selectedPrim.Rotation.X = (float)rotX.Value;
                selectedPrim.Rotation.Y = (float)rotY.Value;
                selectedPrim.Rotation.Z = (float)rotZ.Value;

                client.Objects.SetRotation(client.Network.CurrentSim, selectedPrim.LocalID, selectedPrim.Rotation);
            }
        }

        private void setScaleToSelection()
        {
            if (selectedPrim != null)
            {
                selectedPrim.Scale.X = (float)scaleX.Value;
                selectedPrim.Scale.Y = (float)scaleY.Value;
                selectedPrim.Scale.Z = (float)scaleZ.Value;

                client.Objects.SetScale(client.Network.CurrentSim, selectedPrim.LocalID, selectedPrim.Scale, true, false);
            }
        }

        private void setPositionToSelection()
        {
            if (selectedPrim != null)
            {
                selectedPrim.Position.X = (float)posX.Value;
                selectedPrim.Position.Y = (float)posY.Value;
                selectedPrim.Position.Z = (float)posZ.Value;

                client.Objects.SetPosition(client.Network.CurrentSim, selectedPrim.LocalID, selectedPrim.Position);
            }
        }

        private void button7_Click(object sender, EventArgs e)
        {
            Prims.Clear();
            Vector3 location = client.Self.SimPosition;

            lock (Prims)
            {
                foreach (var kvp in client.Network.CurrentSim.ObjectsPrimitives)
                {
                    var prim = kvp.Value;
                    int distance = (int)Vector3.Distance(prim.Position, location);
                    if (prim.ParentID == client.Self.LocalID)
                    {
                        distance = 0;
                    }
                    if (IncludePrim(prim) && (prim.Position != Vector3.Zero) && (distance < (int)numRadius.Value))
                    {
                        Prims.Add(prim);
                        if (prim.Properties == null)
                        {
                            propRequester.RequestProps(prim);
                        }
                    }
                }
                lstPrims.VirtualListSize = Prims.Count;
            }
            
            lstPrims.Invalidate();
        }

        private void btn_Save_Click(object sender, EventArgs e)
        {
            if (selectedPrim == null) return;

            try
            {
                setScaleToSelection();
                setRotToSelection();
                setPositionToSelection();

                MessageBox.Show("Object saved!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if(selectedPrim.Properties != null)
                    instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + ": Failed to save object '" + selectedPrim.Properties.Name + "'", ChatBufferTextStyle.Error);
                else
                    instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + ": Failed to save object", ChatBufferTextStyle.Error);

                instance.MainForm.TabConsole.DisplayNotificationInChat(pluginName + ":" + ex, ChatBufferTextStyle.Error);
            }
            
        }
    }
}
