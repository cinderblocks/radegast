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
using System.Threading;
using System.Threading.Tasks;
using LibreMetaverse;
using Radegast.Veles.PluginApi;

namespace Veles.Plugin.OllamaChat;

[VelesPlugin("Ollama Chatbot",
    Description = "AI chat-bot powered by a locally-running Ollama LLM (Llama 3, Mistral, Phi-3, etc.).",
    Author = "Sjofn LLC",
    Version = "1.0.0",
    Url = "https://radegast.life/")]
public sealed class OllamaChatbotPlugin : IVelesPlugin
{
    private IPluginContext _ctx = null!;
    private OllamaClient?  _ollamaClient;
    private ChatbotEngine? _engine;
    private OllamaSettingsControl? _settingsControl;

    // ── Settings (reloaded from ctx on each Apply) ─────────────────────────
    private bool   _enabled           = false;
    private string _baseUrl           = "http://localhost:11434";
    private string _model             = "llama3";
    private bool   _respondWithoutName  = false;
    private float  _respondRange       = -1f;   // -1 = unlimited
    private bool   _shout2shout        = false;
    private bool   _whisper2whisper    = false;
    private bool   _randomDelay        = false;
    private bool   _respondToLocal     = true;
    private bool   _respondToPersonalIM = true;
    private bool   _respondToAdHoc     = false;
    private bool   _respondToGroup     = false;
    private int    _maxTokens          = 512;
    private bool   _speakAnswers      = false;

    private readonly Random _rand = new();
    private readonly SemaphoreSlim _chatLock = new(1, 1);

    // ── IVelesPlugin ──────────────────────────────────────────────────────

    public void Attach(IPluginContext context)
    {
        _ctx = context;
        LoadSettings();
        RebuildEngine();

        _ctx.RegisterCommand("ollama",
            "Control the Ollama chatbot",
            "ollama on|off|status|clear [user]|model <name>|prompt <text>",
            OnCommand);

        _ctx.AddMenuItem(new PluginMenuItemInfo("ollama.toggle",
            "Ollama Chatbot: Toggle", () => SetEnabled(!_enabled)));

        _ctx.AddPreferenceTab(new PluginPreferenceTab(
            "ollama.settings", "Ollama Chatbot",
            () => _settingsControl = new OllamaSettingsControl(_ctx))
        {
            OnApply = OnPreferencesApply,
        });

        _ctx.ChatReceived += OnChatReceived;
        _ctx.IMReceived   += OnIMReceived;
        _ctx.Connected    += OnConnected;
    }

    public void Detach()
    {
        _ctx.ChatReceived -= OnChatReceived;
        _ctx.IMReceived   -= OnIMReceived;
        _ctx.Connected    -= OnConnected;

        _ctx.UnregisterCommand("ollama");
        _ctx.RemoveMenuItem("ollama.toggle");
        _ctx.RemovePreferenceTab("ollama.settings");

        _engine?.Dispose();
        _engine = null;
    }

    public void Dispose() { }

    // ── Settings ───────────────────────────────────────────────────────────

    private string S(string key, string def) => _ctx.GetSetting($"ollama_{key}") ?? def;

    private void LoadSettings()
    {
        _enabled            = S("enabled",              "false") == "true";
        _baseUrl            = S("base_url",             "http://localhost:11434");
        _model              = S("model",                "llama3");
        _respondWithoutName   = S("respond_without_name",   "false") == "true";
        _respondRange        = float.TryParse(S("respond_range", "-1"), out float r) ? r : -1f;
        _shout2shout         = S("shout2shout",             "false") == "true";
        _whisper2whisper     = S("whisper2whisper",          "false") == "true";
        _randomDelay         = S("random_delay",             "false") == "true";
        _respondToLocal      = S("respond_to_local",         "true")  == "true";
        _respondToPersonalIM = S("respond_to_personal_im",   "true")  == "true";
        _respondToAdHoc      = S("respond_to_adhoc",         "false") == "true";
        _respondToGroup      = S("respond_to_group",         "false") == "true";
        _maxTokens           = int.TryParse(S("max_tokens", "512"), out int t) ? t : 512;
        _speakAnswers       = S("speak_answers",         "false") == "true";
    }

    private void OnPreferencesApply()
    {
        _settingsControl?.Apply(_ctx);
        LoadSettings();
        RebuildEngine();
        _ctx.LogToChat("[Ollama] Settings applied.");
    }

    private void RebuildEngine()
    {
        _engine?.Dispose();
        _ollamaClient = new OllamaClient
        {
            BaseUrl   = _baseUrl,
            Model     = _model,
            MaxTokens = _maxTokens,
        };
        _engine = new ChatbotEngine(_ollamaClient)
        {
            SystemPrompt = S("system_prompt",
                "You are a friendly resident of Second Life. Keep replies concise (1-3 sentences). " +
                "You can talk about SL culture, building, scripting, and everyday conversation."),
        };
    }

    private void SetEnabled(bool value)
    {
        _enabled = value;
        _ctx.SetSetting("ollama_enabled", value ? "true" : "false");
        _ctx.LogToChat($"[Ollama] Chatbot {(value ? "enabled" : "disabled")}.");
    }

    // ── Command handler ────────────────────────────────────────────────────

    private void OnCommand(string[] args, Action<string> write)
    {
        string sub = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
        switch (sub)
        {
            case "on":
                SetEnabled(true);
                break;
            case "off":
                SetEnabled(false);
                break;
            case "status":
                write($"[Ollama] Enabled : {_enabled}");
                write($"[Ollama] URL     : {_baseUrl}");
                write($"[Ollama] Model   : {_model}");
                write($"[Ollama] Range    : {(_respondRange < 0 ? "unlimited" : $"{_respondRange}m")}");
                write($"[Ollama] ByName   : {!_respondWithoutName}");
                write($"[Ollama] Local    : {_respondToLocal}");
                write($"[Ollama] PersonalIM: {_respondToPersonalIM}");
                write($"[Ollama] AdHoc   : {_respondToAdHoc}");
                write($"[Ollama] Group   : {_respondToGroup}");
                break;
            case "clear":
                if (args.Length > 1)
                {
                    string user = string.Join(" ", args[1..]);
                    _engine?.ClearUser(user);
                    write($"[Ollama] Cleared history for {user}.");
                }
                else
                {
                    _engine?.ClearAll();
                    write("[Ollama] Cleared all conversation histories.");
                }
                break;
            case "model":
                if (args.Length < 2) { write("Usage: ollama model <name>"); return; }
                _model = args[1];
                _ctx.SetSetting("ollama_model", _model);
                RebuildEngine();
                write($"[Ollama] Model set to {_model}.");
                break;
            case "prompt":
                if (args.Length < 2) { write("Usage: ollama prompt <text>"); return; }
                var prompt = string.Join(" ", args[1..]);
                _ctx.SetSetting("ollama_system_prompt", prompt);
                if (_engine != null) _engine.SystemPrompt = prompt;
                write("[Ollama] System prompt updated.");
                break;
            case "models":
                _ = Task.Run(async () =>
                {
                    var models = await _ollamaClient!.ListModelsAsync();
                    if (models.Count == 0)
                        write("[Ollama] No models found (is Ollama running?)");
                    else
                        foreach (var m in models)
                            write($"  • {m}");
                });
                break;
            default:
                write("Usage: ollama on|off|status|clear [user]|model <name>|prompt <text>|models");
                break;
        }
    }

    // ── Event handlers ─────────────────────────────────────────────────────

    private void OnConnected(object? sender, EventArgs e)
    {
        if (_enabled)
            _ctx.LogToChat("[Ollama] Chatbot ready.");
    }

    private void OnChatReceived(object? sender, ChatEventArgs e)
    {
        if (!_enabled || !_respondToLocal) return;
        if (e.SourceType != ChatSourceType.Agent) return;
        if (e.SourceID == _ctx.Client.Self.AgentID) return;
        if (e.Type is not (ChatType.Normal or ChatType.Shout or ChatType.Whisper)) return;
        if (string.IsNullOrWhiteSpace(e.Message)) return;

        // Proximity filter
        if (_respondRange >= 0f)
        {
            float dist = Vector3.Distance(_ctx.Client.Self.SimPosition, e.Position);
            if (dist > _respondRange) return;
        }

        // Name filter
        string myFirst = FirstName(_ctx.Client.Self.Name);
        if (!_respondWithoutName &&
            !e.Message.Contains(myFirst, StringComparison.OrdinalIgnoreCase))
            return;

        // Strip own name from message so the LLM sees a cleaner input
        string msg = e.Message
            .Replace(myFirst, "", StringComparison.OrdinalIgnoreCase)
            .Trim();
        if (string.IsNullOrWhiteSpace(msg)) return;

        string fromName = e.FromName;
        ChatType replyType = e.Type;
        if (!_shout2shout)   replyType = ChatType.Normal;
        if (!_whisper2whisper && replyType == ChatType.Whisper) replyType = ChatType.Normal;
        if (_shout2shout   && e.Type == ChatType.Shout)   replyType = ChatType.Shout;
        if (_whisper2whisper && e.Type == ChatType.Whisper) replyType = ChatType.Whisper;

        var finalType = replyType;
        _ = RespondToChatAsync(fromName, msg, e.Position, finalType);
    }

    private void OnIMReceived(object? sender, InstantMessageEventArgs e)
    {
        if (!_enabled) return;
        if (e.IM.FromAgentID == _ctx.Client.Self.AgentID) return;
        if (string.IsNullOrWhiteSpace(e.IM.Message)) return;

        UUID fromId    = e.IM.FromAgentID;
        UUID sessionId = e.IM.IMSessionID;
        string fromName = e.IM.FromAgentName;
        string message  = e.IM.Message;

        // Group chat: dialog is SessionSend with GroupIM flag
        if (e.IM.Dialog == InstantMessageDialog.SessionSend && e.IM.GroupIM)
        {
            if (!_respondToGroup) return;
            // Only reply if mentioned by name in group chat
            string myFirst = FirstName(_ctx.Client.Self.Name);
            if (!message.Contains(myFirst, StringComparison.OrdinalIgnoreCase)) return;
            _ = RespondToIMAsync(fromId, sessionId, fromName, message);
            return;
        }

        if (e.IM.Dialog != InstantMessageDialog.MessageFromAgent) return;

        // Ad-hoc / conference session: session ID differs from the sender's agent ID
        if (sessionId != fromId)
        {
            if (!_respondToAdHoc) return;
            _ = RespondToIMAsync(fromId, sessionId, fromName, message);
            return;
        }

        // Personal 1-to-1 IM
        if (!_respondToPersonalIM) return;
        _ = RespondToIMAsync(fromId, sessionId, fromName, message);
    }

    // ── Reply logic ────────────────────────────────────────────────────────

    private async Task RespondToChatAsync(
        string fromName, string message, Vector3 fromPos, ChatType replyType)
    {
        await _chatLock.WaitAsync();
        try
        {
            if (_randomDelay)
                await Task.Delay(1000 + _rand.Next(2000));

            _ctx.Client.Self.Movement.TurnToward(fromPos);

            await Task.Delay(_randomDelay ? 2000 + _rand.Next(3000) : 800);

            // Show typing animation while the LLM is thinking.
            bool typingAnim = !_ctx.NoTypingAnim;
            if (typingAnim) _ctx.Instance.State.SetTyping(true);
            string reply;
            try
            {
                reply = await _engine!.ReplyAsync(fromName, message);
            }
            finally
            {
                if (typingAnim) _ctx.Instance.State.SetTyping(false);
            }
            reply = TruncateSL(reply);

            _ctx.Client.Self.Chat(reply, 0, replyType);
            if (_speakAnswers) _ctx.VoiceSynth?.Speak(reply);
        }
        finally
        {
            _chatLock.Release();
        }
    }

    private async Task RespondToIMAsync(
        UUID fromId, UUID sessionId, string fromName, string message)
    {
        await _chatLock.WaitAsync();
        try
        {
            if (_randomDelay)
                await Task.Delay(1000 + _rand.Next(2000));

            // Show IM typing indicator and optional in-world typing animation
            // for the entire duration the LLM is generating a reply.
            bool typingAnim = !_ctx.NoTypingAnim;
            _ctx.NetCom.SendIMStartTyping(fromId, sessionId);
            if (typingAnim) _ctx.Instance.State.SetTyping(true);
            string reply;
            try
            {
                reply = await _engine!.ReplyAsync(fromName, message);
            }
            finally
            {
                _ctx.NetCom.SendIMStopTyping(fromId, sessionId);
                if (typingAnim) _ctx.Instance.State.SetTyping(false);
            }
            reply = TruncateSL(reply);

            _ctx.NetCom.SendInstantMessage(reply, fromId, sessionId);
            // Only speak IM replies over voice when already in an active P2P call with that user.
            if (_speakAnswers && _ctx.IsInP2PCall) _ctx.VoiceSynth?.Speak(reply);
        }
        finally
        {
            _chatLock.Release();
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string FirstName(string fullName)
        => fullName.Split(' ')[0];

    /// <summary>SL chat is capped at 1023 bytes; truncate gracefully.</summary>
    private static string TruncateSL(string text, int maxLen = 1000)
        => text.Length <= maxLen ? text : text[..maxLen] + "…";
}
