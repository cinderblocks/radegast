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
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Veles.Plugin.OllamaChat;

/// <summary>
/// Thin async wrapper around the Ollama /api/chat endpoint.
/// Uses the OpenAI-style message list format that Ollama accepts.
/// No extra NuGet packages required — only System.Net.Http / System.Text.Json.
/// </summary>
internal sealed class OllamaClient : IDisposable
{
    private readonly HttpClient _http;
    private bool _disposed;

    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model   { get; set; } = "llama3";

    /// <summary>Maximum number of tokens to generate. 0 = model default.</summary>
    public int MaxTokens { get; set; } = 512;

    public OllamaClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    }

    // ── Chat ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Send <paramref name="messages"/> to Ollama and return the complete assistant reply.
    /// Uses streaming internally so partial tokens are discarded — the caller only
    /// sees the finished response.
    /// </summary>
    public async Task<string> ChatAsync(
        IReadOnlyList<OllamaMessage> messages,
        CancellationToken ct = default)
    {
        var request = new OllamaChatRequest
        {
            Model    = Model,
            Messages = messages,
            Stream   = true,
            Options  = MaxTokens > 0
                ? new OllamaOptions { NumPredict = MaxTokens }
                : null,
        };

        string url = BaseUrl.TrimEnd('/') + "/api/chat";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(request, options: OllamaJson.Options),
        };

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var sb = new System.Text.StringBuilder();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;

            var chunk = JsonSerializer.Deserialize<OllamaChatChunk>(line, OllamaJson.Options);
            if (chunk?.Message?.Content is { Length: > 0 } content)
                sb.Append(content);

            if (chunk?.Done == true) break;
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Fetch the list of locally available models from Ollama.
    /// Returns an empty list if the server is unreachable.
    /// </summary>
    public async Task<List<string>> ListModelsAsync(CancellationToken ct = default)
    {
        try
        {
            string url = BaseUrl.TrimEnd('/') + "/api/tags";
            using var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            var tags = await resp.Content.ReadFromJsonAsync<OllamaTagsResponse>(OllamaJson.Options, ct);
            var names = new List<string>();
            if (tags?.Models != null)
                foreach (var m in tags.Models)
                    if (!string.IsNullOrEmpty(m.Name))
                        names.Add(m.Name);
            return names;
        }
        catch
        {
            return [];
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}

// ── JSON DTOs ─────────────────────────────────────────────────────────────

internal sealed class OllamaMessage
{
    [JsonPropertyName("role")]    public string Role    { get; set; } = string.Empty;
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;

    public static OllamaMessage System(string content)    => new() { Role = "system",    Content = content };
    public static OllamaMessage User(string content)      => new() { Role = "user",      Content = content };
    public static OllamaMessage Assistant(string content) => new() { Role = "assistant", Content = content };
}

internal sealed class OllamaChatRequest
{
    [JsonPropertyName("model")]    public string Model    { get; set; } = string.Empty;
    [JsonPropertyName("messages")] public IReadOnlyList<OllamaMessage> Messages { get; set; } = [];
    [JsonPropertyName("stream")]   public bool Stream { get; set; } = true;
    [JsonPropertyName("options")]  public OllamaOptions? Options { get; set; }
}

internal sealed class OllamaOptions
{
    [JsonPropertyName("num_predict")] public int NumPredict { get; set; }
}

internal sealed class OllamaChatChunk
{
    [JsonPropertyName("message")] public OllamaMessage? Message { get; set; }
    [JsonPropertyName("done")]    public bool Done { get; set; }
}

internal sealed class OllamaTagsResponse
{
    [JsonPropertyName("models")] public List<OllamaModelEntry>? Models { get; set; }
}

internal sealed class OllamaModelEntry
{
    [JsonPropertyName("name")] public string? Name { get; set; }
}

internal static class OllamaJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };
}
