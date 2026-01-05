/**
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

using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    public partial class ntfGroupNotice : Notification
    {
        private readonly RadegastInstanceForms instance;
        private GridClient client => instance.Client;
        private readonly InstantMessage msg;
        private readonly AssetType type = AssetType.Unknown;
        private UUID destinationFolderID;
        private readonly UUID groupID;
        private Group group;

        public ntfGroupNotice(RadegastInstanceForms instance, InstantMessage msg)
            : base(NotificationType.GroupNotice)
        {
            InitializeComponent();
            Disposed += ntfGroupNotice_Disposed;

            this.instance = instance;
            this.msg = msg;
            client.Groups.GroupProfile += Groups_GroupProfile;

            if (msg.BinaryBucket.Length > 18 && msg.BinaryBucket[0] != 0)
            {
                type = (AssetType)msg.BinaryBucket[1];
                destinationFolderID = client.Inventory.FindFolderForType(type);
                int icoIndx = InventoryConsole.GetItemImageIndex(type.ToString().ToLower());
                if (icoIndx >= 0)
                {
                    icnItem.Image = frmMain.ResourceImages.Images[icoIndx];
                    icnItem.Visible = true;
                }
                txtItemName.Text = Utils.BytesToString(msg.BinaryBucket, 18, msg.BinaryBucket.Length - 19);
                btnSave.Enabled = true;
                btnSave.Visible = icnItem.Visible = txtItemName.Visible = true;
            }


            groupID = msg.BinaryBucket.Length >= 18 ? new UUID(msg.BinaryBucket, 2) : msg.FromAgentID;

            int pos = msg.Message.IndexOf('|');
            string title = msg.Message.Substring(0, pos);
            lblTitle.Text = title;
            string text = msg.Message.Replace("\n", System.Environment.NewLine);
            text = text.Remove(0, pos + 1);

            lblSentBy.Text = $"Sent by {msg.FromAgentName}";
            txtNotice.Text = text;

            // Accessible metadata
            InitializeAccessibleMetadata("Group Notice", lblTitle.Text + " " + lblSentBy.Text + " " + txtNotice.Text);

            if (instance.Groups.TryGetValue(groupID, out var id))
            {
                group = id;
                ShowNotice();
            }
            else
            {
                client.Groups.RequestGroupProfile(groupID);
            }

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        protected override void OnLoad(System.EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                if (btnSave != null)
                {
                    if (string.IsNullOrEmpty(btnSave.AccessibleName)) btnSave.AccessibleName = btnSave.Text;
                    if (string.IsNullOrEmpty(btnSave.AccessibleDescription)) btnSave.AccessibleDescription = $"Press Enter to activate {btnSave.Text}";
                }
                if (btnOK != null)
                {
                    if (string.IsNullOrEmpty(btnOK.AccessibleName)) btnOK.AccessibleName = btnOK.Text;
                    if (string.IsNullOrEmpty(btnOK.AccessibleDescription)) btnOK.AccessibleDescription = $"Press Enter to activate {btnOK.Text}";
                    try { btnOK.Focus(); } catch { }
                }
            }
            catch { }
        }

        private void ShowNotice()
        {
            if (group.ID == UUID.Zero) return;
            
            imgGroup.Init(instance, group.InsigniaID, string.Empty);
            lblSentBy.Text += ", " + group.Name;

            // Fire off event
            NotificationEventArgs args = new NotificationEventArgs(instance)
            {
                Text = string.Format("{0}{1}{2}{3}{4}",
                    lblTitle.Text, System.Environment.NewLine,
                    lblSentBy.Text, System.Environment.NewLine,
                    txtNotice.Text
                )
            };
            if (btnSave.Visible)
            {
                args.Buttons.Add(btnSave);
                args.Text += $"{System.Environment.NewLine}Attachment: {txtItemName.Text}";
            }
            args.Buttons.Add(btnOK);
            FireNotificationCallback(args);
        }

        private void ntfGroupNotice_Disposed(object sender, System.EventArgs e)
        {
            client.Groups.GroupProfile -= Groups_GroupProfile;
        }

        private void Groups_GroupProfile(object sender, GroupProfileEventArgs e)
        {
            if (groupID != e.Group.ID) return;

            if (instance.MainForm.InvokeRequired)
            {
                instance.MainForm.BeginInvoke(new MethodInvoker(() => Groups_GroupProfile(sender, e)));
                return;
            }

            group = e.Group;
            ShowNotice();
        }

        private void SendReply(InstantMessageDialog dialog, byte[] bucket)
        {
            client.Self.InstantMessage(client.Self.Name, msg.FromAgentID, string.Empty, msg.IMSessionID, dialog, InstantMessageOnline.Offline, client.Self.SimPosition, client.Network.CurrentSim.RegionID, bucket);
        }

        private void btnOK_Click(object sender, System.EventArgs e)
        {
            instance.MainForm.RemoveNotification(this);
        }

        private void btnSave_Click(object sender, System.EventArgs e)
        {
            SendReply(InstantMessageDialog.GroupNoticeInventoryAccepted, destinationFolderID.GetBytes());
            btnSave.Enabled = false;
            btnOK.Focus();
        }
    }
}
