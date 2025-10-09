/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2020, Sjofn, LLC
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
using System.Linq;
using Radegast;
using OpenMetaverse;

namespace RadegastSpeech.Conversation
{
    class Friends : Mode
    {
        private FriendsConsole frTab;
        public bool Announce { get; set; }
        private System.Windows.Forms.ListBox friends;

        #region statechange
        internal Friends(PluginControl pc)
            : base(pc)
        {
            Title = "friends";
            Announce = false;
            frTab = (FriendsConsole)control.instance.TabConsole.Tabs["friends"].Control;
            friends = frTab.listFriends;

            control.instance.Client.Friends.FriendOffline +=
                Friends_OnFriendOffline;
            control.instance.Client.Friends.FriendOnline +=
                Friends_OnFriendOnline;
        }

        /// <summary>
        /// Friends conversation becomes active
        /// </summary>
        internal override void Start()
        {
            base.Start();
            friends.SelectedIndexChanged += friends_ItemSelectionChanged;
            Talker.SayMore("Friends");
        }

        /// <summary>
        /// Friends conversation becomes inactive
        /// </summary>
        internal override void Stop()
        {
            friends.SelectedIndexChanged -= friends_ItemSelectionChanged;
            base.Stop();
        }
        #endregion

        #region generalevents
        private void Friends_OnFriendOffline(object sender, FriendInfoEventArgs e)
        {
            if (Announce)
                Talker.Say( "Your friend " + e.Friend.Name + " went off line.");
        }

        private void Friends_OnFriendOnline(object sender, FriendInfoEventArgs e)
        {
            if (Announce)
                Talker.Say("Your friend " + e.Friend.Name + " came on line.");
        }
        #endregion

        #region focusevents
 
        /// <summary>
        /// Speech input commands for Friends conversation
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        internal override bool Hear(string message)
        {
            switch (message)
            {
                case "who is online":
                    ListFriends();
                    return true;
            }

            return false;
        }
        #endregion

        /// <summary>
        /// Was previous selection multiple
        /// </summary>
        bool prevMultiple = false;
        /// <summary>
        /// Announce which friend has been selected in the list.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void friends_ItemSelectionChanged(object sender, EventArgs e)
        {
            FriendInfo f = (FriendInfo)friends.SelectedItem;
            bool multiple = friends.SelectedItems.Count > 1;
            string desc = f.Name;
            bool IsSelected = true;

            if (multiple || prevMultiple)
            {
                desc += IsSelected ? " selected" : " deselected";
                Talker.SayMore(desc);
            }
            else if (IsSelected)
            {
                if (!f.IsOnline)
                    desc += " is off line.";
                Talker.SayMore(desc);
            }

            prevMultiple = multiple;
        }

        /// <summary>
        /// Speak names of all online friends.
        /// </summary>
        private void ListFriends()
        {
            var onlineFriends = (from friend in control.instance.Client.Friends.FriendList 
                where friend.Value != null && friend.Value.IsOnline select friend.Value).ToList();

            string list = onlineFriends.Aggregate("", (current, f) => current + (f.Name + ", "));
            list += "are online.";
            Talker.SayMore(list);
        }

    }
}
