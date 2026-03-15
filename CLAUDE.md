# FortniteForge

**"The Model Context Protocol for Master Control of your Creative Projects"**

An MCP server + CLI + Web dashboard that gives Claude Code direct access to UEFN (Unreal Editor for Fortnite) project files via UAssetAPI. Read, audit, modify, and build — all through native MCP tools. **Safe to run alongside UEFN with your map open.**

## Architecture

```
FortniteForge.sln
├── src/FortniteForge.Core/         # All business logic (no UI)
│   ├── Config/ForgeConfig.cs       # forge.config.json loader (UEFN + UE support)
│   ├── Models/                     # AssetInfo, DeviceInfo, AuditResult, etc.
│   ├── Safety/AssetGuard.cs        # Cooked detection + path validation + mode awareness
│   └── Services/
│       ├── SafeFileAccess.cs       # Copy-on-read + staged writes (UEFN-safe)
│       ├── UefnDetector.cs         # UEFN process detection + URC awareness
│       ├── AssetService.cs         # Asset reading (via SafeFileAccess)
│       ├── ModificationService.cs  # Two-step modify with staged writes
│       ├── ActorPlacementService.cs # Clone-and-modify actor placement
│       ├── AuditService.cs         # Project/level auditing
│       ├── DeviceService.cs        # Device listing + schema
│       ├── BackupService.cs        # Pre-modification backups
│       ├── BuildService.cs         # UEFN build triggering
│       ├── DigestService.cs        # .digest schema parsing
│       └── MapGeneration/          # Procedural map generation
├── src/FortniteForge.MCP/          # MCP Server (Claude's interface)
│   ├── Program.cs                  # stdio transport, DI setup
│   └── Tools/                      # 30+ MCP tool definitions
├── src/FortniteForge.CLI/          # Command-line interface with status line
├── src/FortniteForge.Web/          # Web dashboard (local browser UI)
│   ├── Program.cs                  # Minimal API + static file serving
│   ├── SpatialExtractor.cs         # External actor class/position extraction
│   └── wwwroot/                    # SPA frontend (vanilla JS, dark theme)
└── tests/FortniteForge.Tests/
```

## Key Design Rules

1. **NEVER modify cooked assets** — `AssetGuard.CanModify()` gates all writes
2. **NEVER modify ReadOnlyFolders** (FortniteGame, Engine) — enforced by path validation
3. **Copy-on-read** — All file reads go through `SafeFileAccess`, which copies to temp first. This prevents file locking conflicts with UEFN.
4. **Staged writes** — When UEFN is running, modifications write to `.fortniteforge/staged/` instead of the source. Apply when UEFN is closed.
5. **Two-step modifications** — All changes require preview (dry-run) then apply
6. **Auto-backup** — Every direct-mode modification backs up the original first
7. **JSON output** — All MCP tools return structured JSON for token efficiency
8. **Summarize, then drill-down** — Don't dump entire assets; summarize first, inspect on demand

## Operation Modes

The tool automatically selects a mode based on the environment:

| Mode | When | Reads | Writes |
|------|------|-------|--------|
| **Read-Only** | `readOnly: true` in config | Copy-on-read | Blocked |
| **Staged** | UEFN is running or URC active | Copy-on-read | To `.fortniteforge/staged/` |
| **Direct** | UEFN not running | Copy-on-read | To source (with backup) |

## UEFN Project Support

FortniteForge understands UEFN's directory layout:
- Detects `.uefnproject` files (not just `.uproject`)
- Resolves `Plugins/<ProjectName>/Content/` as the Content path
- Detects `.urc/urc.sqlite` (Unreal Revision Control) and blocks direct writes when active
- Scans `__ExternalActors__/<LevelName>/` for per-actor `.uasset` files

## Technology

- .NET 8, C#
- **UAssetAPI** (submodule at `lib/UAssetAPI/`, targets net8.0) — .uasset/.umap binary parsing
- **ModelContextProtocol** (0.1.0-preview.9 NuGet) — MCP server SDK
- **System.CommandLine** (2.0.0-beta4) — CLI framework
- **ASP.NET Minimal APIs** — Web dashboard backend
- **EngineVersion**: Use `EngineVersion.VER_UE5_4` (in `UAssetAPI.UnrealTypes`)

## Build & Run

```bash
dotnet restore
dotnet build

# CLI (with status line)
dotnet run --project src/FortniteForge.CLI -- --config path/to/forge.config.json status
dotnet run --project src/FortniteForge.CLI -- -c forge.config.json list
dotnet run --project src/FortniteForge.CLI -- -c forge.config.json audit
dotnet run --project src/FortniteForge.CLI -- -c forge.config.json staged

# Web Dashboard (opens at http://localhost:5120)
dotnet run --project src/FortniteForge.Web -- path/to/forge.config.json

# MCP Server
dotnet run --project src/FortniteForge.MCP -- path/to/forge.config.json
```

## Config

Per-project `forge.config.json` — see `forge.config.example.json` for template.

Key settings:
- `projectPath` — Path to UEFN project root (folder with `.uefnproject`)
- `readOnly` — Hard kill switch for all writes (recommended for active projects)
- `readOnlyFolders` — Folders that should never be modified
- `modifiableFolders` — Restrict writes to specific folders (empty = all non-cooked)
- `stagingDirectory` — Where staged modifications are written (default: `.fortniteforge/staged`)
- `autoBackup`, `requireDryRun` — Safety settings

Sandbox config for the Z: drive map collection: `forge.config.sandbox.json`

## Important Context

- `.digest` files (fortnite.digest, verse.digest, unreal.digest) are **Verse source** containing device class/property definitions — they're our schema reference
- User-created child assets of Epic classes ARE modifiable (exposed properties only)
- Actor placement uses **clone-and-modify** pattern — clone an existing actor, change transform
- The user (Luke) has a **separate Claude Code session for Verse scripts** — this tool handles everything else
- UAssetAPI cannot parse property data from UEFN external actor files (export types are recognized but `NormalExport.Data` is empty) — class names and structure are still extractable
- UEFN map collection at `Z:\UEFN_Resources\mapContent\` — ~50 maps with 330+ Verse files, used as sandbox
