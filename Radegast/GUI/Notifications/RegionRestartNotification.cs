/*
 * Radegast Metaverse Client
 * Copyright(c) 2021-2025, Sjofn, LLC
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

namespace Radegast
{
    public partial class ntfRegionRestart : Notification
    {
        private readonly RadegastInstanceForms instance;
        private string RegionName;
        private int CountdownSeconds;
        private readonly System.Timers.Timer CountdownTimer;

        public override bool SingleInstance => true;

        public ntfRegionRestart(RadegastInstanceForms instance, string region_name, int countdown_seconds)
            : base(NotificationType.RegionRestart)
        {
            InitializeComponent();
            this.instance = instance;
            this.RegionName = region_name;
            this.CountdownSeconds = countdown_seconds;

            txtHead.BackColor = instance.MainForm.NotificationBackground;
            txtCountdownLabel.BackColor = instance.MainForm.NotificationBackground;
            txtCountdown.BackColor = instance.MainForm.NotificationBackground;

            CountdownTimer = new System.Timers.Timer
            {
                Interval = 1000 // 1s
            };
            CountdownTimer.Elapsed += OnCountdownTimerEvent;
            CountdownTimer.Start();

            btnHome.Focus();

            this.instance.MediaManager.PlayUISound(UISounds.Warning);

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void OnCountdownTimerEvent(object sender, System.Timers.ElapsedEventArgs e)
        {
            Invoke(new Action(() =>
            {
                var ts = TimeSpan.FromSeconds(--CountdownSeconds);
                txtCountdown.Text = ts.ToString(@"mm\:ss");
            }));
        }

        // TODO: we need a notification closed event to hook up to...
        private void OnNotificationClosed(object sender, NotificationEventArgs e)
        {
            CountdownTimer.Stop();
        }

        private void btnHome_Click(object sender, EventArgs e)
        {
            instance.Client.Self.RequestTeleport(OpenMetaverse.UUID.Zero);
            
            instance.MainForm.RemoveNotification(this);
        }

        private void btnElsewhere_Click(object sender, EventArgs e)
        {
            Automation.PseudoHomePreferences prefs = instance.State.PseudoHome?.Preferences;

            if (prefs != null && prefs.Region.Trim() != string.Empty)
            {
                instance.Client.Self.Teleport(prefs.Region, prefs.Position);
            }
            else
            {
                // idk, this is silly and Second Life specific.
                instance.Client.Self.Teleport("Hippo Hollow", new OpenMetaverse.Vector3(180, 205, 44));
            }
            instance.MainForm.RemoveNotification(this);
        }

        private void btnIgnore_Click(object sender, EventArgs e)
        {
            instance.MainForm.RemoveNotification(this);
        }
    }
}
