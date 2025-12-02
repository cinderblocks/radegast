using Newtonsoft.Json;
using OpenMetaverse.StructuredData;
using OpenMetaverse;
using System;
using System.Collections.Generic;
using SkiaSharp;
using System.Text.RegularExpressions;
using System.Drawing;

namespace Radegast
{
    public abstract class TextManagerBase : IDisposable
    {
        protected readonly RadegastInstanceForms instance;
        private readonly SlUriParser uriParser;

        public ITextPrinter TextPrinter { get; }
        protected Dictionary<string, SettingsForms.FontSetting> FontSettings { get; set; }

        private static readonly Dictionary<string, string> EmojiMap = new Dictionary<string, string>
        {
            {":smile:", "😊"},
            {":laugh:", "😄"},
            {":sad:", "😢"},
            {":wink:", "😉"},
            {":heart:", "❤️"},
            {":thumbsup:", "👍"}
        };

        private static readonly Regex HtmlTagRegex = new Regex("<.*?>", RegexOptions.Compiled);
        private static readonly Regex ControlCharsRegex = new Regex("[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]+", RegexOptions.Compiled);
        private static readonly Regex WwwRegex = new Regex("^www\\.", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BoldRegex = new Regex(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
        private static readonly Regex ItalicRegex = new Regex(@"\*(.+?)\*", RegexOptions.Compiled);
        private static readonly Regex MentionRegex = new Regex(@"@([\w\-\.]+)", RegexOptions.Compiled);
        private static readonly Regex EmojiRegex = new Regex(@":smile:|:laugh:|:sad:|:wink:|:heart:|:thumbsup:", RegexOptions.Compiled);

        protected TextManagerBase(RadegastInstanceForms instance, ITextPrinter textPrinter)
        {
            TextPrinter = textPrinter;

            this.instance = instance;
            uriParser = new SlUriParser(instance);

            InitializeConfig();

            this.instance.GlobalSettings.OnSettingChanged += OnSettingChanged;
        }

        public abstract void ReprintAllText();

        public virtual void Dispose()
        {
            instance.GlobalSettings.OnSettingChanged -= OnSettingChanged;
        }

        private void InitializeConfig()
        {
            ReloadFonts();
        }

        private void ReloadFonts()
        {
            if (instance.GlobalSettings["chat_fonts"].Type == OSDType.Unknown)
            {
                try
                {
                    instance.GlobalSettings["chat_fonts"] = JsonConvert.SerializeObject(SettingsForms.DefaultFontSettings);
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show("Failed to save default font settings: " + ex.Message);
                }
            }

            try
            {
                FontSettings = JsonConvert.DeserializeObject<Dictionary<string, SettingsForms.FontSetting>>(instance.GlobalSettings["chat_fonts"]);

                foreach (var fontSetting in SettingsForms.DefaultFontSettings)
                {
                    if (!FontSettings.ContainsKey(fontSetting.Key))
                    {
                        FontSettings.Add(fontSetting.Key, fontSetting.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Failed to read chat font settings: " + ex.Message);
            }
        }

        protected virtual void OnSettingChanged(object sender, SettingsEventArgs e)
        {
            if (e.Key == "chat_fonts")
            {
                ReloadFonts();
                ReprintAllText();
            }
        }

        protected void ProcessAndPrintText(string text, bool isNewMessage, bool addNewline)
        {
            // Sanitize incoming text to remove any embedded HTML tags or control characters
            text = SanitizeText(text);

            var playedSounds = new HashSet<UUID>();
            var lineParts = SlUriParser.UrlRegex.Split(text);
            int linePartIndex;

            // 'text' will be split into 1 + NumLinks*2 parts...
            // If 'text' has no links in it:
            //    lineParts[0] = text
            // If 'text' has one link in it:
            //    lineParts[0] = <Text before first link>
            //    lineParts[1] = <first link>
            //    lineParts[2] = <text after first link>
            // If 'text' has two links in it:
            //    lineParts[0] = <Text before first link>
            //    lineParts[1] = <first link>
            //    lineParts[2] = <text after first link>
            //    lineParts[3] = <second link>
            //    lineParts[4] = <text after second link>
            // ...
            for (linePartIndex = 0; linePartIndex < lineParts.Length - 1; linePartIndex += 2)
            {
                var beforeLink = lineParts[linePartIndex];
                // process inline markup for non-link text
                RenderInlineAndPrint(beforeLink, isNewMessage);

                var rawLink = lineParts[linePartIndex + 1];
                var sanitizedLink = SanitizeLinkUri(rawLink);
                var linkTextInfo = uriParser.GetLinkName(rawLink);

                var originalForeColor = TextPrinter.ForeColor;
                var originalBackColor = TextPrinter.BackColor;
                var originalFont = TextPrinter.Font;

                if (!string.IsNullOrEmpty(linkTextInfo.RequestedFontSettingName) && FontSettings.ContainsKey(linkTextInfo.RequestedFontSettingName))
                {
                    var fontSetting = FontSettings[linkTextInfo.RequestedFontSettingName];

                    if (fontSetting.ForeColor != SKColors.Transparent)
                    {
                        TextPrinter.ForeColor = fontSetting.ForeColor;
                    }
                    if (fontSetting.BackColor != SKColors.Transparent)
                    {
                        TextPrinter.BackColor = fontSetting.BackColor;
                    }

                    TextPrinter.Font = fontSetting.Font;
                }

                if (isNewMessage && linkTextInfo.RequestedSoundUUID != UUID.Zero)
                {
                    if (!playedSounds.Contains(linkTextInfo.RequestedSoundUUID))
                    {
                        instance.MediaManager.PlayUISound(linkTextInfo.RequestedSoundUUID);
                        playedSounds.Add(linkTextInfo.RequestedSoundUUID);
                    }
                }

                // If link is not sanitized or settings force plaintext, print as text. Otherwise insert a safe link.
                if (sanitizedLink == null || instance.MonoRuntime || instance.GlobalSettings["resolve_uris_as_plaintext"])
                {
                    TextPrinter.PrintText(linkTextInfo.DisplayText);
                }
                else
                {
                    TextPrinter.InsertLink(linkTextInfo.DisplayText, sanitizedLink);
                }

                TextPrinter.Font = originalFont;
                TextPrinter.ForeColor = originalForeColor;
                TextPrinter.BackColor = originalBackColor;
            }

            if (linePartIndex != lineParts.Length)
            {
                RenderInlineAndPrint(lineParts[linePartIndex], isNewMessage);
            }

            if (addNewline)
            {
                TextPrinter.PrintTextLine("");
            }
        }

        // Remove basic HTML tags and control characters; limit overall length
        private string SanitizeText(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // Strip basic HTML tags and remove control chars using precompiled regexes
            string noHtml = HtmlTagRegex.Replace(input, string.Empty);
            noHtml = ControlCharsRegex.Replace(noHtml, string.Empty);

            // Trim and limit length to avoid extremely long crafted payloads
            noHtml = noHtml.Trim();
            const int MaxLen = 2000;
            if (noHtml.Length > MaxLen) noHtml = noHtml.Substring(0, MaxLen);
            return noHtml;
        }

        // Basic URI sanitization: allow http, https and secondlife schemes; reject javascript/data/file schemes
        private string SanitizeLinkUri(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri)) return null;
            string trimmed = uri.Trim();
            string lower = trimmed.ToLowerInvariant();

            // Reject obviously dangerous schemes
            if (lower.StartsWith("javascript:") || lower.StartsWith("data:") || lower.StartsWith("file:") || lower.StartsWith("vbscript:"))
            {
                return null;
            }

            // Allow secondlife protocol directly
            if (lower.StartsWith("secondlife:")) return trimmed;

            // Allow http(s)
            if (lower.StartsWith("http://") || lower.StartsWith("https://"))
            {
                if (Uri.TryCreate(trimmed, UriKind.Absolute, out var u))
                {
                    return u.AbsoluteUri;
                }
                return null;
            }

            // Support links that start with www. by prepending http://
            if (WwwRegex.IsMatch(trimmed))
            {
                var candidate = "http://" + trimmed;
                if (Uri.TryCreate(candidate, UriKind.Absolute, out var u2))
                {
                    return u2.AbsoluteUri;
                }
                return null;
            }

            // Otherwise reject unknown schemes
            return null;
        }

        private void RenderInlineAndPrint(string text, bool isNewMessage)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Replace emoji shortcodes using a single regex pass to avoid multiple allocations
            text = EmojiRegex.Replace(text, m => EmojiMap.TryGetValue(m.Value, out var v) ? v : m.Value);

            // Handle bold (**text**)
            int pos = 0;
            foreach (Match m in BoldRegex.Matches(text))
            {
                if (m.Index > pos)
                {
                    PrintTextWithMentions(text.Substring(pos, m.Index - pos));
                }
                PrintWithFontStyle(m.Groups[1].Value, FontStyle.Bold);
                pos = m.Index + m.Length;
            }

            if (pos < text.Length)
            {
                var remainder = text.Substring(pos);
                // Handle italic inside remainder
                int p2 = 0;
                foreach (Match mi in ItalicRegex.Matches(remainder))
                {
                    if (mi.Index > p2)
                    {
                        PrintTextWithMentions(remainder.Substring(p2, mi.Index - p2));
                    }
                    PrintWithFontStyle(mi.Groups[1].Value, FontStyle.Italic);
                    p2 = mi.Index + mi.Length;
                }

                if (p2 < remainder.Length)
                {
                    PrintTextWithMentions(remainder.Substring(p2));
                }
            }
        }

        private void PrintWithFontStyle(string text, FontStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            var originalFont = TextPrinter.Font;
            try
            {
                var newFont = new Font(originalFont, style);
                TextPrinter.Font = newFont;
                PrintTextWithMentions(text);
            }
            catch
            {
                // fallback
                PrintTextWithMentions(text);
            }
            finally
            {
                TextPrinter.Font = originalFont;
            }
        }

        private void PrintTextWithMentions(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            int pos = 0;
            foreach (Match m in MentionRegex.Matches(text))
            {
                if (m.Index > pos)
                {
                    TextPrinter.PrintText(text.Substring(pos, m.Index - pos));
                }

                var mention = m.Groups[1].Value;
                var originalFore = TextPrinter.ForeColor;
                var originalBack = TextPrinter.BackColor;
                var originalFont = TextPrinter.Font;

                bool isMe = IsMentioningMe(mention);
                var settingName = isMe ? "MentionMe" : "MentionOthers";
                if (FontSettings.ContainsKey(settingName))
                {
                    var fs = FontSettings[settingName];
                    if (fs.ForeColor != SKColors.Transparent) TextPrinter.ForeColor = fs.ForeColor;
                    if (fs.BackColor != SKColors.Transparent) TextPrinter.BackColor = fs.BackColor;
                    if (fs.Font != null) TextPrinter.Font = fs.Font;
                }

                TextPrinter.PrintText(m.Value);

                TextPrinter.ForeColor = originalFore;
                TextPrinter.BackColor = originalBack;
                TextPrinter.Font = originalFont;

                pos = m.Index + m.Length;
            }

            if (pos < text.Length)
            {
                TextPrinter.PrintText(text.Substring(pos));
            }
        }

        private bool IsMentioningMe(string mention)
        {
            try
            {
                var self = instance.Client?.Self?.Name ?? instance.NetCom?.LoginOptions?.FullName ?? string.Empty;
                if (string.IsNullOrEmpty(self)) return false;
                var tokens = self.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var t in tokens)
                {
                    if (string.Equals(t, mention, StringComparison.OrdinalIgnoreCase)) return true;
                }
                // Also handle common aliases like "me"
                if (string.Equals(mention, "me", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }
    }
}
