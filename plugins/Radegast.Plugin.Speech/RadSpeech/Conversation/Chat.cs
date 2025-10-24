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
using System.Linq;
using OpenMetaverse;

namespace RadegastSpeech.Conversation
{
    /// <summary>
    /// Conversation mode for chatting with other avatars.
    /// </summary>
    internal class Chat : Mode, IDisposable
    {
        private bool muteObjects = false;
        private const string MUTE_OBJECTS = "mute objects";
        private const string UNMUTE_OBJECTS = "unmute objects";
//        private Radegast.RadegastMovement movement;
        internal System.Windows.Forms.Control Console { set; get;  }
        private Radegast.ListViewNoFlicker nearby;

        internal Chat(PluginControl pc) : base(pc)
        {
            // We want to process incoming chat
            control.instance.Client.Self.ChatFromSimulator +=
                OnChat;
            control.instance.Client.Self.AlertMessage +=
                OnAlertMessage;
            var chatTab = control.instance.TabConsole.Tabs["chat"];
            var chatscreen = (Radegast.ChatConsole)chatTab.Control;

            nearby = chatscreen.lvwObjects;
            nearby.SelectedIndexChanged += nearby_SelectedIndexChanged;

            nearby.GotFocus += nearby_GotFocus;
            chatscreen.ChatInputText.GotFocus += cbxInput_GotFocus;

            Title = "chat";

            // Make a recognition grammar to improve accuracy.
            Listener.CreateGrammar("chat",
                new string[] {
                    MUTE_OBJECTS,
                    UNMUTE_OBJECTS });
       }

        public void Dispose()
        {
            control.instance.Client.Self.ChatFromSimulator -=
                OnChat;
            control.instance.Client.Self.AlertMessage -=
                OnAlertMessage;
            
            if (control.instance.TabConsole != null && control.instance.TabConsole.TabExists("chat"))
            {
                var chatTab = control.instance.TabConsole.Tabs["chat"];
                var chatscreen = (Radegast.ChatConsole)chatTab.Control;

                nearby = chatscreen.lvwObjects;
                nearby.SelectedIndexChanged -= nearby_SelectedIndexChanged;

                nearby.GotFocus -= nearby_GotFocus;
                chatscreen.ChatInputText.GotFocus -= cbxInput_GotFocus;
            }

            nearby = null;
        }

        private void cbxInput_GotFocus(object sender, EventArgs e)
        {
            Talker.SayMore("chat input");
        }

        private void nearby_GotFocus(object sender, EventArgs e)
        {
            Talker.SayMore("near bye avatars");
        }

        /// <summary>
        /// Handle somebody speaking near us.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="audible"></param>
        /// <param name="type"></param>
        /// <param name="sourceType"></param>
        /// <param name="fromName"></param>
        /// <param name="id"></param>
        /// <param name="ownerid"></param>
        /// <param name="position"></param>
        private void OnChat(
            object sender,
            ChatEventArgs e)
        {
/*             string message,
            ChatAudibleLevel audible,
            ChatType type,
            ChatSourceType sourceType,
            string fromName,
            UUID id, UUID ownerid, Vector3 position)
*/
            // Ignore some chat types.
            switch (e.Type)
            {
                case ChatType.Debug:
                case ChatType.StartTyping:
                case ChatType.StopTyping:
                    return;
                default:
                    break;
            }

            var message = e.Message;

            // Ignore empty messages.
            if (message == "") return;

            // Speak it according to what kind of thing said it.
            switch (e.SourceType)
            {
                case ChatSourceType.System:
                    Talker.Say(message);
                    break;
                case ChatSourceType.Object:
                    if (muteObjects) return;
                    Talker.SayObject(e.FromName, message, e.Position );
                    break;
                case ChatSourceType.Agent:
                    Talker.SayPerson(
                        FriendlyName(e.FromName, e.SourceID),
                        message,
                        e.Position,
                        Talker.voices.VoiceFor(e.SourceID) );
                    break;
            }
        }

        private void OnAlertMessage(object sender, AlertMessageEventArgs e)
        {
            Talker.Say(e.Message);
        }

        /// <summary>
        /// Process recognized speech
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        internal override bool Hear( string message )
        {
            if (base.Hear(message)) return true;

            // Watch for commands relating to chat
            switch (message.ToLower())
            {
                case MUTE_OBJECTS:
                    muteObjects = true;
                    Talker.SayMore("Objects muted.");
                    return true;
                case UNMUTE_OBJECTS:
                    muteObjects = false;
                    Talker.SayMore("Objects un-muted.");
                    return true;
/*
                case "walk":
                    movement.MovingForward = true;
                    Talker.SayMore("walking");
                    return true;
                case "turn left":
                    movement.TurningLeft = true;
                    Talker.SayMore("turning left");
                    return true;
                case "turn right":
                    movement.TurningRight = true;
                    Talker.SayMore("turning right");
                    return true;
                case "stop":
                    movement.MovingForward = movement.MovingBackward =
                        movement.TurningLeft = movement.TurningRight = false;
                    Talker.SayMore("stopped");
                    return true;
 */
            }

            // If none of those, put it into local chat.
            Client.Self.Chat( message, 0, ChatType.Normal);
            return true;
        }

        internal override void Start()
        {
            base.Start();
            Listener.ActivateGrammar("chat");
            Talker.SayMore("Chat." );
            nearby_SelectedIndexChanged(null, null);
            /*
                        movement = new Radegast.SleekMovement(control.instance.Client);

                        Console.KeyDown +=
                            new System.Windows.Forms.KeyEventHandler(MainForm_KeyDown);
                        control.instance.MainForm.KeyUp +=
                            new System.Windows.Forms.KeyEventHandler(MainForm_KeyUp);
            */        }

        internal override void Stop()
        {
            base.Stop();
            Listener.DeactivateGrammar("chat");
/*
            movement = null;
            control.instance.MainForm.KeyDown -=
                new System.Windows.Forms.KeyEventHandler(MainForm_KeyDown);
            control.instance.MainForm.KeyUp -=
                new System.Windows.Forms.KeyEventHandler(MainForm_KeyUp);
*/
        }
/*        
        /// <summary>
        /// Handle walking-around keys going DOWN
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void MainForm_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (e.Shift) return;
            if (e.Control) return;
            if (e.Alt) return;

            switch (e.KeyCode)
            {
                case System.Windows.Forms.Keys.Up:
                    movement.MovingForward = true;
                    e.SuppressKeyPress = e.Handled = true;
                    break;
                case System.Windows.Forms.Keys.Down:
                    movement.MovingBackward = true;
                    e.SuppressKeyPress = e.Handled = true;
                    break;
                case System.Windows.Forms.Keys.Left:
                    movement.TurningLeft = true;
                    e.SuppressKeyPress = e.Handled = true;
                    break;
                case System.Windows.Forms.Keys.Right:
                    movement.TurningRight = true;
                    e.SuppressKeyPress = e.Handled = true;
                    break;
            }

        }

        /// <summary>
        /// Handle walking-around keys going DOWN
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void MainForm_KeyUp(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (e.Shift) return;
            if (e.Control) return;
            if (e.Alt) return;

            switch (e.KeyCode)
            {
                case System.Windows.Forms.Keys.Up:
                    movement.MovingForward = false;
                    e.SuppressKeyPress = e.Handled = true;
                    break;
                case System.Windows.Forms.Keys.Down:
                    movement.MovingBackward = false;
                    e.SuppressKeyPress = e.Handled = true;
                    break;
                case System.Windows.Forms.Keys.Left:
                    movement.TurningLeft = false;
                    e.SuppressKeyPress = e.Handled = true;
                    break;
                case System.Windows.Forms.Keys.Right:
                    movement.TurningRight = false;
                    e.SuppressKeyPress = e.Handled = true;
                    break;
            }

        }
*/
        /// <summary>
        /// Describe a nearby avatar.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void nearby_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (nearby.SelectedItems.Count != 0)
            {
                var kvp = Client.Network.CurrentSim.ObjectsAvatars.FirstOrDefault(a =>
                    a.Value.ID == (UUID)nearby.SelectedItems[0].Tag);

                if (kvp.Value == null)
                {
                    // Not sure why this would happen.
                    Talker.SayMore("Avatar in another region.");
                    return;
                }
                var currentAvatar = kvp.Value;

                // Selecting self is not too interesting
                if ((UUID)nearby.SelectedItems[0].Tag == Client.Self.AgentID)
                {
                }
                var where = control.env.people.Location(currentAvatar.Position);
                var what = control.env.people.Describe(currentAvatar.ID);
                var who = currentAvatar.Name;

                // "John Smith, 3m to your right, is male and 2.1 meters tall"
                var description = who;
                if (where != null)
                    description += ", " + where;
                if (what != null)
                    description += ", is " + what;
                Talker.SayMore(description);

            }
        }

    }
}
