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

namespace Veles.Plugin.Macros;

/// <summary>
/// Executes macro step sequences asynchronously, one step at a time.
/// Only one macro can run at once; starting a new one cancels the current.
/// </summary>
internal sealed class MacroRunner : IDisposable
{
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public bool IsRunning { get; private set; }
    public string? RunningMacroName { get; private set; }

    /// <summary>
    /// Run <paramref name="macro"/> against the given plugin context.
    /// Cancels any currently running macro first.
    /// </summary>
    public Task RunAsync(Macro macro, IPluginContext ctx)
    {
        Stop();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        return Task.Run(async () =>
        {
            IsRunning = true;
            RunningMacroName = macro.Name;
            try
            {
                foreach (var step in macro.Steps)
                {
                    ct.ThrowIfCancellationRequested();
                    await ExecuteStepAsync(step, ctx, ct).ConfigureAwait(false);
                }
                ctx.LogToChat($"[Macro] \"{macro.Name}\" complete.");
            }
            catch (OperationCanceledException)
            {
                ctx.LogToChat($"[Macro] \"{macro.Name}\" stopped.");
            }
            catch (Exception ex)
            {
                ctx.LogToChat($"[Macro] Error in \"{macro.Name}\": {ex.Message}");
            }
            finally
            {
                IsRunning = false;
                RunningMacroName = null;
            }
        }, ct);
    }

    public void Stop() => _cts?.Cancel();

    private static async Task ExecuteStepAsync(MacroStep step, IPluginContext ctx, CancellationToken ct)
    {
        switch (step.Type)
        {
            case StepType.Say:
            {
                var chatType = step.ChatVolume switch
                {
                    "Whisper" => ChatType.Whisper,
                    "Shout"   => ChatType.Shout,
                    _         => ChatType.Normal,
                };
                ctx.Client.Self.Chat(step.Text, step.Channel, chatType);
                break;
            }

            case StepType.Emote:
                ctx.Client.Self.Chat($"/me {step.Text}", 0, ChatType.Normal);
                break;

            case StepType.Wait:
                await Task.Delay(Math.Clamp(step.DelayMs, 0, 60_000), ct).ConfigureAwait(false);
                break;

            case StepType.IM:
                if (UUID.TryParse(step.TargetId, out var imTarget))
                    ctx.Client.Self.InstantMessage(imTarget, step.Text);
                break;

            case StepType.Stand:
                ctx.Instance.State.SetSitting(false, UUID.Zero);
                break;

            case StepType.Sit:
                if (UUID.TryParse(step.TargetId, out var sitTarget))
                    ctx.Instance.State.SetSitting(true, sitTarget);
                break;

            case StepType.PlayGesture:
                if (UUID.TryParse(step.AssetId, out var gestureId))
                    await ctx.Client.Self.PlayGestureAsync(gestureId).ConfigureAwait(false);
                break;

            case StepType.Command:
                if (!string.IsNullOrWhiteSpace(step.Text))
                    ctx.Instance.CommandsManager.ExecuteCommand(step.Text);
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
