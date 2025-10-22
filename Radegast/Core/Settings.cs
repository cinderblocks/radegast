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
using System.Collections;
using System.IO;
using System.Xml;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.Serialization;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Radegast
{
    public class Settings : IDictionary<string, OSD>
    {
        private readonly string SettingsFile;
        private readonly OSDMap SettingsData;

        public delegate void SettingChangedCallback(object sender, SettingsEventArgs e);
        public event SettingChangedCallback OnSettingChanged;

        public static readonly Dictionary<string, FontSetting> DefaultFontSettings = new Dictionary<string, FontSetting>()
        {
            {"Normal", new FontSetting {
                Name = "Normal",
                ForeColor = SystemColors.ControlText.ToSKColor(),
                BackColor = SKColors.Transparent,
                Font = FontSetting.DefaultFont,
            }},
            {"StatusBlue", new FontSetting {
                Name = "StatusBlue",
                ForeColor = SKColors.Blue,
                BackColor = SKColors.Transparent,
                Font = FontSetting.DefaultFont,
            }},
            {"StatusDarkBlue", new FontSetting {
                Name = "StatusDarkBlue",
                ForeColor = SKColors.DarkBlue,
                BackColor = SKColors.Transparent,
                Font = FontSetting.DefaultFont,
            }},
            {"LindenChat", new FontSetting {
                Name = "LindenChat",
                ForeColor = SKColors.DarkGreen,
                BackColor = SKColors.Transparent,
                Font = FontSetting.DefaultFont,
            }},
            {"ObjectChat", new FontSetting {
                Name = "ObjectChat",
                ForeColor = SKColors.DarkCyan,
                BackColor = SKColors.Transparent,
                Font = FontSetting.DefaultFont,
            }},
            {"StartupTitle", new FontSetting {
                Name = "StartupTitle",
                ForeColor = SystemColors.ControlText.ToSKColor(),
                BackColor = SKColors.Transparent,
                Font = new Font(FontSetting.DefaultFont, FontStyle.Bold),
            }},
            {"Alert", new FontSetting {
                Name = "Alert",
                ForeColor = SKColors.DarkRed,
                BackColor = SKColors.Transparent,
                Font = FontSetting.DefaultFont,
            }},
            {"Error", new FontSetting {
                Name = "Error",
                ForeColor = SKColors.Red,
                BackColor = SKColors.Transparent,
                Font = FontSetting.DefaultFont,
            }},
            {"OwnerSay", new FontSetting {
                Name = "OwnerSay",
                ForeColor = SKColors.DarkGoldenrod,
                BackColor = SKColors.Transparent,
                Font = FontSetting.DefaultFont,
            }},
            {"Timestamp", new FontSetting {
                Name = "Timestamp",
                ForeColor = SystemColors.GrayText.ToSKColor(),
                BackColor = SKColors.Transparent,
                Font = FontSetting.DefaultFont,
            }},
            {"Name", new FontSetting {
                Name = "Name",
                ForeColor = SystemColors.ControlText.ToSKColor(),
                BackColor = SKColors.Transparent,
                Font = FontSetting.DefaultFont,
            }},
            {"Notification", new FontSetting {
                Name = "Notification",
                ForeColor = SystemColors.ControlText.ToSKColor(),
                BackColor = SKColors.Transparent,
                Font = FontSetting.DefaultFont,
            }},
            {"IncomingIM", new FontSetting {
                Name = "IncomingIM",
                ForeColor = SystemColors.ControlText.ToSKColor(),
                BackColor = SKColors.Transparent,
                Font = FontSetting.DefaultFont,
            }},
            {"OutgoingIM", new FontSetting {
                Name = "OutgoingIM",
                ForeColor = SystemColors.ControlText.ToSKColor(),
                BackColor = SKColors.Transparent,
                Font = FontSetting.DefaultFont,
            }},
            {"Emote", new FontSetting {
                Name = "Emote",
                ForeColor = SystemColors.ControlText.ToSKColor(),
                BackColor = SKColors.Transparent,
                Font = FontSetting.DefaultFont,
            }},
            {"Self", new FontSetting {
                Name = "Self",
                ForeColor = SystemColors.ControlText.ToSKColor(),
                BackColor = SKColors.Transparent,
                Font = FontSetting.DefaultFont,
            }},
            {"MentionMe", new FontSetting {
                Name = "MentionMe",
                ForeColor = SystemColors.ControlText.ToSKColor(),
                BackColor = SKColors.Yellow,
                Font = FontSetting.DefaultFont,
            }},
            {"MentionOthers", new FontSetting {
                Name = "MentionOthers",
                ForeColor = SystemColors.ControlText.ToSKColor(),
                BackColor = SKColors.Transparent,
                Font = FontSetting.DefaultFont,
            }},
        };

        public class FontSetting
        {
            [IgnoreDataMember]
            public static readonly Font DefaultFont = new Font(FontFamily.GenericSansSerif, 8.0f);

            [IgnoreDataMember]
            public Font Font;

            [IgnoreDataMember]
            public SKColor ForeColor;

            [IgnoreDataMember]
            public SKColor BackColor;


            public string Name;

            public string ForeColorString
            {
                get => ColorTranslator.ToHtml(ForeColor.ToDrawingColor());
                set => ForeColor = ColorTranslator.FromHtml(value).ToSKColor();
            }

            public string BackColorString
            {
                get => ColorTranslator.ToHtml(BackColor.ToDrawingColor());
                set => BackColor = ColorTranslator.FromHtml(value).ToSKColor();
            }

            public string FontString
            {
                get
                {
                    if (Font != null)
                    {
                        TypeConverter converter = TypeDescriptor.GetConverter(typeof(Font));
                        return converter.ConvertToString(Font);
                    }
                    return null;
                }
                set
                {
                    try
                    {
                        TypeConverter converter = TypeDescriptor.GetConverter(typeof(Font));
                        Font = converter.ConvertFromString(value) as Font;
                    }
                    catch (Exception)
                    {
                        Font = DefaultFont;
                    }

                }
            }

            public override string ToString()
            {
                return Name;
            }
        }

        public Settings(string fileName)
        {
            SettingsFile = fileName;

            try
            {
                string xml = File.ReadAllText(SettingsFile);
                SettingsData = (OSDMap)OSDParser.DeserializeLLSDXml(xml);
            }
            catch
            {
                Logger.DebugLog($"Failed opening Settings file: {fileName}");
                SettingsData = new OSDMap();
                Save();
            }
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(SettingsFile, SerializeLLSDXmlStringFormatted(SettingsData.Copy()));
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to save settings", Helpers.LogLevel.Warning, ex);
            }
        }

        public static string SerializeLLSDXmlStringFormatted(OSD data)
        {
            using (StringWriter sw = new StringWriter())
            {
                using (XmlTextWriter writer = new XmlTextWriter(sw))
                {
                    writer.Formatting = Formatting.Indented;
                    writer.Indentation = 4;
                    writer.IndentChar = ' ';

                    writer.WriteStartElement(string.Empty, "llsd", string.Empty);
                    OSDParser.SerializeLLSDXmlElement(writer, data);
                    writer.WriteEndElement();

                    writer.Close();

                    return sw.ToString();
                }
            }
        }

        private void FireEvent(string key, OSD val)
        {
            if (OnSettingChanged != null)
            {
                try { OnSettingChanged(this, new SettingsEventArgs(key, val)); }
                catch (Exception) {}
            }
        }

        #region IDictionary Implementation

        public int Count => SettingsData.Count;
        public bool IsReadOnly => false;
        public ICollection<string> Keys => SettingsData.Keys;
        public ICollection<OSD> Values => SettingsData.Values;

        public OSD this[string key]
        {
            get => SettingsData[key];
            set 
            {
                if (string.IsNullOrEmpty(key))
                {
                    Logger.DebugLog("Warning: trying to set an empty setting: " + Environment.StackTrace);
                }
                else
                {
                    SettingsData[key] = value;
                    FireEvent(key, value);
                    Save();
                }
            }
        }

        public bool ContainsKey(string key)
        {
            return SettingsData.ContainsKey(key);
        }

        public void Add(string key, OSD llsd)
        {
            SettingsData.Add(key, llsd);
            FireEvent(key, llsd);
            Save();
        }

        public void Add(KeyValuePair<string, OSD> kvp)
        {
            SettingsData.Add(kvp.Key, kvp.Value);
            FireEvent(kvp.Key, kvp.Value);
            Save();
        }

        public bool Remove(string key)
        {
            bool ret = SettingsData.Remove(key);
            FireEvent(key, null);
            Save();
            return ret;
        }

        public bool TryGetValue(string key, out OSD llsd)
        {
            return SettingsData.TryGetValue(key, out llsd);
        }

        public void Clear()
        {
            SettingsData.Clear();
            Save();
        }

        public bool Contains(KeyValuePair<string, OSD> kvp)
        {
            // This is a bizarre function... we don't really implement it
            // properly, hopefully no one wants to use it
            return SettingsData.ContainsKey(kvp.Key);
        }

        public void CopyTo(KeyValuePair<string, OSD>[] array, int index)
        {
            throw new NotImplementedException();
        }

        public bool Remove(KeyValuePair<string, OSD> kvp)
        {
            bool ret = SettingsData.Remove(kvp.Key);
            FireEvent(kvp.Key, null);
            Save();
            return ret;
        }

        public IEnumerator<KeyValuePair<string, OSD>> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return SettingsData.GetEnumerator();
        }

        #endregion IDictionary Implementation

    }

    public class SettingsEventArgs : EventArgs
    {
        public string Key;
        public OSD Value;

        public SettingsEventArgs(string key, OSD val)
        {
            Key = key;
            Value = val;
        }
    }
}
