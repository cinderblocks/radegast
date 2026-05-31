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
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading;

namespace Veles.Plugin.Email;

// ── Data types ────────────────────────────────────────────────────────────────

public enum DigestCategory { Nearby, Im, Group }

public sealed record DigestEntry(
    DigestCategory Category,
    string From,
    string Message,
    string TypeHint,
    DateTime Timestamp = default)
{
    public DateTime Timestamp { get; } = Timestamp == default ? DateTime.Now : Timestamp;
}

public sealed class EmailConfig
{
    public string SmtpHost     { get; init; } = string.Empty;
    public int    SmtpPort     { get; init; } = 587;
    public bool   UseSsl       { get; init; } = true;
    public string Username     { get; init; } = string.Empty;
    public string Password     { get; init; } = string.Empty;
    public string FromAddress  { get; init; } = string.Empty;
    public string ToAddress    { get; init; } = string.Empty;
    public string Subject      { get; init; } = "SL Chat Digest — {date}";
    public int    IntervalMins { get; init; } = 60;
    public int    MaxMessages  { get; init; } = 200;
    public bool   IncludeNearby { get; init; } = true;
    public bool   IncludeIm     { get; init; } = true;
    public bool   IncludeGroup  { get; init; } = true;

    public bool IsValid(out string reason)
    {
        if (string.IsNullOrWhiteSpace(SmtpHost))  { reason = "SMTP host is required";          return false; }
        if (string.IsNullOrWhiteSpace(ToAddress)) { reason = "Recipient address is required";   return false; }
        if (string.IsNullOrWhiteSpace(FromAddress)) { reason = "From address is required";      return false; }
        reason = string.Empty;
        return true;
    }
}

// ── Core digest engine ────────────────────────────────────────────────────────

/// <summary>
/// Collects chat messages in a thread-safe buffer and flushes them to an
/// SMTP server on a configurable timer interval.
/// </summary>
public sealed class EmailDigest : IDisposable
{
    private readonly EmailConfig _cfg;
    private readonly Action<string> _log;
    private readonly Lock _lock = new();
    private readonly List<DigestEntry> _buffer = new();
    private Timer? _timer;
    private bool _disposed;

    public bool IsRunning => _timer != null && !_disposed;
    public int  BufferedCount { get { lock (_lock) return _buffer.Count; } }

    public EmailDigest(EmailConfig cfg, Action<string> log)
    {
        _cfg = cfg;
        _log = log;
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EmailDigest));
        var interval = TimeSpan.FromMinutes(_cfg.IntervalMins);
        _timer = new Timer(_ => Flush(), null, interval, interval);
    }

    /// <summary>Add a message to the buffer (thread-safe). Trims to MaxMessages if needed.</summary>
    public void Buffer(DigestEntry entry)
    {
        if (_disposed) return;

        bool included = entry.Category switch
        {
            DigestCategory.Nearby => _cfg.IncludeNearby,
            DigestCategory.Im     => _cfg.IncludeIm,
            DigestCategory.Group  => _cfg.IncludeGroup,
            _                     => true,
        };
        if (!included) return;

        lock (_lock)
        {
            _buffer.Add(entry);
            // Drop oldest messages if we exceed the cap
            if (_buffer.Count > _cfg.MaxMessages)
                _buffer.RemoveAt(0);
        }
    }

    /// <summary>Trigger an immediate flush regardless of the schedule.</summary>
    public void SendNow() => ThreadPool.QueueUserWorkItem(_ => Flush());

    private void Flush()
    {
        List<DigestEntry> entries;
        lock (_lock)
        {
            if (_buffer.Count == 0) return;
            entries = new List<DigestEntry>(_buffer);
            _buffer.Clear();
        }

        try
        {
            Send(entries);
        }
        catch (Exception ex)
        {
            _log($"[Email] Send failed: {ex.Message}");
            // Re-buffer so messages aren't lost on transient failures
            lock (_lock)
                _buffer.InsertRange(0, entries);
        }
    }

    private void Send(List<DigestEntry> entries)
    {
        var body  = BuildBody(entries);
        var subject = _cfg.Subject.Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

        using var msg = new MailMessage(_cfg.FromAddress, _cfg.ToAddress, subject, body)
        {
            IsBodyHtml = false,
            BodyEncoding = Encoding.UTF8,
        };

        using var smtp = new SmtpClient(_cfg.SmtpHost, _cfg.SmtpPort)
        {
            EnableSsl = _cfg.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };

        if (!string.IsNullOrWhiteSpace(_cfg.Username))
            smtp.Credentials = new NetworkCredential(_cfg.Username, _cfg.Password);

        smtp.Send(msg);
        _log($"[Email] Digest sent — {entries.Count} message(s) to {_cfg.ToAddress}.");
    }

    private static string BuildBody(List<DigestEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Second Life Chat Digest — {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();

        foreach (var group in entries.GroupBy(e => e.Category).OrderBy(g => g.Key))
        {
            string header = group.Key switch
            {
                DigestCategory.Nearby => "Nearby Chat",
                DigestCategory.Im     => "Instant Messages",
                DigestCategory.Group  => "Group / Conference Chat",
                _                     => group.Key.ToString(),
            };
            sb.AppendLine($"── {header} ──────────────────────────────────────");
            foreach (var entry in group.OrderBy(e => e.Timestamp))
            {
                sb.AppendLine($"[{entry.Timestamp:HH:mm:ss}] {entry.From}: {entry.Message}");
            }
            sb.AppendLine();
        }

        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Total: {entries.Count} message(s)");
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }
}
