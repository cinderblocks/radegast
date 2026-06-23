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
using System.Threading;
using System.Threading.Tasks;

namespace Veles.Plugin.OllamaChat;

/// <summary>
/// Maintains per-user conversation history and forwards messages to <see cref="OllamaClient"/>.
/// Each unique avatar name gets its own message history (trimmed to <see cref="MaxHistoryMessages"/>
/// to keep context within reason).
/// </summary>
internal sealed class ChatbotEngine : IDisposable
{
    private readonly OllamaClient _client;

    // Per-user history: userName → list of messages (system prompt NOT stored here)
    private readonly Dictionary<string, List<OllamaMessage>> _histories = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>System prompt sent at the head of every conversation.</summary>
    public string SystemPrompt { get; set; } =
        "You are a friendly resident of Second Life. Keep your replies concise (1-3 sentences). " +
        "You can talk about SL culture, building, scripting, and everyday conversation. " +
        "Avoid explicit content and controversial real-world topics.";

    /// <summary>Maximum number of user+assistant turns kept per user.</summary>
    public int MaxHistoryMessages { get; set; } = 20;

    public ChatbotEngine(OllamaClient client)
    {
        _client = client;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Send <paramref name="userMessage"/> from <paramref name="userName"/> to the LLM
    /// and return the assistant reply.
    /// </summary>
    public async Task<string> ReplyAsync(string userName, string userMessage,
        CancellationToken ct = default)
    {
        List<OllamaMessage> history;
        lock (_lock)
        {
            if (!_histories.TryGetValue(userName, out history!))
            {
                history = [];
                _histories[userName] = history;
            }
            history.Add(OllamaMessage.User(userMessage));
        }

        // Build the full message list: system prompt + trimmed history
        var messages = BuildMessages(history);

        string reply;
        try
        {
            reply = await _client.ChatAsync(messages, ct);
            if (string.IsNullOrWhiteSpace(reply))
                reply = "...";
        }
        catch (OperationCanceledException)
        {
            reply = "(response cancelled)";
        }
        catch (Exception ex)
        {
            reply = $"(error: {ex.Message})";
        }

        lock (_lock)
        {
            history.Add(OllamaMessage.Assistant(reply));
            TrimHistory(history);
        }

        return reply;
    }

    /// <summary>Clear conversation history for all users.</summary>
    public void ClearAll()
    {
        lock (_lock) _histories.Clear();
    }

    /// <summary>Clear conversation history for a specific user.</summary>
    public void ClearUser(string userName)
    {
        lock (_lock) _histories.Remove(userName);
    }

    public void Dispose() => _client.Dispose();

    // ── Private ────────────────────────────────────────────────────────────

    private List<OllamaMessage> BuildMessages(List<OllamaMessage> history)
    {
        var messages = new List<OllamaMessage>(history.Count + 1)
        {
            OllamaMessage.System(SystemPrompt)
        };

        lock (_lock)
        {
            // Take only the last MaxHistoryMessages entries
            int start = Math.Max(0, history.Count - MaxHistoryMessages);
            for (int i = start; i < history.Count; i++)
                messages.Add(history[i]);
        }

        return messages;
    }

    private void TrimHistory(List<OllamaMessage> history)
    {
        // Keep at most MaxHistoryMessages pairs (user + assistant = 2 items each)
        int max = MaxHistoryMessages * 2;
        if (history.Count > max)
            history.RemoveRange(0, history.Count - max);
    }
}
