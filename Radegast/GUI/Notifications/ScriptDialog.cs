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
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast
{
    public partial class ntfScriptDialog : Notification
    {
        private readonly RadegastInstanceForms instance;
        private readonly UUID objectID;
        private readonly int chatChannel;

        public ntfScriptDialog(RadegastInstanceForms instance, string message, string objectName, UUID imageID, UUID objectID, string firstName, string lastName, int chatChannel, List<string> buttons)
            : base(NotificationType.ScriptDialog)
        {
            InitializeComponent();

            this.instance = instance;
            this.chatChannel = chatChannel;
            this.objectID = objectID;

            descBox.BackColor = instance.MainForm.NotificationBackground;
            descBox.Text = firstName + " " + lastName + "'s " + objectName + "\r\n\r\n" + message.Replace("\n", "\r\n") + "\r\n";

            // Accessible metadata
            InitializeAccessibleMetadata("Script Dialog", descBox.Text);

            NotificationEventArgs args = new NotificationEventArgs(instance) {Text = descBox.Text};

            int btnWidth = 90;
            int btnHeight = 23;

            int i = 0;
            if (buttons.Count == 1 && buttons[0] == "!!llTextBox!!")
            {
                txtTextBox.Visible = true;
                sendBtn.Visible = true;
                args.Buttons.Add(ignoreBtn);
            }
            else
            {
                foreach (string label in buttons)
                {
                    Button b = new Button
                    {
                        Size = new Size(btnWidth, btnHeight),
                        Text = label,
                        Location = new Point(5 + (i % 3) * (btnWidth + 5),
                            btnsPanel.Size.Height - (i / 3) * (btnHeight + 5) - (btnHeight + 5)),
                        Name = i.ToString()
                    };
                    b.Click += b_Click;
                    b.UseVisualStyleBackColor = true;
                    b.Margin = new Padding(0, 3, 0, 3);
                    b.Padding = new Padding(0);
                    btnsPanel.Controls.Add(b);
                    args.Buttons.Add(b);
                    i++;
                }
            }
            // Fire off event
            args.Buttons.Add(ignoreBtn);
            FireNotificationCallback(args);

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                // Ensure keyboard hints for all buttons (including dynamically created ones)
                var btns = new List<Button>();
                btns.AddRange(GetAllButtons(this));

                foreach (var b in btns)
                {
                    if (b == null) continue;
                    if (string.IsNullOrEmpty(b.AccessibleName)) b.AccessibleName = b.Text;
                    if (string.IsNullOrEmpty(b.AccessibleDescription)) b.AccessibleDescription = $"Press Enter to activate {b.Text}";
                }

                // Also set accessible properties for textbox/send button if present
                try
                {
                    if (txtTextBox != null && txtTextBox.Visible)
                    {
                        if (string.IsNullOrEmpty(txtTextBox.AccessibleName)) txtTextBox.AccessibleName = "Input";
                        if (string.IsNullOrEmpty(txtTextBox.AccessibleDescription)) txtTextBox.AccessibleDescription = "Enter text to send to the script";
                    }

                    if (sendBtn != null && sendBtn.Visible)
                    {
                        if (string.IsNullOrEmpty(sendBtn.AccessibleName)) sendBtn.AccessibleName = sendBtn.Text;
                        if (string.IsNullOrEmpty(sendBtn.AccessibleDescription)) sendBtn.AccessibleDescription = $"Press Enter to activate {sendBtn.Text}";
                    }
                }
                catch { }

                // Set tab order and focus first button if available
                this.TabIndex = 0;
                var first = btns.Count > 0 ? btns[0] : ignoreBtn;
                if (first != null && !first.IsDisposed)
                {
                    try { first.Select(); } catch { }
                }
            }
            catch { }
        }

        private void b_Click(object sender, EventArgs e)
        {
            string label = ((Button)sender).Text;
            int index = int.Parse(((Button)sender).Name);
            instance.Client.Self.ReplyToScriptDialog(chatChannel, index, label, objectID);
            instance.RemoveNotification(this);
        }

        private void ignoreBtn_Click(object sender, EventArgs e)
        {
            instance.RemoveNotification(this);
        }

        private void sendBtn_Click(object sender, EventArgs e)
        {
            instance.Client.Self.ReplyToScriptDialog(chatChannel, 0, txtTextBox.Text, objectID);
            instance.RemoveNotification(this);
        }
    }
}
