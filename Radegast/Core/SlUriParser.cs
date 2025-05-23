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
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using OpenMetaverse;

namespace Radegast
{
    public class SlUriParser
    {
        private enum ResolveType
        {
            /// <summary>
            /// Client specified name format
            /// </summary>
            AgentDefaultName,
            /// <summary>
            /// Display
            /// </summary>
            AgentDisplayName,
            /// <summary>
            /// first.last
            /// </summary>
            AgentUsername,
            /// <summary>
            /// Group name
            /// </summary>
            Group,
            /// <summary>
            /// Parcel name
            /// </summary>
            Parcel
        };

        public class ParsedUriInfo
        {
            public string DisplayText { get; set; }
            public string RequestedFontSettingName { get; set; }
            public UUID RequestedSoundUUID { get; set; }
        }

        public static readonly string UrlRegexString = @"(https?://[^ \r\n]+)|(\[secondlife://[^ \]\r\n]* ?(?:[^\]\r\n]*)])|(secondlife://[^ \r\n]*)";
        public static readonly Regex UrlRegex = new Regex(UrlRegexString, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly RadegastInstance instance;

        private static readonly Regex MapLinkRegex = new Regex(@"^((https?://(slurl\.com|maps\.secondlife\.com)/secondlife/)(?<region>[^ /]+)(/(?<coords>\d+)){0,3})",
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase
        );

        public class MapLinkInfo
        {
            public string RegionName { get; set; }
            public int? X { get; set; }
            public int? Y { get; set; }
            public int? Z { get; set; }

            public override string ToString()
            {
                var extraRegionInfo = "";
                if (Z != null)
                {
                    extraRegionInfo += $" ({X ?? 0},{Y ?? 0},{Z})";
                }
                else if (Y != null)
                {
                    extraRegionInfo += $" ({X ?? 0},{Y})";
                }
                else if (X != null)
                {
                    extraRegionInfo += $" ({X}";
                }

                return RegionName + extraRegionInfo;
            }
        }
        public static bool TryParseMapLink(string link, out MapLinkInfo mapLinkInfo)
        {
            Match match = MapLinkRegex.Match(link);
            if (!match.Success)
            {
                mapLinkInfo = null;
                return false;
            }

            var region = "";
            var coords = new List<int>();

            var regionMatch = match.Groups["region"];
            if (regionMatch.Success)
            {
                region = regionMatch.Value;
            }

            var coordsMatch = match.Groups["coords"];
            if (coordsMatch.Success)
            {
                foreach (Capture coordRaw in coordsMatch.Captures)
                {
                    if (!int.TryParse(coordRaw.Value, out int coord))
                    {
                        break;
                    }

                    coords.Add(coord);
                }
            }

            var x = coords.Count > 0 ? coords[0] : (int?)null;
            var y = coords.Count > 1 ? coords[1] : (int?)null;
            var z = coords.Count > 2 ? coords[2] : (int?)null;

            mapLinkInfo = new MapLinkInfo()
            {
                RegionName = region,
                X = x,
                Y = y,
                Z = z
            };

            return true;
        }

        // Regular expression created by following the majority of http://wiki.secondlife.com/wiki/Viewer_URI_Name_Space (excluding support for secondlife:///app/login).
        //  This is a nasty one and should really only be used on single links to minimize processing time.
        private readonly Regex patternUri = new Regex(
            @"(?<startingbrace>\[)?(" +
                @"(?<regionuri>secondlife://(?<region_name>[^\]/ ]+)(/(?<local_x>[0-9]+))?(/(?<local_y>[0-9]+))?(/(?<local_z>[0-9]+))?)|" +
                @"(?<appuri>secondlife:///app/(" +
                    @"(?<appcommand>agent)/(?<agent_id>[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})/(?<action>[a-z]+)|" +
                    @"(?<appcommand>apperance)/show|" +
                    @"(?<appcommand>balance)/request|" +
                    @"(?<appcommand>chat)/(?<channel>\d+)/(?<text>[^\] ]+)|" + 
                    @"(?<appcommand>classified)/(?<classified_id>[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})/about|" +
                    @"(?<appcommand>event)/(?<event_id>[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})/about|" +
                    @"(?<appcommand>group)/(" +
                        @"(?<group_id>[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})/(?<action>[a-z]+)|" +
                        @"(?<action>create)|" +
                        @"(?<action>list/show))|" +
                    @"(?<appcommand>help)/?<help_query>([^\] ]+)|" +
                    @"(?<appcommand>inventory)/(" +
                        @"(?<inventory_id>[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})/(?<action>select)/?" +
                            @"([?&](" +
                                @"name=(?<name>[^& ]+)" +
                            @"))*|" +
                        @"(?<action>show))|" +
                    @"(?<appcommand>maptrackavatar)/(?<friend_id>[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})|" +
                    @"(?<appcommand>objectim)/(?<object_id>[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})/?" +
                        @"([?&](" +
                            @"name=(?<name>[^& ]+)|" +
                            @"owner=(?<owner>[^& ]+)|" +
                            @"groupowned=(?<groupowned>true)|" +
                            @"slurl=(?<region_name>[^\]/ ]+)(/(?<x>[0-9]+\.?[0-9]*))?(/(?<y>[0-9]+\.?[0-9]*))?(/(?<z>[0-9]+\.?[0-9]*))?" +
                        @"))*|" +
                    @"(?<appcommand>parcel)/(?<parcel_id>[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})/about|" +
                    @"(?<appcommand>search)/(?<category>[a-z]+)/(?<search_term>[^\]/ ]+)|" +
                    @"(?<appcommand>sharewithavatar)/(?<agent_id>[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})|" +
                    @"(?<appcommand>teleport)/(?<region_name>[^\]/ ]+)(/(?<local_x>[0-9]+))?(/(?<local_y>[0-9]+))?(/(?<local_z>[0-9]+))?|" +
                    @"(?<appcommand>voicecallavatar)/(?<agent_id>[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})|" +
                    @"(?<appcommand>wear_folder)/?folder_id=(?<inventory_folder_uuid>[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})|" +
                    @"(?<appcommand>worldmap)/(?<region_name>[^\]/ ]+)(/(?<local_x>[0-9]+))?(/(?<local_y>[0-9]+))?(/(?<local_z>[0-9]+))?)))" +
            @"( (?<endingbrace>[^\]]*)\])?", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);


        public SlUriParser(RadegastInstance instance)
        {
            this.instance = instance;
        }

        /// <summary>
        /// Gets the display text for the specified URI
        /// </summary>
        /// <param name="uri">URI to get the display text of</param>
        /// <returns>Display text for URI</returns>
        public ParsedUriInfo GetLinkName(string uri)
        {
            if (!instance.GlobalSettings["resolve_uris"])
            {
                return new ParsedUriInfo()
                {
                    DisplayText = uri
                };
            }

            if (TryParseMapLink(uri, out var mapLinkInfo))
            {
                return new ParsedUriInfo()
                {
                    DisplayText = mapLinkInfo.ToString()
                };
            }

            Match match = patternUri.Match(uri);
            if (!match.Success)
            {
                return new ParsedUriInfo()
                {
                    DisplayText = uri
                };
            }

            // Custom named links in the form of [secondlife://<truncated> Custom%20Link%20Name] will
            //   result in a link named 'Custom Link Name' regardless of the previous secondlife URI.
            if (match.Groups["startingbrace"].Success && match.Groups["endingbrace"].Length > 0)
            {
                return new ParsedUriInfo()
                {
                    DisplayText = HttpUtility.UrlDecode(match.Groups["endingbrace"].Value)
                };
            }

            if (match.Groups["regionuri"].Success)
            {
                return new ParsedUriInfo()
                {
                    DisplayText = GetLinkNameRegionUri(match)
                };
            }

            if (match.Groups["appuri"].Success)
            {
                string appcommand = match.Groups["appcommand"].Value;

                var displayTextInfo = new ParsedUriInfo();

                switch (appcommand)
                {
                    case "agent":
                        displayTextInfo = GetLinkNameAgent(match);
                        break;
                    case "appearance":
                        displayTextInfo.DisplayText = match.ToString();
                        break;
                    case "balance":
                        displayTextInfo.DisplayText = match.ToString();
                        break;
                    case "chat":
                        displayTextInfo.DisplayText = GetLinkNameChat(match);
                        break;
                    case "classified":
                        displayTextInfo.DisplayText = GetLinkNameClassified(match);
                        break;
                    case "event":
                        displayTextInfo.DisplayText = GetLinkNameEvent(match);
                        break;
                    case "group":
                        displayTextInfo.DisplayText = GetLinkNameGroup(match);
                        break;
                    case "help":
                        displayTextInfo.DisplayText = GetLinkNameHelp(match);
                        break;
                    case "inventory":
                        displayTextInfo.DisplayText = GetLinkNameInventory(match);
                        break;
                    case "maptrackavatar":
                        displayTextInfo.DisplayText = GetLinkNameTrackAvatar(match);
                        break;
                    case "objectim":
                        displayTextInfo.DisplayText = GetLinkNameObjectIm(match);
                        break;
                    case "parcel":
                        displayTextInfo.DisplayText = GetLinkNameParcel(match);
                        break;
                    case "search":
                        displayTextInfo.DisplayText = GetLinkNameSearch(match);
                        break;
                    case "sharewithavatar":
                        displayTextInfo.DisplayText = GetLinkNameShareWithAvatar(match);
                        break;
                    case "teleport":
                        displayTextInfo.DisplayText = GetLinkNameTeleport(match);
                        break;
                    case "voicecallavatar":
                        displayTextInfo.DisplayText = GetLinkNameVoiceCallAvatar(match);
                        break;
                    case "wear_folder":
                        displayTextInfo.DisplayText = GetLinkNameWearFolder(match);
                        break;
                    case "worldmap":
                        displayTextInfo.DisplayText = GetLinkNameWorldMap(match);
                        break;
                    default:
                        displayTextInfo.DisplayText = match.ToString();
                        break;
                }

                return displayTextInfo;
            }

            return new ParsedUriInfo()
            {
                DisplayText = match.ToString()
            };
        }

        /// <summary>
        /// Parses and executes the specified SecondLife URI if valid
        /// </summary>
        /// <param name="uri">URI to parse and execute</param>
        public void ExecuteLink(string uri)
        {
            Match match = patternUri.Match(uri);
            if (!match.Success)
            {
                return;
            }

            if (match.Groups["regionuri"].Success)
            {
                ExecuteLinkRegionUri(match);
            }
            else if (match.Groups["appuri"].Success)
            {
                string appcommand = match.Groups["appcommand"].Value;

                switch (appcommand)
                {
                    case "agent":
                        ExecuteLinkAgent(match);
                        return;
                    case "appearance":
                        ExecuteLinkShowApperance();
                        return;
                    case "balance":
                        ExecuteLinkShowBalance();
                        return;
                    case "chat":
                        ExecuteLinkChat(match);
                        return;
                    case "classified":
                        ExecuteLinkClassified(match);
                        return;
                    case "event":
                        ExecuteLinkEvent(match);
                        return;
                    case "group":
                        ExecuteLinkGroup(match);
                        return;
                    case "help":
                        ExecuteLinkHelp(match);
                        return;
                    case "inventory":
                        ExecuteLinkInventory(match);
                        return;
                    case "maptrackavatar":
                        ExecuteLinkTrackAvatar(match);
                        return;
                    case "objectim":
                        ExecuteLinkObjectIm(match);
                        return;
                    case "parcel":
                        ExecuteLinkParcel(match);
                        return;
                    case "search":
                        ExecuteLinkSearch(match);
                        return;
                    case "sharewithavatar":
                        ExecuteLinkShareWithAvatar(match);
                        return;
                    case "teleport":
                        ExecuteLinkTeleport(match);
                        return;
                    case "voicecallavatar":
                        ExecuteLinkVoiceCallAvatar(match);
                        return;
                    case "wear_folder":
                        ExecuteLinkWearFolder(match);
                        return;
                    case "worldmap":
                        ExecuteLinkWorldMap(match);
                        return;
                }
            }
        }

        #region Name Resolution

        /// <summary>
        /// Gets the name of an agent by UUID. Will block for a short period of time to allow for name resolution.
        /// </summary>
        /// <param name="agentID">Agent UUID</param>
        /// <param name="nameType">Type of name resolution. See ResolveType</param>
        /// <returns>Name of agent on success, INCOMPLETE_NAME on failure or timeout</returns>
        private string GetAgentName(UUID agentID, ResolveType nameType)
        {
            string name = RadegastInstance.INCOMPLETE_NAME;

            using (ManualResetEvent gotName = new ManualResetEvent(false))
            {
                EventHandler<UUIDNameReplyEventArgs> handler = (sender, e) =>
                {
                    if (e.Names.ContainsKey(agentID))
                    {
                        name = e.Names[agentID];
                        try
                        {
                            gotName.Set();
                        }
                        catch (ObjectDisposedException) { }
                    }
                };

                instance.Names.NameUpdated += handler;

                if (nameType == ResolveType.AgentDefaultName)
                {
                    name = instance.Names.Get(agentID);
                }
                else if (nameType == ResolveType.AgentUsername)
                {
                    name = instance.Names.GetUserName(agentID);
                }
                else if (nameType == ResolveType.AgentDisplayName)
                {
                    name = instance.Names.GetDisplayName(agentID);
                }
                else
                {
                    instance.Names.NameUpdated -= handler;
                    return agentID.ToString();
                }

                if (name == RadegastInstance.INCOMPLETE_NAME)
                {
                    gotName.WaitOne(instance.GlobalSettings["resolve_uri_time"], false);
                }

                instance.Names.NameUpdated -= handler;
            }

            return name;
        }

        /// <summary>
        /// Gets the name of a group by UUID. Will block for a short period of time to allow for name resolution.
        /// </summary>
        /// <param name="groupID">Group UUID</param>
        /// <returns>Name of the group on success, INCOMPLETE_NAME on failure or timeout</returns>
        private string GetGroupName(UUID groupID)
        {
            string name = RadegastInstance.INCOMPLETE_NAME;

            using (ManualResetEvent gotName = new ManualResetEvent(false))
            {
                EventHandler<GroupNamesEventArgs> handler = (sender, e) =>
                {
                    if (e.GroupNames.ContainsKey(groupID))
                    {
                        name = e.GroupNames[groupID];
                        try
                        {
                            gotName.Set();
                        }
                        catch (ObjectDisposedException) { }
                    }
                };

                instance.Client.Groups.GroupNamesReply += handler;
                instance.Client.Groups.RequestGroupName(groupID);
                if (name == RadegastInstance.INCOMPLETE_NAME)
                {
                    gotName.WaitOne(instance.GlobalSettings["resolve_uri_time"], false);
                }

                instance.Client.Groups.GroupNamesReply -= handler;
            }

            return name;
        }

        /// <summary>
        /// Gets the name of a parcel by UUID. Will block for a short period of time to allow for name resolution.
        /// </summary>
        /// <param name="parcelID">Parcel UUID</param>
        /// <returns>Name of the parcel on success, INCOMPLETE_NAME on failure or timeout</returns>
        private string GetParcelName(UUID parcelID)
        {
            string name = RadegastInstance.INCOMPLETE_NAME;
            
            using (ManualResetEvent gotName = new ManualResetEvent(false))
            {
                EventHandler<ParcelInfoReplyEventArgs> handler = (sender, e) =>
                {
                    if (e.Parcel.ID == parcelID)
                    {
                        name = e.Parcel.Name;
                        try
                        {
                            gotName.Set();
                        }
                        catch (ObjectDisposedException) { }
                    }
                };

                instance.Client.Parcels.ParcelInfoReply += handler;
                instance.Client.Parcels.RequestParcelInfo(parcelID);
                if (name == RadegastInstance.INCOMPLETE_NAME)
                {
                    gotName.WaitOne(instance.GlobalSettings["resolve_uri_time"], false);
                }

                instance.Client.Parcels.ParcelInfoReply -= handler;
            }

            return name;
        }
        #endregion

        /// <summary>
        /// Attempts to resolve the name of a given key by type (Agent, Group, Parce, etc)
        /// </summary>
        /// <param name="id">UUID of object to resolve</param>
        /// <param name="type">Type of object</param>
        /// <returns>Revoled name</returns>
        private string Resolve(UUID id, ResolveType type)
        {
            switch (type)
            {
                case ResolveType.AgentDefaultName:
                case ResolveType.AgentDisplayName:
                case ResolveType.AgentUsername:
                    return GetAgentName(id, type);
                case ResolveType.Group:
                    return GetGroupName(id);
                case ResolveType.Parcel:
                    return GetParcelName(id);
                default:
                    return id.ToString();
            }
        }

        #region Link name resolution

        private string GetLinkNameRegionUri(Match match)
        {
            string name = HttpUtility.UrlDecode(match.Groups["region_name"].Value);

            string coordinateString = "";
            if (match.Groups["local_x"].Success)
            {
                coordinateString += " (" + match.Groups["local_x"].Value;
            }
            if (match.Groups["local_y"].Success)
            {
                coordinateString += "," + match.Groups["local_y"].Value;
            }
            if (match.Groups["local_z"].Success)
            {
                coordinateString += "," + match.Groups["local_z"].Value;
            }
            if (coordinateString != "")
            {
                coordinateString += ")";
            }

            return string.Format("{0}{1}", name, coordinateString);
        }

        private ParsedUriInfo GetLinkNameAgent(Match match)
        {
            var agentID = new UUID(match.Groups["agent_id"].Value);
            var action = match.Groups["action"].Value;

            var parsedUriInfo = new ParsedUriInfo();

            switch (action)
            {
                case "about":
                case "inspect":
                case "completename":
                    parsedUriInfo.DisplayText = Resolve(agentID, ResolveType.AgentDefaultName);
                    break;
                case "displayname":
                    parsedUriInfo.DisplayText = Resolve(agentID, ResolveType.AgentDisplayName);
                    break;
                case "username":
                    parsedUriInfo.DisplayText = Resolve(agentID, ResolveType.AgentUsername);
                    break;
                case "im":
                    parsedUriInfo.DisplayText = "IM " + Resolve(agentID, ResolveType.AgentDefaultName);
                    break;
                case "offerteleport":
                    parsedUriInfo.DisplayText = "Offer Teleport to " + Resolve(agentID, ResolveType.AgentDefaultName);
                    break;
                case "pay":
                    parsedUriInfo.DisplayText = "Pay " + Resolve(agentID, ResolveType.AgentDefaultName);
                    break;
                case "requestfriend":
                    parsedUriInfo.DisplayText = "Friend Request " + Resolve(agentID, ResolveType.AgentDefaultName);
                    break;
                case "mute":
                    parsedUriInfo.DisplayText = "Mute " + Resolve(agentID, ResolveType.AgentDefaultName);
                    break;
                case "unmute":
                    parsedUriInfo.DisplayText = "Unmute " + Resolve(agentID, ResolveType.AgentDefaultName);
                    break;
                case "mention":
                    parsedUriInfo.DisplayText = "@" + Resolve(agentID, ResolveType.AgentDefaultName);
                    if(agentID == instance.Client.Self.AgentID)
                    {
                        parsedUriInfo.RequestedFontSettingName = "MentionMe";
                        parsedUriInfo.RequestedSoundUUID = UUID.Zero;

                        if (instance.GlobalSettings["mention_me_sound"].AsBoolean())
                        {
                            parsedUriInfo.RequestedSoundUUID = instance.GlobalSettings["mention_me_sound_uuid"].AsUUID();
                        }
                    }
                    else
                    {
                        parsedUriInfo.RequestedFontSettingName = "MentionOthers";
                    }
                    break;
                default:
                    parsedUriInfo.DisplayText = match.ToString();
                    break;
            }

            return parsedUriInfo;
        }

        private string GetLinkNameChat(Match match)
        {
            //string channel = match.Groups["channel"].Value;
            //string text = System.Web.HttpUtility.UrlDecode(match.Groups["text"].Value);

            return match.ToString();
        }

        private string GetLinkNameClassified(Match match)
        {
            //UUID classifiedID = new UUID(match.Groups["classified_id"].Value);

            return match.ToString();
        }

        private string GetLinkNameEvent(Match match)
        {
            //UUID eventID = new UUID(match.Groups["event_id"].Value);

            return match.ToString();
        }

        private string GetLinkNameGroup(Match match)
        {
            string action = match.Groups["action"].Value;

            switch (action)
            {
                case "about":
                case "inspect":
                {
                    UUID groupID = new UUID(match.Groups["group_id"].Value);
                    return Resolve(groupID, ResolveType.Group);
                }
                case "create":
                case "list/show":
                    return match.ToString();
            }

            return match.ToString();
        }

        private string GetLinkNameHelp(Match match)
        {
            //string helpQuery = HttpUtility.UrlDecode(match.Groups["help_query"].Value);

            return match.ToString();
        }

        private string GetLinkNameInventory(Match match)
        {
            //UUID inventoryID = new UUID(match.Groups["agent_id"].Value);
            string action = match.Groups["action"].Value;

            if (action == "select" && match.Groups["name"].Success)
            {
                return HttpUtility.UrlDecode(match.Groups["name"].Value);
            }

            return match.ToString();
        }

        private string GetLinkNameTrackAvatar(Match match)
        {
            //UUID agentID = new UUID(match.Groups["friend_id"].Value);

            return match.ToString();
        }

        private string GetLinkNameObjectIm(Match match)
        {
            //UUID objectID = new UUID(match.Groups["object_id"].Value);
            string name = HttpUtility.UrlDecode(match.Groups["name"].Value);
            //UUID ownerID = new UUID(match.Groups["owner"].Value);
            //string groupowned = match.Groups["groupowned"].Value;
            //string slurl = match.Groups["slurl"].Value;

            if (name != string.Empty)
            {
                return name;
            }

            return match.ToString();
        }

        private string GetLinkNameParcel(Match match)
        {
            UUID parcelID = new UUID(match.Groups["parcel_id"].Value);
            return Resolve(parcelID, ResolveType.Parcel);
        }

        private string GetLinkNameSearch(Match match)
        {
            //string category = match.Groups["category"].Value;
            //string searchTerm = HttpUtility.UrlDecode(match.Groups["search_term"].Value);

            return match.ToString();
        }

        private string GetLinkNameShareWithAvatar(Match match)
        {
            //UUID agentID = new UUID(match.Groups["agent_id"].Value);

            return match.ToString();
        }

        private string GetLinkNameTeleport(Match match)
        {
            string name = HttpUtility.UrlDecode(match.Groups["region_name"].Value);

            string coordinateString = "";
            if (match.Groups["local_x"].Success)
            {
                coordinateString += " (" + match.Groups["local_x"].Value;
            }
            if (match.Groups["local_y"].Success)
            {
                coordinateString += "," + match.Groups["local_y"].Value;
            }
            if (match.Groups["local_z"].Success)
            {
                coordinateString += "," + match.Groups["local_z"].Value;
            }
            if (coordinateString != "")
            {
                coordinateString += ")";
            }

            return string.Format("Teleport to {0}{1}", name, coordinateString);
        }

        private string GetLinkNameVoiceCallAvatar(Match match)
        {
            //UUID agentID = new UUID(match.Groups["agent_id"].Value);

            return match.ToString();
        }

        private string GetLinkNameWearFolder(Match match)
        {
            //UUID folderID = new UUID(match.Groups["inventory_folder_uuid"].Value);

            return match.ToString();
        }

        private string GetLinkNameWorldMap(Match match)
        {
            string name = HttpUtility.UrlDecode(match.Groups["region_name"].Value);
            string x = match.Groups["local_x"].Success ? match.Groups["local_x"].Value : "128";
            string y = match.Groups["local_y"].Success ? match.Groups["local_y"].Value : "128";
            string z = match.Groups["local_z"].Success ? match.Groups["local_z"].Value : "0";

            return string.Format("Show Map for {0} ({1},{2},{3})", name, x, y, z);
        }
        #endregion

        #region Link Execution
        private void ExecuteLinkRegionUri(Match match)
        {
            string name = HttpUtility.UrlDecode(match.Groups["region_name"].Value);
            int x = match.Groups["local_x"].Success ? int.Parse(match.Groups["local_x"].Value) : 128;
            int y = match.Groups["local_y"].Success ? int.Parse(match.Groups["local_y"].Value) : 128;
            int z = match.Groups["local_z"].Success ? int.Parse(match.Groups["local_z"].Value) : 0;

            instance.MainForm.MapTab.Select();
            instance.MainForm.WorldMap.DisplayLocation(name, x, y, z);
        }

        private void ExecuteLinkAgent(Match match)
        {
            UUID agentID = new UUID(match.Groups["agent_id"].Value);
            //string action = match.Groups["action"].Value;

            instance.MainForm.ShowAgentProfile(instance.Names.Get(agentID), agentID);
        }

        private void ExecuteLinkShowApperance()
        {

        }

        private void ExecuteLinkShowBalance()
        {

        }

        private void ExecuteLinkChat(Match match)
        {
            //string channel = match.Groups["channel"].Value;
            //string text = System.Web.HttpUtility.UrlDecode(match.Groups["text"].Value);
        }

        private void ExecuteLinkClassified(Match match)
        {
            //UUID classifiedID = new UUID(match.Groups["classified_id"].Value);
        }

        private void ExecuteLinkEvent(Match match)
        {
            //UUID eventID = new UUID(match.Groups["event_id"].Value);
        }

        private void ExecuteLinkGroup(Match match)
        {
            string action = match.Groups["action"].Value;

            switch (action)
            {
                case "about":
                case "inspect":
                {
                    UUID groupID = new UUID(match.Groups["group_id"].Value);
                    instance.MainForm.ShowGroupProfile(groupID);
                    return;
                }
                case "create":
                    return;
                case "list/show":
                    return;
            }
        }

        private void ExecuteLinkHelp(Match match)
        {
            //string helpQuery = HttpUtility.UrlDecode(match.Groups["help_query"].Value);
        }

        private void ExecuteLinkInventory(Match match)
        {
            //UUID inventoryID = new UUID(match.Groups["agent_id"].Value);
            //string action = match.Groups["action"].Value;
        }

        private void ExecuteLinkTrackAvatar(Match match)
        {
            //UUID agentID = new UUID(match.Groups["friend_id"].Value);
        }

        private void ExecuteLinkObjectIm(Match match)
        {
            //UUID objectID = new UUID(match.Groups["object_id"].Value);
            //string name = HttpUtility.UrlDecode(match.Groups["name"].Value);
            //UUID ownerID = new UUID(match.Groups["owner"].Value);
            //string groupowned = match.Groups["groupowned"].Value;
            //string slurl = match.Groups["slurl"].Value;
        }

        private void ExecuteLinkParcel(Match match)
        {
            //UUID parcelID = new UUID(match.Groups["parcel_id"].Value);
        }

        private void ExecuteLinkSearch(Match match)
        {
            //string category = match.Groups["category"].Value;
            //string searchTerm = HttpUtility.UrlDecode(match.Groups["search_term"].Value);
        }

        private void ExecuteLinkShareWithAvatar(Match match)
        {
            //UUID agentID = new UUID(match.Groups["agent_id"].Value);
        }

        private void ExecuteLinkTeleport(Match match)
        {
            //string name = HttpUtility.UrlDecode(match.Groups["region_name"].Value);
            //string x = match.Groups["local_x"].Success ? match.Groups["local_x"].Value : "128";
            //string y = match.Groups["local_y"].Success ? match.Groups["local_y"].Value : "128";
            //string z = match.Groups["local_z"].Success ? match.Groups["local_z"].Value : "0";
        }

        private void ExecuteLinkVoiceCallAvatar(Match match)
        {
            //UUID agentID = new UUID(match.Groups["agent_id"].Value);
        }

        private void ExecuteLinkWearFolder(Match match)
        {
            //UUID folderID = new UUID(match.Groups["inventory_folder_uuid"].Value);
        }

        private void ExecuteLinkWorldMap(Match match)
        {
            string name = HttpUtility.UrlDecode(match.Groups["region_name"].Value);
            int x = match.Groups["local_x"].Success ? int.Parse(match.Groups["local_x"].Value) : 128;
            int y = match.Groups["local_y"].Success ? int.Parse(match.Groups["local_y"].Value) : 128;
            int z = match.Groups["local_z"].Success ? int.Parse(match.Groups["local_z"].Value) : 0;

            instance.MainForm.MapTab.Select();
            instance.MainForm.WorldMap.DisplayLocation(name, x, y, z);
        }
        #endregion
    }
}
