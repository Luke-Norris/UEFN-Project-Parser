using WellVersed.Core.Models;
using WellVersed.Core.Services;
using WellVersed.Core.Services.VerseGeneration;
using VerseTemplateType = WellVersed.Core.Models.VerseTemplateType;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace WellVersed.MCP.Tools;

/// <summary>
/// High-level MCP tools for generating complete UEFN systems from descriptions.
///
/// These orchestrate the lower-level tools (placement, verse gen, widget building,
/// system extraction) into a single workflow. For common patterns, these provide
/// fast paths. For custom requests, they produce structured plans that Claude
/// can execute step-by-step using individual tools.
///
/// This is the primary interface for the "idea → map" pipeline.
/// </summary>
[McpServerToolType]
public class GenerationTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Generates a complete game system from a natural language description. " +
        "Returns a structured plan with all devices, verse code, widgets, and wiring needed. " +
        "For common patterns (capture point, elimination, economy, spawning), provides pre-built recipes. " +
        "For custom systems, returns a step-by-step plan to execute with individual tools.\n\n" +
        "This is the primary entry point for the 'idea → map' pipeline.")]
    public string generate_system(
        SystemExtractor systemExtractor,
        VerseDeviceGenerator verseGen,
        VerseUIGenerator uiGen,
        [Description("Natural language description of the game system to generate. " +
            "Example: 'A capture point system where players stand in a zone to capture it, " +
            "with a progress timer and score tracking'")] string description,
        [Description("Target level path (.umap) where the system will be placed. " +
            "Optional — if not provided, generates a standalone recipe.")] string? levelPath = null,
        [Description("Center position X coordinate for device placement.")] float centerX = 0,
        [Description("Center position Y coordinate for device placement.")] float centerY = 0,
        [Description("Center position Z coordinate for device placement.")] float centerZ = 0)
    {
        var lower = description.ToLower();
        var plan = new GenerationPlan
        {
            Description = description,
            LevelPath = levelPath,
            Center = new Vector3Info(centerX, centerY, centerZ)
        };

        // Classify the system
        plan.Category = ClassifyRequest(lower);

        // Generate the appropriate recipe
        switch (plan.Category)
        {
            case "capture":
                BuildCapturePointPlan(plan, verseGen);
                break;
            case "elimination":
                BuildEliminationPlan(plan, verseGen);
                break;
            case "economy":
                BuildEconomyPlan(plan, verseGen);
                break;
            case "spawning":
                BuildSpawnAreaPlan(plan);
                break;
            case "tycoon":
                BuildTycoonPlan(plan, verseGen);
                break;
            default:
                BuildCustomPlan(plan, lower, verseGen);
                break;
        }

        // If we have a level, check for existing systems
        if (!string.IsNullOrEmpty(levelPath))
        {
            try
            {
                var analysis = systemExtractor.AnalyzeLevel(levelPath);
                if (analysis.Systems.Count > 0)
                {
                    plan.ExistingContext = $"{analysis.TotalDevices} devices already in level, " +
                        $"{analysis.Systems.Count} existing systems detected. " +
                        "New system will be placed relative to existing devices.";
                }
            }
            catch { /* Non-critical */ }
        }

        // Generate execution steps
        plan.Steps = BuildExecutionSteps(plan);

        return JsonSerializer.Serialize(plan, JsonOpts);
    }

    [McpServerTool, Description(
        "Lists all available system categories that can be generated with fast paths. " +
        "Use this to understand what the generation pipeline supports natively " +
        "vs what requires custom Claude orchestration.")]
    public string list_system_categories()
    {
        return JsonSerializer.Serialize(new[]
        {
            new { category = "capture", description = "Zone capture/control point systems", devices = "Trigger + Timer + Score + HUD + VFX", hasVerse = true },
            new { category = "elimination", description = "Kill tracking with rounds and scoring", devices = "Game Manager + Spawners + Elimination + Timer + Score", hasVerse = true },
            new { category = "economy", description = "Item shops with vending machines and currency", devices = "Vending Machines + Item Granter + VFX + HUD", hasVerse = true },
            new { category = "spawning", description = "Protected spawn areas with barriers", devices = "Spawners + Barrier + Timer", hasVerse = false },
            new { category = "tycoon", description = "Economy progression with passive income and upgrades", devices = "Timers + Buttons + Barriers + Billboard", hasVerse = true },
            new { category = "custom", description = "Custom system — describe your mechanics and Claude will orchestrate individual tools", devices = "Variable", hasVerse = true },
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Generates a Verse device from a behavioral description. " +
        "More flexible than templates — uses the description to construct custom game logic. " +
        "References the Book of Verse and device schemas for correct syntax.")]
    public string generate_verse_from_description(
        VerseDeviceGenerator verseGen,
        [Description("Description of the device behavior. Example: 'A device that tracks how many " +
            "times each player has been eliminated and displays it on a billboard'")] string description,
        [Description("Name for the Verse class (snake_case). Example: 'elimination_counter'")] string className,
        [Description("List of editable properties the device should have, as 'name:type' pairs. " +
            "Example: 'Billboard:billboard_device,MaxEliminations:int'")] string? editableProperties = null,
        [Description("List of events the device should subscribe to. " +
            "Example: 'EliminationEvent,PlayerAddedEvent'")] string? events = null)
    {
        var builder = new VerseCodeBuilder();

        builder.Comment($"Generated by WellVersed — {description}")
            .Line()
            .StandardUsings()
            .Line();

        // Build class definition
        builder.ClassDef(className);

        // Add editable properties
        if (!string.IsNullOrEmpty(editableProperties))
        {
            foreach (var propDef in editableProperties.Split(',', StringSplitOptions.TrimEntries))
            {
                var parts = propDef.Split(':', 2);
                if (parts.Length == 2)
                {
                    builder.Editable(parts[0].Trim(), parts[1].Trim(), $"{parts[1].Trim()}{{}}");

                }
            }
            builder.Line();
        }

        // Add state variables based on description
        var lower = description.ToLower();
        if (lower.Contains("count") || lower.Contains("track"))
            builder.Var("Count", "int", "0");
        if (lower.Contains("score") || lower.Contains("point"))
            builder.Var("Score", "int", "0");
        if (lower.Contains("active") || lower.Contains("enabled"))
            builder.Var("IsActive", "logic", "false");
        if (lower.Contains("timer") || lower.Contains("cooldown"))
            builder.Var("TimeRemaining", "float", "0.0");

        builder.Line();

        // OnBegin with event subscriptions
        builder.OnBegin();
        if (!string.IsNullOrEmpty(events))
        {
            foreach (var evt in events.Split(',', StringSplitOptions.TrimEntries))
            {
                builder.Line($"# Subscribe to {evt}");
                builder.Line($"# TODO: Wire up {evt} handler");
            }
        }
        else
        {
            builder.Line($"Print(\"{className} initialized\")");
        }
        builder.EndBlock();

        builder.Line();
        builder.Comment("TODO: Implement event handlers based on description:");
        builder.Comment($"  {description}");

        return JsonSerializer.Serialize(new
        {
            className,
            description,
            sourceCode = builder.ToString(),
            notes = new[]
            {
                "This is a starting point — flesh out the event handlers for your specific mechanics.",
                "Compile in UEFN and place the device in your level.",
                "Use preview_verse_code to see the full output before writing.",
                "Use generate_verse_file to write the .verse file to your project."
            }
        }, JsonOpts);
    }

    // ========= Plan Builders =========

    private static string ClassifyRequest(string lower)
    {
        if (lower.Contains("capture") || lower.Contains("control point") || lower.Contains("zone") && lower.Contains("stand"))
            return "capture";
        if (lower.Contains("elimination") || lower.Contains("deathmatch") || lower.Contains("kill track"))
            return "elimination";
        if (lower.Contains("shop") || lower.Contains("vending") || lower.Contains("purchase") || lower.Contains("buy"))
            return "economy";
        if (lower.Contains("spawn") && (lower.Contains("protect") || lower.Contains("barrier") || lower.Contains("team")))
            return "spawning";
        if (lower.Contains("tycoon") || lower.Contains("passive income") || lower.Contains("upgrade") && lower.Contains("tier"))
            return "tycoon";
        return "custom";
    }

    private static void BuildCapturePointPlan(GenerationPlan plan, VerseDeviceGenerator verseGen)
    {
        plan.SystemName = "Capture Point System";

        plan.Devices.Add(new PlannedDevice { Role = "capture_zone", DeviceClass = "BP_TriggerDevice_C", Type = "Trigger Device", Offset = new Vector3Info(0, 0, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "capture_timer", DeviceClass = "BP_TimerDevice_C", Type = "Timer Device", Offset = new Vector3Info(200, 0, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "score_manager", DeviceClass = "BP_ScoreManagerDevice_C", Type = "Score Manager", Offset = new Vector3Info(400, 0, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "hud_message", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(200, 200, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "vfx_indicator", DeviceClass = "BP_VFXSpawnerDevice_C", Type = "VFX Spawner", Offset = new Vector3Info(0, 200, 0) });

        plan.Wiring.Add(new PlannedWiring { SourceRole = "capture_zone", OutputEvent = "OnTriggered", TargetRole = "capture_timer", InputAction = "Start" });
        plan.Wiring.Add(new PlannedWiring { SourceRole = "capture_zone", OutputEvent = "OnTriggered", TargetRole = "hud_message", InputAction = "Show" });
        plan.Wiring.Add(new PlannedWiring { SourceRole = "capture_zone", OutputEvent = "OnUntriggered", TargetRole = "capture_timer", InputAction = "Pause" });
        plan.Wiring.Add(new PlannedWiring { SourceRole = "capture_timer", OutputEvent = "OnCompleted", TargetRole = "score_manager", InputAction = "Activate" });
        plan.Wiring.Add(new PlannedWiring { SourceRole = "capture_timer", OutputEvent = "OnCompleted", TargetRole = "vfx_indicator", InputAction = "Disable" });

        // Generate verse
        var verseResult = verseGen.Generate(new VerseFileRequest { TemplateType = VerseTemplateType.GameManager, FileName = "capture_point_manager" });
        plan.VerseCode = verseResult.SourceCode;
    }

    private static void BuildEliminationPlan(GenerationPlan plan, VerseDeviceGenerator verseGen)
    {
        plan.SystemName = "Elimination Game Mode";

        plan.Devices.Add(new PlannedDevice { Role = "team_a_spawn_1", DeviceClass = "BP_PlayerSpawnerDevice_C", Type = "Player Spawner", Offset = new Vector3Info(-2000, 0, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "team_a_spawn_2", DeviceClass = "BP_PlayerSpawnerDevice_C", Type = "Player Spawner", Offset = new Vector3Info(-2000, 500, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "team_b_spawn_1", DeviceClass = "BP_PlayerSpawnerDevice_C", Type = "Player Spawner", Offset = new Vector3Info(2000, 0, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "team_b_spawn_2", DeviceClass = "BP_PlayerSpawnerDevice_C", Type = "Player Spawner", Offset = new Vector3Info(2000, 500, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "elim_tracker", DeviceClass = "BP_EliminationManagerDevice_C", Type = "Elimination Manager", Offset = new Vector3Info(0, 0, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "round_timer", DeviceClass = "BP_TimerDevice_C", Type = "Timer Device", Offset = new Vector3Info(0, 200, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "score_tracker", DeviceClass = "BP_ScoreManagerDevice_C", Type = "Score Manager", Offset = new Vector3Info(0, 400, 0) });

        plan.Wiring.Add(new PlannedWiring { SourceRole = "elim_tracker", OutputEvent = "OnElimination", TargetRole = "score_tracker", InputAction = "IncrementScore" });
        plan.Wiring.Add(new PlannedWiring { SourceRole = "round_timer", OutputEvent = "OnCompleted", TargetRole = "round_timer", InputAction = "Reset" });

        var verseResult = verseGen.Generate(new VerseFileRequest { TemplateType = VerseTemplateType.EliminationTracker, FileName = "elimination_game" });
        plan.VerseCode = verseResult.SourceCode;
    }

    private static void BuildEconomyPlan(GenerationPlan plan, VerseDeviceGenerator verseGen)
    {
        plan.SystemName = "Item Shop System";

        plan.Devices.Add(new PlannedDevice { Role = "vendor_1", DeviceClass = "BP_VendingMachineDevice_C", Type = "Vending Machine", Offset = new Vector3Info(0, 0, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "vendor_2", DeviceClass = "BP_VendingMachineDevice_C", Type = "Vending Machine", Offset = new Vector3Info(300, 0, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "vendor_3", DeviceClass = "BP_VendingMachineDevice_C", Type = "Vending Machine", Offset = new Vector3Info(600, 0, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "item_granter", DeviceClass = "BP_ItemGranterDevice_C", Type = "Item Granter", Offset = new Vector3Info(300, 300, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "purchase_vfx", DeviceClass = "BP_VFXSpawnerDevice_C", Type = "VFX Spawner", Offset = new Vector3Info(300, -200, 0) });

        plan.Wiring.Add(new PlannedWiring { SourceRole = "vendor_1", OutputEvent = "OnItemPurchased", TargetRole = "item_granter", InputAction = "GrantItem" });
        plan.Wiring.Add(new PlannedWiring { SourceRole = "vendor_2", OutputEvent = "OnItemPurchased", TargetRole = "item_granter", InputAction = "GrantItem" });
        plan.Wiring.Add(new PlannedWiring { SourceRole = "vendor_3", OutputEvent = "OnItemPurchased", TargetRole = "item_granter", InputAction = "GrantItem" });
        plan.Wiring.Add(new PlannedWiring { SourceRole = "vendor_1", OutputEvent = "OnItemPurchased", TargetRole = "purchase_vfx", InputAction = "Spawn" });

        plan.VerseCode = null; // Economy can work purely with device wiring
    }

    private static void BuildSpawnAreaPlan(GenerationPlan plan)
    {
        plan.SystemName = "Protected Spawn Area";

        plan.Devices.Add(new PlannedDevice { Role = "spawn_1", DeviceClass = "BP_PlayerSpawnerDevice_C", Type = "Player Spawner", Offset = new Vector3Info(-200, -200, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "spawn_2", DeviceClass = "BP_PlayerSpawnerDevice_C", Type = "Player Spawner", Offset = new Vector3Info(200, -200, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "spawn_3", DeviceClass = "BP_PlayerSpawnerDevice_C", Type = "Player Spawner", Offset = new Vector3Info(-200, 200, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "spawn_4", DeviceClass = "BP_PlayerSpawnerDevice_C", Type = "Player Spawner", Offset = new Vector3Info(200, 200, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "barrier", DeviceClass = "BP_BarrierDevice_C", Type = "Barrier Device", Offset = new Vector3Info(0, 0, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "warmup_timer", DeviceClass = "BP_TimerDevice_C", Type = "Timer Device", Offset = new Vector3Info(0, 0, 200) });

        plan.Wiring.Add(new PlannedWiring { SourceRole = "warmup_timer", OutputEvent = "OnCompleted", TargetRole = "barrier", InputAction = "Disable" });

        plan.VerseCode = null;
    }

    private static void BuildTycoonPlan(GenerationPlan plan, VerseDeviceGenerator verseGen)
    {
        plan.SystemName = "Tycoon Economy System";

        plan.Devices.Add(new PlannedDevice { Role = "income_timer", DeviceClass = "BP_TimerDevice_C", Type = "Timer Device", Offset = new Vector3Info(0, 0, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "upgrade_t1", DeviceClass = "BP_ButtonDevice_C", Type = "Button Device", Offset = new Vector3Info(500, 0, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "upgrade_t2", DeviceClass = "BP_ButtonDevice_C", Type = "Button Device", Offset = new Vector3Info(1000, 0, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "upgrade_t3", DeviceClass = "BP_ButtonDevice_C", Type = "Button Device", Offset = new Vector3Info(1500, 0, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "gate_1", DeviceClass = "BP_BarrierDevice_C", Type = "Barrier Device", Offset = new Vector3Info(500, 500, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "gate_2", DeviceClass = "BP_BarrierDevice_C", Type = "Barrier Device", Offset = new Vector3Info(1000, 500, 0) });
        plan.Devices.Add(new PlannedDevice { Role = "balance_display", DeviceClass = "BP_BillboardDevice_C", Type = "Billboard Device", Offset = new Vector3Info(0, 300, 200) });

        plan.Wiring.Add(new PlannedWiring { SourceRole = "income_timer", OutputEvent = "OnCompleted", TargetRole = "income_timer", InputAction = "Start" });
        plan.Wiring.Add(new PlannedWiring { SourceRole = "upgrade_t1", OutputEvent = "OnInteracted", TargetRole = "gate_1", InputAction = "Disable" });
        plan.Wiring.Add(new PlannedWiring { SourceRole = "upgrade_t2", OutputEvent = "OnInteracted", TargetRole = "gate_2", InputAction = "Disable" });

        var verseResult = verseGen.Generate(new VerseFileRequest { TemplateType = VerseTemplateType.CollectibleSystem, FileName = "tycoon_manager" });
        plan.VerseCode = verseResult.SourceCode;
    }

    private static void BuildCustomPlan(GenerationPlan plan, string lower, VerseDeviceGenerator verseGen)
    {
        plan.SystemName = "Custom System";
        plan.IsCustom = true;
        plan.CustomInstructions =
            "This system doesn't match a pre-built recipe. Use the individual MCP tools to build it:\n\n" +
            "1. Use `analyze_level_systems` to understand existing devices in your level\n" +
            "2. Use `list_verse_templates` to see available Verse template types\n" +
            "3. Use `clone_actor` to place devices from templates already in the level\n" +
            "4. Use `preview_set_property` + `apply_modification` to configure device properties\n" +
            "5. Use `generate_verse_file` to create the Verse game logic\n" +
            "6. Use `preview_wire_devices` + `apply_modification` to connect device signals\n" +
            "7. Use `get_widget_reference` for UI widget creation\n\n" +
            "Describe the system in more detail for a more specific plan.";

        // Generate a basic device template
        var verseResult = verseGen.Generate(new VerseFileRequest { TemplateType = VerseTemplateType.EmptyDevice, FileName = "custom_device" });
        plan.VerseCode = verseResult.SourceCode;
    }

    private static List<ExecutionStep> BuildExecutionSteps(GenerationPlan plan)
    {
        var steps = new List<ExecutionStep>();
        int order = 1;

        if (plan.IsCustom)
        {
            steps.Add(new ExecutionStep { Order = order++, Tool = "manual", Description = plan.CustomInstructions ?? "Follow custom instructions" });
            return steps;
        }

        // Device placement
        foreach (var device in plan.Devices)
        {
            var pos = new Vector3Info(
                plan.Center.X + device.Offset.X,
                plan.Center.Y + device.Offset.Y,
                plan.Center.Z + device.Offset.Z);

            steps.Add(new ExecutionStep
            {
                Order = order++,
                Tool = "clone_actor",
                Description = $"Place {device.Role} ({device.Type}) at ({pos.X}, {pos.Y}, {pos.Z})",
                Parameters = new Dictionary<string, string>
                {
                    ["level"] = plan.LevelPath ?? "(select level)",
                    ["sourceActor"] = device.DeviceClass,
                    ["x"] = pos.X.ToString(),
                    ["y"] = pos.Y.ToString(),
                    ["z"] = pos.Z.ToString(),
                }
            });
        }

        // Wiring
        foreach (var wire in plan.Wiring)
        {
            steps.Add(new ExecutionStep
            {
                Order = order++,
                Tool = "preview_wire_devices + apply_modification",
                Description = $"Wire {wire.SourceRole}.{wire.OutputEvent} → {wire.TargetRole}.{wire.InputAction}",
                Parameters = new Dictionary<string, string>
                {
                    ["sourceDevice"] = wire.SourceRole,
                    ["outputEvent"] = wire.OutputEvent,
                    ["targetDevice"] = wire.TargetRole,
                    ["inputAction"] = wire.InputAction,
                }
            });
        }

        // Verse code
        if (!string.IsNullOrEmpty(plan.VerseCode))
        {
            steps.Add(new ExecutionStep
            {
                Order = order++,
                Tool = "generate_verse_file",
                Description = $"Generate Verse device controller for {plan.SystemName}"
            });
        }

        return steps;
    }
}

// Generation Plan models moved to WellVersed.Core.Models.GenerationPlanModels
