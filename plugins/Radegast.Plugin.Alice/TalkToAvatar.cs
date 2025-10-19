using System.Windows.Forms;
using AIMLBot = AIMLbot.Bot;

using OpenMetaverse;

namespace Radegast.Plugin.Alice
{
    public sealed class TalkToAvatar : ContextAction
    {
        private readonly AIMLBot aimlBot;
        public TalkToAvatar(RadegastInstance inst, AIMLBot bot)
            : base(inst)
        {
            ContextType = typeof (Avatar);
            Label = "Talk to";
            aimlBot = bot;
        }

        public override bool Contributes(object o, System.Type type)
        {
            if (!IsEnabledInRadegast) return false;
            return type == typeof(FriendInfo) || type == typeof(Avatar);
        }

        public override bool IsEnabled(object target)
        {
            if (!IsEnabledInRadegast) return false;
            Avatar a = target as Avatar;
            return (a != null && !string.IsNullOrEmpty(a.Name)) || base.IsEnabled(target);
        }

        private bool IsEnabledInRadegast => Instance.GlobalSettings["plugin.alice.enabled"].AsBoolean();

        public override void OnInvoke(object sender, System.EventArgs e, object target)
        {
            ListViewItem ali = target as ListViewItem;
            string username = null;
            UUID uuid = UUID.Zero;
            if (ali != null)
            {
                uuid = (UUID)ali.Tag;
                username = instance.Names.Get(uuid);
            }
            else
            {
                if (target is FriendInfo fi) { uuid = fi.UUID; }
                username = instance.Names.Get(uuid);

            }
            if (username==null)
            {
                instance.TabConsole.DisplayNotificationInChat($"I don't know how to DeRef {target} being a {target.GetType()}");                               
                return;
            }
            string outp = username + ", " + aimlBot.Chat("INTERJECTION", username).Output;
            if (outp.Length > 1000)
            {
                outp = outp.Substring(0, 1000);
            }

            Client.Self.Chat(outp, 0, ChatType.Normal);
        }
    }
}