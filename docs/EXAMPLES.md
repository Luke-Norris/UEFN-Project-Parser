# FortniteForge — Usage Examples & Architecture

## CLI Output Examples

### `fortniteforge list`

```
$ fortniteforge list --folder MyMaps

{
  "count": 4,
  "assets": [
    {
      "name": "MainLevel",
      "relativePath": "MyMaps/MainLevel.umap",
      "assetClass": "World",
      "size": "12.4 MB",
      "isModifiable": true,
      "isCooked": false,
      "summary": "World with 147 exports (1 World, 1 Level, 89 StaticMeshActor, 23 BP_TriggerDevice_C, ...)"
    },
    {
      "name": "ArenaLevel",
      "relativePath": "MyMaps/ArenaLevel.umap",
      "assetClass": "World",
      "size": "8.1 MB",
      "isModifiable": true,
      "isCooked": false,
      "summary": "World with 93 exports (1 World, 1 Level, 52 StaticMeshActor, 15 BP_MutatorZone_C, ...)"
    },
    {
      "name": "HubArea",
      "relativePath": "MyMaps/HubArea.umap",
      "assetClass": "World",
      "size": "3.2 MB",
      "isModifiable": true,
      "isCooked": false,
      "summary": "World with 41 exports (1 World, 1 Level, 22 StaticMeshActor, 8 BP_PlayerSpawner_C, ...)"
    },
    {
      "name": "TestZone",
      "relativePath": "MyMaps/TestZone.umap",
      "assetClass": "World",
      "size": "1.1 MB",
      "isModifiable": true,
      "isCooked": false,
      "summary": "World with 18 exports (1 World, 1 Level, 9 StaticMeshActor, 3 BP_ItemGranter_C, ...)"
    }
  ]
}
```

### `fortniteforge devices <level>`

```
$ fortniteforge devices "C:\Users\Luke\Documents\FortniteProjects\MyProject\Content\MyMaps\MainLevel.umap"

{
  "level": "MainLevel.umap",
  "deviceCount": 12,
  "devices": [
    {
      "actorName": "BP_TriggerDevice_C_3",
      "deviceType": "Trigger Device",
      "label": "Entrance Trigger",
      "location": "(1200, -3400, 150)",
      "propertyCount": 8,
      "wiringCount": 2,
      "isVerseDevice": false
    },
    {
      "actorName": "BP_MutatorZone_C_1",
      "deviceType": "Mutator Zone",
      "label": "Speed Boost Area",
      "location": "(2400, -1800, 100)",
      "propertyCount": 14,
      "wiringCount": 1,
      "isVerseDevice": false
    },
    {
      "actorName": "BP_ItemGranter_C_0",
      "deviceType": "Item Granter",
      "label": "Loadout Granter",
      "location": "(800, -2200, 200)",
      "propertyCount": 6,
      "wiringCount": 0,
      "isVerseDevice": false
    },
    {
      "actorName": "MyVerseDevice_C_0",
      "deviceType": "MyVerseDevice",
      "label": "Game Manager",
      "location": "(0, 0, 0)",
      "propertyCount": 3,
      "wiringCount": 0,
      "isVerseDevice": true
    }
  ]
}
```

### `fortniteforge device TriggerDevice_3`

```
$ fortniteforge device BP_TriggerDevice_C_3

Found in: C:\Users\Luke\Documents\FortniteProjects\MyProject\Content\MyMaps\MainLevel.umap

{
  "actorName": "BP_TriggerDevice_C_3",
  "deviceClass": "BP_TriggerDevice_C",
  "deviceType": "Trigger Device",
  "label": "Entrance Trigger",
  "location": { "x": 1200.0, "y": -3400.0, "z": 150.0 },
  "rotation": { "x": 0.0, "y": 45.0, "z": 0.0 },
  "scale": { "x": 3.0, "y": 3.0, "z": 2.0 },
  "properties": [
    { "name": "TriggerVisibility", "type": "EnumProperty", "value": "Hidden", "isConfigurable": true },
    { "name": "TriggeredByTeam", "type": "EnumProperty", "value": "AnyTeam", "isConfigurable": true },
    { "name": "NumberOfUsesAllowed", "type": "IntProperty", "value": "0", "isConfigurable": true },
    { "name": "Duration", "type": "FloatProperty", "value": "0.5", "isConfigurable": true },
    { "name": "Enabled", "type": "BoolProperty", "value": "True", "isConfigurable": true },
    { "name": "TriggerDelay", "type": "FloatProperty", "value": "0.0", "isConfigurable": true }
  ],
  "wiring": [
    {
      "outputEvent": "OnTriggered",
      "targetDevice": "BP_MutatorZone_C_1",
      "inputAction": "Enable",
      "channel": null
    },
    {
      "outputEvent": "OnTriggered",
      "targetDevice": "BP_ItemGranter_C_0",
      "inputAction": "GrantItem",
      "channel": null
    }
  ],
  "isVerseDevice": false,
  "verseClassPath": null
}
```

### `fortniteforge audit --level MainLevel.umap`

```
$ fortniteforge audit --level MainLevel.umap

{
  "target": "MainLevel.umap",
  "status": "Warning",
  "summary": "0 errors, 3 warnings, 2 info items.",
  "findings": [
    {
      "severity": "Warning",
      "category": "Wiring",
      "message": "Device 'BP_TriggerDevice_C_7' (Trigger Device) has no signal connections but typically requires wiring.",
      "location": "BP_TriggerDevice_C_7",
      "suggestion": "Connect this Trigger Device to other devices to make it functional."
    },
    {
      "severity": "Warning",
      "category": "Configuration",
      "message": "Spawner 'BP_ItemSpawner_C_2' does not appear to have an item configured.",
      "location": "BP_ItemSpawner_C_2",
      "suggestion": "Set the item/class property to define what this spawner creates."
    },
    {
      "severity": "Warning",
      "category": "Naming",
      "message": "Multiple devices share the label 'Trigger': BP_TriggerDevice_C_3, BP_TriggerDevice_C_5",
      "location": "Trigger",
      "suggestion": "Use unique labels to avoid confusion when debugging."
    },
    {
      "severity": "Info",
      "category": "Configuration",
      "message": "Device 'BP_Barrier_C_0' (Barrier) appears to use mostly default values.",
      "location": "BP_Barrier_C_0",
      "suggestion": "Review if this device needs custom configuration."
    },
    {
      "severity": "Info",
      "category": "Schema",
      "message": "No schema found for device class 'MyVerseDevice_C'. Cannot validate properties.",
      "location": "MyVerseDevice_C_0"
    }
  ]
}
```

### `fortniteforge build-log`

```
$ fortniteforge build-log

Build Status: Failed
Log File: C:\Users\Luke\Documents\FortniteProjects\MyProject\Saved\Logs\Build_20240115.log

=== ERRORS (2) ===
  [E0001] Missing reference to 'BP_CustomProp_C' in level MainLevel.umap (MainLevel.umap:0)
  [ERR] Verse compilation failed with 1 error(s) (Verse:0)

=== VERSE ERRORS (1) ===
  Content/Scripts/GameManager.verse:42 — Type mismatch: expected 'int' but got 'float'
    > Score := TotalKills * DamageMultiplier

=== WARNINGS (3) ===
  Asset 'BP_OldDevice_C' is deprecated and will be removed in a future update
  Texture 'T_CustomDecal' exceeds recommended size (4096x4096)
  Material instance 'MI_GlowEffect' references unused parameters
```

### MCP: Claude inspects a device mid-conversation

```
User: "Why isn't my entrance trigger granting items?"

Claude uses: inspect_device("BP_TriggerDevice_C_3")
Claude uses: inspect_device("BP_ItemGranter_C_0")

Claude: "I can see the issue. Your Entrance Trigger (BP_TriggerDevice_C_3) IS wired
        to the Item Granter with 'OnTriggered → GrantItem', but the Item Granter's
        properties show no item is configured — all item slots are 'None'.

        Let me check what properties are available..."

Claude uses: get_device_schema("item_granter_device")

Claude: "The Item Granter needs an 'ItemDefinition' property set. Want me to fix it?
        I'll do a dry-run first so you can see exactly what changes."

Claude uses: preview_set_property(
    assetPath="...MainLevel.umap",
    targetObject="BP_ItemGranter_C_0",
    propertyName="ItemDefinition",
    newValue="WID_Assault_AutoHigh_Athena_SR_Ore_T03"
)

Claude: "Here's what will change:
         BP_ItemGranter_C_0.ItemDefinition: (not set) → WID_Assault_AutoHigh_...
         Affected files: MainLevel.umap
         Backup will be created automatically.

         Want me to apply this?"
```

### MCP: Claude generates a forest

```
User: "I need a forest of pine trees around the spawn area"

Claude uses: find_levels()
Claude uses: find_devices_by_type("Spawner")
Claude uses: list_devices("MainLevel.umap")

Claude: "I found your spawn area near (5000, 3000, 200). I can see you have
        a StaticMeshActor_12 that's a pine tree mesh at (4800, 2900, 200).
        I'll use that as a template and scatter 40 copies in the area.
        Let me preview first..."

Claude uses: preview_scatter_place(
    levelPath="MainLevel.umap",
    sourceActorName="StaticMeshActor_12",
    centerX=5000, centerY=3000, centerZ=200,
    areaWidth=3000, areaLength=3000,
    count=40,
    minSpacing=200,
    randomRotation=true,
    scaleMin=0.7, scaleMax=1.3
)

Claude: "Here's the plan:
         - 40 pine trees scattered across a 3000x3000 area
         - Centered on (5000, 3000, 200)
         - Min 200 units apart, random rotation and scale (0.7x-1.3x)
         - A backup will be created before any changes

         Ready to place them?"

Claude uses: apply_scatter_place(...)

Claude: "Done! Placed 40/40 pine trees. Backup saved at:
        .fortniteforge/backups/MyMaps/MainLevel_20240115_143022.umap

        Open the level in UEFN to see them. If anything looks off,
        I can restore from backup instantly."
```

### `check_project_health` — first thing to run

```
{
  "healthy": true,
  "checks": [
    {
      "check": "Configuration",
      "status": "PASS",
      "details": "Config loaded. Project: C:\\Users\\Luke\\Documents\\FortniteProjects\\MyProject"
    },
    {
      "check": "Content Directory",
      "status": "PASS",
      "details": "Found at: C:\\Users\\Luke\\Documents\\FortniteProjects\\MyProject\\Content"
    },
    {
      "check": "Digest Files",
      "status": "PASS",
      "details": "Loaded 247 device schemas from digest files."
    },
    {
      "check": "Project Assets",
      "status": "PASS",
      "details": "143 .uasset files, 4 .umap files"
    },
    {
      "check": "Backup Directory",
      "status": "PASS",
      "details": "Exists at: C:\\...\\MyProject\\.fortniteforge\\backups"
    },
    {
      "check": "Build Config",
      "status": "WARN",
      "details": "Build command not configured. Set build.buildCommand in forge.config.json for build integration."
    },
    {
      "check": "Safety Settings",
      "status": "PASS",
      "details": "DryRun: True, AutoBackup: True, ReadOnly folders: [FortniteGame, Engine]"
    }
  ]
}
```
