# CICMessenger — Development Conventions

## Overview
Peer-to-peer LAN instant messenger. No central server — peers discover each other via multicast and communicate directly.

## Environment
- **.NET 9 SDK** required (no `global.json` — uses installed SDK)
- Primary platform: **Windows** (auto-start service uses Windows registry)
- Cross-platform via Avalonia, but some features are Windows-only

## Build

CICMessenger targets **.NET 9** and uses the `dotnet` CLI. Build with:

```powershell
dotnet build CICMessenger.sln
```

Run the UI project:

```powershell
dotnet run --project CICMessenger.UI/CICMessenger.UI.csproj
```

There are no automated tests in this repository.

## Architecture

CICMessenger is a **peer-to-peer LAN instant messenger** built with WPF. There is no central server — peers discover each other via multicast and communicate directly.

### Layer diagram

```
CICMessenger.UI          (WPF app — windows, viewmodels, plugin loader)
    ↓
CICMessenger.Client      (Facade — ChatClient exposes buddy list, login, chat events)
    ↓
CICMessenger.Core        (Networking — presence discovery, chat transport, message serialization)
    ↓
CICMessenger.Infrastructure  (Shared abstractions — async pipes, serialization helpers)
CICMessenger.Utilities       (Cross-cutting helpers used by all layers)
```

### Feature modules (plug into Client/UI)

| Project | Purpose | Key dependency |
|---|---|---|
| `CICMessenger.FileTransfer` | P2P file transfer | CICMessenger.Client |
| `CICMessenger.VoiceChat` | Voice chat via NAudio + Speex | CICMessenger.Client, NAudio |
| `CICMessenger.Screenshot` | Screen capture & send | CICMessenger.FileTransfer |
| `CICMessenger.Translate` | Message translation | CICMessenger.Client, Newtonsoft.Json |
| `CICMessenger.History` | Chat history persistence | EntityFramework 6, System.Data.SQLite |
| `CICMessenger.Multicast` | Multicast presence (standalone exe) | CICMessenger.Core |
| `CICMessenger.Bridge` | Cross-subnet/WAN bridging (standalone exe) | CICMessenger.Core, protobuf-net |
| `CICMessenger.Setup` | WiX MSI installer | WiX Toolset |

### Networking

- **Presence/discovery**: Multicast UDP (`UdpMulticastService`) or TCP fallback (`TcpMulticastService`) — peers announce themselves and listen for others on the LAN.
- **Chat transport**: Unicast message pipes between peers. `ChatHost` deserializes incoming messages and raises typed events; `ChatService` creates and manages chat sessions.
- **Serialization**: protobuf-net for wire protocol messages.
- **Bridge**: A standalone exe that forwards presence/chat messages between subnets for cross-LAN communication.

### UI patterns

- Avalonia UI 11 with Fluent theme and MVVM-style bindings. `MainWindow` uses a `ClientViewModel` and command pattern.
- Plugin/extension system: `CICMessenger.Core` defines `IExtension`, `IMessageFilter`, `IMessageParser` interfaces; `PluginLoader` in the UI discovers and loads plugins at startup.
- Entry point is `CICMessenger.UI/Program.cs` which enforces single-instance via a Mutex.
- Settings are persisted as JSON in `%AppData%/CICMessenger/settings.json` via `SettingsService`.

### Migration status

- **CICMessenger.UI**: Fully migrated to Avalonia 11
- **CICMessenger.Translate**: Still uses WPF (`<UseWPF>true</UseWPF>`)
- All other projects are framework-agnostic (.NET class libraries)

## Key Files

| File | Purpose |
|------|---------|
| `CICMessenger.UI/Program.cs` | Entry point — single-instance mutex + Avalonia startup |
| `CICMessenger.UI/App.axaml.cs` | DI container setup, MainWindow creation, `/background` arg handling |
| `CICMessenger.UI/MainWindow.axaml.cs` | Main UI — viewmodel init, tray icon, notifications |
| `CICMessenger.Client/ChatClient.cs` | Facade — buddy list, login, chat events |
| `CICMessenger.Core/Presence/UdpMulticastService.cs` | Multicast peer discovery |
| `CICMessenger.Core/Chat/ChatHost.cs` | Incoming message deserialization + typed events |
| `CICMessenger.UI/Services/WindowsAutoStartService.cs` | Windows-only auto-start via registry |

## Gotchas

- **Single-instance enforcement**: `Program.cs` uses a `Mutex` — launching a second instance will exit silently
- **WindowsAutoStartService registered unconditionally**: it's in DI regardless of OS, but decorated with `[SupportedOSPlatform("windows")]` — calling it on non-Windows will throw
- **Multicast requires LAN**: peers won't discover each other across subnets without the Bridge exe
- **No automated tests**: this repo has no test suite — validate changes by building and manual testing
- **CICMessenger.Translate still uses WPF**: don't try to build it on non-Windows or without Windows Desktop SDK

## Deploy / Release

The `.github/workflows/release.yml` GitHub Action (triggered by pushing a `v*` tag) is
currently **non-functional** — the GitHub account this repo lives under (`vhgminh82`) has
Actions locked for a billing issue, so every release run since at least v0.12.0 fails in ~3s
with "The job was not started because your account is locked due to a billing issue." Don't
assume tagging alone produces a release; build and publish locally instead, until that's
resolved.

**Standing deploy process** (do this instead of relying on the Action):

1. Bump `<Version>`/`<AssemblyVersion>` in `Directory.Build.props` (Minor +1, e.g.
   `0.15.0` → `0.16.0`).
2. Full clean build + test pass: `dotnet build CICMessenger.sln --no-incremental` then
   `dotnet test CICMessenger.Tests/CICMessenger.Tests.csproj` — 0 warnings beyond the known
   `CA1416`/`IL2026`/designer-file ones, 0 test failures.
3. Commit, push to `origin/main`, tag `vX.Y.Z`, push the tag:
   ```powershell
   git add -A
   git commit -m "..."
   git push origin main
   git tag -a vX.Y.Z -m "vX.Y.Z"
   git push origin vX.Y.Z
   ```
4. Build the release artifacts locally, mirroring `release.yml` exactly (adjust the version
   string each time). Publish from **outside** the repo (e.g. `../dist`, a sibling of
   `Squiggle/`) so build output never lands under version control:
   ```powershell
   dotnet publish CICMessenger.UI/CICMessenger.UI.csproj `
     --configuration Release --runtime win-x64 --self-contained true `
     -p:PublishSingleFile=false -p:PublishTrimmed=false -p:Version=X.Y.Z `
     --output ../dist/CICMessenger/app

   dotnet publish CICMessenger.Launcher/CICMessenger.Launcher.csproj `
     --configuration Release --runtime win-x64 --self-contained true `
     -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=true `
     --output ../dist/CICMessenger
   ```
5. Generate `manifest.json` (sha256 + size per file under `app/`) and the release assets —
   same shape the Action produces: `manifest.json`, the individual `CICMessenger*` files (for
   differential updates), and a full `CICMessenger-vX.Y-setup.zip` of the install dir. See
   the "Generate manifest and assets" step in `release.yml` for the exact logic; PowerShell's
   `-replace` with a literal backslash pattern is finicky here — use
   `.Replace('\', '/')` instead of a regex replace.
6. `gh run list --repo vhgminh82/cicmessenger --workflow=release.yml` to confirm/reconfirm the
   Action failure reason before assuming step 4 was necessary; `gh` defaults to the
   `upstream` remote (`hasankhan/Squiggle`) in this repo, so **always pass `--repo
   vhgminh82/cicmessenger`** explicitly or you'll be looking at the wrong repo's runs.
7. Creating the actual public GitHub Release (uploading the assets from step 5) is a
   separate, explicit-permission action — confirm with the user before running `gh release
   create`.

## Conventions

- Events are initialized with empty delegates (`event ... = delegate { };`) to avoid null checks.
- Diagnostic logging uses Serilog (file sink in `logs/` folder).
- Translations live in `CICMessenger.UI/Styles/Translations.axaml` as merged resource dictionaries.
- Settings persistence uses `System.Text.Json` to `%AppData%/CICMessenger/settings.json`.
