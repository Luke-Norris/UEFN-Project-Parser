# FortniteForge

**"The Model Context Protocol for Master Control of your Creative Projects"**

An MCP server + CLI that gives Claude Code direct access to UEFN (Unreal Editor for Fortnite) project files via UAssetAPI. Read, audit, modify, and build — all through native MCP tools.

## Architecture

```
FortniteForge.sln
├── src/FortniteForge.Core/     # All business logic (no UI)
│   ├── Config/ForgeConfig.cs   # forge.config.json loader
│   ├── Models/                 # AssetInfo, DeviceInfo, AuditResult, etc.
│   ├── Safety/AssetGuard.cs    # Cooked asset detection + path validation
│   └── Services/               # Asset, Device, Audit, Modification, Build, Backup, Digest, ActorPlacement
├── src/FortniteForge.MCP/      # MCP Server (Claude's interface)
│   ├── Program.cs              # stdio transport, DI setup
│   └── Tools/                  # 30+ MCP tool definitions
├── src/FortniteForge.CLI/      # Command-line interface
└── tests/FortniteForge.Tests/
```

## Key Design Rules

1. **NEVER modify cooked assets** — Check `AssetGuard.CanModify()` before any write
2. **NEVER modify ReadOnlyFolders** (FortniteGame, Engine) — enforced by path validation
3. **Two-step modifications** — All changes require preview (dry-run) then apply
4. **Auto-backup** — Every modification backs up the original file first
5. **JSON output** — All MCP tools return structured JSON for token efficiency
6. **Summarize, then drill-down** — Don't dump entire assets; summarize first, inspect on demand

## Technology

- .NET 8, C#
- **UAssetAPI** (1.0.4) — .uasset/.umap binary parsing
- **ModelContextProtocol** (0.1.0-preview.15) — MCP server SDK
- **System.CommandLine** (2.0.0-beta4) — CLI framework

## Important Context

- `.digest` files (fortnite.digest, verse.digest, unreal.digest) are **Verse source** containing device class/property definitions — they're our schema reference
- User-created child assets of Epic classes ARE modifiable (exposed properties only)
- Actor placement uses **clone-and-modify** pattern — clone an existing actor, change transform
- The user (Luke) has a **separate Claude Code session for Verse scripts** — this tool handles everything else
- Verse compilation errors should be extractable and forwardable to the Verse session

## Build & Run

```bash
dotnet restore
dotnet build

# CLI
dotnet run --project src/FortniteForge.CLI -- list

# MCP Server
dotnet run --project src/FortniteForge.MCP -- path/to/forge.config.json
```

## Config

Per-project `forge.config.json` — see `forge.config.example.json` for template.
Key settings: `projectPath`, `readOnlyFolders`, `autoBackup`, `requireDryRun`.
