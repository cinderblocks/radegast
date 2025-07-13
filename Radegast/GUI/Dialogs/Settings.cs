/*
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2023, Sjofn, LLC
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
using System.Text;
using System.Windows.Forms;
using Newtonsoft.Json;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using Radegast.Automation;

namespace Radegast
{
    public enum AutoResponseType
    {
        WhenBusy = 0,
        WhenFromNonFriend = 1,
        Always = 2
    }

    public partial class frmSettings : RadegastForm
    {
        private Settings s;
        private static bool settingInitialized = false;
        private Settings.FontSetting currentlySelectedFontSetting = null;
        Dictionary<string, Settings.FontSetting> chatFontSettings;

        public static void InitSettings(Settings s)
        {
            if (s["im_timestamps"].Type == OSDType.Unknown)
            {
                s["im_timestamps"] = OSD.FromBoolean(true);
            }

            if (s["rlv_enabled"].Type == OSDType.Unknown)
            {
                s["rlv_enabled"] = OSD.FromBoolean(false);
            }

            if (s["rlv_debugcommands"].Type == OSDType.Unknown)
            {
                s["rlv_debugcommands"] = OSD.FromBoolean(false);
            }

            if (s["mu_emotes"].Type == OSDType.Unknown)
            {
                s["mu_emotes"] = OSD.FromBoolean(false);
            }

            if (s["friends_notification_highlight"].Type == OSDType.Unknown)
            {
                s["friends_notification_highlight"] = OSD.FromBoolean(true);
            }

            if (!s.ContainsKey("no_typing_anim")) s["no_typing_anim"] = OSD.FromBoolean(false);

            if (!s.ContainsKey("auto_response_type"))
            {
                s["auto_response_type"] = (int)AutoResponseType.WhenBusy;
                s["auto_response_text"] = "The Resident you messaged is in 'busy mode' which means they have requested not to be disturbed.  Your message will still be shown in their IM panel for later viewing.";
            }

            if (!s.ContainsKey("script_syntax_highlight")) s["script_syntax_highlight"] = OSD.FromBoolean(true);

            if (!s.ContainsKey("display_name_mode")) s["display_name_mode"] = (int)NameMode.Smart;

            // Convert legacy settings from first last name to username
            if (!s.ContainsKey("username") && (s.ContainsKey("first_name") && s.ContainsKey("last_name")))
            {
                s["username"] = s["first_name"] + " " + s["last_name"];
                s.Remove("first_name");
                s.Remove("last_name");
            }

            if (!s.ContainsKey("reconnect_time")) s["reconnect_time"] = 120;

            if (!s.ContainsKey("resolve_uri_time")) s["resolve_uri_time"] = 100;

            if (!s.ContainsKey("resolve_uris")) s["resolve_uris"] = true;

            if (!s.ContainsKey("transaction_notification_chat")) s["transaction_notification_chat"] = true;

            if (!s.ContainsKey("transaction_notification_dialog")) s["transaction_notification_dialog"] = true;

            if (!s.ContainsKey("minimize_to_tray")) s["minimize_to_tray"] = false;

            if (!s.ContainsKey("scene_window_docked")) s["scene_window_docked"] = true;

            if (!s.ContainsKey("taskbar_highlight")) s["taskbar_highlight"] = true;

            if (!s.ContainsKey("rendering_occlusion_culling_enabled2")) s["rendering_occlusion_culling_enabled2"] = false;

            if (!s.ContainsKey("rendering_use_vbo")) s["rendering_use_vbo"] = true;

            if (!s.ContainsKey("log_to_file")) s["log_to_file"] = true;

            if (!s.ContainsKey("disable_chat_im_log")) s["disable_chat_im_log"] = false;

            if (!s.ContainsKey("disable_look_at")) s["disable_look_at"] = false;

            if (!s.ContainsKey("confirm_exit")) s["confirm_exit"] = false;

            if (!s.ContainsKey("highlight_on_chat")) s["highlight_on_chat"] = true;

            if (!s.ContainsKey("highlight_on_im")) s["highlight_on_im"] = true;

            if (!s.ContainsKey("highlight_on_group_im")) s["highlight_on_group_im"] = true;

            if (!s.ContainsKey("group_im_sound")) s["group_im_sound"] = true;

            if (!s.ContainsKey("mention_me_sound")) s["mention_me_sound"] = true;

            if (!s.ContainsKey("mention_me_sound_uuid")) s["mention_me_sound_uuid"] = UISounds.ChatMention;

            if (!s.ContainsKey("av_name_link")) s["av_name_link"] = false;

            if (!s.ContainsKey("on_script_question"))
            {
                s["on_script_question"] = "Ask";
            }

            if (!s.ContainsKey("chat_log_dir"))
            {
                s["chat_log_dir"] = OSD.FromString("");
            }
        }

        private void InitColorSettings()
        {
            for (int i = 1; i <= 48; i++)
            {
                cbxFontSize.Items.Add((float)i);
                cbxFontSize.Items.Add((float)i + 0.5f);
            }

            foreach (var font in FontFamily.Families)
            {
                cbxFont.Items.Add(font.Name);
            }

            //var colorTypes = typeof(System.Drawing.Color);
            //var props = colorTypes.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly);
            var knownColors = typeof(KnownColor).GetEnumValues();

            foreach (var item in knownColors)
            {
                var color = Color.FromKnownColor((KnownColor)item);
                cbxForeground.Items.Add(color);
                cbxBackground.Items.Add(color);
            }

            cbxFont.SelectedItem = SystemFonts.DefaultFont.Name;
            cbxFontSize.SelectedItem = SystemFonts.DefaultFont.Size;
            cbxBold.Checked = SystemFonts.DefaultFont.Bold;
            cbxItalic.Checked = SystemFonts.DefaultFont.Italic;
            cbxForeground.SelectedItem = SystemColors.ControlText;
            cbxBackground.SelectedItem = SystemColors.Control;

            ReloadFontSettings();
        }

        private void ReloadFontSettings()
        {
            lbxColorItems.Items.Clear();

            var chatFontsJson = Instance.GlobalSettings["chat_fonts"];
            if (chatFontsJson.Type != OSDType.Unknown)
            {
                Dictionary<string, Settings.FontSetting> unpacked = new Dictionary<string, Settings.FontSetting>();
                chatFontSettings = JsonConvert.DeserializeObject<Dictionary<string, Settings.FontSetting>>(chatFontsJson);

                foreach (var fontSetting in Settings.DefaultFontSettings)
                {
                    if (!chatFontSettings.ContainsKey(fontSetting.Key))
                    {
                        chatFontSettings.Add(fontSetting.Key, fontSetting.Value);
                    }
                }
            }
            else
            {
                chatFontSettings = Settings.DefaultFontSettings;
            }

            foreach (var item in chatFontSettings)
            {
                item.Value.Name = item.Key;
                lbxColorItems.Items.Add(item.Value);
            }
            if (chatFontSettings.Count > 0)
            {
                lbxColorItems.SetSelected(0, true);
            }
        }

        public frmSettings(RadegastInstance instance)
            : base(instance)
        {
            if (settingInitialized)
            {
                InitSettings(instance.GlobalSettings);
            }

            InitializeComponent();
            AutoSavePosition = true;
            InitColorSettings();

            s = instance.GlobalSettings;
            tbpGraphics.Controls.Add(new Rendering.GraphicsPreferences(instance));
            cbChatTimestamps.Checked = s["chat_timestamps"].AsBoolean();

            cbIMTimeStamps.Checked = s["im_timestamps"].AsBoolean();

            cbChatTimestamps.CheckedChanged += cbChatTimestamps_CheckedChanged;
            cbIMTimeStamps.CheckedChanged += cbIMTimeStamps_CheckedChanged;

            cbTrasactDialog.Checked = s["transaction_notification_dialog"].AsBoolean();
            cbTransactChat.Checked = s["transaction_notification_chat"].AsBoolean();

            cbFriendsNotifications.Checked = s["show_friends_online_notifications"].AsBoolean();
            cbFriendsNotifications.CheckedChanged += cbFriendsNotifications_CheckedChanged;

            cbAutoReconnect.Checked = s["auto_reconnect"].AsBoolean();
            cbAutoReconnect.CheckedChanged += cbAutoReconnect_CheckedChanged;

            cbResolveURIs.Checked = s["resolve_uris"].AsBoolean();
            cbResolveURIs.CheckedChanged += cbResolveURIs_CheckedChanged;

            cbHideLoginGraphics.Checked = s["hide_login_graphics"].AsBoolean();
            cbHideLoginGraphics.CheckedChanged += cbHideLoginGraphics_CheckedChanged;

            cbIgnoreConferenceChats.Checked = s["ignore_conference_chats"].AsBoolean();
            cbIgnoreConferenceChats.CheckedChanged += cbIgnoreConferenceChats_CheckedChanged;

            cbAllowConferenceChatsFromFriends.Enabled = cbIgnoreConferenceChats.Checked;
            cbAllowConferenceChatsFromFriends.Checked = s["allow_conference_chats_from_friends"].AsBoolean();
            cbAllowConferenceChatsFromFriends.CheckedChanged += cbAllowConferenceChatsFromFriends_CheckedChanged;

            cbLogIgnoredConferencesToChat.Enabled = cbIgnoreConferenceChats.Checked;
            cbLogIgnoredConferencesToChat.Checked = s["log_ignored_conferences_to_chat"].AsBoolean();
            cbLogIgnoredConferencesToChat.CheckedChanged += cbLogIgnoredConferencesToChat_CheckedChanged;

            cbRLV.Checked = s["rlv_enabled"].AsBoolean();
            cbRLV.CheckedChanged += (sender, e) =>
            {
                s["rlv_enabled"] = new OSDBoolean(cbRLV.Checked);
            };

            cbRLVDebug.Checked = s["rlv_debugcommands"].AsBoolean();
            cbRLVDebug.CheckedChanged += (sender, e) =>
            {
                s["rlv_debugcommands"] = new OSDBoolean(cbRLVDebug.Checked);
            };

            cbMUEmotes.Checked = s["mu_emotes"].AsBoolean();
            cbMUEmotes.CheckedChanged += (sender, e) =>
            {
                s["mu_emotes"] = new OSDBoolean(cbMUEmotes.Checked);
            };

            if (!s.ContainsKey("minimize_to_tray")) s["minimize_to_tray"] = OSD.FromBoolean(false);
            cbMinToTrey.Checked = s["minimize_to_tray"].AsBoolean();
            cbMinToTrey.CheckedChanged += (sender, e) =>
            {
                s["minimize_to_tray"] = OSD.FromBoolean(cbMinToTrey.Checked);
            };

            cbNoTyping.Checked = s["no_typing_anim"].AsBoolean();
            cbNoTyping.CheckedChanged += (sender, e) =>
            {
                s["no_typing_anim"] = OSD.FromBoolean(cbNoTyping.Checked);
            };

            txtAutoResponse.Text = s["auto_response_text"];
            txtAutoResponse.TextChanged += (sender, e) =>
            {
                s["auto_response_text"] = txtAutoResponse.Text;
            };
            AutoResponseType art = (AutoResponseType)s["auto_response_type"].AsInteger();
            switch (art)
            {
                case AutoResponseType.WhenBusy: rbAutobusy.Checked = true; break;
                case AutoResponseType.WhenFromNonFriend: rbAutoNonFriend.Checked = true; break;
                case AutoResponseType.Always: rbAutoAlways.Checked = true; break;
            }

            cbSyntaxHighlight.Checked = s["script_syntax_highlight"].AsBoolean();
            cbSyntaxHighlight.CheckedChanged += (sender, e) =>
            {
                s["script_syntax_highlight"] = OSD.FromBoolean(cbSyntaxHighlight.Checked);
            };

            switch ((NameMode)s["display_name_mode"].AsInteger())
            {
                case NameMode.Standard: rbDNOff.Checked = true; break;
                case NameMode.Smart: rbDNSmart.Checked = true; break;
                case NameMode.DisplayNameAndUserName: rbDNDandUsernme.Checked = true; break;
                case NameMode.OnlyDisplayName: rbDNOnlyDN.Checked = true; break;
            }

            txtReconnectTime.Text = s["reconnect_time"].AsInteger().ToString();

            txtResolveURITime.Text = s["resolve_uri_time"].AsInteger().ToString();

            cbOnInvOffer.SelectedIndex = s["inv_auto_accept_mode"].AsInteger();
            cbOnInvOffer.SelectedIndexChanged += (sender, e) =>
            {
                s["inv_auto_accept_mode"] = cbOnInvOffer.SelectedIndex;
            };

            cbRadegastLogToFile.Checked = s["log_to_file"];

            cbDisableChatIMLog.Checked = s["disable_chat_im_log"];
            cbDisableChatIMLog.CheckedChanged += (sender, e) =>
            {
                s["disable_chat_im_log"] = cbDisableChatIMLog.Checked;
            };

            cbDisableLookAt.Checked = s["disable_look_at"];
            cbDisableLookAt.CheckedChanged += (sender, e) =>
            {
                s["disable_look_at"] = cbDisableLookAt.Checked;
            };

            cbConfirmExit.Checked = s["confirm_exit"];
            cbConfirmExit.CheckedChanged += (sender, e) =>
            {
                s["confirm_exit"] = cbConfirmExit.Checked;
            };

            cbThemeCompatibilityMode.Checked = s["theme_compatibility_mode"];
            cbThemeCompatibilityMode.CheckedChanged += (sender, e) =>
            {
                s["theme_compatibility_mode"] = cbThemeCompatibilityMode.Checked;
            };

            cbTaskBarHighLight.Checked = s["taskbar_highlight"];
            cbTaskBarHighLight.CheckedChanged += (sender, e) =>
            {
                s["taskbar_highlight"] = cbTaskBarHighLight.Checked;
                UpdateEnabled();
            };

            cbFriendsHighlight.Checked = s["friends_notification_highlight"].AsBoolean();
            cbFriendsHighlight.CheckedChanged += (sender, e) =>
            {
                s["friends_notification_highlight"] = new OSDBoolean(cbFriendsHighlight.Checked);
            };

            cbHighlightChat.Checked = s["highlight_on_chat"];
            cbHighlightChat.CheckedChanged += (sender, e) =>
            {
                s["highlight_on_chat"] = cbHighlightChat.Checked;
            };

            cbHighlightIM.Checked = s["highlight_on_im"];
            cbHighlightIM.CheckedChanged += (sender, e) =>
            {
                s["highlight_on_im"] = cbHighlightIM.Checked;
            };

            cbHighlightGroupIM.Checked = s["highlight_on_group_im"];
            cbHighlightGroupIM.CheckedChanged += (sender, e) =>
            {
                s["highlight_on_group_im"] = cbHighlightGroupIM.Checked;
            };

            // disable_av_name_link
            if (instance.MonoRuntime)
            {
                cbNameLinks.Visible = false;
            }
            else
            {
                cbNameLinks.Checked = s["av_name_link"];
                cbNameLinks.CheckedChanged += (sender, e) =>
                {
                    s["av_name_link"] = cbNameLinks.Checked;
                };
            }

            cbGroupIMSound.Checked = s["group_im_sound"];
            cbGroupIMSound.CheckedChanged += (sender, e) =>
            {
                s["group_im_sound"] = cbGroupIMSound.Checked;
            };

            cbMentionMeSound.Checked = s["mention_me_sound"];
            cbMentionMeSound.CheckedChanged += (sender, e) =>
            {
                s["mention_me_sound"] = cbMentionMeSound.Checked;
                txtMentionMeSoundUUID.Enabled = cbMentionMeSound.Checked;
            };

            txtMentionMeSoundUUID.Text = s["mention_me_sound_uuid"];
            txtMentionMeSoundUUID.Enabled = cbMentionMeSound.Checked;
            txtMentionMeSoundUUID.TextChanged += (sender, e) =>
            {
                if (UUID.TryParse(txtMentionMeSoundUUID.Text, out UUID newMentionMeSoundUUID))
                {
                    txtMentionMeSoundUUID.ForeColor = DefaultForeColor;
                    s["mention_me_sound_uuid"] = newMentionMeSoundUUID;
                }
                else
                {
                    txtMentionMeSoundUUID.ForeColor = Color.Red;
                    s["mention_me_sound_uuid"] = UISounds.ChatMention;
                }
            };

            cbShowScriptErrors.Checked = s["show_script_errors"];
            cbShowScriptErrors.CheckedChanged += (sender, e) =>
            {
                s["show_script_errors"] = cbShowScriptErrors.Checked;
            };

            txtChatLogDir.Text = s["chat_log_dir"];

            autoSitPrefsUpdate();
            pseudoHomePrefsUpdated();
            LSLHelperPrefsUpdate();

            cbAutoScriptPermission.Text = s["on_script_question"];

            UpdateEnabled();

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        void UpdateEnabled()
        {
            if (cbTaskBarHighLight.Checked)
            {
                cbFriendsHighlight.Enabled = cbHighlightChat.Enabled = cbHighlightGroupIM.Enabled = cbHighlightIM.Enabled = true;
            }
            else
            {
                cbFriendsHighlight.Enabled = cbHighlightChat.Enabled = cbHighlightGroupIM.Enabled = cbHighlightIM.Enabled = false;
            }
        }

        void cbHideLoginGraphics_CheckedChanged(object sender, EventArgs e)
        {
            s["hide_login_graphics"] = OSD.FromBoolean(cbHideLoginGraphics.Checked);
        }

        void cbAutoReconnect_CheckedChanged(object sender, EventArgs e)
        {
            s["auto_reconnect"] = OSD.FromBoolean(cbAutoReconnect.Checked);
        }

        private void cbResolveURIs_CheckedChanged(object sender, EventArgs e)
        {
            s["resolve_uris"] = OSD.FromBoolean(cbResolveURIs.Checked);
        }

        void cbFriendsNotifications_CheckedChanged(object sender, EventArgs e)
        {
            s["show_friends_online_notifications"] = OSD.FromBoolean(cbFriendsNotifications.Checked);
        }

        void cbChatTimestamps_CheckedChanged(object sender, EventArgs e)
        {
            s["chat_timestamps"] = OSD.FromBoolean(cbChatTimestamps.Checked);
        }

        void cbIMTimeStamps_CheckedChanged(object sender, EventArgs e)
        {
            s["im_timestamps"] = OSD.FromBoolean(cbIMTimeStamps.Checked);
        }

        private void cbTrasactDialog_CheckedChanged(object sender, EventArgs e)
        {
            s["transaction_notification_dialog"] = OSD.FromBoolean(cbTrasactDialog.Checked);
        }

        private void cbTrasactChat_CheckedChanged(object sender, EventArgs e)
        {
            s["transaction_notification_chat"] = OSD.FromBoolean(cbTransactChat.Checked);
        }

        private void rbAutobusy_CheckedChanged(object sender, EventArgs e)
        {
            s["auto_response_type"] = (int)AutoResponseType.WhenBusy;
        }

        private void rbAutoNonFriend_CheckedChanged(object sender, EventArgs e)
        {
            s["auto_response_type"] = (int)AutoResponseType.WhenFromNonFriend;
        }

        private void rbAutoAlways_CheckedChanged(object sender, EventArgs e)
        {
            s["auto_response_type"] = (int)AutoResponseType.Always;
        }

        private void rbDNOff_CheckedChanged(object sender, EventArgs e)
        {
            if (rbDNOff.Checked)
            {
                Instance.Names.CleanCache();
                s["display_name_mode"] = (int)NameMode.Standard;
            }
        }

        private void rbDNSmart_CheckedChanged(object sender, EventArgs e)
        {
            if (rbDNSmart.Checked)
            {
                Instance.Names.CleanCache();
                s["display_name_mode"] = (int)NameMode.Smart;
            }
        }

        private void rbDNDandUsernme_CheckedChanged(object sender, EventArgs e)
        {
            if (rbDNDandUsernme.Checked)
            {
                Instance.Names.CleanCache();
                s["display_name_mode"] = (int)NameMode.DisplayNameAndUserName;
            }
        }

        private void rbDNOnlyDN_CheckedChanged(object sender, EventArgs e)
        {
            if (rbDNOnlyDN.Checked)
            {
                Instance.Names.CleanCache();
                s["display_name_mode"] = (int)NameMode.OnlyDisplayName;
            }
        }

        private void cbIgnoreConferenceChats_CheckedChanged(object sender, EventArgs e)
        {
            cbAllowConferenceChatsFromFriends.Enabled = cbIgnoreConferenceChats.Checked;
            cbLogIgnoredConferencesToChat.Enabled = cbIgnoreConferenceChats.Checked;

            s["ignore_conference_chats"] = OSD.FromBoolean(cbIgnoreConferenceChats.Checked);
        }

        private void cbAllowConferenceChatsFromFriends_CheckedChanged(object sender, EventArgs e)
        {
            s["allow_conference_chats_from_friends"] = OSD.FromBoolean(cbAllowConferenceChatsFromFriends.Checked);
        }

        private void cbLogIgnoredConferencesToChat_CheckedChanged(object sender, EventArgs e)
        {
            s["log_ignored_conferences_to_chat"] = OSD.FromBoolean(cbLogIgnoredConferencesToChat.Checked);
        }

        private void txtReconnectTime_TextChanged(object sender, EventArgs e)
        {
            string input = System.Text.RegularExpressions.Regex.Replace(txtReconnectTime.Text, @"[^\d]", "");
            int t = 120;
            int.TryParse(input, out t);

            if (txtReconnectTime.Text != t.ToString())
            {
                txtReconnectTime.Text = t.ToString();
                txtReconnectTime.Select(txtReconnectTime.Text.Length, 0);
            }

            s["reconnect_time"] = t;
        }

        private void txtResolveURITime_TextChanged(object sender, EventArgs e)
        {
            string input = System.Text.RegularExpressions.Regex.Replace(txtResolveURITime.Text, @"[^\d]", "");
            int t = 100;
            int.TryParse(input, out t);

            if (txtResolveURITime.Text != t.ToString())
            {
                txtResolveURITime.Text = t.ToString();
                txtResolveURITime.Select(txtResolveURITime.Text.Length, 0);
            }

            s["resolve_uri_time"] = t;
        }

        private void cbRadegastLogToFile_CheckedChanged(object sender, EventArgs e)
        {
            s["log_to_file"] = OSD.FromBoolean(cbRadegastLogToFile.Checked);
        }

        private void cbConfirmExit_CheckedChanged(object sender, EventArgs e)
        {
            s["confirm_exit"] = OSD.FromBoolean(cbConfirmExit.Checked);
        }

        private void cbThemeCompatibilityMode_CheckedChanged(object sender, EventArgs e)
        {
            s["theme_compatibility_mode"] = OSD.FromBoolean(cbThemeCompatibilityMode.Checked);
        }

        #region Auto-Sit

        private void autoSitPrefsUpdate()
        {
            autoSit.Enabled = (Instance.Client.Network.Connected && Instance.ClientSettings != null);
            if (!autoSit.Enabled)
            {
                return;
            }
            AutoSitPreferences prefs = Instance.State.AutoSit.Preferences;
            autoSitName.Text = prefs.PrimitiveName;
            autoSitUUID.Text = prefs.Primitive.ToString();
            autoSitSit.Enabled = prefs.Primitive != UUID.Zero;
            autoSitEnabled.Checked = prefs.Enabled;
        }

        private void autoSitClear_Click(object sender, EventArgs e)
        {
            Instance.State.AutoSit.Preferences = new AutoSitPreferences();
            autoSitPrefsUpdate();
        }

        private void autoSitNameLabel_Click(object sender, EventArgs e)
        {
            autoSitName.SelectAll();
        }

        private void autoSitUUIDLabel_Click(object sender, EventArgs e)
        {
            autoSitUUID.SelectAll();
        }

        private void autoSitSit_Click(object sender, EventArgs e)
        {
            Instance.State.AutoSit.TrySit();
        }

        private void autoSitEnabled_CheckedChanged(object sender, EventArgs e)
        {
            Instance.State.AutoSit.Preferences = new AutoSitPreferences
            {
                Primitive = Instance.State.AutoSit.Preferences.Primitive,
                PrimitiveName = Instance.State.AutoSit.Preferences.PrimitiveName,
                Enabled = autoSitEnabled.Checked
            };

            if (Instance.State.AutoSit.Preferences.Enabled)
            {
                Instance.State.AutoSit.TrySit();
            }
        }

        private void autoSitUUID_Leave(object sender, EventArgs e)
        {
            UUID primID = UUID.Zero;
            if (UUID.TryParse(autoSitUUID.Text, out primID))
            {
                Instance.State.AutoSit.Preferences = new AutoSitPreferences
                {
                    Primitive = primID,
                    PrimitiveName = autoSitName.Text,
                    Enabled = autoSitEnabled.Checked
                };

                if (Instance.State.AutoSit.Preferences.Enabled)
                {
                    Instance.State.AutoSit.TrySit();
                }
            }
            else
            {
                autoSitUUID.Text = UUID.Zero.ToString();
            }
        }
        #endregion

        #region Pseudo Home

        private void pseudoHomePrefsUpdated()
        {
            pseudoHome.Enabled = (Instance.Client.Network.Connected && Instance.ClientSettings != null);
            if (!pseudoHome.Enabled)
            {
                return;
            }
            PseudoHomePreferences prefs = Instance.State.PseudoHome.Preferences;
            pseudoHomeLocation.Text = (prefs.Region != string.Empty) ? string.Format("{0} <{1}, {2}, {3}>", prefs.Region, (int)prefs.Position.X, (int)prefs.Position.Y, (int)prefs.Position.Z) : "";
            pseudoHomeEnabled.Checked = prefs.Enabled;
            pseudoHomeTP.Enabled = (prefs.Region.Trim() != string.Empty);
            pseudoHomeTolerance.Value = Math.Max(pseudoHomeTolerance.Minimum, Math.Min(pseudoHomeTolerance.Maximum, prefs.Tolerance));
        }

        private void pseudoHomeLabel_Click(object sender, EventArgs e)
        {
            pseudoHomeLocation.SelectAll();
        }

        private void pseudoHomeEnabled_CheckedChanged(object sender, EventArgs e)
        {
            Instance.State.PseudoHome.Preferences = new PseudoHomePreferences
            {
                Enabled = pseudoHomeEnabled.Checked,
                Region = Instance.State.PseudoHome.Preferences.Region,
                Position = Instance.State.PseudoHome.Preferences.Position,
                Tolerance = Instance.State.PseudoHome.Preferences.Tolerance
            };
        }

        private void pseudoHomeTP_Click(object sender, EventArgs e)
        {
            Instance.State.PseudoHome.ETGoHome();
        }

        private void pseudoHomeSet_Click(object sender, EventArgs e)
        {
            Instance.State.PseudoHome.Preferences = new PseudoHomePreferences
            {
                Enabled = Instance.State.PseudoHome.Preferences.Enabled,
                Region = Instance.Client.Network.CurrentSim.Name,
                Position = Instance.Client.Self.SimPosition,
                Tolerance = Instance.State.PseudoHome.Preferences.Tolerance
            };
            pseudoHomePrefsUpdated();
        }

        private void pseudoHomeTolerance_ValueChanged(object sender, EventArgs e)
        {
            Instance.State.PseudoHome.Preferences = new PseudoHomePreferences
            {
                Enabled = Instance.State.PseudoHome.Preferences.Enabled,
                Region = Instance.State.PseudoHome.Preferences.Region,
                Position = Instance.State.PseudoHome.Preferences.Position,
                Tolerance = (uint)pseudoHomeTolerance.Value
            };
        }

        private void pseudoHomeClear_Click(object sender, EventArgs e)
        {
            Instance.State.PseudoHome.Preferences = new PseudoHomePreferences();
            pseudoHomePrefsUpdated();
        }
        #endregion

        #region LSL Helper
        private void LSLHelperPrefsUpdate()
        {
            gbLSLHelper.Enabled = (Instance.Client.Network.Connected && Instance.ClientSettings != null);

            if (!gbLSLHelper.Enabled)
            {
                return;
            }

            Instance.State.LSLHelper.LoadSettings();
            tbLSLAllowedOwner.Text = string.Join(Environment.NewLine, Instance.State.LSLHelper.AllowedOwners);
            cbLSLHelperEnabled.CheckedChanged -= cbLSLHelperEnabled_CheckedChanged;
            cbLSLHelperEnabled.Checked = Instance.State.LSLHelper.Enabled;
            cbLSLHelperEnabled.CheckedChanged += cbLSLHelperEnabled_CheckedChanged;
        }

        private void LSLHelperPrefsSave()
        {
            if (Instance.ClientSettings == null)
            {
                return;
            }

            Instance.State.LSLHelper.Enabled = cbLSLHelperEnabled.Checked;
            Instance.State.LSLHelper.AllowedOwners.Clear();

            var warnings = new StringBuilder();
            foreach (var line in tbLSLAllowedOwner.Lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var owner = line.Trim().ToLower();
                if (!UUID.TryParse(owner, out _))
                {
                    warnings.Append("Invalid owner UUID: ").AppendLine(line);
                }
                else
                {
                    Instance.State.LSLHelper.AllowedOwners.Add(owner);
                }
            }
            if (warnings.Length > 0)
            {
                MessageBox.Show(warnings.ToString(), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            Instance.State.LSLHelper.SaveSettings();
            LSLHelperPrefsUpdate();
        }

        private void llLSLHelperInstructios_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Instance.MainForm.ProcessLink("http://radegast.life/documentation/lsl-helper", false);
        }

        private void tbLSLAllowedOwner_Leave(object sender, EventArgs e)
        {
            LSLHelperPrefsSave();
        }

        private void lblLSLUUID_Click(object sender, EventArgs e)
        {
            tbLSLAllowedOwner.SelectAll();
        }

        private void cbLSLHelperEnabled_CheckedChanged(object sender, EventArgs e)
        {
            LSLHelperPrefsSave();
        }
        #endregion LSL Helper

        private void cbAutoScriptPermission_SelectedIndexChanged(object sender, EventArgs e)
        {
            s["on_script_question"] = cbAutoScriptPermission.Text;
        }

        private void cbxForeground_DrawItem(object sender, DrawItemEventArgs e)
        {
            const int kPreviewPadding = 2;
            const int kTextOffset = 15;

            var graphics = e.Graphics;
            var bounds = e.Bounds;

            if (e.Index >= 0 && sender is ComboBox sourceControl)
            {
                var selectedColor = (Color)sourceControl.Items[e.Index];
                {
                    var brushPreview = new SolidBrush(selectedColor);

                    e.DrawBackground();

                    if (e.State == DrawItemState.Selected)
                    {
                        graphics.DrawRectangle(SystemPens.Highlight, bounds);
                    }

                    graphics.DrawString(brushPreview.Color.Name,
                        SystemFonts.DefaultFont,
                        SystemBrushes.ControlText,
                        bounds.X + kTextOffset,
                        bounds.Top + kPreviewPadding);

                    graphics.FillRectangle(brushPreview,
                        bounds.X + kPreviewPadding,
                        bounds.Y + kPreviewPadding,
                        bounds.Height - kPreviewPadding,
                        bounds.Height - kPreviewPadding);
                }
            }
        }

        private void cbxFont_DrawItem(object sender, DrawItemEventArgs e)
        {
            const int kPreviewFontSize = 8;

            var graphics = e.Graphics;
            var bounds = e.Bounds;

            if (e.Index >= 0 && sender is ComboBox sourceControl)
            {
                var fontName = sourceControl.Items[e.Index].ToString();
                var fontPreview = new Font(fontName, kPreviewFontSize);

                e.DrawBackground();

                graphics.DrawRectangle(e.State == DrawItemState.Selected
                        ? SystemPens.Highlight : SystemPens.Window,
                    bounds);

                graphics.DrawString(fontName,
                    fontPreview,
                    SystemBrushes.ControlText,
                    bounds.X,
                    bounds.Top);
            }
        }

        private Settings.FontSetting GetPreviewFontSettings()
        {
            float fontSize = SystemFonts.DefaultFont.Size;
            string fontName = SystemFonts.DefaultFont.Name;
            Color backColor = SystemColors.Window;
            Color foreColor = SystemColors.ControlText;
            FontStyle style = FontStyle.Regular;

            if (cbxFontSize.SelectedItem is float item)
            {
                fontSize = item;
            }
            if (cbxFont.SelectedItem is string selectedItem)
            {
                fontName = selectedItem;
            }
            if (cbxForeground.SelectedItem is Color color)
            {
                foreColor = color;
            }
            if (cbxBackground.SelectedItem is Color backgroundSelectedItem)
            {
                backColor = backgroundSelectedItem;
            }

            if (cbxBold.Checked)
            {
                style |= FontStyle.Bold;
            }
            if (cbxItalic.Checked)
            {
                style |= FontStyle.Italic;
            }

            var previewFontSettings = new Settings.FontSetting()
            {
                Name = string.Empty,
                Font = new Font(fontName, fontSize, style),
                ForeColor = foreColor,
                BackColor = backColor
            };

            return previewFontSettings;
        }

        private void UpdatePreview()
        {
            var previewFontSettings = GetPreviewFontSettings();

            lblPreview.Font = previewFontSettings.Font;
            lblPreview.ForeColor = previewFontSettings.ForeColor;
            lblPreview.BackColor = previewFontSettings.BackColor;
        }

        private void UpdateSelection(Settings.FontSetting selected)
        {
            currentlySelectedFontSetting = selected;
            cbxFontSize.SelectedItem = selected.Font.Size;
            cbxFont.SelectedItem = selected.Font.Name;
            cbxForeground.SelectedItem = selected.ForeColor;
            cbxBackground.SelectedItem = selected.BackColor;
            cbxBold.Checked = selected.Font.Bold;
            cbxItalic.Checked = selected.Font.Italic;
        }

        private void SaveCurrentFontSetting()
        {
            if (currentlySelectedFontSetting != null)
            {
                try
                {
                    var previewFontSettings = GetPreviewFontSettings();
                    previewFontSettings.Name = currentlySelectedFontSetting.Name;

                    chatFontSettings[currentlySelectedFontSetting.Name] = previewFontSettings;

                    var json = JsonConvert.SerializeObject(chatFontSettings);
                    Instance.GlobalSettings["chat_fonts"] = json;
                    Instance.GlobalSettings.Save();

                    var previousIndex = lbxColorItems.SelectedIndex;
                    ReloadFontSettings();
                    lbxColorItems.SelectedIndex = previousIndex;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to save font setting: " + ex.Message);
                }
            }
        }

        private void ResetFontSettings()
        {
            try
            {
                var json = JsonConvert.SerializeObject(Settings.DefaultFontSettings);
                Instance.GlobalSettings["chat_fonts"] = json;
                Instance.GlobalSettings.Save();
                ReloadFontSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to reset font settings: " + ex.Message);
            }
        }

        private void SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdatePreview();
        }

        private void cbxItalic_CheckStateChanged(object sender, EventArgs e)
        {
            UpdatePreview();
        }

        private void cbxBold_CheckStateChanged(object sender, EventArgs e)
        {
            UpdatePreview();
        }

        private void lbxColorItems_SelectedIndexChanged(object sender, EventArgs e)
        {
            var sourceListbox = sender as ListBox;
            if (sourceListbox?.SelectedItem is Settings.FontSetting fontSettings)
            {
                UpdateSelection(fontSettings);
            }
        }

        private void lbxColorItems_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.None)
            {
                if (sender is ListBox sourceListbox)
                {
                    int itemIndex = sourceListbox.IndexFromPoint(new Point(e.X, e.Y));
                    if (itemIndex != -1)
                    {
                        if (sourceListbox.Items[itemIndex] is Settings.FontSetting selectedItem
                           && selectedItem != currentlySelectedFontSetting)
                        {
                            UpdateSelection(selectedItem);
                            sourceListbox.SelectedIndex = itemIndex;
                        }
                    }
                }
            }
        }

        private void lbxColorItems_MouseDown(object sender, MouseEventArgs e)
        {
            lbxColorItems_MouseMove(sender, e);
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            SaveCurrentFontSetting();
        }

        private void btnResetFontSettings_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Reset all color settings to the default values?", "Confirmation",
                   MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) == DialogResult.Yes)
            {
                ResetFontSettings();
            }
        }

        private void FrmSettings_FormClosing(object sender, System.Windows.Forms.FormClosingEventArgs e)
        {
            LSLHelperPrefsSave();
        }

        private void btnChatLogDir_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog folderDlg = new FolderBrowserDialog
            {
                ShowNewFolderButton = true
            };
            // Show the FolderBrowserDialog.  
            DialogResult result = folderDlg.ShowDialog();
            if (result == DialogResult.OK)
            {
                txtChatLogDir.Text = folderDlg.SelectedPath;
                s["chat_log_dir"] = folderDlg.SelectedPath;
            }
        }
    }
}
