using Newtonsoft.Json;
using OpenMetaverse.StructuredData;
using OpenMetaverse;
using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Radegast
{
    public abstract class TextManagerBase : IDisposable
    {
        protected readonly RadegastInstance instance;
        private readonly SlUriParser uriParser;

        public ITextPrinter TextPrinter { get; }
        protected Dictionary<string, Settings.FontSetting> FontSettings { get; set; }

        public TextManagerBase(RadegastInstance instance, ITextPrinter textPrinter)
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
                    instance.GlobalSettings["chat_fonts"] = JsonConvert.SerializeObject(Settings.DefaultFontSettings);
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show("Failed to save default font settings: " + ex.Message);
                }
            }

            try
            {
                FontSettings = JsonConvert.DeserializeObject<Dictionary<string, Settings.FontSetting>>(instance.GlobalSettings["chat_fonts"]);

                foreach (var fontSetting in Settings.DefaultFontSettings)
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
                var linkTextInfo = uriParser.GetLinkName(lineParts[linePartIndex + 1]);

                TextPrinter.PrintText(lineParts[linePartIndex]);

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

                if (instance.MonoRuntime)
                {
                    TextPrinter.PrintText(linkTextInfo.DisplayText);
                }
                else
                {
                    TextPrinter.InsertLink(linkTextInfo.DisplayText, lineParts[linePartIndex + 1]);
                }

                TextPrinter.Font = originalFont;
                TextPrinter.ForeColor = originalForeColor;
                TextPrinter.BackColor = originalBackColor;
            }

            if (linePartIndex != lineParts.Length)
            {
                TextPrinter.PrintText(lineParts[linePartIndex]);
            }

            if (addNewline)
            {
                TextPrinter.PrintTextLine("");
            }
        }
    }
}
