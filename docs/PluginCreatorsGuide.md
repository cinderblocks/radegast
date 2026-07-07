# Veles Plugin Creator's Guide

This guide explains how to create plugins for **Radegast Veles** using the
`Radegast.Veles.PluginApi` SDK.

---

## Overview

Veles plugins are .NET 10 class library assemblies that:

1. Reference the `Radegast.Veles.PluginApi` NuGet / project.
2. Contain **exactly one** public class decorated with `[VelesPlugin]` that
   implements `IVelesPlugin`.
3. Are placed (as `.dll` files) in the `plugins/` directory next to the main
   Radegast Veles executable.

Plugins are loaded into **isolated, collectible `AssemblyLoadContext`s** so they
can be unloaded and reloaded at runtime without restarting the application.

---

## Quick Start

### 1. Create a Class Library

```bash
dotnet new classlib -n MyPlugin -f net10.0
```

### 2. Reference the Plugin API

```xml
<ItemGroup>
  <ProjectReference Include="path/to/Radegast.Veles.PluginApi.csproj">
    <Private>false</Private>
    <ExcludeAssets>runtime</ExcludeAssets>
  </ProjectReference>
</ItemGroup>
```

> **Important:** Set `<Private>false</Private>` and `<ExcludeAssets>runtime</ExcludeAssets>`
> so that the API assembly is **not** copied into your output — the host application
> already loads it.

Add `<EnableDynamicLoading>true</EnableDynamicLoading>` to your `<PropertyGroup>`
so that the build copies dependencies that the host does *not* provide.

### 3. Implement the Plugin

```csharp
using System;
using Radegast.Veles.PluginApi;

namespace MyPlugin;

[VelesPlugin("My Plugin",
    Description = "A short description.",
    Author = "Your Name",
    Version = "1.0.0",
    Url = "https://example.com/my-plugin")]
public sealed class MyPlugin : IVelesPlugin
{
    private IPluginContext _ctx = null!;

    public void Attach(IPluginContext context)
    {
        _ctx = context;
        _ctx.LogToChat("[MyPlugin] Hello, world!");
    }

    public void Detach()
    {
        _ctx.LogToChat("[MyPlugin] Goodbye!");
    }

    public void Dispose() { }
}
```

### 4. Build & Deploy

```bash
dotnet build -c Release
```

Copy the resulting `MyPlugin.dll` (and any extra dependencies) into the
`plugins/` folder next to `RadegastVeles.exe`. Restart Veles — or open
**Plugins → Plugin Manager** and click **Rescan**.

---

## Plugin Lifecycle

| Phase | Method | Description |
|-------|--------|-------------|
| Load | *(automatic)* | Assembly scanned for `[VelesPlugin]` + `IVelesPlugin`. |
| Start | `Attach(IPluginContext)` | Register commands, menus, event handlers. |
| Stop | `Detach()` | Unregister handlers, save state. |
| Unload | `Dispose()` | Release unmanaged resources. The `AssemblyLoadContext` is then unloaded. |

A plugin can be **reloaded** at runtime via the Plugin Manager — this performs
Stop → Unload → Load → Start.

---

## IPluginContext Reference

The `IPluginContext` is your gateway to the Veles application. It is passed to
`Attach()` and should be stored for the lifetime of the plugin.

### Client Access

| Member | Description |
|--------|-------------|
| `Client` | The `GridClient` for the current session. |
| `NetCom` | Network communication helper (`INetCom`). |
| `Instance` | The core `IRadegastInstance`. |
| `VoiceSynth` | Built-in voice-synthesis service (`IVoiceSynthService?`) — lets a plugin speak text over the active WebRTC voice channel. `null` when voice is unavailable. |
| `IsInP2PCall` | `true` when a P2P voice call is active with at least one other avatar. |
| `NoTypingAnim` | `true` when the user has disabled the in-world typing animation in Preferences; check this before driving typing state. |

### Commands

```csharp
context.RegisterCommand("greet", "Greet someone", "greet <name>",
    (args, writeLine) =>
    {
        writeLine($"Hello, {string.Join(" ", args)}!");
    });

// Remove later:
context.UnregisterCommand("greet");
```

Commands are automatically cleaned up when the plugin is stopped.

### Menu Items

```csharp
context.AddMenuItem(new PluginMenuItemInfo(
    id: "my_action",
    header: "Do _Something",
    onClick: () => { /* handle click */ }));

context.RemoveMenuItem("my_action");
```

Menu items appear under the **Plugins** menu in the main window.

### Preference Tabs

```csharp
context.AddPreferenceTab(new PluginPreferenceTab(
    id: "my_prefs",
    header: "My Plugin",
    contentFactory: () => new MyPrefsPanel())
{
    OnApply = () => SaveMySettings()
});

context.RemovePreferenceTab("my_prefs");
```

> **Note:** `OnApply` is a settable property, not a constructor parameter —
> set it via an object initializer as shown above.

The tab will appear both in the main Preferences window and in the
per-plugin **Settings** window (opened from the plugin's entry in the
**Plugins** menu, or from the Plugin Manager). `ContentFactory` is called
each time a window opens, so return a fresh control rather than caching one.

### File Pickers

```csharp
var files = await context.OpenFilePickerAsync(new FilePickerOpenOptions
{
    Title = "Import Rules",
    AllowMultiple = false
});

var target = await context.SaveFilePickerAsync(new FilePickerSaveOptions
{
    Title = "Export Rules",
    SuggestedFileName = "rules.json"
});
```

Both dialogs are platform-native and must be called from the UI thread.
`OpenFilePickerAsync` returns an empty list if the user cancels;
`SaveFilePickerAsync` returns `null`.

### Notifications

```csharp
context.ShowNotification("Title", "Body text");
context.LogToChat("[MyPlugin] Something happened.");
```

### Plugin Settings

```csharp
context.SetSetting("volume", "75");
string? vol = context.GetSetting("volume"); // "75"
```

Settings are persisted per-plugin, per-user in the global settings store.

### Events

| Event | Args | Fires When |
|-------|------|------------|
| `ChatReceived` | `ChatEventArgs` | Nearby chat message received. |
| `IMReceived` | `InstantMessageEventArgs` | Instant message received. |
| `Connected` | `EventArgs` | Client connects to a simulator. |
| `Disconnected` | `EventArgs` | Client disconnects. |
| `ObjectUpdated` | `PrimEventArgs` | An object or avatar update is received from the simulator. |
| `TeleportProgress` | `TeleportEventArgs` | At each step of a teleport sequence. |
| `FriendOnline` | `FriendInfoEventArgs` | A friend comes online. |
| `FriendOffline` | `FriendInfoEventArgs` | A friend goes offline. |
| `GroupChatJoined` | `GroupChatJoinedEventArgs` | A group chat session is joined or updated. |

```csharp
context.ChatReceived += (s, e) =>
{
    if (e.Message.Contains("hello"))
        context.LogToChat($"{e.FromName} said hello!");
};
```

> **Tip:** Always unsubscribe in `Detach()`. If you forget, the context
> auto-cleans tracked registrations — but explicit cleanup is best practice.

---

## Project Structure Conventions

```
plugins/
  Veles.Plugin.MyPlugin/
    Veles.Plugin.MyPlugin.csproj
    MyPlugin.cs
    ... other source files ...
```

### Recommended `.csproj` Template

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Veles.Plugin.MyPlugin</RootNamespace>
    <AssemblyName>Veles.Plugin.MyPlugin</AssemblyName>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <!-- Output directly to the plugins folder for easy testing -->
    <OutputPath>..\..\bin\$(Configuration)\plugins\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Radegast.Veles.PluginApi\Radegast.Veles.PluginApi.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
  </ItemGroup>
</Project>
```

---

## Tips & Best Practices

- **One plugin per assembly.** The loader uses the first class it finds with
  both `[VelesPlugin]` and `IVelesPlugin`.
- **Keep dependencies minimal.** Shared assemblies (`Radegast.Core`,
  `Radegast.Veles.PluginApi`, `Avalonia`, `LibreMetaverse`) are resolved from
  the host — do not ship them with your plugin.
- **Thread safety.** Events can fire on background threads. Use
  `Avalonia.Threading.Dispatcher.UIThread.Post(...)` when updating UI.
- **Settings keys** are scoped per-plugin automatically — you do not need to
  prefix them.
- **Avoid long-running work** in `Attach`/`Detach`. Use `Task.Run` or timers
  for background processing.
- **Graceful degradation.** If the grid client disconnects, handle
  `Disconnected` and pause background work.

---

## Debugging

1. Set your plugin project's launch profile to start `RadegastVeles.exe`.
2. Attach the debugger to the Veles process.
3. Breakpoints in your plugin code will work because the debugger resolves
   symbols through the `AssemblyLoadContext`.

Alternatively, use `context.LogToChat(...)` for quick printf-style debugging.

---

## Examples

- **Demo Plugin** (`plugins/Veles.Plugin.Demo/`) — Minimal example showing
  commands, menu items, chat events, and settings. Start here.
- **Automation Plugin** (`plugins/Veles.Plugin.Automation/`) — Reimplements
  AutoSit, PseudoHome, and LSL Helper, plus a full rule engine with a
  Preferences tab and file-picker-based import/export.

Veles ships with several other bundled plugins (chat bridges to Discord/IRC,
an email digest, a local-LLM chatbot, a macro recorder, and a content
import/export tool) — see the [Plugin Catalog](PluginCatalog.md) for the full
list and what each one does, and [Veles User Guide](VelesUserGuide.md) for
how to install and enable plugins from the app.
