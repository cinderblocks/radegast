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
using System.Threading.Tasks;
using LibreMetaverse;
using LibreMetaverse.Assets;

namespace Radegast
{
    /// <summary>
    /// Lightweight gesture trigger helper. Monitors inventory for gesture assets and
    /// can preprocess chat lines to detect and play active gestures.
    /// Moved from LibreMetaverse library (removed in v3.0) to application layer.
    /// </summary>
    public class GestureManager : IDisposable
    {
        private class GestureTrigger
        {
            public string TriggerLower { get; set; } = string.Empty;
            public string Replacement { get; set; } = string.Empty;
            public UUID AssetID { get; set; }
        }

        private readonly GridClient Client;
        private readonly ConcurrentDictionary<UUID, GestureTrigger> _gestures = new ConcurrentDictionary<UUID, GestureTrigger>();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<UUID, byte>> _triggersByWord = new ConcurrentDictionary<string, ConcurrentDictionary<UUID, byte>>(StringComparer.Ordinal);
        private readonly Random _rand = new Random();
        private readonly TimeSpan AssetLoadTimeout = TimeSpan.FromSeconds(15);

        public event Action<UUID, string>? GestureTriggered;

        public GestureManager(GridClient client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public void BeginMonitoring()
        {
            if (Client?.Inventory?.Store == null) { return; }

            Client.Inventory.Store.InventoryObjectAdded += Store_InventoryObjectAdded;
            Client.Inventory.Store.InventoryObjectUpdated += Store_InventoryObjectUpdated;

            try
            {
                foreach (var pair in Client.Self.ActiveGestures)
                {
                    if (pair.Value != UUID.Zero)
                    {
                        if (Client.Inventory.Store.TryGetValue(pair.Key, out var invBase) && invBase is InventoryItem it && it.InventoryType == InventoryType.Gesture)
                        {
                            _ = UpdateInventoryGestureAsync(it);
                        }
                    }
                }
            }
            catch { }
        }

        public void StopMonitoring()
        {
            try
            {
                if (Client?.Inventory?.Store == null) { return; }
                Client.Inventory.Store.InventoryObjectAdded -= Store_InventoryObjectAdded;
                Client.Inventory.Store.InventoryObjectUpdated -= Store_InventoryObjectUpdated;
            }
            catch { }
        }

        public (string processed, UUID played) PreProcessChatMessage(string message)
        {
            TryPreProcessChatMessage(message, out var processed, out var played);
            return (processed, played);
        }

        public bool TryPreProcessChatMessage(string message, out string processed, out UUID played)
        {
            processed = message;
            played = UUID.Zero;

            if (string.IsNullOrWhiteSpace(message)) return false;

            var outString = new StringBuilder(message.Length);
            var words = message.Split(new[] { ' ' }, StringSplitOptions.None);
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
                    if (ProcessWord(word, outString, out var assetPlayed))
                    {
                        gestureWasTriggered = true;
                        played = assetPlayed;
                    }
                }
            }

            if (outString.Length > 0 && outString[outString.Length - 1] == ' ')
                outString.Remove(outString.Length - 1, 1);

            processed = outString.ToString();
            return gestureWasTriggered;
        }

        private bool ProcessWord(string word, StringBuilder outString, out UUID played)
        {
            played = UUID.Zero;
            if (string.IsNullOrEmpty(word))
            {
                outString.Append(word);
                outString.Append(' ');
                return false;
            }

            var lw = word.ToLowerInvariant();

            if (!_triggersByWord.TryGetValue(lw, out var idDict) || idDict.IsEmpty)
            {
                outString.Append(word);
                outString.Append(' ');
                return false;
            }

            var possible = new List<GestureTrigger>();
            foreach (var kv in idDict)
            {
                var id = kv.Key;
                if (!Client.Self.ActiveGestures.ContainsKey(id)) continue;
                if (!_gestures.TryGetValue(id, out var g)) continue;
                possible.Add(g);
            }

            if (possible.Count == 0)
            {
                outString.Append(word);
                outString.Append(' ');
                return false;
            }

            GestureTrigger toPlay = possible.Count == 1 ? possible[0] : possible[_rand.Next(possible.Count)];

            try
            {
                _ = Client.Self.PlayGestureAsync(toPlay.AssetID);
            }
            catch { }

            played = toPlay.AssetID;
            GestureTriggered?.Invoke(toPlay.AssetID, toPlay.TriggerLower);

            if (!string.IsNullOrEmpty(toPlay.Replacement))
            {
                outString.Append(toPlay.Replacement);
                outString.Append(' ');
            }

            return true;
        }

        private void Store_InventoryObjectUpdated(object? sender, InventoryObjectUpdatedEventArgs e)
        {
            if (e.NewObject is InventoryItem item && item.InventoryType == InventoryType.Gesture)
                _ = UpdateInventoryGestureAsync(item);
        }

        private void Store_InventoryObjectAdded(object? sender, InventoryObjectAddedEventArgs e)
        {
            if (e.Obj is InventoryItem item && item.InventoryType == InventoryType.Gesture)
                _ = UpdateInventoryGestureAsync(item);
        }

        private async Task UpdateInventoryGestureAsync(InventoryItem gestureItem)
        {
            try
            {
                UUID assetID = gestureItem.AssetUUID;

                var rawAsset = await Client.Assets.RequestAssetAsync(assetID, AssetType.Gesture, false).ConfigureAwait(false);
                if (rawAsset is not AssetGesture assetGesture || !assetGesture.Decode()) { return; }

                var newTrigger = (assetGesture.Trigger ?? string.Empty).ToLowerInvariant();
                var existing = _gestures.GetOrAdd(gestureItem.UUID, _ => new GestureTrigger());
                var oldTrigger = existing.TriggerLower;

                existing.TriggerLower = newTrigger;
                existing.Replacement = assetGesture.ReplaceWith;
                existing.AssetID = assetGesture.AssetID != UUID.Zero ? assetGesture.AssetID : assetID;

                if (!string.Equals(oldTrigger, newTrigger, StringComparison.Ordinal))
                {
                    if (!string.IsNullOrEmpty(oldTrigger))
                    {
                        if (_triggersByWord.TryGetValue(oldTrigger, out var oldDict))
                        {
                            oldDict.TryRemove(gestureItem.UUID, out _);
                            if (oldDict.IsEmpty)
                                _triggersByWord.TryRemove(oldTrigger, out _);
                        }
                    }

                    if (!string.IsNullOrEmpty(newTrigger))
                    {
                        var dict = _triggersByWord.GetOrAdd(newTrigger, _ => new ConcurrentDictionary<UUID, byte>());
                        dict[gestureItem.UUID] = 0;
                    }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            StopMonitoring();
            _gestures.Clear();
            _triggersByWord.Clear();
        }
    }
}
