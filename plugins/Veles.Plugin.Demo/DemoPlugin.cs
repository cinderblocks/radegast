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
using LibreMetaverse;
using Radegast.Veles.PluginApi;

namespace Veles.Plugin.Demo;

[VelesPlugin("Demo Plugin",
    Description = "Demonstrates the Veles plugin API: commands, menu items, chat events, and settings.",
    Author = "Sjofn LLC",
    Version = "1.0.0",
    Url = "https://radegast.life/")]
public sealed class DemoPlugin : IVelesPlugin
{
    private IPluginContext _ctx = null!;

    public void Attach(IPluginContext context)
    {
        _ctx = context;

        _ctx.RegisterCommand("demo", "Demo plugin echo command",
            "demo <message> — echoes a message back to chat",
            OnDemoCommand);

        _ctx.AddMenuItem(new PluginMenuItemInfo(
            "demo_hello", "Demo: Say _Hello", OnHelloMenuClick));

        _ctx.ChatReceived += OnChatReceived;
        _ctx.Connected += OnConnected;

        var lastGreet = _ctx.GetSetting("last_greet");
        _ctx.LogToChat(lastGreet != null
            ? $"[Demo] Plugin attached. Last greeting was at {lastGreet}."
            : "[Demo] Plugin attached for the first time!");
    }

    public void Detach()
    {
        _ctx.ChatReceived -= OnChatReceived;
        _ctx.Connected -= OnConnected;
        _ctx.LogToChat("[Demo] Plugin detached.");
    }

    public void Dispose()
    {
        // Nothing extra to dispose beyond what Detach cleans up.
    }

    private void OnDemoCommand(string[] args, Action<string> writeLine)
    {
        if (args.Length == 0)
        {
            writeLine("Usage: demo <message>");
            return;
        }

        var message = string.Join(" ", args);
        writeLine($"[Demo] Echo: {message}");
    }

    private void OnHelloMenuClick()
    {
        _ctx.Client.Self.Chat("Hello from the Demo Plugin!", 0, ChatType.Normal);
        _ctx.SetSetting("last_greet", DateTime.UtcNow.ToString("O"));
    }

    private void OnChatReceived(object? sender, ChatEventArgs e)
    {
        if (e.Type == ChatType.Normal
            && e.SourceType == ChatSourceType.Agent
            && e.SourceID != _ctx.Client.Self.AgentID
            && e.Message.Contains("demo plugin", StringComparison.OrdinalIgnoreCase))
        {
            _ctx.LogToChat($"[Demo] {e.FromName} mentioned the demo plugin!");
        }
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        _ctx.LogToChat("[Demo] Connected to the grid!");
    }
}
