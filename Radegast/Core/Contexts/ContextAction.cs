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
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenMetaverse;
using OpenMetaverse.Assets;

namespace Radegast
{
    public class ContextAction : IContextAction
    {
        public Type ContextType;
        public bool ExactContextType = false;
        public virtual string Label { get; set; }
        public EventHandler Handler;
        protected RadegastInstanceForms instance;
        public bool ExecAsync = true;
        public bool Enabled { get; set; }
        public virtual object DeRef(object o)
        {
            if (o is Control)
            {
                Control control = (Control) o;
                if (control.Tag != null) return control.Tag;
                if (!string.IsNullOrEmpty(control.Text)) return control.Text;
                if (!string.IsNullOrEmpty(control.Name)) return control.Name;
            }
            else if (o is ListViewItem)
            {
                ListViewItem control = (ListViewItem) o;
                if (control.Tag != null) return control.Tag;
                if (!string.IsNullOrEmpty(control.Name)) return control.Name;
                if (!string.IsNullOrEmpty(control.Text)) return control.Text;
            }
            return o;
        }
        protected ContextActionsManager ContextActionManager => Instance.ContextActionManager;

        public virtual RadegastInstanceForms Instance
        {
            get => instance;
            set => instance = value;
        }

        protected virtual GridClient Client => Instance.Client;

        public ContextAction(RadegastInstanceForms inst)
        {
            Enabled = true;
            instance = inst;
        }

        public virtual bool IsEnabled(object target)
        {
            return Enabled && Contributes(target,target?.GetType()) || Enabled;
        }

        public virtual string ToolTipText(object target)
        {
            return LabelFor(target) + " " + target;
        }

        public virtual IEnumerable<ToolStripMenuItem> GetToolItems(object target, Type type)
        {
            return new List<ToolStripMenuItem>()
            {
                new ToolStripMenuItem(LabelFor(target), null, (sender, e) => TCI(sender, e, target))
                {
                    Enabled = IsEnabled(target), ToolTipText = ToolTipText(target)
                }
            };
        }

        private void TCI(object sender, EventArgs e, object target)
        {
            if (!ExecAsync)
            {
                TryCatch(() => OnInvoke(sender, e, target));
                return;
            }

            Task command = Task.Run(() => TryCatch(() => OnInvoke(sender, e, target)));                
        }

        protected void InvokeSync(MethodInvoker func)
        {
            try
            {
                // ThreadingHelper expects an Action; wrap MethodInvoker
                ThreadingHelper.SafeInvokeSync(instance.MainForm, new Action(func), instance.MonoRuntime);
            }
            catch (Exception e)
            {
                DebugLog("Exception: " + e);
            }
        }

        protected void TryCatch(MethodInvoker func)
        {
            try
            {
                func();
            }
            catch (Exception e)
            {
                DebugLog("Exception: " + e);
            }
        }

        public virtual string LabelFor(object target)
        {
            return Label;
        }

        public virtual bool TypeContributes(Type o)
        {
            if (ExactContextType) return o.IsAssignableFrom(ContextType);
            return ContextType.IsAssignableFrom(o);
        }

        public virtual bool Contributes(object o, Type type)
        {
            if (o==null) return false;
            if(TypeContributes(type)) return true;
            object oo = DeRef(o);
            return (oo != null && oo != o && Contributes(oo, oo.GetType()));
        }

        public virtual void OnInvoke(object sender, EventArgs e, object target)
        {

            object oneOf = target ?? sender;
            oneOf = GetValue(ContextType, oneOf);
            if (!ContextType.IsInstanceOfType(oneOf))
            {
                oneOf = GetValue(ContextType, DeRef(oneOf));
            }
            Handler?.Invoke(oneOf, e);
        }

        public virtual void IContextAction(RadegastInstanceForms instance)
        {
            Instance = instance;
        }

        public virtual void Dispose()
        {           
        }
        public object GetValue(Type type, object lastObject)
        {
            if (type.IsInstanceOfType(lastObject)) return lastObject;
            if (type.IsAssignableFrom(typeof(Primitive))) return ToPrimitive(lastObject);
            if (type.IsAssignableFrom(typeof(Avatar))) return ToAvatar(lastObject);
            if (type.IsAssignableFrom(typeof(UUID))) return ToUUID(lastObject);
            return lastObject;
        }
        public Primitive ToPrimitive(object target)
        {
            Primitive prim = (target as Primitive);
            if (prim != null) { return prim; }
            var oo = DeRef(target);
            if (oo != target)
            {
                prim = ToAvatar(oo);
                if (prim != null) { return prim; }
            }
            UUID uuid = ((target is UUID id) ? id : UUID.Zero);
            if (uuid != UUID.Zero)
            {
                var kvp = Client.Network.CurrentSim.ObjectsPrimitives.FirstOrDefault(p => p.Value.ID == uuid);;
                prim = kvp.Value;
            }

            if (uuid != UUID.Zero)
            {
                var kvp = Client.Network.CurrentSim.ObjectsAvatars.FirstOrDefault(a => a.Value.ID == uuid);
                prim = kvp.Value;
            }

            return prim;
        }

        public Avatar ToAvatar(object target)
        {
            Primitive thePrim = (target as Primitive);
            if (thePrim is Avatar avatar) { return avatar; }
            if (thePrim != null && thePrim.Properties != null && thePrim.Properties.OwnerID != UUID.Zero)
            {
                target = thePrim.Properties.OwnerID;
            }
            var oo = DeRef(target);
            if (oo != target)
            {
                thePrim = ToAvatar(oo);
                if (thePrim != null) { return (Avatar)thePrim; }
            }
            UUID uuid = ((target is UUID id) ? id : UUID.Zero);
            if (uuid == UUID.Zero) { return thePrim as Avatar; }
            var kvp = Client.Network.CurrentSim.ObjectsAvatars.FirstOrDefault(
                prim => prim.Value.ID == uuid);
            if (kvp.Value != null)
            {
                thePrim = kvp.Value;
            }

            return (Avatar)thePrim;
        }

        public UUID ToUUID(object target)
        {
            UUID uuid = ((target is UUID id) ? id : UUID.Zero);
            if (target is FriendInfo friendInfo)
            {
                return friendInfo.UUID;
            }
            if (target is GroupMember groupMember)
            {
                return groupMember.ID;
            }
            if (target is Group group)
            {
                return group.ID;
            }
            if (target is Primitive primitive)
            {
                return primitive.ID;
            }
            if (target is Asset asset)
            {
                return asset.AssetID;
            }
            if (target is InventoryItem item)
            {
                return item.AssetUUID;
            }
            if (uuid != UUID.Zero) return uuid;
            object oo = DeRef(target);
            if (oo != target)
            {
                uuid = ToUUID(oo);
                if (uuid != UUID.Zero) return uuid;
            }
            string str = (target as string);
            if (!string.IsNullOrEmpty(str))
            {
                if (UUID.TryParse(str, out uuid))
                {
                    return uuid;
                }
            }
            Primitive prim = ToPrimitive(target);
            if (prim != null) { return prim.ID; }
            Avatar avatar = ToAvatar(target);
            if (avatar != null) { return avatar.ID; }
            return uuid;
        }

        public void DebugLog(string s)
        {
           // instance.DisplayNotificationInChat(string.Format("ContextAction {0}: {1}", Label, s));
        }

        protected bool TryFindPos(object initial, out Simulator simulator, out Vector3 vector3)
        {
            simulator = Client.Network.CurrentSim;
            vector3 = Vector3.Zero;
            if (initial is Vector3)
            {
                vector3 = (Vector3) initial;
                return true;
            }
            if (initial is Vector3d)
            {
                var v3d = (Vector3d) initial;
                float lx, ly;
                ulong handle = Helpers.GlobalPosToRegionHandle((float) v3d.X, (float) v3d.Y, out lx, out ly);
                Simulator[] Simulators = null;
                lock (Client.Network.Simulators)
                {
                    Simulators = Client.Network.Simulators.ToArray();
                }
                foreach (Simulator s in Simulators)
                {
                    if (handle == s.Handle)
                    {
                        simulator = s;
                    }
                }
                vector3 = new Vector3(lx, ly, (float) v3d.Z);
                return true;
            }
            if (initial is UUID)
            {
                return instance.State.TryFindPrim((UUID)initial, out simulator, out vector3, false);
            }
            if (initial is Primitive)
            {
                return instance.State.TryLocatePrim((Primitive)initial, out simulator, out vector3);
            }
            UUID toUUID = ToUUID(initial);
            if (toUUID == UUID.Zero) return false;
            return instance.State.TryFindPrim(toUUID, out simulator, out vector3, false);
        }
    }
}
