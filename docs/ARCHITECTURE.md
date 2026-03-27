# WellVersed — Architecture

## System Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        CLAUDE CODE                               │
│                                                                  │
│   "Why isn't my trigger working?"                               │
│   "Generate a forest near spawn"                                 │
│   "Audit my level for issues"                                    │
│                                                                  │
│         │ MCP Protocol (JSON-RPC over stdin/stdout)              │
└─────────┼────────────────────────────────────────────────────────┘
          │
          ▼
┌─────────────────────────────────────────────────────────────────┐
│                    WellVersed.MCP                             │
│                   (MCP Server Process)                           │
│                                                                  │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐          │
│  │  Asset   │ │  Device  │ │  Audit   │ │  Modify  │          │
│  │  Tools   │ │  Tools   │ │  Tools   │ │  Tools   │          │
│  │ (6)      │ │ (6)      │ │ (4)      │ │ (7)      │          │
│  └────┬─────┘ └────┬─────┘ └────┬─────┘ └────┬─────┘          │
│       │             │            │             │                 │
│  ┌────┴─────┐ ┌────┴─────┐ ┌───┴──────┐ ┌───┴──────┐         │
│  │Placement │ │  Build   │ │  Backup  │ │ Utility  │          │
│  │  Tools   │ │  Tools   │ │  Tools   │ │  Tools   │          │
│  │ (4)      │ │ (4)      │ │ (3)      │ │ (4)      │          │
│  └────┬─────┘ └────┬─────┘ └────┬─────┘ └────┬─────┘          │
│       │             │            │             │                 │
└───────┼─────────────┼────────────┼─────────────┼────────────────┘
        │             │            │             │
        └──────┬──────┴────────────┴──────┬──────┘
               │                          │
               ▼                          ▼
┌──────────────────────────┐   ┌──────────────────────────┐
│   WellVersed.Core     │   │   WellVersed.CLI      │
│                          │   │   (Manual Interface)      │
│   The shared brain       │   │                          │
│   All business logic     │   │   wellversed list     │
│                          │   │   wellversed inspect   │
│                          │   │   wellversed audit     │
│                          │   │   wellversed build     │
└──────────────────────────┘   └──────────────────────────┘
```

## Core Library Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                     WellVersed.Core                        │
│                                                               │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                    Config Layer                          │ │
│  │                                                          │ │
│  │  forge.config.json ──→ ForgeConfig                      │ │
│  │    • projectPath          • readOnlyFolders             │ │
│  │    • modifiableFolders    • autoBackup                  │ │
│  │    • requireDryRun        • build settings              │ │
│  └──────────────────────────────┬──────────────────────────┘ │
│                                 │                             │
│  ┌──────────────────────────────▼──────────────────────────┐ │
│  │                   Safety Layer                           │ │
│  │                                                          │ │
│  │  AssetGuard                                              │ │
│  │    ├── CheckIfCooked()    ← binary header flags          │ │
│  │    ├── CheckPath()        ← config allowlist/blocklist   │ │
│  │    └── CanModify()        ← combined gate (MUST pass)    │ │
│  └──────────────────────────────┬──────────────────────────┘ │
│                                 │                             │
│  ┌──────────────────────────────▼──────────────────────────┐ │
│  │                  Service Layer                           │ │
│  │                                                          │ │
│  │  AssetService          DeviceService                     │ │
│  │    ├── ListAssets()      ├── ListDevicesInLevel()        │ │
│  │    ├── InspectAsset()    ├── GetDevice()                 │ │
│  │    ├── SearchAssets()    ├── FindDevicesByType()          │ │
│  │    └── OpenAsset()       └── GetDeviceSchema()           │ │
│  │                                                          │ │
│  │  DigestService         AuditService                      │ │
│  │    ├── LoadDigests()     ├── AuditLevel()                │ │
│  │    ├── GetDeviceSchema() ├── AuditDevice()               │ │
│  │    └── ValidateProp()    └── AuditProject()              │ │
│  │                                                          │ │
│  │  ModificationService   ActorPlacementService             │ │
│  │    ├── Preview...()      ├── CloneActor()                │ │
│  │    └── Apply...()        └── ScatterPlace()              │ │
│  │                                                          │ │
│  │  BackupService         BuildService                      │ │
│  │    ├── CreateBackup()    ├── TriggerBuild()              │ │
│  │    ├── RestoreBackup()   ├── ReadBuildLog()              │ │
│  │    └── ListBackups()     └── GetVerseErrors()            │ │
│  └──────────────────────────────────────────────────────────┘ │
│                                 │                             │
│                                 ▼                             │
│  ┌──────────────────────────────────────────────────────────┐ │
│  │                   UAssetAPI Layer                         │ │
│  │                                                          │ │
│  │  .uasset / .umap files ←→ UAsset object model           │ │
│  │    • Exports (actors, components, blueprints)            │ │
│  │    • Imports (class references, mesh refs)               │ │
│  │    • Properties (name/value pairs on each export)        │ │
│  └──────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

## Modification Safety Flow

```
  Claude wants to change a property
              │
              ▼
  ┌─────────────────────┐
  │ preview_set_property │ ◄── Step 1: DRY RUN
  └──────────┬──────────┘
             │
             ▼
  ┌──────────────────────┐     ┌─────────────────┐
  │   AssetGuard.        │────►│  BLOCKED         │
  │   CanModify()?       │ NO  │  "Cooked asset"  │
  │                      │     │  "Read-only path" │
  └──────────┬───────────┘     └─────────────────┘
             │ YES
             ▼
  ┌──────────────────────┐
  │  Read current value   │
  │  Show what will change│
  │  Return preview +     │
  │  requestId            │
  └──────────┬───────────┘
             │
             ▼
  ┌──────────────────────┐
  │  Claude shows user:   │
  │                       │
  │  "Duration: 0.5 → 3.0"│
  │  "File: MainLevel.umap"│
  │  "Backup: automatic"  │
  │                       │
  │  "Apply this change?" │
  └──────────┬───────────┘
             │
             ▼
  ┌──────────────────────┐
  │  apply_modification   │ ◄── Step 2: EXECUTE
  │  (requestId)          │
  └──────────┬───────────┘
             │
             ▼
  ┌──────────────────────┐
  │  Create backup        │──► .wellversed/backups/
  │  (auto)               │    MainLevel_20240115_143022.umap
  └──────────┬───────────┘
             │
             ▼
  ┌──────────────────────┐
  │  Apply change via     │
  │  UAssetAPI            │
  │  Write file           │
  └──────────┬───────────┘
             │
             ▼
  ┌──────────────────────┐
  │  Return result:       │
  │  ✓ Success            │
  │  ✓ Backup path        │
  │  ✓ Modified files     │
  └──────────────────────┘
```

## Actor Clone & Placement Flow

```
  "Place 40 trees around spawn"
              │
              ▼
  ┌──────────────────────────┐
  │  preview_scatter_place    │
  │                           │
  │  sourceActor: "Tree_12"   │
  │  center: (5000, 3000, 200)│
  │  area: 3000 x 3000        │
  │  count: 40                 │
  │  spacing: 200 min          │
  └──────────┬───────────────┘
             │
             ▼
  ┌──────────────────────────┐
  │  Generate positions       │
  │                           │
  │  For each of 40 copies:   │
  │    • Random XY in area    │
  │    • Check min spacing    │
  │    • Random yaw (0-360°)  │
  │    • Random scale (0.7-1.3)│
  └──────────┬───────────────┘
             │
             ▼
  ┌──────────────────────────┐
  │  Show preview to Claude   │
  │                           │
  │  "40 trees, 3000x3000    │
  │   area, here are the     │
  │   planned positions..."   │
  └──────────┬───────────────┘
             │  User approves
             ▼
  ┌──────────────────────────┐
  │  apply_scatter_place      │
  └──────────┬───────────────┘
             │
             ▼
  ┌──────────────────────────┐
  │  Backup MainLevel.umap   │
  └──────────┬───────────────┘
             │
             ▼
  ┌──────────────────────────┐
  │  For each of 40 trees:    │
  │                           │
  │  ┌──────────────────────┐ │
  │  │ 1. Open .umap        │ │
  │  │ 2. Find "Tree_12"    │ │
  │  │ 3. Find its children │ │
  │  │    (components)       │ │
  │  │ 4. Deep clone export  │ │
  │  │    + all components   │ │
  │  │ 5. Fix references     │ │
  │  │ 6. Set new transform  │ │
  │  │ 7. Register in Level  │ │
  │  │    Actors list        │ │
  │  │ 8. Save .umap         │ │
  │  └──────────────────────┘ │
  └──────────┬───────────────┘
             │
             ▼
  ┌──────────────────────────┐
  │  Result:                  │
  │  "Placed 40/40 trees"     │
  │  Backup: ...backups/...   │
  └──────────────────────────┘
```

## Asset Safety Detection

```
  Any write operation
         │
         ▼
  ┌──────────────────────────────────────────────┐
  │              AssetGuard.CanModify()            │
  │                                                │
  │  CHECK 1: Path Validation                      │
  │  ┌──────────────────────────────────────────┐  │
  │  │ Is file inside Content/?          NO ──► BLOCK │
  │  │ Is file in ReadOnlyFolders?       YES ─► BLOCK │
  │  │ Is file in ModifiableFolders?     NO ──► BLOCK │
  │  │ (if configured)                          │  │
  │  └────────────────────┬─────────────────────┘  │
  │                       │ PASS                    │
  │                       ▼                         │
  │  CHECK 2: Cooked Asset Detection                │
  │  ┌──────────────────────────────────────────┐  │
  │  │ Parse .uasset binary header via UAssetAPI │  │
  │  │                                           │  │
  │  │ UsesEventDrivenLoader?        YES ──────► BLOCK │
  │  │ PKG_FilterEditorOnly flag?    YES ──────► BLOCK │
  │  │ Cooked-only class exports?    YES ──────► BLOCK │
  │  │ Parse error?                  YES ──────► BLOCK │
  │  │ (fail safe — treat as cooked)             │  │
  │  └────────────────────┬──────────────────────┘  │
  │                       │ PASS                    │
  │                       ▼                         │
  │                   ✓ ALLOWED                     │
  └──────────────────────────────────────────────────┘
```

## Data Flow: How Claude Reads Your Project

```
                    .uasset / .umap files
                    (binary on disk)
                           │
                           ▼
                   ┌───────────────┐
                   │   UAssetAPI    │
                   │   Parse binary │
                   └───────┬───────┘
                           │
              ┌────────────┼────────────┐
              ▼            ▼            ▼
        ┌──────────┐ ┌──────────┐ ┌──────────┐
        │  Exports  │ │  Imports  │ │Properties │
        │  (actors, │ │  (class   │ │ (config   │
        │  comps)   │ │  refs)    │ │  values)  │
        └────┬─────┘ └────┬─────┘ └────┬─────┘
             │             │            │
             └──────┬──────┴────────────┘
                    │
                    ▼
        ┌────────────────────────┐
        │  WellVersed Models   │
        │                         │
        │  AssetInfo (summary)    │ ◄── Claude sees this FIRST
        │    • name, class, size  │     (token-efficient)
        │    • isCooked flag      │
        │    • export count       │
        │                         │
        │  AssetDetail (full)     │ ◄── Claude drills in ON DEMAND
        │    • all exports        │     (only when needed)
        │    • all properties     │
        │    • all imports        │
        │                         │
        │  DeviceInfo             │ ◄── Device-specific view
        │    • properties         │     (what you see in UEFN
        │    • wiring             │      Details panel)
        │    • transform          │
        └────────┬───────────────┘
                 │
                 ▼
        ┌────────────────────────┐
        │  JSON Serialization     │
        │  (structured output)    │
        └────────┬───────────────┘
                 │
                 ▼
        ┌────────────────────────┐
        │  Claude Code Context    │
        │                         │
        │  Claude reasons about   │
        │  your project using     │
        │  structured data, not   │
        │  raw binary dumps       │
        └────────────────────────┘
```

## Digest Schema System

```
  fortnite.digest (Verse source)     verse.digest        unreal.digest
  ┌─────────────────────────────┐   ┌──────────────┐   ┌──────────────┐
  │ trigger_device := class(...):│   │ Verse types  │   │ UE base types│
  │   Duration : float = 0.0    │   │ and funcs    │   │ and structs  │
  │   Enabled : logic = true    │   │              │   │              │
  │   OnTriggered : event()     │   │              │   │              │
  │                              │   │              │   │              │
  │ mutator_zone := class(...): │   │              │   │              │
  │   ZoneShape : enum = Box    │   │              │   │              │
  │   ...                        │   │              │   │              │
  └──────────────┬──────────────┘   └──────┬───────┘   └──────┬───────┘
                 │                          │                   │
                 └──────────┬──────────────┘                   │
                            │                                   │
                            ▼                                   │
                 ┌──────────────────────┐                      │
                 │    DigestService      │◄─────────────────────┘
                 │                       │
                 │  Parse Verse syntax   │
                 │  Build schema map:    │
                 │                       │
                 │  "trigger_device" ──► │
                 │    Properties:        │
                 │      Duration (float) │
                 │      Enabled (logic)  │
                 │    Events:            │
                 │      OnTriggered      │
                 │    Functions:          │
                 │      Enable()         │
                 └───────────┬──────────┘
                             │
                 ┌───────────┴──────────────────────┐
                 ▼                                   ▼
  ┌──────────────────────────┐   ┌──────────────────────────┐
  │  Audit Validation         │   │  Modification Validation  │
  │                           │   │                           │
  │  "This device should      │   │  "Is 'Duration' a valid   │
  │   have 'Duration' set,    │   │   property on trigger_    │
  │   but it's at default"    │   │   device? YES (float)"    │
  └──────────────────────────┘   └──────────────────────────┘
```

## Build Integration Flow

```
  ┌─────────────────┐
  │  trigger_build   │
  └────────┬────────┘
           │
           ▼
  ┌─────────────────────────────┐
  │  Start UEFN build process    │
  │  (configured in forge.config)│
  └────────┬────────────────────┘
           │
           ▼
  ┌─────────────────────────────┐
  │  Capture stdout/stderr       │
  │  Monitor for timeout         │
  └────────┬────────────────────┘
           │
           ▼
  ┌─────────────────────────────┐
  │  Parse output for:           │
  │    • Errors (regex match)    │
  │    • Warnings                │
  │    • Verse errors (special)  │
  └────────┬────────────────────┘
           │
     ┌─────┴─────┐
     ▼           ▼
  ┌────────┐ ┌──────────────────────────────┐
  │ Build  │ │  Verse Errors                 │
  │ Result │ │                               │
  │        │ │  Structured for forwarding    │
  │ errors │ │  to Verse Claude Code session │
  │ warns  │ │                               │
  │ status │ │  • file path                  │
  └────────┘ │  • line number                │
             │  • error message              │
             │  • code snippet               │
             └──────────────────────────────┘
```
