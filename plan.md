# Implementation Plan: 8 New Features + Terrain System

## Overview
8 features to implement: Terrain Height System, Live Project Diffing (#1), Device Wiring Graph + Auto-Wirer (#2), Asset Dependency Graph (#4), Template/Prefab System (#5), Cross-Session Bridge (#9), Level Migration (#11), Smart Naming/Organization (#12).

---

## Feature 0: Terrain Height System
**Goal**: Maps aren't flat. Generate height variation using noise + terrain piece tiling.

### New Files
- `src/FortniteForge.Core/Services/MapGeneration/TerrainEngine.cs`

### What it does
- `HeightMap` class: generates Perlin-style noise heightmap for a given map size
- Biome-specific terrain profiles:
  - Desert: gentle rolling dunes (low amplitude, long wavelength)
  - Forest: moderate rolling hills (medium amplitude)
  - Urban: mostly flat with slight elevation changes
  - Snow: dramatic ridges (high amplitude, varied wavelength)
- `GetHeightAt(x, y)` → returns Z offset for any world position
- Terrain piece tiling: if catalog has terrain pieces, tile them across map with Z from heightmap
- All actor placement in MapGenerator uses `heightMap.GetHeightAt(x, y)` to offset Z

### Changes to existing files
- `MapGenerator.cs`: Accept `TerrainEngine`, call `GetHeightAt()` for every placed actor's Z
- `MapLayout.cs`: Add `HeightMapConfig` to layout plan (amplitude, wavelength, octaves)
- `BiomeConfig.cs`: Add terrain profile settings per biome

---

## Feature 1: Live Project Diffing / Version Comparison
**Goal**: Compare two versions of a level and get structured diff.

### New Files
- `src/FortniteForge.Core/Services/DiffService.cs`
- `src/FortniteForge.Core/Models/DiffResult.cs`
- `src/FortniteForge.MCP/Tools/DiffTools.cs`

### DiffResult model
- `LevelDiff`: actors added, removed, moved, property changes
- `ActorDiff`: per-actor change detail (which properties changed, old→new values)
- `DiffSummary`: human-readable summary string

### DiffService API
- `DiffLevels(pathA, pathB)` → `LevelDiff` — compare two .umap files
- `SnapshotLevel(levelPath, snapshotName)` → saves a lightweight JSON snapshot (actor names, classes, transforms, properties)
- `DiffSnapshot(snapshotName, currentLevelPath)` → compare against saved snapshot
- `ListSnapshots()` → list available snapshots

### MCP Tools
- `diff_levels` — compare two .umap files
- `snapshot_level` — save current state for later comparison
- `diff_snapshot` — compare current state against saved snapshot
- `list_snapshots` — list saved snapshots

---

## Feature 2: Device Wiring Graph + Auto-Wirer
**Goal**: Parse all wiring, build a graph, detect dead ends, suggest/auto-wire connections.

### New Files
- `src/FortniteForge.Core/Services/WiringService.cs`
- `src/FortniteForge.Core/Models/WiringGraph.cs`
- `src/FortniteForge.MCP/Tools/WiringTools.cs`

### WiringGraph model
- `WiringGraph`: nodes (devices) + edges (signal connections)
- `WiringNode`: device name, class, events available, functions available
- `WiringEdge`: source device → output event → target device → input action
- `WiringIssue`: dead ends, unwired outputs, unreachable devices, loops

### WiringService API
- `BuildWiringGraph(levelPath)` → `WiringGraph` — parse all device connections
- `AnalyzeWiring(graph)` → `List<WiringIssue>` — find problems
- `SuggestWiring(graph, sourceDevice, targetDevice)` → suggest valid connections based on digest schemas
- `TraceSignalChain(graph, startDevice, outputEvent)` → trace where a signal ends up
- `GetUnwiredDevices(graph)` → devices with no connections at all

### MCP Tools
- `build_wiring_graph` — parse and return the full wiring graph for a level
- `analyze_wiring` — find wiring issues (dead ends, unwired devices)
- `suggest_wiring` — suggest valid connections between two devices
- `trace_signal` — follow a signal chain from source to all endpoints

---

## Feature 4: Asset Dependency Graph
**Goal**: Map every reference between assets — know what depends on what.

### New Files
- `src/FortniteForge.Core/Services/DependencyService.cs`
- `src/FortniteForge.Core/Models/DependencyGraph.cs`
- `src/FortniteForge.MCP/Tools/DependencyTools.cs`

### DependencyGraph model
- `DependencyGraph`: nodes (assets) + edges (references)
- `DependencyNode`: asset path, class, direct dependencies, reverse dependencies
- `DependencyEdge`: from asset → to asset, reference type (import, soft ref, etc.)

### DependencyService API
- `BuildDependencyGraph(subfolder?)` → `DependencyGraph` — scan all assets and map imports
- `GetDependencies(assetPath)` → what this asset depends on
- `GetDependents(assetPath)` → what depends on this asset ("what breaks if I delete this?")
- `FindUnusedAssets()` → assets with no dependents (safe to clean up)
- `FindBrokenReferences()` → imports that point to non-existent assets

### MCP Tools
- `get_dependencies` — what does this asset depend on
- `get_dependents` — what depends on this asset
- `find_unused_assets` — find assets safe to delete
- `find_broken_references` — find broken import references

---

## Feature 5: Template/Prefab System
**Goal**: Save groups of configured, wired devices as reusable templates. Drop them into any level.

### New Files
- `src/FortniteForge.Core/Services/TemplateService.cs`
- `src/FortniteForge.Core/Models/PrefabTemplate.cs`
- `src/FortniteForge.MCP/Tools/TemplateTools.cs`

### PrefabTemplate model
- `PrefabTemplate`: name, description, list of `TemplateActor` entries, wiring definitions
- `TemplateActor`: source actor name, relative offset from template origin, class name, key properties
- `TemplateWiring`: relative wiring definitions (actor A event → actor B action)

### TemplateService API
- `CaptureTemplate(levelPath, actorNames[], templateName)` → captures selected actors + wiring as a template, saves to JSON
- `PlaceTemplate(levelPath, templateName, location, rotation?)` → clones all template actors into level, re-creates wiring, offsets all positions relative to placement point
- `PreviewPlaceTemplate(...)` → dry-run preview
- `ListTemplates()` → available saved templates
- `InspectTemplate(name)` → view template contents
- `DeleteTemplate(name)` → remove a saved template

### MCP Tools
- `capture_template` — save selected actors as a reusable template
- `place_template` — drop a template into a level at a position
- `preview_place_template` — dry-run of template placement
- `list_templates` — list available templates
- `inspect_template` — view what's in a template

---

## Feature 9: Cross-Session Bridge
**Goal**: Export level state as a structured handoff file for the Verse Claude Code session.

### New Files
- `src/FortniteForge.Core/Services/BridgeService.cs`
- `src/FortniteForge.Core/Models/BridgeHandoff.cs`
- `src/FortniteForge.MCP/Tools/BridgeTools.cs`

### BridgeHandoff model
- `BridgeHandoff`: timestamp, project path, levels, devices with full context
- `DeviceContext`: device name, class, available events/functions (from digest), current wiring, properties
- `VerseContext`: list of Verse devices, their class paths, known errors
- `LevelContext`: level name, all actors, device summary

### BridgeService API
- `ExportHandoff(levelPaths?, outputPath?)` → generates a comprehensive JSON handoff file
- `ExportVerseContext(levelPath?)` → exports just Verse-relevant info (devices, events, errors)
- `ImportVerseErrors(errorsFilePath)` → reads errors from the Verse session and maps them to devices/levels
- `GetHandoffPath()` → returns the standard handoff file location

### MCP Tools
- `export_handoff` — generate full handoff for Verse session
- `export_verse_context` — export Verse-specific context
- `import_verse_errors` — import errors from Verse session

---

## Feature 11: Level Migration / Porting
**Goal**: Copy specific actors (with wiring and config) from one level to another.

### New Files
- `src/FortniteForge.Core/Services/MigrationService.cs`
- `src/FortniteForge.Core/Models/MigrationResult.cs`
- `src/FortniteForge.MCP/Tools/MigrationTools.cs`

### MigrationResult model
- `MigrationResult`: success, actors migrated, wiring recreated, warnings
- `MigrationPlan`: what will be copied, conflicts detected

### MigrationService API
- `PreviewMigration(sourceLevelPath, targetLevelPath, actorNames[], offset?)` → `MigrationPlan` — dry-run showing what will be copied and any name conflicts
- `MigrateActors(sourceLevelPath, targetLevelPath, actorNames[], offset?)` → `MigrationResult` — copies actors + components, fixes names, recreates wiring, applies position offset
- `MigrateByType(sourceLevelPath, targetLevelPath, deviceType, offset?)` — migrate all devices of a type
- `MigrateAll(sourceLevelPath, targetLevelPath, offset?)` — migrate everything

### MCP Tools
- `preview_migration` — dry-run showing what will be copied
- `migrate_actors` — copy specific actors between levels
- `migrate_by_type` — copy all actors of a type between levels

---

## Feature 12: Smart Naming / Organization
**Goal**: Auto-rename actors to meaningful names. Enforce naming conventions.

### New Files
- `src/FortniteForge.Core/Services/NamingService.cs`
- `src/FortniteForge.Core/Models/NamingResult.cs`
- `src/FortniteForge.MCP/Tools/NamingTools.cs`

### NamingResult model
- `NamingPlan`: list of proposed renames (old name → new name, reason)
- `NamingResult`: renames applied, conflicts resolved

### NamingService API
- `PreviewAutoRename(levelPath, convention?)` → `NamingPlan` — proposes meaningful names for all generic actors based on: device type, location/POI proximity, wiring context, class name
- `ApplyRenames(levelPath, NamingPlan)` → `NamingResult` — applies the renames (updates ObjectName on exports, updates all wiring references)
- `RenameActor(levelPath, oldName, newName)` → renames a single actor and fixes all references
- `ValidateNaming(levelPath, convention?)` → checks if actors follow naming convention, reports violations

### Naming conventions
- `{Type}_{Location}_{Number}` (e.g., "TriggerDevice_POI1_01")
- `{Type}_{Purpose}_{Number}` (e.g., "ItemGranter_WeaponDrop_01")
- Custom regex pattern from config

### MCP Tools
- `preview_auto_rename` — propose meaningful names for generic actors
- `apply_renames` — apply proposed renames
- `rename_actor` — rename a single actor
- `validate_naming` — check naming convention compliance

---

## DI Registration
All new services registered as singletons in:
- `src/FortniteForge.MCP/Program.cs` — `builder.Services.AddSingleton<T>()`
- `src/FortniteForge.CLI/Program.cs` — manual construction + `ServiceBundle` record

New services to register:
- `TerrainEngine`
- `DiffService`
- `WiringService`
- `DependencyService`
- `TemplateService`
- `BridgeService`
- `MigrationService`
- `NamingService`

---

## Implementation Order
1. **TerrainEngine** (enhances MapGenerator, most impactful for map gen)
2. **WiringService** (wiring graph + auto-wirer — foundational for many features)
3. **DiffService** (project diffing — standalone, no deps on other new services)
4. **DependencyService** (asset dependency graph — standalone)
5. **TemplateService** (prefab system — uses ActorPlacementService + WiringService)
6. **NamingService** (smart naming — uses DeviceService context)
7. **MigrationService** (level porting — uses ActorPlacementService + WiringService + NamingService)
8. **BridgeService** (cross-session — uses DeviceService + DigestService + BuildService)

This order minimizes forward dependencies and lets each feature build on the previous ones.
