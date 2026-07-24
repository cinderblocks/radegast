# Veles User Guide

**Radegast Veles** ("Veles") is the cross-platform rewrite of the Radegast
metaverse client, built on Avalonia. It runs on Windows, macOS, and Linux,
alongside **Radegast** ("Legacy"), the original Windows-only WinForms
client. This guide covers installing, updating, and getting around Veles.
For writing plugins, see the
[Plugin Creator's Guide](PluginCreatorsGuide.md); for what the bundled
plugins do, see the [Plugin Catalog](PluginCatalog.md).

---

## Installing

Builds are published from [GitHub Releases](https://github.com/cinderblocks/Radegast/releases)
(look for tags starting with `Veles/`), and from
[radegast.life/downloads](https://radegast.life/downloads/). Official
installers and archives are self-contained — they bundle the .NET runtime,
so there's nothing else to install first.

### Windows

Two install options:

- **`RadegastVelesSetup_<arch>_<version>.msi`** — the standard installer
  (recommended). Installs Veles under Program Files and registers it for
  auto-update. Installer builds are published for **x64** and **ARM64**.
- **`RadegastVeles_<arch>_<version>.tbz`** — a portable archive. Extract
  anywhere and run `RadegastVeles.exe` directly; no install step, no
  registry changes. Portable archives are published for **x86**, **x64**,
  and **ARM64**.

### macOS

A single **universal DMG** covers both Intel and Apple Silicon Macs. It's
ad-hoc signed — there's no Apple Developer ID certificate for this project
yet, so Gatekeeper will refuse a normal double-click launch with an
"unidentified developer" warning. To run it the first time:

1. Open the DMG and drag Radegast Veles to Applications (or wherever you
   like).
2. **Right-click** (or Control-click) the app and choose **Open**.
3. Confirm in the dialog that appears.

You only need to do this once per download — subsequent launches work
normally.

### Linux

Veles ships as a **`.flatpak`** bundle attached to each GitHub Release.
It's not yet published to Flathub, so install it from the downloaded file:

```bash
flatpak install --user RadegastVeles-<version>-x86_64.flatpak
flatpak run life.radegast.RadegastVeles
```

---

## Updating

Windows and macOS builds check for updates automatically in the background
and offer to install them in-app. Linux does not: Flatpak sandboxing means
Veles can't self-replace, so until it's on Flathub (where `flatpak update`
would handle it), Linux users need to grab new versions manually from
Releases.

---

## Getting Around

Veles covers the same ground as Legacy, reorganized around Avalonia
windows and panels:

- **Chat & messaging** — nearby chat, instant messages, group chat
- **Social** — friends list, group profiles and notices
- **Inventory** — inventory tree, item picker, filtering
- **Appearance** — outfits, wearables, gestures, avatar editor
- **Voice** — WebRTC voice calls, plus built-in text-to-speech (Piper) for
  voice synthesis
- **World** — 3D scene viewer, map, land/region and estate info,
  object/prim editing
- **Content creation** — script editor, mesh/image/sound/animation upload
- **Marketplace** browsing

### Preferences

**Preferences** is organized into eight tabs: **General**, **Chat**,
**Notifications**, **Voice**, **Audio**, **Graphics**, **Grids**, and
**Advanced**. General covers chat logging, auto-reconnect, look-at privacy,
and radar history; the rest are scoped to what their name suggests.

---

## Plugins

Veles supports runtime-loadable plugins — small .NET assemblies that add
commands, menu items, preference tabs, and event hooks without needing a
full rebuild.

- Open **Plugins → Plugin Manager** to see what's loaded, start/stop or
  reload a plugin, and rescan the plugins folder after adding new files.
- The plugins folder lives next to the application itself:
  - **Windows**: next to `RadegastVeles.exe`
  - **macOS**: inside the app bundle, at
    `Radegast Veles.app/Contents/MacOS/plugins`
  - **Linux (Flatpak)**: next to the executable inside the sandbox
  - Use the Plugin Manager's **Open Folder** button rather than hunting
    for it manually, especially on macOS.
- Some plugins add their own tab to the main **Preferences** window, or
  their own **Settings** window reachable from their entry in the
  **Plugins** menu.

Veles ships with several plugins out of the box: client automation
(AutoSit/PseudoHome/LSL Helper/rule engine), chat bridges to Discord and
IRC, an email digest, a local-LLM chatbot, a content import/export
toolkit, and a macro recorder — plus a demo plugin as a coding reference.
The chat bridges, email digest, and chatbot all relay chat/IM content to
an external service once configured, so check each one's privacy note
before enabling it. See the [Plugin Catalog](PluginCatalog.md) for the
full rundown, or the [Plugin Creator's Guide](PluginCreatorsGuide.md) to
write your own.

---

## Getting Help

Report issues or request features at
[github.com/cinderblocks/Radegast/issues](https://github.com/cinderblocks/Radegast/issues).
