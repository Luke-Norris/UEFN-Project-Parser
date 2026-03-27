# WellVersed

**A UEFN project management studio — for humans and Claude.**

MCP server + CLI + Web dashboard for reading, auditing, configuring, and managing UEFN (Unreal Editor for Fortnite) project files. Safe to run alongside UEFN with your map open.

## Architecture

```
FortniteForge.sln
├── src/FortniteForge.Core/          # Business logic (no UI), namespace: WellVersed.Core
│   ├── Config/ForgeConfig.cs        # WellVersedConfig class — config loader, project discovery, UEFN layout detection
│   ├── Models/                      # AssetInfo, DeviceInfo, AuditResult, Vector3Info, etc.
│   ├── Safety/AssetGuard.cs         # Cooked detection + path validation + operation mode
│   └── Services/
│       ├── SafeFileAccess.cs        # Copy-on-read + staged writes (UEFN-safe)
│       ├── UefnDetector.cs          # UEFN process detection + URC awareness
│       ├── AssetService.cs          # Asset reading via SafeFileAccess
│       ├── ModificationService.cs   # Two-step modify (preview → apply) with staged writes
│       ├── ActorPlacementService.cs # Clone-and-modify actor placement
│       ├── AuditService.cs          # Project/level auditing
│       ├── DeviceService.cs         # Device listing + digest schema
│       ├── BackupService.cs         # Pre-modification backups
│       ├── BuildService.cs          # UEFN build triggering
│       ├── DigestService.cs         # .digest schema parsing
│       └── MapGeneration/           # Procedural map generation
├── src/FortniteForge.MCP/           # MCP Server (Claude Code interface), namespace: WellVersed.MCP
│   ├── Program.cs                   # stdio transport, DI setup
│   └── Tools/                       # 30+ MCP tool definitions
├── src/FortniteForge.CLI/           # CLI with status line + --config flag, namespace: WellVersed.CLI
├── src/FortniteForge.Web/           # Web dashboard (localhost SPA), namespace: WellVersed.Web
│   ├── Program.cs                   # Minimal API + project management + browse/pick-folder
│   ├── ProjectManager.cs            # Persistent multi-project with safety tiers
│   ├── DeviceClassifier.cs          # Device vs prop classification + property extraction
│   ├── SpatialExtractor.cs          # Actor position extraction for spatial views
│   └── wwwroot/                     # Vanilla JS SPA, dark theme
└── tests/FortniteForge.Tests/       # Unit + integration + sandbox tests, namespace: WellVersed.Tests
```

Note: Directory names on disk still use `FortniteForge.*` but all namespaces, assembly names, and branding use `WellVersed.*`.

## UEFN Asset Structure

Understanding what's in a UEFN project:

| Location | Contents | Editable? |
|----------|----------|-----------|
| `Plugins/<Name>/Content/*.uasset` | **User-created definitions** — blueprints, materials, custom props, textures | Full access |
| `Content/__ExternalActors__/<Level>/*.uasset` | **Placed instances** — each file is one actor in the level with transform + property overrides | Override properties only |
| `Content/__ExternalObjects__/<Level>/*.uasset` | **Non-actor objects** — data assets referenced by level (rare) | Read-only |
| `Content/*.umap` | **Level metadata** — world settings, HLOD, partitioning | Read-only |
| `Content/*.verse` | **Verse source** — gameplay logic scripts | Via WellVersed (verse generation planned) |

External actors only store **non-default property overrides**. If a property is present in an external actor file, it means the creator explicitly changed it from Epic's default.

## Project Safety

### Safety Tiers (Web Dashboard)
- **My Project** — Staged writes when UEFN open, direct with backup when closed. Full safety.
- **Library** — Always read-only. For browsing, searching, and copying assets FROM.

### Operation Modes (automatic)
| Mode | When | Reads | Writes |
|------|------|-------|--------|
| **Read-Only** | `readOnly: true` or Library project | Copy-on-read | Blocked |
| **Staged** | UEFN running or URC active | Copy-on-read | To `.wellversed/staged/` |
| **Direct** | UEFN not running, My Project | Copy-on-read | To source (with backup) |

### Key Rules
1. **Copy-on-read** — ALL reads go through `SafeFileAccess` (copies to temp). Never locks source files.
2. **NEVER modify cooked assets** — `AssetGuard.CanModify()` gates all writes
3. **Pending changes model** — edits queue as diffs, reviewed before applying
4. **Auto-backup** — every direct modification backs up the original

## UAssetAPI Fix (ScriptSerializationOffset)

We patched UAssetAPI to fix UE5.4 external actor property parsing. The issue: `NormalExport.Read()` skipped 4 dummy bytes that don't exist when `ScriptSerializationEndOffset > 0`. Our fix in `lib/UAssetAPI/UAssetAPI/ExportTypes/NormalExport.cs` seeks directly to the property data region using the offset fields. This enables reading all 130+ properties from devices like vending machines.

## Technology

- .NET 8, C#
- **UAssetAPI** (submodule at `lib/UAssetAPI/`) — .uasset binary parsing (with our property fix)
- **ModelContextProtocol** (0.1.0-preview.9 NuGet) — MCP server SDK
- **System.CommandLine** (2.0.0-beta4) — CLI framework
- **ASP.NET Minimal APIs** — Web dashboard backend
- **EngineVersion**: `EngineVersion.VER_UE5_4` (in `UAssetAPI.UnrealTypes`)

## Build & Run

```bash
dotnet build

# Web Dashboard (primary interface) — http://localhost:5120
dotnet run --project src/FortniteForge.Web -- path/to/forge.config.json

# CLI
dotnet run --project src/FortniteForge.CLI -- -c forge.config.json status
dotnet run --project src/FortniteForge.CLI -- -c forge.config.json list
dotnet run --project src/FortniteForge.CLI -- -c forge.config.json audit

# MCP Server (for Claude Code)
dotnet run --project src/FortniteForge.MCP -- path/to/forge.config.json

# Tests (sandbox requires WELLVERSED_SANDBOX env var)
dotnet test
WELLVERSED_SANDBOX="Z:/UEFN_Resources/mapContent/map_resources" dotnet test --filter SandboxTests
```

## Web Dashboard Features

- **Multi-project management** — add projects as "My Project" or "Library", scan directories, switch active project
- **Device inspector** — devices classified separately from props/terrain, grouped by type, property importance ranking (meaningful config first, rendering noise collapsed)
- **Asset browser** — User-Created tab (custom definitions) + Epic Assets tab (placed instances with counts)
- **Property editing** — inline editable fields with pending changes queue and diff preview
- **Audit** — project and level auditing with severity-coded findings
- **Breadcrumb navigation** — always know where you are

## Config

Per-project `forge.config.json`:
- `projectPath` — UEFN project root (folder with `.uefnproject`)
- `readOnly` — Hard kill switch for all writes
- `stagingDirectory` — Where staged writes go (default: `.wellversed/staged`)
- `referenceLibraryPath` — Path to verse reference files

Sandbox config: `forge.config.sandbox.json` (read-only, points at Z: drive Bedwars)

## Important Context

- WellVersed handles verse generation directly (not delegated to separate session) — it has full project context
- UEFN projects always use `Plugins/<Name>/Content/` layout. Map collections may have extra nesting — use `WellVersedConfig.DiscoverProjects()` to find project roots.
- `.digest` files are Verse source containing device class/property schema definitions
- The sandbox at `Z:\UEFN_Resources\mapContent\` has ~92 UEFN projects used as a reference library
- Cross-map asset import is planned — simple for Epic-only references, complex when user assets have dependencies
