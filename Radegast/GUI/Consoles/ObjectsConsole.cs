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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using System.Windows.Forms;
using System.Threading;
using OpenMetaverse;
using System.Threading.Tasks;

namespace Radegast
{
    public partial class ObjectsConsole : UserControl, IContextMenuProvider
    {
        public List<Primitive> Prims = new List<Primitive>();

        private readonly RadegastInstance instance;
        private GridClient client => instance.Client;

        private float searchRadius = 40.0f;
        private readonly PropertiesQueue propRequester;
        private ObjectConsoleFilter filter;
        private readonly ObjectSorter PrimSorter;
        private readonly CancellationTokenSource contentsDownloadCancelToken;

        public Primitive CurrentPrim { get; private set; } = new Primitive();

        public ObjectsConsole(RadegastInstance instance)
        {
            InitializeComponent();
            Disposed += frmObjects_Disposed;

            this.instance = instance;

            propRequester = new PropertiesQueue(instance);
            propRequester.OnTick += propRequester_OnTick;

            btnPointAt.Text = (this.instance.State.IsPointing ? "Unpoint" : "Point At");
            State_SitStateChanged(this, new SitEventArgs(instance.State.IsSitting));

            nudRadius.Value = (decimal)searchRadius;
            nudRadius.ValueChanged += nudRadius_ValueChanged;

            PrimSorter = new ObjectSorter(client.Self);

            lstContents.LargeImageList = frmMain.ResourceImages;
            lstContents.SmallImageList = frmMain.ResourceImages;

            filter = (ObjectConsoleFilter)instance.GlobalSettings["object_console_filter"].AsInteger();
            comboFilter.SelectedIndex = 0;
            try
            {
                comboFilter.SelectedIndex = (int)filter;
            }
            catch { }
            comboFilter.SelectedIndexChanged += (ssender, se) =>
            {
                instance.GlobalSettings["object_console_filter"] = comboFilter.SelectedIndex;
                filter = (ObjectConsoleFilter)comboFilter.SelectedIndex;
                btnRefresh_Click(null, null);
            };

            contentsDownloadCancelToken = new CancellationTokenSource();

            // Callbacks
            instance.Netcom.ClientDisconnected += Netcom_ClientDisconnected;
            instance.State.SitStateChanged += State_SitStateChanged;
            client.Objects.ObjectUpdate += Objects_ObjectUpdate;
            client.Objects.KillObjects += Objects_KillObjects;
            client.Objects.ObjectProperties += Objects_ObjectProperties;
            client.Objects.ObjectPropertiesFamily += Objects_ObjectPropertiesFamily;
            client.Network.SimChanged += Network_SimChanged;
            client.Self.MuteListUpdated += Self_MuteListUpdated;
            instance.Names.NameUpdated += Avatars_UUIDNameReply;
            instance.State.OnWalkStateCanged += State_OnWalkStateCanged;

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void frmObjects_Disposed(object sender, EventArgs e)
        {
            try
            {
                contentsDownloadCancelToken.Cancel();
                contentsDownloadCancelToken.Dispose();
            } catch (ObjectDisposedException) { }

            propRequester.Dispose();
            instance.Netcom.ClientDisconnected -= Netcom_ClientDisconnected;
            instance.State.SitStateChanged -= State_SitStateChanged;
            client.Objects.ObjectUpdate -= Objects_ObjectUpdate;
            client.Objects.KillObjects -= Objects_KillObjects;
            client.Objects.ObjectProperties -= Objects_ObjectProperties;
            client.Objects.ObjectPropertiesFamily -= Objects_ObjectPropertiesFamily;
            client.Network.SimChanged -= Network_SimChanged;
            client.Self.MuteListUpdated -= Self_MuteListUpdated;
            instance.Names.NameUpdated -= Avatars_UUIDNameReply;
            instance.State.OnWalkStateCanged -= State_OnWalkStateCanged;
        }

        private void State_SitStateChanged(object sender, SitEventArgs e)
        {
            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                {
                    BeginInvoke(new MethodInvoker(() => State_SitStateChanged(sender, e)));
                }
                return;
            }

            btnSitOn.Text = (instance.State.IsSitting ? "Stand Up" : "Sit On");
        }

        public void RefreshObjectList()
        {
            btnRefresh_Click(this, EventArgs.Empty);
        }

        public List<Primitive> GetObjectList()
        {
            return new List<Primitive>(Prims);
        }

        private void propRequester_OnTick(int remaining)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(delegate()
                    {
                        propRequester_OnTick(remaining);
                    }
                ));
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
                Prims.Sort(PrimSorter);
                lstPrims.VirtualListSize = Prims.Count;
            }
            lstPrims.Invalidate();
            lblStatus.Text = sb.ToString();
        }

        private void Network_SimChanged(object sender, SimChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() => Network_SimChanged(sender, e)));
                return;
            }

            btnRefresh_Click(null, null);
        }

        private void Avatars_UUIDNameReply(object sender, UUIDNameReplyEventArgs e)
        {
            int minIndex = -1;
            int maxIndex = -1;
            bool updated = false;

            lock (Prims)
            {
                for (int i = 0; i < Prims.Count; i++)
                {
                    Primitive prim = Prims[i];
                    if (prim.Properties != null && e.Names.ContainsKey(prim.Properties.OwnerID))
                    {
                        if (minIndex == -1 || i < minIndex) minIndex = i;
                        if (i > maxIndex) maxIndex = i;
                        updated = true;
                    }
                }
            }

            if (updated)
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new MethodInvoker(() =>
                    {
                        try
                        {
                            lstPrims.RedrawItems(minIndex, maxIndex, true);
                        }
                        catch { }
                    }));
                }
                else
                {
                    lstPrims.RedrawItems(minIndex, maxIndex, true);
                }
            }
        }

        private void UpdateProperties(Primitive.ObjectProperties props)
        {
            lock (Prims)
            {
                for (int i = 0; i < Prims.Count; i++)
                {
                    if (Prims[i].ID == props.ObjectID)
                    {
                        Prims[i].Properties = props;
                        try
                        {
                            lstPrims.RedrawItems(i, i, true);
                        }
                        catch { }
                        break;
                    }
                }
            }

            lock (lstChildren.Items)
            {
                if (lstChildren.Items.ContainsKey(props.ObjectID.ToString()))
                {
                    if (lstChildren.Items[props.ObjectID.ToString()].Tag is Primitive prim)
                    {
                        prim.Properties = props;
                        lstChildren.Items[props.ObjectID.ToString()].Text = prim.Properties.Name;
                    }
                }
            }

            if (props.ObjectID == CurrentPrim.ID)
            {
                UpdateCurrentObject(false);
            }
        }

        private void Objects_ObjectProperties(object sender, ObjectPropertiesEventArgs e)
        {
            if (e.Simulator.Handle != client.Network.CurrentSim.Handle)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(delegate() { Objects_ObjectProperties(sender, e); }));
                return;
            }

            UpdateProperties(e.Properties);
        }

        private void Objects_ObjectPropertiesFamily(object sender, ObjectPropertiesFamilyEventArgs e)
        {
            if (e.Simulator.Handle != client.Network.CurrentSim.Handle)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(delegate() { Objects_ObjectPropertiesFamily(sender, e); }));
                return;
            }

            UpdateProperties(e.Properties);
        }


        private void UpdateObjectContents()
        {
            lstContents.Items.Clear();
            ListViewItem entry = new ListViewItem();
            entry.SubItems.Add("Loading...");
            lstContents.Items.Add(entry);

            Task contentsTask = Task.Run(() =>
            {
                lstContents.Tag = CurrentPrim;
                List<InventoryBase> items =
                    client.Inventory.GetTaskInventory(CurrentPrim.ID, CurrentPrim.LocalID, TimeSpan.FromSeconds(30));
                lstContents.Invoke(new MethodInvoker(() => UpdateContentsList(items)));
            }, contentsDownloadCancelToken.Token);
        }

        private void UpdateContentsList(List<InventoryBase> items)
        {
            //object inventory in items List
            lstContents.Items.Clear();
            btnOpen.Enabled = false;
            Primitive prim = (Primitive)lstContents.Tag;

            //subitems = items;
            if (items == null)
            {
                ListViewItem entry = new ListViewItem();
                entry.SubItems.Add("(failed to fetch contents)");
                entry.SubItems[0].Font = new System.Drawing.Font(entry.SubItems[0].Font, System.Drawing.FontStyle.Italic);
                lstContents.Items.Add(entry);
            }
            else
            {
                if (items.Count == 0)
                {
                    ListViewItem entry = new ListViewItem();
                    entry.SubItems.Add("(empty object)");
                    entry.SubItems[0].Font = new System.Drawing.Font(entry.SubItems[0].Font, System.Drawing.FontStyle.Italic);
                    lstContents.Items.Add(entry);
                }
                else
                {
                    btnOpen.Enabled = prim.Properties != null && prim.Properties.OwnerID == client.Self.AgentID;

                    foreach (var i in items)
                    {
                        if (i is InventoryItem item)
                        {
                            ListViewItem entry = new ListViewItem
                            {
                                ImageIndex =
                                    InventoryConsole.GetItemImageIndex(item.AssetType.ToString().ToLower()),
                                Tag = item
                            };


                            ListViewItem.ListViewSubItem sub;

                            entry.SubItems.Add(sub = new ListViewItem.ListViewSubItem()
                            {
                                Name = item.UUID.ToString(),
                                Text = item.Name
                            });

                            if ((item.Permissions.OwnerMask & PermissionMask.Modify) == 0)
                                sub.Text += " (no modify)";

                            if ((item.Permissions.OwnerMask & PermissionMask.Copy) == 0)
                                sub.Text += " (no copy)";

                            if ((item.Permissions.OwnerMask & PermissionMask.Transfer) == 0)
                                sub.Text += " (no transfer)";

                            sub.Text += $" ({item.InventoryType})";
                            entry.ToolTipText = sub.Text;

                            lstContents.Items.Add(entry);
                        }
                    }
                }
            }
        }

        private void ctxContents_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = false;

            if (!(lstContents.Tag is Primitive prim))
            {
                e.Cancel = true;
                return;
            }

            ctxContents.Items.Clear();
            bool canModify = (prim.Flags & PrimFlags.ObjectModify) == PrimFlags.ObjectModify;

            if (lstContents.SelectedItems.Count == 1)
            {
                ListViewItem entry = lstContents.SelectedItems[0];

                if (entry.Tag is InventoryItem item)
                {
                    if (canModify)
                    {
                        switch (item.InventoryType)
                        {
                            case InventoryType.Notecard:
                                ctxContents.Items.Add("Edit Notecard", null, (sd, ev) =>
                                    {
                                        InventoryNotecard inv = (InventoryNotecard)entry.Tag;
                                        new Notecard(instance, inv, prim) { Detached = true };
                                    }
                                );
                                break;

                            case InventoryType.LSL:
                                ctxContents.Items.Add("Edit Script", null, (sd, ev) =>
                                    {
                                        InventoryLSL inv = (InventoryLSL)entry.Tag;
                                        new ScriptEditor(instance, inv, prim) { Detached = true };
                                    }
                                );
                                break;
                        }

                    }
                }
            }

            if (lstContents.SelectedItems.Count > 0)
            {
                ctxContents.Items.Add("Delete", null, (sd, ev) =>
                {
                    foreach (ListViewItem i in lstContents.SelectedItems)
                    {
                        if (i.Tag is InventoryItem it)
                        {
                            client.Inventory.RemoveTaskInventory(prim.LocalID, it.UUID, client.Network.CurrentSim);
                        }
                    }
                });
            }

            if (ctxContents.Items.Count != 0)
            {
                ctxContents.Items.Add(new ToolStripSeparator());
            }

            if ((canModify || (prim.Flags & PrimFlags.AllowInventoryDrop) == PrimFlags.AllowInventoryDrop) && instance.InventoryClipboard != null)
            {
                if (instance.InventoryClipboard.Item is InventoryItem)
                {
                    ctxContents.Items.Add("Paste from Inventory", null, (sd, ev) =>
                    {
                        if (instance.InventoryClipboard.Item is InventoryLSL)
                        {
                            client.Inventory.CopyScriptToTask(prim.LocalID, (InventoryItem)instance.InventoryClipboard.Item, true);
                        }
                        else
                        {
                            client.Inventory.UpdateTaskInventory(prim.LocalID, (InventoryItem)instance.InventoryClipboard.Item);
                        }
                    });
                }
                else if (instance.InventoryClipboard.Item is InventoryFolder)
                {
                    ctxContents.Items.Add("Paste Folder Contents", null, (sd, ev) =>
                    {
                        foreach (InventoryBase oldItem in client.Inventory.Store.GetContents(
                                     (InventoryFolder)instance.InventoryClipboard.Item))
                        {
                            if (!(oldItem is InventoryItem item)) continue;

                            if (item is InventoryLSL)
                            {
                                client.Inventory.CopyScriptToTask(prim.LocalID, item, true);
                            }
                            else
                            {
                                client.Inventory.UpdateTaskInventory(prim.LocalID, item);
                            }
                        }
                    });
                }
            }

            if (canModify)
            {
                ctxContents.Items.Add("Open (copy all to inventory)", null, OpenObject);
            }

            ctxContents.Items.Add("Close", null, btnCloseContents_Click);
        }

        private void OpenObject(object sender, EventArgs e)
        {
            Primitive prim = lstContents.Tag as Primitive;
            if (prim?.Properties == null) return;

            List<InventoryItem> items = new List<InventoryItem>();

            foreach (ListViewItem item in lstContents.Items)
            {
                if (item.Tag is InventoryItem tag)
                    items.Add(tag);
            }

            if (items.Count == 0) return;

            UUID folderID = client.Inventory.CreateFolder(client.Inventory.Store.RootFolder.UUID, prim.Properties.Name);

            foreach (var item in items)
            {
                client.Inventory.MoveTaskInventory(prim.LocalID, item.UUID, folderID, client.Network.CurrentSim);
            }

            instance.TabConsole.DisplayNotificationInChat(
                $"Items from object contents copied to new inventory folder {prim.Properties.Name}");

        }

        private void UpdateCurrentObject(bool updateContents = true)
        {
            if (CurrentPrim.Properties == null) return;

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(delegate() { UpdateCurrentObject(updateContents); }));
                return;
            }

            // currentItem.Text = GetObjectName(currentPrim);

            txtObjectName.Text = CurrentPrim.Properties.Name;
            txtDescription.Text = CurrentPrim.Properties.Description;
            if ((CurrentPrim.Flags & PrimFlags.ObjectModify) == PrimFlags.ObjectModify)
            {
                txtObjectName.ReadOnly = txtDescription.ReadOnly = false;
                gbxObjectDetails.Text = "Object details (you can modify)";
            }
            else
            {
                txtObjectName.ReadOnly = txtDescription.ReadOnly = true;
                gbxObjectDetails.Text = "Object details";
            }

            txtHover.Text = CurrentPrim.Text;
            txtOwner.AgentID = CurrentPrim.Properties.OwnerID;
            txtCreator.AgentID = CurrentPrim.Properties.CreatorID;

            Permissions p = CurrentPrim.Properties.Permissions;
            cbOwnerModify.Checked = (p.OwnerMask & PermissionMask.Modify) != 0;
            cbOwnerCopy.Checked = (p.OwnerMask & PermissionMask.Copy) != 0;
            cbOwnerTransfer.Checked = (p.OwnerMask & PermissionMask.Transfer) != 0;

            cbNextOwnModify.CheckedChanged -= cbNextOwnerUpdate_CheckedChanged;
            cbNextOwnCopy.CheckedChanged -= cbNextOwnerUpdate_CheckedChanged;
            cbNextOwnTransfer.CheckedChanged -= cbNextOwnerUpdate_CheckedChanged;

            cbNextOwnModify.Checked = (p.NextOwnerMask & PermissionMask.Modify) != 0;
            cbNextOwnCopy.Checked = (p.NextOwnerMask & PermissionMask.Copy) != 0;
            cbNextOwnTransfer.Checked = (p.NextOwnerMask & PermissionMask.Transfer) != 0;

            cbNextOwnModify.CheckedChanged += cbNextOwnerUpdate_CheckedChanged;
            cbNextOwnCopy.CheckedChanged += cbNextOwnerUpdate_CheckedChanged;
            cbNextOwnTransfer.CheckedChanged += cbNextOwnerUpdate_CheckedChanged;

            if (CurrentPrim.Properties.OwnerID == client.Self.AgentID)
            {
                cbNextOwnModify.Enabled = cbNextOwnCopy.Enabled = cbNextOwnTransfer.Enabled = true;
            }
            else
            {
                cbNextOwnModify.Enabled = cbNextOwnCopy.Enabled = cbNextOwnTransfer.Enabled = false;
            }

            txtPrims.Text = (client.Network.CurrentSim.ObjectsPrimitives.FindAll(
                prim => prim.ParentID == CurrentPrim.LocalID || prim.LocalID == CurrentPrim.LocalID)).Count.ToString();

            btnPay.Enabled = (CurrentPrim.Flags & PrimFlags.Money) != 0;

            if (CurrentPrim.Properties.SaleType != SaleType.Not)
            {
                btnBuy.Text = $"Buy $L{CurrentPrim.Properties.SalePrice}";
                btnBuy.Enabled = true;
            }
            else
            {
                btnBuy.Text = "Buy";
                btnBuy.Enabled = false;
            }

            if (gbxContents.Visible /*&& updateContents*/)
            {
                UpdateObjectContents();
            }

            UpdateMuteButton();
        }

        private void Netcom_ClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                {
                    BeginInvoke(new MethodInvoker(() => Netcom_ClientDisconnected(sender, e)));
                }
                return;
            }

            if (instance.TabConsole.TabExists("objects"))
            {
                instance.TabConsole.Tabs["objects"].Close();
            }
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
                    AutoResetEvent nameReceivedSignal = new AutoResetEvent(false);
                    EventHandler<GroupNamesEventArgs> cbGroupName = delegate(object sender, GroupNamesEventArgs e)
                    {
                        if (e.GroupNames.ContainsKey(prim.Properties.GroupID))
                        {
                            e.GroupNames.TryGetValue(prim.Properties.GroupID, out ownerName);
                            if (string.IsNullOrEmpty(ownerName))
                                ownerName = "Loading...";
                            nameReceivedSignal.Set();
                        }
                    };
                    client.Groups.GroupNamesReply += cbGroupName;
                    client.Groups.RequestGroupName(prim.Properties.GroupID);
                    nameReceivedSignal.WaitOne(5000, false);
                    nameReceivedSignal.Close();
                    client.Groups.GroupNamesReply -= cbGroupName;
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

        private void AddPrim(Primitive prim)
        {
            lock (Prims)
            {
                string name = GetObjectName(prim);
                if (!Prims.Contains(prim) 
                    && txtSearch.IsHandleCreated 
                    && (txtSearch.Text.Length == 0 || name.ToLower().Contains(txtSearch.Text.ToLower())))
                {
                    Prims.Add(prim);
                    if (prim.Properties == null)
                    {
                        propRequester.RequestProps(prim);
                    }
                }
            }
        }

        private void Objects_ObjectUpdate(object sender, PrimEventArgs e)
        {
            if (e.Simulator.Handle != client.Network.CurrentSim.Handle || e.Prim.Position == Vector3.Zero || e.Prim is Avatar) return;

            if (IncludePrim(e.Prim))
            {
                if (e.Prim.ParentID == 0)
                {
                    int distance = (int)Vector3.Distance(client.Self.SimPosition, e.Prim.Position);
                    if (distance < searchRadius)
                    {
                        AddPrim(e.Prim);
                    }
                }
                else if (e.Prim.ParentID == client.Self.LocalID)
                {
                    AddPrim(e.Prim);
                }
            }

            if (e.Prim.ID == CurrentPrim.ID)
            {
                if (CurrentPrim.Properties != null)
                {
                    UpdateCurrentObject(false);
                }
            }
        }

        private void Objects_KillObjects(object sender, KillObjectsEventArgs e)
        {
            if (e.Simulator.Handle != client.Network.CurrentSim.Handle) return;

            lock (Prims)
            {
                List<Primitive> killed = Prims.FindAll((p) =>
                {
                    return e.ObjectLocalIDs.Any(t => p.LocalID == t);
                });

                foreach (Primitive prim in killed)
                {
                    Prims.Remove(prim);
                }
            }

            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(() =>
                {
                    lstPrims.VirtualListSize = Prims.Count;
                    lstPrims.Invalidate();
                }));
            }
            else
            {
                lstPrims.VirtualListSize = Prims.Count;
                lstPrims.Invalidate();
            }

        }

        private bool IncludePrim(Primitive prim)
        {
            if ((prim.ParentID == client.Self.LocalID) && (filter == ObjectConsoleFilter.Attached || filter == ObjectConsoleFilter.Both))
            {
                return true;
            }
            else if ((prim.ParentID == 0) && (filter == ObjectConsoleFilter.Rezzed || filter == ObjectConsoleFilter.Both))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private void AddAllObjects()
        {
            Vector3 location = client.Self.SimPosition;

            lock (Prims)
            {
                /*
                var prims = client.Network.CurrentSim.ObjectsPrimitives.FindAll(prim =>
                {
                    return ((prim.ParentID == client.Self.LocalID) && (filter == ObjectConsoleFilter.Attached || filter == ObjectConsoleFilter.Both)) ||
                        ((prim.ParentID == 0) && (filter == ObjectConsoleFilter.Rezzed || filter == ObjectConsoleFilter.Both));
                });
                */
                client.Network.CurrentSim.ObjectsPrimitives.ForEach(prim =>
                {
                    int distance = (int)Vector3.Distance(prim.Position, location);
                    if (prim.ParentID == client.Self.LocalID)
                    {
                        distance = 0;
                    }
                    if (IncludePrim(prim) &&
                        (prim.Position != Vector3.Zero) &&
                        (distance < searchRadius) &&
                        (txtSearch.Text.Length == 0 || 
                         (prim.Properties != null && prim.Properties.Name.ToLower().Contains(txtSearch.Text.ToLower()))) && //root prims and attachments only
                        !Prims.Contains(prim))
                    {
                        Prims.Add(prim);
                        if (prim.Properties == null)
                        {
                            propRequester.RequestProps(prim);
                        }
                    }
                });
                Prims.Sort(PrimSorter);
                lstPrims.VirtualListSize = Prims.Count;
                lstPrims.Invalidate();
            }
        }

        private void btnPointAt_Click(object sender, EventArgs e)
        {
            if (btnPointAt.Text == "Point At")
            {
                instance.State.SetPointing(CurrentPrim, 3);
                btnPointAt.Text = "Unpoint";
            }
            else if (btnPointAt.Text == "Unpoint")
            {
                instance.State.UnSetPointing();
                btnPointAt.Text = "Point At";
            }
        }

        private void btnSitOn_Click(object sender, EventArgs e)
        {
            instance.State.SetSitting(!instance.State.IsSitting, CurrentPrim.ID);
        }

        private void btnTouch_Click(object sender, EventArgs e)
        {
            client.Self.Touch(CurrentPrim.LocalID);
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            btnRefresh_Click(null, null);
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            txtSearch.Clear();
            txtSearch.Select();
            btnRefresh_Click(null, null);
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            instance.State.SetDefaultCamera();
            Cursor.Current = Cursors.WaitCursor;
            Prims.Clear();
            AddAllObjects();

            Cursor.Current = Cursors.Default;
        }

        private void lstPrims_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstPrims.SelectedIndices.Count == 1)
            {
                lock (Prims)
                {
                    try
                    {
                        CurrentPrim = Prims[lstPrims.SelectedIndices[0]];
                    }
                    catch
                    {
                        gbxInworld.Enabled = false;
                        return;
                    }
                }

                gbxInworld.Enabled = true;
                btnBuy.Tag = CurrentPrim;

                if (CurrentPrim.Properties == null || CurrentPrim.OwnerID == UUID.Zero || (CurrentPrim.Properties != null && CurrentPrim.Properties.CreatorID == UUID.Zero))
                {
                    client.Objects.SelectObject(client.Network.CurrentSim, CurrentPrim.LocalID);
                }

                UpdateCurrentObject();
                UpdateChildren();
            }
            else
            {
                lstChildren.Visible = false;
                gbxInworld.Enabled = false;
            }
        }

        private void lstChildren_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstChildren.SelectedItems.Count == 1)
            {
                gbxInworld.Enabled = true;
                CurrentPrim = lstChildren.SelectedItems[0].Tag as Primitive;
                btnBuy.Tag = CurrentPrim;

                if (CurrentPrim != null && (CurrentPrim.Properties == null || (CurrentPrim.Properties != null && CurrentPrim.Properties.CreatorID == UUID.Zero)))
                {
                    client.Objects.SelectObject(client.Network.CurrentSim, CurrentPrim.LocalID);
                }

                UpdateCurrentObject();
            }
            else
            {
                gbxInworld.Enabled = false;
            }
        }

        private void UpdateChildren()
        {
            if (CurrentPrim == null) return;
            var prims = client.Network.CurrentSim.ObjectsPrimitives.FindAll(p => p.ParentID == CurrentPrim.LocalID);
            if (prims == null || prims.Count == 0) return;
            List<uint> toGetNames = new List<uint>();

            lstChildren.BeginUpdate();
            lock (lstChildren.Items)
            {
                lstChildren.Items.Clear();
                foreach (var prim in prims)
                {
                    var item = new ListViewItem();

                    if (prim.Properties != null)
                    {
                        item.Text = prim.Properties.Name;
                    }
                    else
                    {
                        item.Text = "Loading...";
                        toGetNames.Add(prim.LocalID);
                    }

                    item.Tag = prim;
                    item.Name = prim.ID.ToString();
                    lstChildren.Items.Add(item);
                }
            }
            lstChildren.EndUpdate();
            lstChildren.Visible = true;
            if (toGetNames.Count > 0)
            {
                client.Objects.SelectObjects(client.Network.CurrentSim, toGetNames.ToArray(), true);
            }
        }

        private void btnPay_Click(object sender, EventArgs e)
        {
            (new frmPay(instance, CurrentPrim.ID, CurrentPrim.Properties.Name, true)).ShowDialog();
        }

        private void btnDetach_Click(object sender, EventArgs e)
        {
            var toDetach = CurrentOutfitFolder.GetAttachmentItem(CurrentPrim);
            if (toDetach == UUID.Zero) return;

            if (client.Inventory.Store.Contains(toDetach))
            {
                instance.COF.Detach(client.Inventory.Store[toDetach] as InventoryItem);
            }
        }

        private void btnView_Click(object sender, EventArgs e)
        {
            if (CurrentPrim.PrimData.PCode != PCode.Prim)
            {
                instance.TabConsole.DisplayNotificationInChat("Cannot display objects of that type", ChatBufferTextStyle.Error);
                return;
            }

            Rendering.frmPrimWorkshop pw = new Rendering.frmPrimWorkshop(instance, CurrentPrim.LocalID);
            pw.Show();
        }

        private void nudRadius_ValueChanged(object sender, EventArgs e)
        {
            searchRadius = (float)nudRadius.Value;
            btnRefresh_Click(null, null);
        }

        private void btnBuy_Click(object sender, EventArgs e)
        {
            if (lstPrims.SelectedIndices.Count != 1) return;
            btnBuy.Enabled = false;
            client.Objects.BuyObject(client.Network.CurrentSim, CurrentPrim.LocalID, 
                CurrentPrim.Properties.SaleType, CurrentPrim.Properties.SalePrice, client.Self.ActiveGroup, 
                client.Inventory.FindFolderForType(AssetType.Object));
        }

        private void rbDistance_CheckedChanged(object sender, EventArgs e)
        {
            if (rbDistance.Checked)
            {
                PrimSorter.SortByName = false;
                lock (Prims) Prims.Sort(PrimSorter);
                lstPrims.Invalidate();
            }
        }

        private void rbName_CheckedChanged(object sender, EventArgs e)
        {
            if (rbName.Checked)
            {
                PrimSorter.SortByName = true;
                lock (Prims) Prims.Sort(PrimSorter);
                lstPrims.Invalidate();
            }
        }

        private void btnTurnTo_Click(object sender, EventArgs e)
        {
            if (lstPrims.SelectedIndices.Count != 1) return;
            client.Self.Movement.TurnToward(CurrentPrim.Position);
        }

        private void btnWalkTo_Click(object sender, EventArgs e)
        {
            if (lstPrims.SelectedIndices.Count != 1) return;

            if (instance.State.IsWalking)
            {
                instance.State.EndWalking();
            }
            else
            {
                instance.State.WalkTo(CurrentPrim);
            }
        }

        private void State_OnWalkStateCanged(bool walking)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(delegate() { State_OnWalkStateCanged(walking); }));
                return;
            }

            if (walking)
            {
                btnWalkTo.Text = "Stop";
            }
            else
            {
                btnWalkTo.Text = "Walk to";
                btnRefresh_Click(null, null);
            }
        }

        private void nudRadius_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
            }
        }

        private void nudRadius_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
            }
        }

        private void ctxMenuObjects_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = false;

            if (lstPrims.SelectedIndices.Count != 1)
            {
                e.Cancel = true;
                return;
            }

            ctxMenuObjects.Items.Clear();
            ctxMenuObjects.Items.Add("Click/Touch", null, btnTouch_Click);

            if (CurrentPrim.ParentID == client.Self.LocalID)
                ctxMenuObjects.Items.Add("Detach", null, btnDetach_Click);

            if ((CurrentPrim.Flags & PrimFlags.Money) != 0)
                ctxMenuObjects.Items.Add("Pay", null, btnPay_Click);

            if (CurrentPrim.Properties != null && CurrentPrim.Properties.SaleType != SaleType.Not)
                ctxMenuObjects.Items.Add($"Buy for ${CurrentPrim.Properties.SalePrice}", null, btnBuy_Click);

            if (gbxInworld.Visible)
                ctxMenuObjects.Items.Add("Show Contents", null, btnContents_Click);
            else
                ctxMenuObjects.Items.Add("Hide Contents", null, btnCloseContents_Click);

            ctxMenuObjects.Items.Add(instance.State.IsSitting ? "Stand Up" : "Sit On", null, btnSitOn_Click);
            ctxMenuObjects.Items.Add("Turn To", null, btnTurnTo_Click);
            ctxMenuObjects.Items.Add("Walk To", null, btnWalkTo_Click);
            ctxMenuObjects.Items.Add(instance.State.IsPointing ? "Unpoint" : "Point At", null, btnPointAt_Click);
            ctxMenuObjects.Items.Add("3D View", null, btnView_Click);
            ctxMenuObjects.Items.Add("Take", null, btnTake_Click);
            ctxMenuObjects.Items.Add("Delete", null, btnDelete_Click);
            ctxMenuObjects.Items.Add("Return", null, btnReturn_Click);

            if (CurrentPrim.Properties != null)
            {
                if (CurrentPrim.Properties.CreatorID == client.Self.AgentID &&
                    CurrentPrim.Properties.OwnerID == client.Self.AgentID)
                {
                    ctxMenuObjects.Items.Add("Export", null, btnExport_Click);
                }
            }

            if (CurrentPrim.Properties != null)
            {
                bool isMuted = null != client.Self.MuteList.Find(me => me.Type == MuteType.Object && me.ID == CurrentPrim.ID);

                if (isMuted)
                {
                    ctxMenuObjects.Items.Add("Unmute", null, (a, b) =>
                        {
                            client.Self.RemoveMuteListEntry(CurrentPrim.ID, CurrentPrim.Properties.Name);
                        });
                }
                else
                {
                    ctxMenuObjects.Items.Add("Mute", null, (a, b) =>
                    {
                        client.Self.UpdateMuteListEntry(MuteType.Object, CurrentPrim.ID, CurrentPrim.Properties.Name);
                    });
                }
            }

            //if (currentPrim.MediaURL != null && currentPrim.MediaURL.StartsWith("x-mv:"))
            //{
            //    ctxMenuObjects.Items.Add("Test", null, (object menuSender, EventArgs menuE) =>
            //        {
            //            client.Objects.RequestObjectMedia(currentPrim.ID, client.Network.CurrentSim, (bool success, string version, MediaEntry[] faceMedia) =>
            //                {
            //                    int foo = 1;
            //                }
            //            );
            //        }
            //    );
            //}

            instance.ContextActionManager.AddContributions(ctxMenuObjects, CurrentPrim);
        }

        public RadegastContextMenuStrip GetContextMenu()
        {
            return ctxMenuObjects;
        }

        private void btnTake_Click(object sender, EventArgs e)
        {
            instance.MediaManager.PlayUISound(UISounds.ObjectDelete);
            client.Inventory.RequestDeRezToInventory(CurrentPrim.LocalID);
        }

        private void btnDelete_Click(object sender, EventArgs e)
        {
            if (CurrentPrim.Properties != null && CurrentPrim.Properties.OwnerID != client.Self.AgentID)
                btnReturn_Click(sender, e);
            else
            {
                instance.MediaManager.PlayUISound(UISounds.ObjectDelete);
                client.Inventory.RequestDeRezToInventory(CurrentPrim.LocalID, 
                    DeRezDestination.AgentInventoryTake, client.Inventory.FindFolderForType(FolderType.Trash), UUID.Random());
            }
        }

        private void btnReturn_Click(object sender, EventArgs e)
        {
            instance.MediaManager.PlayUISound(UISounds.ObjectDelete);
            client.Inventory.RequestDeRezToInventory(CurrentPrim.LocalID, 
                DeRezDestination.ReturnToOwner, UUID.Zero, UUID.Random());
        }

        private void btnCloseContents_Click(object sender, EventArgs e)
        {
            gbxContents.Hide();
            gbxInworld.Show();
            lstPrims.Focus();
        }

        private void btnContents_Click(object sender, EventArgs e)
        {
            gbxInworld.Hide();
            gbxContents.Show();
            UpdateObjectContents();
            lstContents.Focus();
        }

        private void btnExport_Click(object sender, EventArgs e)
        {
            instance.MainForm.DisplayExportConsole(CurrentPrim.LocalID);
        }

        private void lstContents_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (lstContents.SelectedItems.Count != 1) return;

            ListViewItem contentItem = lstContents.SelectedItems[0];

            if (contentItem.Tag is InventoryLSL inv1)
            {
                Primitive prim = (Primitive)lstContents.Tag;
                new ScriptEditor(instance, inv1, prim) { Detached = true };
            }
            else if (contentItem.Tag is InventoryNotecard inv)
            {
                Primitive prim = (Primitive)lstContents.Tag;
                new Notecard(instance, inv, prim) { Detached = true };
            }

        }

        private void lstContents_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                lstContents_MouseDoubleClick(null, null);

                e.SuppressKeyPress = e.Handled = true;
            }
            //else if (e.KeyCode == Keys.Apps)
            //{
            //    Point pos = new Point(50, 30);

            //    if (lstContents.SelectedItems.Count > 0)
            //    {
            //        pos = lstContents.SelectedItems[0].Position;
            //        pos.Y += 10;
            //        pos.X += 120;
            //    }

            //    ctxContents.Show(lstContents, pos);

            //    e.SuppressKeyPress = e.Handled = true;
            //    return;
            //}
        }

        private void Self_MuteListUpdated(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                {
                    BeginInvoke(new MethodInvoker(() => Self_MuteListUpdated(sender, e)));
                }
                return;
            }

            if (lstPrims.SelectedIndices.Count != 1) return;

            UpdateMuteButton();
        }

        private void UpdateMuteButton()
        {
            bool isMuted = null != client.Self.MuteList.Find(me => me.Type == MuteType.Object && me.ID == CurrentPrim.ID);

            btnMute.Text = isMuted ? "Unmute" : "Mute";
        }

        private void btnMute_Click(object sender, EventArgs e)
        {
            if (lstPrims.SelectedIndices.Count != 1) return;

            if (CurrentPrim.Properties == null) return;

            if (btnMute.Text == "Mute")
            {
                client.Self.UpdateMuteListEntry(MuteType.Object, CurrentPrim.ID, CurrentPrim.Properties.Name);
            }
            else
            {
                client.Self.RemoveMuteListEntry(CurrentPrim.ID, CurrentPrim.Properties.Name);
            }
        }

        private void txtObjectName_Leave(object sender, EventArgs e)
        {
            if (CurrentPrim == null) return;
            if (CurrentPrim.Properties == null || (CurrentPrim.Properties != null && CurrentPrim.Properties.Name != txtObjectName.Text))
            {
                client.Objects.SetName(client.Network.CurrentSim, CurrentPrim.LocalID, txtObjectName.Text);
            }
        }

        private void txtDescription_Leave(object sender, EventArgs e)
        {
            if (CurrentPrim == null) return;
            if (CurrentPrim.Properties == null || (CurrentPrim.Properties != null && CurrentPrim.Properties.Description != txtDescription.Text))
            {
                client.Objects.SetDescription(client.Network.CurrentSim, CurrentPrim.LocalID, txtDescription.Text);
            }
        }

        private void cbNextOwnerUpdate_CheckedChanged(object sender, EventArgs e)
        {
            CheckBox cb = (CheckBox)sender;
            PermissionMask pm = PermissionMask.None;

            if (cb == cbNextOwnCopy) pm = PermissionMask.Copy;
            if (cb == cbNextOwnModify) pm = PermissionMask.Modify;
            if (cb == cbNextOwnTransfer) pm = PermissionMask.Transfer;

            if (pm == PermissionMask.None) return;

            client.Objects.SetPermissions(client.Network.CurrentSim, new List<uint>() { CurrentPrim.LocalID }, PermissionWho.NextOwner, pm, cb.Checked);
        }

        private void lstPrims_Enter(object sender, EventArgs e)
        {
            lstPrims_SelectedIndexChanged(sender, EventArgs.Empty);
        }

        private void lstChildren_Enter(object sender, EventArgs e)
        {
            lstChildren_SelectedIndexChanged(sender, EventArgs.Empty);
        }

        private void lstPrims_DoubleClick(object sender, EventArgs e)
        {
            btnView.PerformClick();
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
            var item = new ListViewItem(name) {Tag = prim, Name = prim.ID.ToString()};
            e.Item = item;
        }

    }

    public enum ObjectConsoleFilter
    {
        Rezzed = 0,
        Attached = 1,
        Both = 2
    }

    public class ObjectSorter : IComparer<Primitive>
    {
        private readonly AgentManager me;

        public bool SortByName { get; set; } = false;

        public ObjectSorter(AgentManager me)
        {
            this.me = me;
        }

        private int NameCompare(Primitive prim1, Primitive prim2)
        {
            if (prim1.Properties == null && prim2.Properties == null)
            {
                return 0;
            }
            else if (prim1.Properties != null && prim2.Properties == null)
            {
                return -1;
            }
            else if (prim1.Properties == null && prim2.Properties != null)
            {
                return 1;
            }

            return string.CompareOrdinal(prim1.Properties.Name, prim2.Properties.Name);
        }
        //this routine should return -1 if xy and 0 if x==y.
        // for our sample we'll just use string comparison
        public int Compare(Primitive prim1, Primitive prim2)
        {
            if (SortByName)
            {
                return NameCompare(prim1, prim2);
            }

            float dist1 = prim1.ParentID == me.LocalID ? 0 : Vector3.Distance(me.SimPosition, prim1.Position);
            float dist2 = prim2.ParentID == me.LocalID ? 0 : Vector3.Distance(me.SimPosition, prim2.Position);

            if (dist1 == dist2)
            {
                return NameCompare(prim1, prim2);
            }
            else
            {
                if (dist1 < dist2)
                {
                    return -1;
                }
                return 1;
            }
        }
    }

    public class PropertiesQueue : IDisposable
    {
        private readonly object sync = new object();
        private RadegastInstance instance;
        private System.Timers.Timer qTimer;
        private Queue<Primitive> props = new Queue<Primitive>();

        public delegate void TickCallback(int remaining);
        public event TickCallback OnTick;

        public PropertiesQueue(RadegastInstance instance)
        {
            this.instance = instance;
            qTimer = new System.Timers.Timer(2500) {Enabled = true};
            qTimer.Elapsed += qTimer_Elapsed;
        }

        public void RequestProps(Primitive prim)
        {
            lock (sync)
            {
                if (!props.Contains(prim))
                {
                    props.Enqueue(prim);
                }
            }
        }

        private void qTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            lock (sync)
            {
                for (int i = 0; i < 25 && props.Count > 0; i++)
                {
                    Primitive prim = props.Dequeue();
                    if (prim.ParentID == 0)
                    {
                        instance.Client.Objects.RequestObjectPropertiesFamily(
                            instance.Client.Network.CurrentSim, prim.ID, true);
                    }
                    else
                    {
                        instance.Client.Objects.SelectObject(instance.Client.Network.CurrentSim,
                            prim.LocalID, true);
                    }
                }

                OnTick?.Invoke(props.Count);
            }
        }

        public void Dispose()
        {
            if (qTimer != null)
            {
                qTimer.Elapsed -= qTimer_Elapsed;
                qTimer.Enabled = false;
                qTimer = null;
            }
            props = null;
            instance = null;
        }
    }

}