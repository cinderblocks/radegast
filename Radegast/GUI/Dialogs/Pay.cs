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

using System;
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    public partial class frmPay : RadegastForm
    {
        private readonly UUID target;
        private readonly string name;
        private UUID owner;
        private readonly bool isObject;
        private readonly Button[] buttons;
        private readonly int[] defaultAmounts = new int[] { 1, 5, 10, 20 };
        public static int LastPayed = -1;

        public frmPay(RadegastInstanceForms instance, UUID target, string name, bool isObject)
            : base(instance)
        {
            InitializeComponent();
            Disposed += frmPay_Disposed;

            this.target = target;
            this.name = name;
            this.isObject = isObject;

            // Buttons
            buttons = new Button[] { btnFastPay1, btnFastPay2, btnFastPay3, btnFastPay4 };
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Click += frmPay_Click;
                buttons[i].Text = $"L${defaultAmounts[i]}";
                buttons[i].Tag = defaultAmounts[i];
            }

            // Callbacks
            Client.Objects.PayPriceReply += Objects_PayPriceReply;
            Client.Objects.ObjectPropertiesFamily += Objects_ObjectPropertiesFamily;
            instance.Names.NameUpdated += Avatars_UUIDNameReply;

            if (isObject)
            {
                Client.Objects.RequestPayPrice(Client.Network.CurrentSim, target);
                Client.Objects.RequestObjectPropertiesFamily(Client.Network.CurrentSim, target);
                lblObject.Visible = true;
                lblObject.Text = $"Via object: {name}: ";
            }
            else
            {
                lblObject.Visible = false;
                lblResident.Text = $"Pay resident: {name}";
            }

            if (LastPayed > 0)
            {
                txtAmount.Text = LastPayed.ToString();
            }

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void frmPay_Disposed(object sender, EventArgs e)
        {
            Client.Objects.PayPriceReply -= Objects_PayPriceReply;
            Client.Objects.ObjectPropertiesFamily -= Objects_ObjectPropertiesFamily;
            Instance.Names.NameUpdated -= Avatars_UUIDNameReply;
        }

        private void frmPay_Click(object sender, EventArgs e)
        {
            int amount = (int)((Button)sender).Tag;

            if (!isObject)
            {
                Client.Self.GiveAvatarMoney(target, amount);
            }
            else
            {
                Client.Self.GiveObjectMoney(target, amount, name);
            }
            Close();
        }

        private void UpdateResident()
        {
            if (InvokeRequired)
            {
                ThreadingHelper.SafeInvoke(this, new Action(UpdateResident), Instance.MonoRuntime);
                return;
            }

            lblResident.Text = $"Pay resident: {Instance.Names.Get(owner)}";
        }

        private void Avatars_UUIDNameReply(object sender, UUIDNameReplyEventArgs e)
        {
            if (e.Names.ContainsKey(owner))
            {
                Instance.Names.NameUpdated -= Avatars_UUIDNameReply;
                UpdateResident();
            }
        }


        private void Objects_ObjectPropertiesFamily(object sender, ObjectPropertiesFamilyEventArgs e)
        {
            if (e.Properties.ObjectID == target)
            {
                owner = e.Properties.OwnerID;
                UpdateResident();
            }
        }

        private void Objects_PayPriceReply(object sender, PayPriceReplyEventArgs e)
        {
            if (e.ObjectID != target) return;

            if (InvokeRequired)
            {
                ThreadingHelper.SafeInvoke(this, new Action(() => Objects_PayPriceReply(sender, e)), Instance.MonoRuntime);
                return;
            }

            switch (e.DefaultPrice)
            {
                case (int)PayPriceType.Default:
                    txtAmount.Text = string.Empty;
                    break;
                case (int)PayPriceType.Hide:
                    txtAmount.Visible = false;
                    break;
                default:
                    txtAmount.Text = e.DefaultPrice.ToString();
                    break;
            }

            for (int i = 0; i < buttons.Length; i++)
            {
                switch (e.ButtonPrices[i])
                {
                    case (int)PayPriceType.Default:
                        buttons[i].Text = string.Format("L${0}", defaultAmounts[i]);
                        buttons[i].Tag = defaultAmounts[i];
                        break;
                    case (int)PayPriceType.Hide:
                        buttons[i].Visible = false;
                        break;
                    default:
                        buttons[i].Text = string.Format("L${0}", e.ButtonPrices[i]);
                        buttons[i].Tag = e.ButtonPrices[i];
                        break;
                }

            }
        }


        private void btnCancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void btnPay_Click(object sender, EventArgs e)
        {
            int amount;

            if (int.TryParse(txtAmount.Text, out amount) && amount > 0)
            {
                if (amount > Client.Self.Balance)
                {
                    lblStatus.Visible = true;
                    return;
                }

                LastPayed = amount;

                if (!isObject)
                {
                    Client.Self.GiveAvatarMoney(target, amount);
                }
                else
                {
                    Client.Self.GiveObjectMoney(target, amount, name);
                }
            }
            Close();
        }

        private void txtAmount_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!(char.IsDigit(e.KeyChar) || char.IsControl(e.KeyChar)))
            {
                e.Handled = true;
            }
        }

        private void txtAmount_TextChanged(object sender, EventArgs e)
        {
            if (int.TryParse(txtAmount.Text, out var amount) && amount > 0)
            {
                if (amount > Client.Self.Balance)
                {
                    lblStatus.Visible = true;
                    btnPay.Enabled = false;
                }
                else
                {
                    lblStatus.Visible = false;
                    btnPay.Enabled = true;
                }
            }
            else
            {
                btnPay.Enabled = false;
            }
        }
    }
}