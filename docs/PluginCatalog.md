# Veles Plugin Catalog

A rundown of the plugins that ship alongside Radegast Veles, for people
deciding what to enable — not a coding guide. If you want to write your own
plugin, see the [Plugin Creator's Guide](PluginCreatorsGuide.md) instead.

All plugins are managed from **Plugins → Plugin Manager** in the main
window. See the [Veles User Guide](VelesUserGuide.md#plugins) for how to
install, enable, and configure plugins.

> **Privacy note:** several bundled plugins relay nearby chat, group chat,
> or IMs to a third-party service (Discord, IRC, email, or a chatbot
> backend) once you configure and enable them. That means messages from
> *other* people, not just you, leave the client — see each plugin's own
> privacy note below before turning one on.

- [Demo Plugin](#demo-plugin) — reference example, safe to ignore
- [Automation Plugin](#automation-plugin) — AutoSit, PseudoHome, LSL Helper, rule engine
- [Discord Relay](#discord-relay) — bridges chat to a Discord channel
- [IRC Relay](#irc-relay) — bridges chat to an IRC channel
- [Email Digest](#email-digest) — batches chat/IMs into periodic emails
- [Ollama Chatbot](#ollama-chatbot) — AI chat replies via a local (or remote) LLM
- [Import/Export](#importexport) — save objects, animations, sounds, textures, wearables to disk and back
- [Macros](#macros) — record and replay named sequences of in-world actions

---

## Demo Plugin

*Source: `plugins/Veles.Plugin.Demo/`*

A minimal reference plugin — not intended for daily use, but harmless to
leave enabled. It demonstrates the four basic plugin building blocks:

- A `demo` chat command that echoes back whatever text you give it.
- A **Demo: Say Hello** entry in the Plugins menu that posts a greeting to
  nearby chat and remembers the last time you used it.
- A nearby-chat listener that pipes up if anyone (including you) says
  "demo plugin" in local chat.
- A persisted setting (`last_greet`) showing plugin settings survive
  restarts.

Safe to disable if you don't need a live example to poke at.

---

## Automation Plugin

*Source: `plugins/Veles.Plugin.Automation/`*

Client-side behavior automation. It reimplements three features Legacy
users may recognize, plus a general-purpose rule engine layered on top.

### AutoSit

Keeps you seated on a designated object. A background check runs every 10
seconds and force-sits your avatar back onto the configured target if
you're found standing.

```
autosit set <uuid> [name]   set the target you should stay seated on
autosit on / off            enable or disable the watcher
autosit status               show current target and state
```

### PseudoHome

A fake "home" location with an automatic return. Every 5 seconds the
plugin checks your position against a saved region + coordinates, and
teleports you back if you've drifted beyond a configured tolerance.

```
pseudohome set [tolerance]   save your current position as home, with optional drift tolerance
pseudohome on / off          enable or disable auto-return
pseudohome status            show saved location, tolerance, and state
```

### LSL Helper

Lets an in-world script remote-control parts of the viewer by sending it
instant messages. For safety, the plugin only accepts commands from
avatar UUIDs you've explicitly allow-listed — a script owned by anyone
else is ignored.

```
lslhelper allow <uuid>   permit an object owner to send commands
lslhelper deny <uuid>    revoke a previously allowed owner
lslhelper on / off       enable or disable the listener
lslhelper status         show allow-list and state
```

Supported script-side commands (sent as an object IM): `send_im`, `say`,
`give_inventory`.

### Rule Engine

A more general automation layer beyond the three features above: rules
made of a **trigger** (proximity enter/leave, payment received, IM
received) and an **action** (invite to group, send IM/chat, give
inventory). Rules are managed from:

- The `rule` chat command, or
- The plugin's **Preferences** tab (added to both the main Preferences
  window and the plugin's own Settings window), which also supports
  importing/exporting your rule set as a file via the platform's native
  file picker.

A `groupinviter` command is included as a quick shortcut for the common
"invite this avatar to my group" rule action.

> **Heads up:** AutoSit, PseudoHome, and the Rule Engine all act on your
> avatar automatically in the background. Review what you've configured
> before leaving a session unattended, especially LSL Helper's allow-list.

---

## Discord Relay

*Source: `plugins/Veles.Plugin.Discord/`*

A two-way bridge between one SL chat source — nearby chat, a group
conference, or a direct IM with someone specific — and one Discord text
channel. Messages sent in SL show up in Discord tagged with the sender's
name; messages typed in Discord appear in SL prefixed `(discord) Name:`.
Reconnects automatically (backing off from 5s up to 2 minutes) if the
connection drops.

```
discord connect      start relaying (also a Plugins-menu item)
discord disconnect   stop relaying
discord status       show current relay target and connection state
```

Configure it from **Preferences → Discord Relay**: a bot token, the target
Discord channel's numeric ID, an optional webhook URL (so relayed messages
show the sender's name instead of the bot's), and which SL chat target to
relay.

**You'll need:** a Discord bot token (create one in the Discord Developer
Portal, invite it to your server, enable the message-content intent) and
the channel ID you want to bridge to.

> **Privacy:** while connected, everything said in the chosen SL chat
> source — including messages from other residents, not just you — is
> sent to Discord's servers (and to the webhook URL, if set). Treat the
> bridged channel as public to anyone with Discord access to it.

---

## IRC Relay

*Source: `plugins/Veles.Plugin.IRC/`*

The same two-way bridge idea as Discord Relay, but for a single IRC
channel, using a built-in TLS + SASL-capable IRC client.

```
irc connect      start relaying (also a Plugins-menu item)
irc disconnect   stop relaying
irc status       show current relay target and connection state
```

Configure it from **Preferences → IRC Relay**: server address and port
(defaults to `irc.libera.chat:6697`), TLS toggle, nickname, channel, and
optional SASL login/password for networks that require authentication —
plus the same SL chat-source picker as Discord Relay.

> **Privacy:** as with Discord Relay, chat/IM content from anyone in the
> chosen SL source is sent to the IRC network while connected, and IRC
> channels are commonly logged by others without your knowledge.

---

## Email Digest

*Source: `plugins/Veles.Plugin.Email/`*

Batches nearby chat, group chat, and IMs, then emails you a digest on a
schedule instead of a live relay. Older messages are dropped once the
batch hits a configured size cap; failed sends are retried.

```
email start     begin batching/sending on the configured schedule
email stop      stop
email sendnow   flush the current batch immediately
email status    show schedule, batch size, and state
```

Configure it from **Preferences → Email Digest**: SMTP host/port/TLS,
SMTP username/password, From/To addresses, a subject line template
(supports a `{date}` placeholder), how often to send, the max messages
per digest, and which categories (nearby/IM/group) to include.

**You'll need:** SMTP server details and credentials for an account you
can send mail through.

> **Privacy:** digested chat/IM content (again, from others as well as
> you) is sent to your SMTP provider and lands in whatever inbox you
> configured as the recipient.

---

## Ollama Chatbot

*Source: `plugins/Veles.Plugin.OllamaChat/`*

An AI chatbot that replies to nearby chat and/or IMs using a locally
running [Ollama](https://ollama.com/) server (Llama 3, Mistral, Phi-3,
etc.). Keeps a short rolling conversation history per user, and can be
scoped to only reply within a chat distance range, only when your name is
mentioned, or only to certain message types.

```
ollama on|off              enable or disable replies (also a menu toggle)
ollama status               show current state and settings
ollama clear [user]         clear conversation history (optionally for one user)
ollama model <name>         switch the model in use
ollama models                list models installed on the Ollama server
ollama prompt <text>         set the system prompt
```

Configure it from **Preferences → Ollama Chatbot**: which message types
trigger a reply, response range, the Ollama Base URL and model, max
tokens, and the system prompt (with a button to fetch installed models).

**You'll need:** Ollama installed and running somewhere reachable at the
configured Base URL — by default `http://localhost:11434` (your own
machine), no API key required.

> **Privacy:** if you leave the Base URL at its local default, chat
> content never leaves your computer. If you point it at a *remote*
> Ollama server instead, chat/IM content (including from other residents)
> is sent to that server.

---

## Import/Export

*Source: `plugins/Veles.Plugin.ImportExport/`*

A local content toolkit — no external service involved. Despite the name,
it covers more than linksets:

- **Objects**: export a linkset (with its textures) to a `.vobj` file and
  re-import it in-world later. Export is only allowed for objects you own
  and created (or, on OpenSim-style grids, own with full permissions) —
  it can't be used to copy no-copy content that isn't yours.
- **Animations**: export to BVH.
- **Scripts & notecards**: export/import as `.lsl`/`.txt` files.
- **Wearables**: export/import shape, skin, hair, eyes, clothing, and
  physics as `.llw` files (type is auto-detected on import).
- **Textures**: export to PNG/JPG/WebP/BMP/TGA, or as raw JPEG2000.
- **Sounds**: export to OGG or decoded WAV.

```
export <localID> [path]                 export a linkset by its in-world local ID
import <path> [x y z]                   import a .vobj, reusing original texture UUIDs
importtex <path> [x y z]                import a .vobj, re-uploading textures instead
exportanim <assetUUID> [path]           export an animation to BVH
exportscript / importscript <path>      export/import a script
exportnote / importnote <path>          export/import a notecard
exportshape / importshape               export/import a shape
exportphysics / importphysics           export/import a physics profile
exportwearable <type> [uuid] [path]     export a wearable by type
importwearable <path> [name]            import and wear a saved wearable
exporttex <assetUUID> [path]            export a texture
exportsound <assetUUID> [path]          export a sound
```

Every command has a matching Plugins-menu entry and a file-picker flow —
you don't need to remember the chat syntax. Files land in
`~/VelesExports/` by default.

> No settings/preferences tab and no credentials needed — this one's
> entirely local file I/O plus normal asset requests to the grid you're
> logged into.

---

## Macros

*Source: `plugins/Veles.Plugin.Macros/`*

Record named sequences of actions and replay them on demand: Say (with
channel/volume), Emote, Wait, IM, Stand, Sit (on a specific object), Play
Gesture, or run any other chat command as a step. Only one macro plays at
a time — starting another cancels the current one.

```
macro list             show your saved macros
macro run <name-or-id>  play a macro back (matches by name, then ID, then partial name)
macro stop              cancel the currently running macro
```

A full GUI editor is available from **Preferences → Macros** or the
**Plugins → Macros…** menu item, for building and editing macros without
touching chat commands. Macros are saved per-avatar and can be imported
or exported as JSON files.

> **Heads up:** macro files are plain JSON and portable — if you import
> one someone else made, check what it does first, since a step could
> reference IMs, UUIDs, or targets you didn't intend to send.

> No external service or credentials involved — everything stays local.
