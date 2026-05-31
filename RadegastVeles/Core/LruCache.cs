/*
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn LLC
 * All rights reserved.
 *
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;

namespace Radegast.Veles.Core;

/// <summary>
/// Thread-safe bounded least-recently-used cache.
/// <para>
/// Internally a <see cref="LinkedList{T}"/> orders entries by recency (head = most recent,
/// tail = least recent) and a <see cref="Dictionary{TKey,TValue}"/> provides O(1) lookup.
/// All operations are guarded by a single lock; expected cache sizes (≤ a few hundred)
/// keep the critical section in the microsecond range.
/// </para>
/// <para>
/// When <see cref="Add"/> or the indexer setter pushes the cache above <see cref="Capacity"/>
/// the least-recently-used entry is evicted. Each eviction invokes the optional
/// <c>onEvicted</c> callback (passed to the constructor) so callers can dispose native
/// resources without taking the cache lock themselves.
/// </para>
/// </summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _list = new();
    private readonly object _lock = new();
    private readonly Action<TKey, TValue>? _onEvicted;
    private int _capacity;

    /// <summary>Creates an LRU cache with the given maximum entry count.</summary>
    /// <param name="capacity">Maximum number of entries before eviction.</param>
    /// <param name="onEvicted">
    /// Optional callback invoked once per evicted entry, after the lock is released.
    /// Useful for disposing native (e.g. <see cref="SkiaSharp.SKBitmap"/>) resources.
    /// </param>
    public LruCache(int capacity, Action<TKey, TValue>? onEvicted = null)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity  = capacity;
        _onEvicted = onEvicted;
        _map       = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(capacity);
    }

    /// <summary>Maximum entry count. Setting a smaller value evicts immediately.</summary>
    public int Capacity
    {
        get { lock (_lock) return _capacity; }
        set
        {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value));
            List<KeyValuePair<TKey, TValue>>? evicted = null;
            lock (_lock)
            {
                _capacity = value;
                while (_list.Count > _capacity)
                {
                    var node = _list.Last!;
                    _list.RemoveLast();
                    _map.Remove(node.Value.Key);
                    (evicted ??= new()).Add(node.Value);
                }
            }
            if (evicted != null && _onEvicted != null)
                foreach (var kv in evicted) _onEvicted(kv.Key, kv.Value);
        }
    }

    /// <summary>Current entry count.</summary>
    public int Count { get { lock (_lock) return _list.Count; } }

    /// <summary>
    /// Tries to retrieve <paramref name="value"/> and marks the entry as most-recently used.
    /// Returns <c>false</c> if the key is not present.
    /// </summary>
    public bool TryGetValue(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    /// <summary>
    /// Inserts or updates the entry for <paramref name="key"/> and marks it most-recently used.
    /// If the cache is full the least-recently used entry is evicted and the eviction callback
    /// (if any) is invoked.
    /// </summary>
    public void AddOrUpdate(TKey key, TValue value)
    {
        KeyValuePair<TKey, TValue>? evicted = null;
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _list.Remove(existing);
                var refreshed = new LinkedListNode<KeyValuePair<TKey, TValue>>(
                    new KeyValuePair<TKey, TValue>(key, value));
                _list.AddFirst(refreshed);
                _map[key] = refreshed;
            }
            else
            {
                var node = new LinkedListNode<KeyValuePair<TKey, TValue>>(
                    new KeyValuePair<TKey, TValue>(key, value));
                _list.AddFirst(node);
                _map[key] = node;
                if (_list.Count > _capacity)
                {
                    var last = _list.Last!;
                    _list.RemoveLast();
                    _map.Remove(last.Value.Key);
                    evicted = last.Value;
                }
            }
        }
        if (evicted is { } e && _onEvicted != null)
            _onEvicted(e.Key, e.Value);
    }

    /// <summary>
    /// Removes the entry for <paramref name="key"/> if present. Returns <c>true</c> and the
    /// removed value on success. The eviction callback is <em>not</em> invoked — explicit
    /// removal is the caller's responsibility to clean up.
    /// </summary>
    public bool TryRemove(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _map.Remove(key);
                value = node.Value.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    /// <summary>Returns <c>true</c> if the key is present (does not affect recency).</summary>
    public bool ContainsKey(TKey key)
    {
        lock (_lock) return _map.ContainsKey(key);
    }

    /// <summary>
    /// Removes every entry. The eviction callback is <em>not</em> invoked — callers that
    /// need to dispose resources should iterate via <see cref="DrainAll"/> first.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _list.Clear();
            _map.Clear();
        }
    }

    /// <summary>
    /// Atomically removes every entry and returns the removed key/value pairs so the caller
    /// can dispose them. Order is most-recent first.
    /// </summary>
    public List<KeyValuePair<TKey, TValue>> DrainAll()
    {
        lock (_lock)
        {
            var result = new List<KeyValuePair<TKey, TValue>>(_list.Count);
            foreach (var kv in _list) result.Add(kv);
            _list.Clear();
            _map.Clear();
            return result;
        }
    }
}
