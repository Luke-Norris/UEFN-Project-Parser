# FortniteForge Setup Guide

**"The Model Context Protocol for Master Control of your Creative Projects"**

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- UEFN installed on your machine
- Claude Code (CLI or VS Code extension)

## Quick Start

### 1. Build the project

```bash
cd UEFN-Project-Parser
dotnet restore
dotnet build
```

### 2. Create your config file

Copy the example config to your UEFN project root:

```bash
cp forge.config.example.json "C:\Users\Luke\Documents\FortniteProjects\MyProject\forge.config.json"
```

Or use the CLI to generate one:

```bash
dotnet run --project src/FortniteForge.CLI -- init "C:\Users\Luke\Documents\FortniteProjects\MyProject"
```

Edit `forge.config.json` and set:
- `projectPath` — your UEFN project root (where the .uproject file lives)
- `uefnInstallPath` — where Fortnite/UEFN is installed
- `modifiableFolders` — which Content/ subfolders are yours (leave empty for all)

### 3. Connect to Claude Code

#### Option A: Claude Code CLI

```bash
claude mcp add fortniteforge -- dotnet run --project /path/to/UEFN-Project-Parser/src/FortniteForge.MCP -- /path/to/forge.config.json
```

#### Option B: VS Code Extension

Add to your `.claude/settings.json`:

```json
{
  "mcpServers": {
    "fortniteforge": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:/path/to/UEFN-Project-Parser/src/FortniteForge.MCP",
        "--",
        "C:/path/to/forge.config.json"
      ]
    }
  }
}
```

#### Option C: Environment Variable

Set the `FORTNITEFORGE_CONFIG` environment variable to your config file path, then:

```bash
claude mcp add fortniteforge -- dotnet run --project /path/to/FortniteForge.MCP
```

### 4. Verify it works

In Claude Code, you should now see FortniteForge tools available. Try:

> "List all assets in my UEFN project"

Claude will call `list_assets` and show you your project contents.

## CLI Usage (Manual/Debugging)

The CLI can be used standalone for testing:

```bash
# Set up an alias (optional)
alias forge="dotnet run --project src/FortniteForge.CLI --"

# List assets
forge list
forge list --folder MyMaps --class World

# Inspect a specific asset
forge inspect "C:\...\Content\MyMaps\MainLevel.umap"

# List devices in a level
forge devices "C:\...\Content\MyMaps\MainLevel.umap"

# Inspect a specific device
forge device TriggerDevice_3

# Audit a level
forge audit --level "C:\...\Content\MyMaps\MainLevel.umap"

# Audit entire project
forge audit

# Check build log
forge build-log

# Look up device schemas
forge schema trigger_device
```

## Available MCP Tools

Once connected, Claude Code has access to these tools:

### Reading & Inspection
| Tool | Description |
|------|-------------|
| `list_assets` | Browse project assets with filters |
| `inspect_asset` | Deep dive into a specific asset |
| `asset_summary` | Quick overview of an asset |
| `search_assets` | Search across the project |
| `get_project_info` | Project overview and stats |

### Devices
| Tool | Description |
|------|-------------|
| `list_devices` | All devices in a level |
| `inspect_device` | Detailed device info |
| `find_levels` | Discover .umap files |
| `find_devices_by_type` | Find all triggers, spawners, etc. |
| `get_device_schema` | Device property definitions |
| `list_device_types` | All known device types |

### Auditing
| Tool | Description |
|------|-------------|
| `audit_level` | Check a level for issues |
| `audit_device` | Check a specific device |
| `audit_project` | Full project audit |
| `validate_property` | Check if a property is valid |

### Modification (Two-Step)
| Tool | Description |
|------|-------------|
| `preview_set_property` | Preview a property change |
| `preview_add_device` | Preview adding a device |
| `preview_remove_device` | Preview removing a device |
| `preview_wire_devices` | Preview wiring connection |
| `apply_modification` | Apply a previewed change |
| `cancel_modification` | Cancel a pending change |
| `list_pending_modifications` | See unapplied previews |

### Build
| Tool | Description |
|------|-------------|
| `trigger_build` | Start a UEFN build |
| `get_build_log` | Read latest build log |
| `build_summary` | Quick build status |
| `get_verse_errors` | Verse compilation errors |

### Backup
| Tool | Description |
|------|-------------|
| `backup_asset` | Manual backup |
| `restore_asset` | Restore from backup |
| `list_backups` | See available backups |

## Safety

FortniteForge has multiple safety layers:

1. **Cooked Asset Detection** — Checks binary headers to identify Epic's assets. These are NEVER modified.
2. **Path Validation** — Only modifies files in your configured folders.
3. **Mandatory Dry-Run** — All modifications require a preview step before execution.
4. **Automatic Backups** — Every modification creates a backup first.
5. **Schema Validation** — Property changes are validated against digest file schemas.

## Project Structure

```
FortniteForge.sln
├── src/
│   ├── FortniteForge.Core/        # All business logic
│   │   ├── Config/                 # Configuration system
│   │   ├── Models/                 # Data models
│   │   ├── Safety/                 # Cooked detection, path guards
│   │   └── Services/              # Asset, Device, Audit, Build, etc.
│   ├── FortniteForge.MCP/         # MCP Server (Claude's interface)
│   │   └── Tools/                  # Tool definitions
│   └── FortniteForge.CLI/         # Command-line interface
└── tests/
    └── FortniteForge.Tests/
```
