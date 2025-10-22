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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using OpenMetaverse;
using OpenMetaverse.Assets;

namespace Radegast.Core
{
    public class GestureManager
    {
        private class GestureTrigger
        {
            public string TriggerLower { get; set; }
            public string Replacement { get; set; }
            public UUID AssetID { get; set; }
        }

        private readonly RadegastInstance Instance;
        private ConcurrentDictionary<UUID, GestureTrigger> Gestures { get; } = new ConcurrentDictionary<UUID, GestureTrigger>();
        private Random Random { get; } = new Random();

        public GestureManager(RadegastInstance instance)
        {
            Instance = instance;
        }

        /// <summary>
        /// Begins monitoring for changes in gestures.
        /// </summary>
        public void BeginMonitoring()
        {
            Instance.Client.Inventory.Store.InventoryObjectAdded += Store_InventoryObjectAdded;
            Instance.Client.Inventory.Store.InventoryObjectUpdated += Store_InventoryObjectUpdated;
        }

        /// <summary>
        /// Stops monitoring for changes in gestures.
        /// </summary>
        public void StopMonitoring()
        {
            Instance.Client.Inventory.Store.InventoryObjectAdded -= Store_InventoryObjectAdded;
            Instance.Client.Inventory.Store.InventoryObjectUpdated -= Store_InventoryObjectUpdated;
        }

        /// <summary>
        /// Processes a chat message and activates a gesture if a gesture trigger is present.
        /// </summary>
        /// <param name="message">Message to process</param>
        /// <returns>New message after gesture has been triggered and trigger word has been replaced</returns>
        public string PreProcessChatMessage(string message)
        {
            var outString = new StringBuilder(message.Length);
            var words = message.Split(' ');
            var gestureWasTriggered = false;

            foreach (var word in words)
            {
                if (gestureWasTriggered)
                {
                    outString.Append(word);
                    outString.Append(' ');
                }
                else
                {
                    gestureWasTriggered = ProcessWord(word, outString);
                }
            }

            // Remove trailing space that was added above at the end of our new sentence
            if (outString.Length > 0 && outString[outString.Length - 1] == ' ')
            {
                outString.Remove(outString.Length - 1, 1);
            }

            return outString.ToString();
        }

        /// <summary>
        /// Checks a single word for a gesture trigger and appends the final word to the output
        /// </summary>
        /// <param name="word">Word to check for gesture triggers</param>
        /// <param name="outString">Where to output the word or replacement word to</param>
        /// <returns>True if a gesture trigger was executed.</returns>
        private bool ProcessWord(string word, StringBuilder outString)
        {
            var possibleTriggers = new List<GestureTrigger>();
            var lowerWord = word.ToLower();

            Instance.Client.Self.ActiveGestures.ForEach(pair =>
            {
                var activeGestureID = pair.Key;
                if (!Gestures.TryGetValue(activeGestureID, out var gesture))
                {
                    return;
                }

                if (lowerWord != gesture.TriggerLower)
                {
                    return;
                }

                possibleTriggers.Add(gesture);
            });

            if (possibleTriggers.Count == 0)
            {
                outString.Append(word);
                outString.Append(' ');
                return false;
            }

            GestureTrigger gestureToPlay;
            if (possibleTriggers.Count > 1)
            {
                var gestureIndexToPlay = Random.Next(possibleTriggers.Count);
                gestureToPlay = possibleTriggers[gestureIndexToPlay];
            }
            else
            {
                gestureToPlay = possibleTriggers[0];
            }

            Instance.Client.Self.PlayGesture(gestureToPlay.AssetID);

            if (!string.IsNullOrEmpty(gestureToPlay.Replacement))
            {
                outString.Append(gestureToPlay.Replacement);
                outString.Append(' ');
            }

            return true;
        }

        /// <summary>
        /// Handles any addition or update to gesture items in the inventory.
        /// </summary>
        /// <param name="gesture">Gesture that was added or updated.</param>
        private void UpdateInventoryGesture(InventoryGesture gesture)
        {
            Instance.Client.Assets.RequestAsset(gesture.AssetUUID, AssetType.Gesture, false, (_, asset) =>
            {
                if (!(asset is AssetGesture assetGesture))
                {
                    return;
                }

                if (!assetGesture.Decode())
                {
                    return;
                }

                var existingGestureTrigger = Gestures.GetOrAdd(gesture.UUID, __ => new GestureTrigger());
                existingGestureTrigger.TriggerLower = assetGesture.Trigger.ToLower();
                existingGestureTrigger.Replacement = assetGesture.ReplaceWith;
                existingGestureTrigger.AssetID = assetGesture.AssetID;
            });
        }

        private void Store_InventoryObjectUpdated(object sender, InventoryObjectUpdatedEventArgs e)
        {
            if (e.NewObject is InventoryGesture gesture)
            {
                UpdateInventoryGesture(gesture);
            }
        }

        private void Store_InventoryObjectAdded(object sender, InventoryObjectAddedEventArgs e)
        {
            if (e.Obj is InventoryGesture gesture)
            {
                UpdateInventoryGesture(gesture);
            }
        }
    }
}
