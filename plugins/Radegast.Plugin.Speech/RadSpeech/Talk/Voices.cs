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
using System.Text.RegularExpressions;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace RadegastSpeech.Talk
{
    internal class Voices
    {
        private readonly PluginControl control;
        /// <summary>
        /// Library of installed voices, keyed by voice name.
        /// </summary>
        private readonly Dictionary<string, AvailableVoice> maleVoiceLibrary;
        private readonly Dictionary<string, AvailableVoice> femaleVoiceLibrary;
        internal Dictionary<string, AvailableVoice> voiceLibrary;
        /// <summary>
        /// Library of assigned voices, keyed by avatar name.
        /// </summary>
        internal Dictionary<UUID, AssignedVoice> assignedVoices;

        private string[] voiceIndex;
        private string[] maleIndex;
        private string[] femaleIndex;
        private Random rgen;
        private readonly OSDMap configVoices;
        private Regex voicePattern;

        internal Voices( PluginControl pc)
        {
            control = pc;
            configVoices = control.config["voices"] as OSDMap;
            maleVoiceLibrary = new Dictionary<string, AvailableVoice>();
            femaleVoiceLibrary = new Dictionary<string, AvailableVoice>();

            assignedVoices = new Dictionary<UUID, AssignedVoice>();
        }

        internal void Populate(Dictionary<string,AvailableVoice> installed)
        {
            if (installed == null)
            {
                voiceLibrary = new Dictionary<string, AvailableVoice>();
                return;
            }
            voiceLibrary = installed;
            int maleCount = 0;
            int femaleCount = 0;

			// Put the available voices into categories
			foreach (AvailableVoice v in installed.Values)
            {
                if (v.Male)
                {
                    maleCount++;
                    maleVoiceLibrary[v.Name] = v;
                }
                else
                {
                    femaleCount++;
                    femaleVoiceLibrary[v.Name] = v;
                }
            }

            // Make an array of the voice names.  This will make it
            // easier to pick one out with a random number.
            List<string> list = new List<string>(voiceLibrary.Keys);
            int i = 0;
            int m = 0;
            int f = 0;
            voiceIndex = new string[list.Count];
            maleIndex = new string[maleVoiceLibrary.Count];
            femaleIndex = new string[femaleVoiceLibrary.Count];
            foreach (string name in list)
            {
                voiceIndex[i++] = name;
                if (maleVoiceLibrary.ContainsKey(name))
                    maleIndex[m++] = name;
                else
                    femaleIndex[f++] = name;
            }
        }

        /// <summary>
        /// Load any voice presets
        /// </summary>
        internal void Start()
        {
            // Format of a line is:
            //    avatar name = "voice name" fast/slow/high/low
            //
            // TODO use regex
            voicePattern = new Regex(
                    @"'([^\']+)'(\s([a-z]+))*",
                    RegexOptions.IgnorePatternWhitespace | RegexOptions.IgnoreCase | RegexOptions.Compiled);


       }

        /// <summary>
        /// Save or update a permanent voice assignment.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="v"></param>
        private void SaveAssignment(string name, AssignedVoice v)
        {
            // Definition is the voice name in single quotes followed
            // by optional modifiers.
            string definition = "'" + v.root.Name + "'";
            switch (v.pitchModification)
            {
                case 0: break;
                case -1:    definition += " low";   break;
                case +1:    definition += " high";  break;
            }

            switch (v.rateModification)
            {
                case 0: break;
                case -1:    definition += " slow";   break;
                case +1:    definition += " fast";  break;
            }

            // Update the configuration file.
            configVoices[name] = new OSDString(definition);
            control.SaveSpeechSettings();
        }

		/// <summary>
		///Get a random number from zero to a limit 
		/// </summary>
		/// <param name="max">
		/// A <see cref="System.Int32"/>
		/// </param>
		/// <returns>
		/// A <see cref="System.Int32"/>
		/// </returns>
        private int GetRandom(int max)
        {
             if (rgen == null)
                rgen = new Random();

             // This returns a value n such that 0 >= n < max
             return rgen.Next(max);
        }

        internal void RemoveIf(Dictionary<string, AvailableVoice> d, string key)
        {
            if (d.ContainsKey(key))
                d.Remove(key);
        }

        internal bool KnowThisPerson(UUID id)
        {
            if (assignedVoices.ContainsKey(id)) return true;
            return false;
        }

        internal AssignedVoice GetNamedVoice(string vname, int r, int p)
        {
            AvailableVoice v;
            if (voiceLibrary.ContainsKey(vname))
            {
                v = voiceLibrary[vname];
            }
            else
            {
                if (voiceIndex != null && voiceIndex.Length > 0)
                    v = voiceLibrary[voiceIndex[GetRandom(voiceIndex.Length)]];
                else
                    return null;
            }

            return new AssignedVoice(v, r, p);
        }

        /// <summary>
        /// Force a particular voice assignment.
        /// </summary>
        /// <param name="v"></param>
        /// <param name="aName"></param>
        /// <param name="id"></param>
        internal void AssignVoice(AssignedVoice v, string aName, UUID id)
        {
            if (id != UUID.Zero && assignedVoices.ContainsKey(id))
                assignedVoices.Remove(id);

            // Update the live dictionary for this session
            assignedVoices[id] = v;

            // Update the preferences file for future sessions
            SaveAssignment(aName, v);
        }

        /// <summary>
        /// Test if an avatar has a pre-selected name.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        internal bool PreAssigned(UUID id)
        {
            // Need the avatar name.
            string avName = control.instance.Names.Get(id);
            if (avName==null) return false;

            AssignedVoice av = PreAssigned(avName);
            if (av == null) return false;
            
            assignedVoices[id] = av;
                return true;
        }

        internal AssignedVoice PreAssigned( string avName )
        {
            // Need an assignment.
            string namedef = configVoices[avName].AsString();
            if (namedef == null) return null;

            // Parse the voice assignment options.
            MatchCollection mc = voicePattern.Matches(namedef);

                if (mc.Count > 0)
                {
                    Match m = mc[0];
                    System.Text.RegularExpressions.Group g1 = m.Groups[1];
                    string vname = g1.Value;

                    // Default to no rate or pitch shift
                    int vrate = 0;
                    int vpitch = 0;

                    // Look for modifiers.
                    for (int i = 2; i < m.Groups.Count; i++)
                    {
                        System.Text.RegularExpressions.Group g = m.Groups[i];

                        switch (g.Value)
                        {
                            case "slow": vrate = -1; break;
                            case "fast": vrate = +1; break;
                            case "high": vpitch = +1; break;
                            case "low": vpitch = -1; break;
                        }
                    }

                    // Save all that as one bundle.
                    return GetNamedVoice(vname, vrate, vpitch);
                }
            
            return null;
        }

        /// <summary>
        /// Choose a random voice from those available.
        /// </summary>
        /// <returns></returns>
        private AssignedVoice PickRandomVoice()
        {
            if (voiceIndex.Length > 0)
                return AddVariation( voiceLibrary[voiceIndex[GetRandom(voiceIndex.Length)]]);
            else
                return null;
        }

        /// <summary>
        /// Choose a random voice constrained by gender.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        private AssignedVoice PickRandomVoice( UUID id )
        {
            AvailableVoice chosen = null;

            // Get the avatar gender, if known.  If not known,
            // this is a 50/50 random guess.
            bool male = control.env.people.isMale(id);
            if (GenderAvailable( male ))
            {
                chosen = male ? maleVoiceLibrary[maleIndex[GetRandom(maleIndex.Length)]] : femaleVoiceLibrary[femaleIndex[GetRandom(femaleIndex.Length)]];

                return AddVariation(chosen);
            }
            else
            {
                return PickRandomVoice();
            }
            
        }

        /// <summary>
        /// Possible gender reassignment based on new information.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="info"></param>
        internal void CheckGender(UUID id, Environment.People.AvatarInfo info)
        {
            // If no voice assigned yet, do not worry.
            if (!assignedVoices.ContainsKey(id)) return;

            AssignedVoice v = assignedVoices[id];
            // A voice is assigned.  If gender matches, do nothing.
            bool isMale = (info.gender > 0);
            if (v.Male == isMale) return;

            // Make a new voice of the correct type, if possible.
            if (!GenderAvailable( isMale )) return;

            // A voice of the correct gender is available, so pick one.
            v = PickRandomVoice(id);
            assignedVoices[id] = v;
        }

        private bool GenderAvailable(bool male)
        {
            if (male)
                return (maleIndex.Length > 0);
            else
                return (femaleIndex.Length > 0);
        }

        /// <summary>
        /// Turn an available voice into an assigned one by adding random variation
        /// </summary>
        /// <param name="v1"></param>
        /// <returns></returns>
        private AssignedVoice AddVariation(AvailableVoice v1)
        {
            int rate = GetRandom(2) - 1;
            int pitch = GetRandom(2) - 1;

            return new AssignedVoice(v1, rate, pitch);
        }

        /// <summary>
        /// Pick a one-time voice for a name.
        /// </summary>
        /// <param name="avName"></param>
        /// <returns></returns>
        /// <remarks>This is used just for System and Object voices.</remarks>
        internal AssignedVoice VoiceFor(string avName)
        {
            AssignedVoice v = PreAssigned(avName);
            if (v != null) return v;

            return PickRandomVoice();
        }

        /// <summary>
        /// Look up or assign a voice with a gender hint.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        internal AssignedVoice VoiceFor(UUID id, bool makeOne )
        {
            // First choice is a voice already matched.
            if (assignedVoices.ContainsKey(id))
                return assignedVoices[id];

            // Then see if pre-assigned.
            if (PreAssigned(id))
                return assignedVoices[id];

            // Make one up.
            if (!makeOne) return null;

            AssignedVoice assigned = PickRandomVoice( id );
            assignedVoices[id] = assigned;
            return assigned;
        }

        internal AssignedVoice VoiceFor(UUID id)
        {
            return VoiceFor(id, true);
        }

    }

    public abstract class AvailableVoice
    {
        public string Name;
        public bool Male = false;
    }

    public class AssignedVoice
    {
        public AvailableVoice root;
        public int rateModification;
        public int pitchModification;
        internal bool Male = false;
        internal AssignedVoice(AvailableVoice r, int rate, int pitch)
        {
            root = r;
            rateModification = rate;
            pitchModification = pitch;
            Male = root.Male;
        }
    }
}
