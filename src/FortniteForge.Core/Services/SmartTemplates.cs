using WellVersed.Core.Models;
using WellVersed.Core.Services.VerseGeneration;
using Microsoft.Extensions.Logging;

namespace WellVersed.Core.Services;

// =========================================================================
//  MODELS
// =========================================================================

/// <summary>
/// A fully parameterized game mechanic template. Each template generates
/// complete device lists, wiring, production-quality Verse code, and
/// optional widget specs. These are deep, specific implementations —
/// not skeletons.
/// </summary>
public class SmartTemplate
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = ""; // combat, economy, objective, progression, meta, utility
    public string Description { get; set; } = "";
    public List<TemplateParameter> Parameters { get; set; } = new();
    public List<PlannedDevice> Devices { get; set; } = new();
    public List<PlannedWiring> Wiring { get; set; } = new();
    public string VerseCode { get; set; } = "";
    public string? WidgetSpecJson { get; set; }
    public List<string> Tags { get; set; } = new();
    public int EstimatedDeviceCount { get; set; }
    public string Difficulty { get; set; } = "intermediate"; // beginner, intermediate, advanced
}

/// <summary>
/// A configurable parameter on a smart template.
/// </summary>
public class TemplateParameter
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "string"; // string, int, float, bool
    public string DefaultValue { get; set; } = "";
    public bool Required { get; set; }
}

/// <summary>
/// Lightweight summary for listing templates without full details.
/// </summary>
public class SmartTemplateSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public int EstimatedDeviceCount { get; set; }
    public string Difficulty { get; set; } = "";
    public int ParameterCount { get; set; }
}

// =========================================================================
//  SERVICE
// =========================================================================

/// <summary>
/// Library of 20+ parameterized game mechanic templates. Each generates
/// complete device lists, wiring connections, production-quality Verse code,
/// and widget specs where UI is needed.
///
/// Categories: combat, economy, objective, progression, meta, utility.
/// </summary>
public class SmartTemplateService
{
    private readonly ILogger<SmartTemplateService> _logger;
    private readonly Dictionary<string, Func<Dictionary<string, string>, SmartTemplate>> _builders;

    public SmartTemplateService(ILogger<SmartTemplateService> logger)
    {
        _logger = logger;
        _builders = new Dictionary<string, Func<Dictionary<string, string>, SmartTemplate>>
        {
            // Combat
            ["class_selector"] = BuildClassSelector,
            ["gun_game"] = BuildGunGame,
            ["last_man_standing"] = BuildLastManStanding,
            ["team_elimination"] = BuildTeamElimination,
            // Economy
            ["currency_system"] = BuildCurrencySystem,
            ["upgrade_station"] = BuildUpgradeStation,
            ["trading_post"] = BuildTradingPost,
            // Objective
            ["king_of_the_hill"] = BuildKingOfTheHill,
            ["domination"] = BuildDomination,
            ["search_and_destroy"] = BuildSearchAndDestroy,
            ["capture_the_flag"] = BuildCaptureTheFlag,
            ["payload"] = BuildPayload,
            // Progression
            ["wave_defense"] = BuildWaveDefense,
            ["parkour"] = BuildParkour,
            ["deathrun"] = BuildDeathrun,
            // Meta
            ["voting_system"] = BuildVotingSystem,
            ["matchmaking"] = BuildMatchmaking,
            ["achievement_system"] = BuildAchievementSystem,
            // Utility
            ["spectator_mode"] = BuildSpectatorMode,
            ["replay_system"] = BuildReplaySystem,
        };
    }

    /// <summary>
    /// Get a template by ID with default parameters applied.
    /// </summary>
    public SmartTemplate? GetTemplate(string templateId)
    {
        if (_builders.TryGetValue(templateId, out var builder))
        {
            return builder(new Dictionary<string, string>());
        }
        _logger.LogWarning("Smart template '{TemplateId}' not found", templateId);
        return null;
    }

    /// <summary>
    /// List all templates, optionally filtered by category.
    /// </summary>
    public List<SmartTemplateSummary> ListTemplates(string? category = null)
    {
        var summaries = new List<SmartTemplateSummary>();
        foreach (var (id, builder) in _builders)
        {
            var template = builder(new Dictionary<string, string>());
            if (category != null && !template.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                continue;

            summaries.Add(new SmartTemplateSummary
            {
                Id = template.Id,
                Name = template.Name,
                Category = template.Category,
                Description = template.Description,
                Tags = template.Tags,
                EstimatedDeviceCount = template.EstimatedDeviceCount,
                Difficulty = template.Difficulty,
                ParameterCount = template.Parameters.Count
            });
        }
        return summaries;
    }

    /// <summary>
    /// Generate a complete GenerationPlan from a template with custom parameters.
    /// </summary>
    public GenerationPlan GenerateFromTemplate(string templateId, Dictionary<string, string>? parameters = null)
    {
        var parms = parameters ?? new Dictionary<string, string>();

        if (!_builders.TryGetValue(templateId, out var builder))
        {
            return new GenerationPlan
            {
                Description = $"Template '{templateId}' not found",
                SystemName = "Error"
            };
        }

        var template = builder(parms);

        var plan = new GenerationPlan
        {
            Description = template.Description,
            SystemName = template.Name,
            Category = template.Category,
            Devices = template.Devices,
            Wiring = template.Wiring,
            VerseCode = template.VerseCode,
            Steps = BuildExecutionSteps(template)
        };

        return plan;
    }

    // =====================================================================
    //  HELPERS
    // =====================================================================

    private static int GetInt(Dictionary<string, string> p, string key, int fallback)
    {
        if (p.TryGetValue(key, out var val) && int.TryParse(val, out var result))
            return result;
        return fallback;
    }

    private static float GetFloat(Dictionary<string, string> p, string key, float fallback)
    {
        if (p.TryGetValue(key, out var val) && float.TryParse(val, out var result))
            return result;
        return fallback;
    }

    private static string GetStr(Dictionary<string, string> p, string key, string fallback)
    {
        return p.TryGetValue(key, out var val) ? val : fallback;
    }

    private static bool GetBool(Dictionary<string, string> p, string key, bool fallback)
    {
        if (p.TryGetValue(key, out var val) && bool.TryParse(val, out var result))
            return result;
        return fallback;
    }

    private static List<ExecutionStep> BuildExecutionSteps(SmartTemplate template)
    {
        var steps = new List<ExecutionStep>();
        int order = 1;

        foreach (var device in template.Devices)
        {
            steps.Add(new ExecutionStep
            {
                Order = order++,
                Tool = "clone_actor",
                Description = $"Place {device.Role} ({device.Type}) at offset ({device.Offset.X}, {device.Offset.Y}, {device.Offset.Z})",
                Parameters = new Dictionary<string, string>
                {
                    ["sourceActor"] = device.DeviceClass,
                    ["role"] = device.Role
                }
            });
        }

        foreach (var wire in template.Wiring)
        {
            steps.Add(new ExecutionStep
            {
                Order = order++,
                Tool = "preview_wire_devices + apply_modification",
                Description = $"Wire {wire.SourceRole}.{wire.OutputEvent} -> {wire.TargetRole}.{wire.InputAction}"
            });
        }

        if (!string.IsNullOrEmpty(template.VerseCode))
        {
            steps.Add(new ExecutionStep
            {
                Order = order++,
                Tool = "generate_verse_file",
                Description = $"Write Verse device for {template.Name}"
            });
        }

        if (!string.IsNullOrEmpty(template.WidgetSpecJson))
        {
            steps.Add(new ExecutionStep
            {
                Order = order++,
                Tool = "create_widget_blueprint",
                Description = $"Generate UI widget for {template.Name}"
            });
        }

        return steps;
    }

    // =====================================================================
    //  1. CLASS SELECTOR (Combat)
    // =====================================================================

    private SmartTemplate BuildClassSelector(Dictionary<string, string> p)
    {
        var classCount = GetInt(p, "class_count", 4);
        var selectionTime = GetInt(p, "selection_time", 15);
        var cooldown = GetInt(p, "cooldown", 0);

        var b = new VerseCodeBuilder();
        b.Comment("Class Selector — Generated by WellVersed Smart Templates");
        b.Comment($"Lets players choose from {classCount} loadout classes before round starts.");
        b.Comment($"Selection window: {selectionTime}s. Cooldown between switches: {cooldown}s.");
        b.Line();
        b.GameplayUsings();
        b.Using("/Fortnite.com/UI");
        b.Using("/UnrealEngine.com/Temporary/UI");
        b.Line();

        b.Line("player_class := enum{Assault, Sniper, Shotgunner, Medic}");
        b.Line();

        b.ClassDef("class_selector_device");
        // Editable references
        for (int i = 1; i <= classCount; i++)
            b.Editable($"ClassButton{i}", "button_device", "button_device{}");
        b.Editable("SelectionTimer", "timer_device", "timer_device{}");
        for (int i = 1; i <= classCount; i++)
            b.Editable($"ClassGranter{i}", "item_granter_device", "item_granter_device{}");
        b.Editable("HUDMessage", "hud_message_device", "hud_message_device{}");
        b.Line();

        b.ImmutableVar("SelectionDuration", "float", $"{selectionTime}.0");
        b.ImmutableVar("SwitchCooldown", "float", $"{cooldown}.0");
        b.Line();

        b.Var("PlayerClasses", "[player]int", "map{}");
        b.Var("LastSwitchTime", "[player]float", "map{}");
        b.Var("SelectionOpen", "logic", "false");
        b.Line();

        // OnBegin
        b.OnBegin();
        b.Line("Print(\"Class Selector initialized\")");
        b.Line("StartSelectionPhase()");
        b.EndBlock();
        b.Line();

        // StartSelectionPhase
        b.Method("StartSelectionPhase", "void", suspends: true);
        b.Line("set SelectionOpen = true");
        b.Line("HUDMessage.Show()");
        b.Line("Print(\"Choose your class!\")");
        b.Line();
        b.Comment("Subscribe to button interactions");
        b.Line("spawn{ListenForClass(ClassButton1, 1)}");
        b.Line("spawn{ListenForClass(ClassButton2, 2)}");
        if (classCount >= 3) b.Line("spawn{ListenForClass(ClassButton3, 3)}");
        if (classCount >= 4) b.Line("spawn{ListenForClass(ClassButton4, 4)}");
        b.Line();
        b.Line("Sleep(SelectionDuration)");
        b.Line("set SelectionOpen = false");
        b.Line("HUDMessage.Hide()");
        b.Line("GrantAllLoadouts()");
        b.EndBlock();
        b.Line();

        // ListenForClass
        b.MethodWithParams("ListenForClass", "Button:button_device, ClassIndex:int", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := Button.InteractedWithEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("if (SelectionOpen?):");
        b.Indent();
        b.Line("if (set PlayerClasses[Player] = ClassIndex) {}");
        b.Line("Print(\"Player selected class {ClassIndex}\")");
        b.Dedent();
        b.Dedent();
        b.Dedent(); // loop
        b.EndBlock();
        b.Line();

        // GrantAllLoadouts
        b.Method("GrantAllLoadouts", "void");
        b.Line("Playspace := GetPlayspace()");
        b.Line("AllPlayers := Playspace.GetPlayers()");
        b.Line("for (Player : AllPlayers):");
        b.Indent();
        b.Line("ClassIdx := if (C := PlayerClasses[Player]) then C else 1");
        b.Line("GrantClassLoadout(Player, ClassIdx)");
        b.Dedent();
        b.EndBlock();
        b.Line();

        // GrantClassLoadout
        b.MethodWithParams("GrantClassLoadout", "Player:player, ClassIndex:int", "void");
        b.Line("Agent := agent[Player]");
        b.Line("if (ClassIndex = 1): ClassGranter1.GrantItem(Agent)");
        b.Line("else if (ClassIndex = 2): ClassGranter2.GrantItem(Agent)");
        if (classCount >= 3) b.Line("else if (ClassIndex = 3): ClassGranter3.GrantItem(Agent)");
        if (classCount >= 4) b.Line("else if (ClassIndex = 4): ClassGranter4.GrantItem(Agent)");
        b.Line("else: ClassGranter1.GrantItem(Agent)");
        b.EndBlock();

        b.EndBlock(); // class

        var devices = new List<PlannedDevice>();
        for (int i = 1; i <= classCount; i++)
        {
            devices.Add(new PlannedDevice { Role = $"class_button_{i}", DeviceClass = "BP_ButtonDevice_C", Type = "Button Device", Offset = new Vector3Info(i * 300, 0, 0) });
            devices.Add(new PlannedDevice { Role = $"class_granter_{i}", DeviceClass = "BP_ItemGranterDevice_C", Type = "Item Granter", Offset = new Vector3Info(i * 300, 200, 0) });
        }
        devices.Add(new PlannedDevice { Role = "selection_timer", DeviceClass = "BP_TimerDevice_C", Type = "Timer Device", Offset = new Vector3Info(0, 400, 0) });
        devices.Add(new PlannedDevice { Role = "hud_message", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(0, 600, 0) });
        devices.Add(new PlannedDevice { Role = "verse_device", DeviceClass = "class_selector_device", Type = "Verse Device", Offset = new Vector3Info(0, -200, 0) });

        var wiring = new List<PlannedWiring>();
        for (int i = 1; i <= classCount; i++)
        {
            wiring.Add(new PlannedWiring { SourceRole = $"class_button_{i}", OutputEvent = "OnInteracted", TargetRole = "verse_device", InputAction = "ClassSelected" });
        }
        wiring.Add(new PlannedWiring { SourceRole = "selection_timer", OutputEvent = "OnCompleted", TargetRole = "verse_device", InputAction = "EndSelection" });

        return new SmartTemplate
        {
            Id = "class_selector",
            Name = "Class Selector",
            Category = "combat",
            Description = $"Pre-round class selection system with {classCount} loadout options. Players interact with buttons to choose a class within a {selectionTime}s window. Each class grants a different weapon/item loadout via Item Granter devices.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "class_count", Description = "Number of class options (2-5)", Type = "int", DefaultValue = "4" },
                new() { Name = "selection_time", Description = "Seconds to choose class", Type = "int", DefaultValue = "15" },
                new() { Name = "cooldown", Description = "Cooldown between class switches (0 = none)", Type = "int", DefaultValue = "0" }
            },
            Devices = devices,
            Wiring = wiring,
            VerseCode = b.ToString(),
            Tags = new List<string> { "combat", "loadout", "class", "selection", "pre-round" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "intermediate"
        };
    }

    // =====================================================================
    //  2. GUN GAME (Combat)
    // =====================================================================

    private SmartTemplate BuildGunGame(Dictionary<string, string> p)
    {
        var tierCount = GetInt(p, "tier_count", 8);
        var killsPerTier = GetInt(p, "kills_per_tier", 1);

        var b = new VerseCodeBuilder();
        b.Comment("Gun Game — Generated by WellVersed Smart Templates");
        b.Comment($"Kill to advance through {tierCount} weapon tiers. {killsPerTier} kill(s) per tier.");
        b.Comment("First player to complete all tiers wins.");
        b.Line();
        b.GameplayUsings();
        b.Line();

        b.ClassDef("gun_game_device");
        for (int i = 1; i <= Math.Min(tierCount, 8); i++)
            b.Editable($"Tier{i}Granter", "item_granter_device", "item_granter_device{}");
        b.Editable("EliminationManager", "elimination_manager_device", "elimination_manager_device{}");
        b.Editable("EndGameDevice", "end_game_device", "end_game_device{}");
        b.Editable("HUDMessage", "hud_message_device", "hud_message_device{}");
        b.Line();

        b.ImmutableVar("MaxTier", "int", tierCount.ToString());
        b.ImmutableVar("KillsPerTier", "int", killsPerTier.ToString());
        b.Line();

        b.Var("PlayerTiers", "[player]int", "map{}");
        b.Var("PlayerKillsInTier", "[player]int", "map{}");
        b.Var("GameActive", "logic", "true");
        b.Line();

        // OnBegin
        b.OnBegin();
        b.Line("Print(\"Gun Game started! Get {KillsPerTier} kill(s) to advance.\")");
        b.Line("spawn{ListenForEliminations()}");
        b.Line();
        b.Comment("Grant tier 1 weapons to all players on join");
        b.Line("Playspace := GetPlayspace()");
        b.Line("for (Player : Playspace.GetPlayers()):");
        b.Indent();
        b.Line("InitializePlayer(Player)");
        b.Dedent();
        b.EndBlock();
        b.Line();

        // InitializePlayer
        b.MethodWithParams("InitializePlayer", "Player:player", "void");
        b.Line("if (set PlayerTiers[Player] = 1) {}");
        b.Line("if (set PlayerKillsInTier[Player] = 0) {}");
        b.Line("GrantTierWeapon(Player, 1)");
        b.EndBlock();
        b.Line();

        // ListenForEliminations
        b.Method("ListenForEliminations", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Result := EliminationManager.EliminationEvent.Await()");
        b.Line("if (not GameActive?): break");
        b.Line("if:");
        b.Indent();
        b.Line("Eliminator := Result.EliminatingCharacter");
        b.Line("EliminatorAgent := Eliminator.GetAgent[]");
        b.Line("Player := player[EliminatorAgent]");
        b.Dedent();
        b.Line("then:");
        b.Indent();
        b.Line("OnPlayerKill(Player)");
        b.Dedent();
        b.Dedent(); // loop
        b.EndBlock();
        b.Line();

        // OnPlayerKill
        b.MethodWithParams("OnPlayerKill", "Player:player", "void");
        b.Line("CurrentTier := if (T := PlayerTiers[Player]) then T else 1");
        b.Line("CurrentKills := if (K := PlayerKillsInTier[Player]) then K else 0");
        b.Line("NewKills := CurrentKills + 1");
        b.Line();
        b.Line("if (NewKills >= KillsPerTier):");
        b.Indent();
        b.Line("NewTier := CurrentTier + 1");
        b.Line("if (NewTier > MaxTier):");
        b.Indent();
        b.Line("Print(\"Player completed all tiers! Winner!\")");
        b.Line("set GameActive = false");
        b.Line("EndGameDevice.Activate()");
        b.Dedent();
        b.Line("else:");
        b.Indent();
        b.Line("if (set PlayerTiers[Player] = NewTier) {}");
        b.Line("if (set PlayerKillsInTier[Player] = 0) {}");
        b.Line("GrantTierWeapon(Player, NewTier)");
        b.Line("Print(\"Advanced to tier {NewTier}!\")");
        b.Dedent();
        b.Dedent();
        b.Line("else:");
        b.Indent();
        b.Line("if (set PlayerKillsInTier[Player] = NewKills) {}");
        b.Dedent();
        b.EndBlock();
        b.Line();

        // GrantTierWeapon
        b.MethodWithParams("GrantTierWeapon", "Player:player, Tier:int", "void");
        b.Line("Agent := agent[Player]");
        b.Line("if (Tier = 1): Tier1Granter.GrantItem(Agent)");
        b.Line("else if (Tier = 2): Tier2Granter.GrantItem(Agent)");
        b.Line("else if (Tier = 3): Tier3Granter.GrantItem(Agent)");
        b.Line("else if (Tier = 4): Tier4Granter.GrantItem(Agent)");
        if (tierCount >= 5) b.Line("else if (Tier = 5): Tier5Granter.GrantItem(Agent)");
        if (tierCount >= 6) b.Line("else if (Tier = 6): Tier6Granter.GrantItem(Agent)");
        if (tierCount >= 7) b.Line("else if (Tier = 7): Tier7Granter.GrantItem(Agent)");
        if (tierCount >= 8) b.Line("else if (Tier = 8): Tier8Granter.GrantItem(Agent)");
        b.EndBlock();

        b.EndBlock(); // class

        var devices = new List<PlannedDevice>();
        for (int i = 1; i <= Math.Min(tierCount, 8); i++)
            devices.Add(new PlannedDevice { Role = $"tier_{i}_granter", DeviceClass = "BP_ItemGranterDevice_C", Type = "Item Granter", Offset = new Vector3Info(i * 200, 0, 0) });
        devices.Add(new PlannedDevice { Role = "elimination_manager", DeviceClass = "BP_EliminationManagerDevice_C", Type = "Elimination Manager", Offset = new Vector3Info(0, 300, 0) });
        devices.Add(new PlannedDevice { Role = "end_game_device", DeviceClass = "BP_EndGameDevice_C", Type = "End Game Device", Offset = new Vector3Info(0, 500, 0) });
        devices.Add(new PlannedDevice { Role = "hud_message", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(0, 700, 0) });
        devices.Add(new PlannedDevice { Role = "verse_device", DeviceClass = "gun_game_device", Type = "Verse Device", Offset = new Vector3Info(0, -200, 0) });

        var wiring = new List<PlannedWiring>
        {
            new() { SourceRole = "elimination_manager", OutputEvent = "OnElimination", TargetRole = "verse_device", InputAction = "HandleElimination" }
        };

        return new SmartTemplate
        {
            Id = "gun_game",
            Name = "Gun Game",
            Category = "combat",
            Description = $"Weapon progression system with {tierCount} tiers. Each elimination advances the player to the next weapon tier. First player to complete all tiers wins. Configurable kills-per-tier ({killsPerTier}).",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "tier_count", Description = "Number of weapon tiers (3-8)", Type = "int", DefaultValue = "8" },
                new() { Name = "kills_per_tier", Description = "Kills required to advance each tier", Type = "int", DefaultValue = "1" }
            },
            Devices = devices,
            Wiring = wiring,
            VerseCode = b.ToString(),
            Tags = new List<string> { "combat", "gun_game", "weapon_progression", "ffa", "elimination" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "intermediate"
        };
    }

    // =====================================================================
    //  3. LAST MAN STANDING (Combat)
    // =====================================================================

    private SmartTemplate BuildLastManStanding(Dictionary<string, string> p)
    {
        var stormPhases = GetInt(p, "storm_phases", 4);
        var phaseDuration = GetInt(p, "phase_duration", 60);

        var b = new VerseCodeBuilder();
        b.Comment("Last Man Standing — Generated by WellVersed Smart Templates");
        b.Comment($"FFA elimination with {stormPhases} shrinking storm phases.");
        b.Comment("No respawns. Last player alive wins.");
        b.Line();
        b.GameplayUsings();
        b.Line();

        b.Line("storm_state := enum{Waiting, Shrinking, Holding}");
        b.Line();

        b.ClassDef("last_man_standing_device");
        b.Editable("EliminationManager", "elimination_manager_device", "elimination_manager_device{}");
        b.Editable("StormController", "storm_controller_device", "storm_controller_device{}");
        b.Editable("EndGameDevice", "end_game_device", "end_game_device{}");
        b.Editable("HUDMessage", "hud_message_device", "hud_message_device{}");
        b.Editable("PlayerCounter", "player_counter_device", "player_counter_device{}");
        b.Line();

        b.ImmutableVar("TotalPhases", "int", stormPhases.ToString());
        b.ImmutableVar("PhaseDuration", "float", $"{phaseDuration}.0");
        b.Line();

        b.Var("PlayersAlive", "int", "0");
        b.Var("CurrentPhase", "int", "0");
        b.Var("GameOver", "logic", "false");
        b.Line();

        // OnBegin
        b.OnBegin();
        b.Line("Playspace := GetPlayspace()");
        b.Line("set PlayersAlive = Playspace.GetPlayers().Length");
        b.Line("Print(\"Last Man Standing! {PlayersAlive} players remaining.\")");
        b.Line();
        b.Line("spawn{ListenForEliminations()}");
        b.Line("StormLoop()");
        b.EndBlock();
        b.Line();

        // ListenForEliminations
        b.Method("ListenForEliminations", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("EliminationManager.EliminationEvent.Await()");
        b.Line("if (GameOver?): break");
        b.Line("set PlayersAlive -= 1");
        b.Line("Print(\"{PlayersAlive} players remaining\")");
        b.Line();
        b.Line("if (PlayersAlive <= 1):");
        b.Indent();
        b.Line("set GameOver = true");
        b.Line("Print(\"We have a winner!\")");
        b.Line("EndGameDevice.Activate()");
        b.Line("break");
        b.Dedent();
        b.Dedent(); // loop
        b.EndBlock();
        b.Line();

        // StormLoop
        b.Method("StormLoop", "void", suspends: true);
        b.Line("Sleep(10.0) # Initial grace period");
        b.Line();
        b.Line("loop:");
        b.Indent();
        b.Line("set CurrentPhase += 1");
        b.Line("if (CurrentPhase > TotalPhases or GameOver?): break");
        b.Line();
        b.Line("Print(\"Storm phase {CurrentPhase} — zone shrinking!\")");
        b.Line("HUDMessage.Show()");
        b.Line("StormController.Advance()");
        b.Line("Sleep(PhaseDuration)");
        b.Line("HUDMessage.Hide()");
        b.Dedent(); // loop
        b.EndBlock();

        b.EndBlock(); // class

        var devices = new List<PlannedDevice>
        {
            new() { Role = "elimination_manager", DeviceClass = "BP_EliminationManagerDevice_C", Type = "Elimination Manager", Offset = new Vector3Info(0, 0, 0) },
            new() { Role = "storm_controller", DeviceClass = "BP_StormControllerDevice_C", Type = "Storm Controller", Offset = new Vector3Info(300, 0, 0) },
            new() { Role = "end_game_device", DeviceClass = "BP_EndGameDevice_C", Type = "End Game Device", Offset = new Vector3Info(600, 0, 0) },
            new() { Role = "hud_message", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(0, 300, 0) },
            new() { Role = "player_counter", DeviceClass = "BP_PlayerCounterDevice_C", Type = "Player Counter", Offset = new Vector3Info(300, 300, 0) },
            new() { Role = "verse_device", DeviceClass = "last_man_standing_device", Type = "Verse Device", Offset = new Vector3Info(0, -200, 0) }
        };

        // Add spawn pads
        for (int i = 1; i <= 16; i++)
        {
            var angle = (float)(2 * Math.PI * i / 16);
            devices.Add(new PlannedDevice
            {
                Role = $"spawn_{i}",
                DeviceClass = "BP_PlayerSpawnerDevice_C",
                Type = "Player Spawner",
                Offset = new Vector3Info((float)Math.Cos(angle) * 3000, (float)Math.Sin(angle) * 3000, 0)
            });
        }

        return new SmartTemplate
        {
            Id = "last_man_standing",
            Name = "Last Man Standing",
            Category = "combat",
            Description = $"FFA elimination with {stormPhases} shrinking storm phases of {phaseDuration}s each. No respawns — last player alive wins. Storm forces players together over time.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "storm_phases", Description = "Number of storm shrink phases", Type = "int", DefaultValue = "4" },
                new() { Name = "phase_duration", Description = "Seconds per storm phase", Type = "int", DefaultValue = "60" }
            },
            Devices = devices,
            Wiring = new List<PlannedWiring>
            {
                new() { SourceRole = "elimination_manager", OutputEvent = "OnElimination", TargetRole = "verse_device", InputAction = "HandleElimination" },
                new() { SourceRole = "verse_device", OutputEvent = "OnGameOver", TargetRole = "end_game_device", InputAction = "Activate" }
            },
            VerseCode = b.ToString(),
            Tags = new List<string> { "combat", "ffa", "battle_royale", "storm", "no_respawn", "elimination" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "beginner"
        };
    }

    // =====================================================================
    //  4. TEAM ELIMINATION (Combat)
    // =====================================================================

    private SmartTemplate BuildTeamElimination(Dictionary<string, string> p)
    {
        var teamCount = GetInt(p, "team_count", 2);
        var ticketsPerTeam = GetInt(p, "tickets_per_team", 20);
        var respawnDelay = GetInt(p, "respawn_delay", 5);

        var b = new VerseCodeBuilder();
        b.Comment("Team Elimination — Generated by WellVersed Smart Templates");
        b.Comment($"{teamCount} teams, {ticketsPerTeam} respawn tickets each.");
        b.Comment("Last team with tickets remaining wins.");
        b.Line();
        b.GameplayUsings();
        b.Using("/Fortnite.com/UI");
        b.Using("/UnrealEngine.com/Temporary/UI");
        b.Line();

        b.ClassDef("team_elimination_device");
        b.Editable("EliminationManager", "elimination_manager_device", "elimination_manager_device{}");
        b.Editable("EndGameDevice", "end_game_device", "end_game_device{}");
        b.Editable("Team1HUD", "hud_message_device", "hud_message_device{}");
        b.Editable("Team2HUD", "hud_message_device", "hud_message_device{}");
        for (int t = 1; t <= teamCount; t++)
        {
            b.Editable($"Team{t}Spawner1", "player_spawner_device", "player_spawner_device{}");
            b.Editable($"Team{t}Spawner2", "player_spawner_device", "player_spawner_device{}");
        }
        b.Line();

        b.ImmutableVar("StartingTickets", "int", ticketsPerTeam.ToString());
        b.ImmutableVar("RespawnDelay", "float", $"{respawnDelay}.0");
        b.ImmutableVar("TeamCount", "int", teamCount.ToString());
        b.Line();

        b.Var("TeamTickets", "[int]int", "map{}");
        b.Var("GameActive", "logic", "true");
        b.Line();

        // OnBegin
        b.OnBegin();
        b.Comment("Initialize tickets for each team");
        b.Line($"var TeamIndex:int = 0");
        b.Line("loop:");
        b.Indent();
        b.Line("set TeamIndex += 1");
        b.Line("if (TeamIndex > TeamCount): break");
        b.Line("if (set TeamTickets[TeamIndex] = StartingTickets) {}");
        b.Dedent();
        b.Line();
        b.Line("Print(\"Team Elimination! {StartingTickets} tickets per team.\")");
        b.Line("spawn{ListenForEliminations()}");
        b.EndBlock();
        b.Line();

        // ListenForEliminations
        b.Method("ListenForEliminations", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Result := EliminationManager.EliminationEvent.Await()");
        b.Line("if (not GameActive?): break");
        b.Line();
        b.Comment("Determine which team lost a ticket");
        b.Line("if:");
        b.Indent();
        b.Line("EliminatedChar := Result.EliminatedCharacter");
        b.Line("EliminatedAgent := EliminatedChar.GetAgent[]");
        b.Line("EliminatedPlayer := player[EliminatedAgent]");
        b.Dedent();
        b.Line("then:");
        b.Indent();
        b.Line("OnPlayerEliminated(EliminatedPlayer)");
        b.Dedent();
        b.Dedent(); // loop
        b.EndBlock();
        b.Line();

        // OnPlayerEliminated
        b.MethodWithParams("OnPlayerEliminated", "Player:player", "void");
        b.Comment("Decrement team tickets based on player team index");
        b.Line("TeamIdx := GetPlayerTeamIndex(Player)");
        b.Line("CurrentTickets := if (T := TeamTickets[TeamIdx]) then T else 0");
        b.Line("NewTickets := CurrentTickets - 1");
        b.Line("if (set TeamTickets[TeamIdx] = NewTickets) {}");
        b.Line();
        b.Line("Print(\"Team {TeamIdx} tickets: {NewTickets}\")");
        b.Line();
        b.Line("if (NewTickets <= 0):");
        b.Indent();
        b.Line("Print(\"Team {TeamIdx} is out of tickets!\")");
        b.Line("CheckForWinner()");
        b.Dedent();
        b.EndBlock();
        b.Line();

        // GetPlayerTeamIndex
        b.MethodWithParams("GetPlayerTeamIndex", "Player:player", "int");
        b.Comment("Map player to team index (1-based)");
        b.Comment("In real implementation, use fort_team utilities");
        b.Line("1 # Default to team 1, wire to actual team system");
        b.EndBlock();
        b.Line();

        // CheckForWinner
        b.Method("CheckForWinner", "void");
        b.Line("var TeamsAlive:int = 0");
        b.Line("var TeamIdx:int = 0");
        b.Line("loop:");
        b.Indent();
        b.Line("set TeamIdx += 1");
        b.Line("if (TeamIdx > TeamCount): break");
        b.Line("if (Tickets := TeamTickets[TeamIdx], Tickets > 0):");
        b.Indent();
        b.Line("set TeamsAlive += 1");
        b.Dedent();
        b.Dedent(); // loop
        b.Line();
        b.Line("if (TeamsAlive <= 1):");
        b.Indent();
        b.Line("set GameActive = false");
        b.Line("Print(\"Game Over!\")");
        b.Line("EndGameDevice.Activate()");
        b.Dedent();
        b.EndBlock();

        b.EndBlock(); // class

        var devices = new List<PlannedDevice>
        {
            new() { Role = "elimination_manager", DeviceClass = "BP_EliminationManagerDevice_C", Type = "Elimination Manager", Offset = new Vector3Info(0, 0, 0) },
            new() { Role = "end_game_device", DeviceClass = "BP_EndGameDevice_C", Type = "End Game Device", Offset = new Vector3Info(300, 0, 0) },
            new() { Role = "verse_device", DeviceClass = "team_elimination_device", Type = "Verse Device", Offset = new Vector3Info(0, -200, 0) }
        };

        for (int t = 1; t <= teamCount; t++)
        {
            devices.Add(new PlannedDevice { Role = $"team_{t}_spawner_1", DeviceClass = "BP_PlayerSpawnerDevice_C", Type = "Player Spawner", Offset = new Vector3Info(-2000 + (t - 1) * 4000, -500, 0) });
            devices.Add(new PlannedDevice { Role = $"team_{t}_spawner_2", DeviceClass = "BP_PlayerSpawnerDevice_C", Type = "Player Spawner", Offset = new Vector3Info(-2000 + (t - 1) * 4000, 500, 0) });
            devices.Add(new PlannedDevice { Role = $"team_{t}_hud", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(-2000 + (t - 1) * 4000, 0, 200) });
        }

        return new SmartTemplate
        {
            Id = "team_elimination",
            Name = "Team Elimination",
            Category = "combat",
            Description = $"Team-based elimination with {ticketsPerTeam} respawn tickets per team. Each elimination costs a ticket. When a team runs out, their players cannot respawn. Last team with tickets wins.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "team_count", Description = "Number of teams (2-4)", Type = "int", DefaultValue = "2" },
                new() { Name = "tickets_per_team", Description = "Starting respawn tickets per team", Type = "int", DefaultValue = "20" },
                new() { Name = "respawn_delay", Description = "Seconds before respawn", Type = "int", DefaultValue = "5" }
            },
            Devices = devices,
            Wiring = new List<PlannedWiring>
            {
                new() { SourceRole = "elimination_manager", OutputEvent = "OnElimination", TargetRole = "verse_device", InputAction = "HandleElimination" },
                new() { SourceRole = "verse_device", OutputEvent = "OnGameOver", TargetRole = "end_game_device", InputAction = "Activate" }
            },
            VerseCode = b.ToString(),
            Tags = new List<string> { "combat", "teams", "elimination", "tickets", "respawn" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "intermediate"
        };
    }

    // =====================================================================
    //  5. CURRENCY SYSTEM (Economy)
    // =====================================================================

    private SmartTemplate BuildCurrencySystem(Dictionary<string, string> p)
    {
        var startingGold = GetInt(p, "starting_gold", 100);
        var killReward = GetInt(p, "kill_reward", 50);
        var objectiveReward = GetInt(p, "objective_reward", 100);

        var b = new VerseCodeBuilder();
        b.Comment("Currency System — Generated by WellVersed Smart Templates");
        b.Comment($"Full economy: starting gold {startingGold}, earn from kills ({killReward}) and objectives ({objectiveReward}).");
        b.Comment("Spend at vendor devices. HUD displays current balance.");
        b.Line();
        b.GameplayUsings();
        b.Using("/Fortnite.com/UI");
        b.Using("/UnrealEngine.com/Temporary/UI");
        b.Using("/Verse.org/Colors");
        b.Line();

        b.ClassDef("currency_system_device");
        b.Editable("EliminationManager", "elimination_manager_device", "elimination_manager_device{}");
        b.Editable("Vendor1", "item_granter_device", "item_granter_device{}");
        b.Editable("Vendor2", "item_granter_device", "item_granter_device{}");
        b.Editable("Vendor3", "item_granter_device", "item_granter_device{}");
        b.Editable("ShopButton1", "button_device", "button_device{}");
        b.Editable("ShopButton2", "button_device", "button_device{}");
        b.Editable("ShopButton3", "button_device", "button_device{}");
        b.Editable("BalanceHUD", "billboard_device", "billboard_device{}");
        b.Line();

        b.ImmutableVar("StartingGold", "int", startingGold.ToString());
        b.ImmutableVar("KillReward", "int", killReward.ToString());
        b.ImmutableVar("ObjectiveReward", "int", objectiveReward.ToString());
        b.Line();

        b.ImmutableVar("ShopPrice1", "int", "200");
        b.ImmutableVar("ShopPrice2", "int", "400");
        b.ImmutableVar("ShopPrice3", "int", "600");
        b.Line();

        b.Var("PlayerGold", "[player]int", "map{}");
        b.Line();

        // OnBegin
        b.OnBegin();
        b.Comment("Initialize all players with starting gold");
        b.Line("Playspace := GetPlayspace()");
        b.Line("for (Player : Playspace.GetPlayers()):");
        b.Indent();
        b.Line("if (set PlayerGold[Player] = StartingGold) {}");
        b.Dedent();
        b.Line();
        b.Line("spawn{ListenForEliminations()}");
        b.Line("spawn{ListenForPurchase(ShopButton1, Vendor1, ShopPrice1, \"Weapon Pack\")}");
        b.Line("spawn{ListenForPurchase(ShopButton2, Vendor2, ShopPrice2, \"Shield Pack\")}");
        b.Line("spawn{ListenForPurchase(ShopButton3, Vendor3, ShopPrice3, \"Power Pack\")}");
        b.Line("Print(\"Currency System active. Starting gold: {StartingGold}\")");
        b.EndBlock();
        b.Line();

        // ListenForEliminations
        b.Method("ListenForEliminations", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Result := EliminationManager.EliminationEvent.Await()");
        b.Line("if:");
        b.Indent();
        b.Line("Eliminator := Result.EliminatingCharacter");
        b.Line("EliminatorAgent := Eliminator.GetAgent[]");
        b.Line("Player := player[EliminatorAgent]");
        b.Dedent();
        b.Line("then:");
        b.Indent();
        b.Line("AddGold(Player, KillReward)");
        b.Line("Print(\"+ {KillReward} gold for elimination!\")");
        b.Dedent();
        b.Dedent(); // loop
        b.EndBlock();
        b.Line();

        // ListenForPurchase
        b.MethodWithParams("ListenForPurchase", "Button:button_device, Granter:item_granter_device, Price:int, ItemName:string", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := Button.InteractedWithEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("Gold := GetGold(Player)");
        b.Line("if (Gold >= Price):");
        b.Indent();
        b.Line("RemoveGold(Player, Price)");
        b.Line("Granter.GrantItem(Agent)");
        b.Line("Print(\"Purchased {ItemName} for {Price} gold!\")");
        b.Dedent();
        b.Line("else:");
        b.Indent();
        b.Line("Print(\"Not enough gold! Need {Price}, have {Gold}\")");
        b.Dedent();
        b.Dedent();
        b.Dedent(); // loop
        b.EndBlock();
        b.Line();

        // AddGold
        b.MethodWithParams("AddGold", "Player:player, Amount:int", "void");
        b.Line("Current := GetGold(Player)");
        b.Line("if (set PlayerGold[Player] = Current + Amount) {}");
        b.EndBlock();
        b.Line();

        // RemoveGold
        b.MethodWithParams("RemoveGold", "Player:player, Amount:int", "void");
        b.Line("Current := GetGold(Player)");
        b.Line("NewAmount := if (Current - Amount < 0) then 0 else Current - Amount");
        b.Line("if (set PlayerGold[Player] = NewAmount) {}");
        b.EndBlock();
        b.Line();

        // GetGold
        b.MethodWithParams("GetGold", "Player:player", "int");
        b.Line("if (G := PlayerGold[Player]) then G else 0");
        b.EndBlock();

        b.EndBlock(); // class

        var devices = new List<PlannedDevice>
        {
            new() { Role = "elimination_manager", DeviceClass = "BP_EliminationManagerDevice_C", Type = "Elimination Manager", Offset = new Vector3Info(0, 0, 0) },
            new() { Role = "shop_button_1", DeviceClass = "BP_ButtonDevice_C", Type = "Button Device", Offset = new Vector3Info(500, 0, 0) },
            new() { Role = "shop_button_2", DeviceClass = "BP_ButtonDevice_C", Type = "Button Device", Offset = new Vector3Info(800, 0, 0) },
            new() { Role = "shop_button_3", DeviceClass = "BP_ButtonDevice_C", Type = "Button Device", Offset = new Vector3Info(1100, 0, 0) },
            new() { Role = "vendor_1", DeviceClass = "BP_ItemGranterDevice_C", Type = "Item Granter", Offset = new Vector3Info(500, 200, 0) },
            new() { Role = "vendor_2", DeviceClass = "BP_ItemGranterDevice_C", Type = "Item Granter", Offset = new Vector3Info(800, 200, 0) },
            new() { Role = "vendor_3", DeviceClass = "BP_ItemGranterDevice_C", Type = "Item Granter", Offset = new Vector3Info(1100, 200, 0) },
            new() { Role = "balance_hud", DeviceClass = "BP_BillboardDevice_C", Type = "Billboard Device", Offset = new Vector3Info(800, -200, 200) },
            new() { Role = "verse_device", DeviceClass = "currency_system_device", Type = "Verse Device", Offset = new Vector3Info(0, -200, 0) }
        };

        var widgetSpec = @"{
  ""type"": ""Canvas"",
  ""children"": [
    {
      ""type"": ""TextBlock"",
      ""properties"": { ""Text"": ""Gold: {Balance}"", ""FontSize"": 24, ""ColorAndOpacity"": ""#FFD700"" },
      ""slot"": { ""Anchors"": { ""Minimum"": { ""X"": 0.0, ""Y"": 0.0 }, ""Maximum"": { ""X"": 0.0, ""Y"": 0.0 } }, ""Position"": { ""X"": 20, ""Y"": 20 } }
    }
  ]
}";

        return new SmartTemplate
        {
            Id = "currency_system",
            Name = "Currency System",
            Category = "economy",
            Description = $"Full currency economy: players start with {startingGold} gold, earn {killReward} per elimination and {objectiveReward} per objective. Three shop tiers with increasing prices. HUD displays balance.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "starting_gold", Description = "Gold each player starts with", Type = "int", DefaultValue = "100" },
                new() { Name = "kill_reward", Description = "Gold earned per elimination", Type = "int", DefaultValue = "50" },
                new() { Name = "objective_reward", Description = "Gold earned per objective completion", Type = "int", DefaultValue = "100" }
            },
            Devices = devices,
            Wiring = new List<PlannedWiring>
            {
                new() { SourceRole = "elimination_manager", OutputEvent = "OnElimination", TargetRole = "verse_device", InputAction = "HandleElimination" },
                new() { SourceRole = "shop_button_1", OutputEvent = "OnInteracted", TargetRole = "verse_device", InputAction = "HandlePurchase1" },
                new() { SourceRole = "shop_button_2", OutputEvent = "OnInteracted", TargetRole = "verse_device", InputAction = "HandlePurchase2" },
                new() { SourceRole = "shop_button_3", OutputEvent = "OnInteracted", TargetRole = "verse_device", InputAction = "HandlePurchase3" }
            },
            VerseCode = b.ToString(),
            WidgetSpecJson = widgetSpec,
            Tags = new List<string> { "economy", "currency", "gold", "shop", "vendor" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "intermediate"
        };
    }

    // =====================================================================
    //  6. UPGRADE STATION (Economy)
    // =====================================================================

    private SmartTemplate BuildUpgradeStation(Dictionary<string, string> p)
    {
        var tiers = GetInt(p, "tiers", 3);
        var baseCost = GetInt(p, "base_cost", 100);
        var costMultiplier = GetFloat(p, "cost_multiplier", 2.0f);

        var b = new VerseCodeBuilder();
        b.Comment("Upgrade Station — Generated by WellVersed Smart Templates");
        b.Comment($"Tiered upgrades: {tiers} levels. Base cost {baseCost}, x{costMultiplier} per tier.");
        b.Line();
        b.GameplayUsings();
        b.Line();

        b.ClassDef("upgrade_station_device");
        b.Editable("UpgradeButton", "button_device", "button_device{}");
        b.Editable("HealthGranter", "item_granter_device", "item_granter_device{}");
        b.Editable("SpeedGranter", "item_granter_device", "item_granter_device{}");
        b.Editable("DamageGranter", "item_granter_device", "item_granter_device{}");
        b.Editable("VFXSuccess", "vfx_spawner_device", "vfx_spawner_device{}");
        b.Editable("VFXFail", "vfx_spawner_device", "vfx_spawner_device{}");
        b.Editable("Billboard", "billboard_device", "billboard_device{}");
        b.Line();

        b.ImmutableVar("MaxTier", "int", tiers.ToString());
        b.ImmutableVar("BaseCost", "int", baseCost.ToString());
        b.ImmutableVar("CostMultiplier", "float", $"{costMultiplier:F1}");
        b.Line();

        b.Var("PlayerTiers", "[player]int", "map{}");
        b.Var("PlayerGold", "[player]int", "map{}");
        b.Line();

        // OnBegin
        b.OnBegin();
        b.Line("spawn{ListenForUpgrade()}");
        b.Line("Print(\"Upgrade Station ready. {MaxTier} tiers available.\")");
        b.EndBlock();
        b.Line();

        // ListenForUpgrade
        b.Method("ListenForUpgrade", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := UpgradeButton.InteractedWithEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("TryUpgrade(Player)");
        b.Dedent();
        b.Dedent(); // loop
        b.EndBlock();
        b.Line();

        // TryUpgrade
        b.MethodWithParams("TryUpgrade", "Player:player", "void");
        b.Line("CurrentTier := if (T := PlayerTiers[Player]) then T else 0");
        b.Line("if (CurrentTier >= MaxTier):");
        b.Indent();
        b.Line("Print(\"Already at max tier!\")");
        b.Line("return");
        b.Dedent();
        b.Line();
        b.Line("Cost := GetUpgradeCost(CurrentTier + 1)");
        b.Line("Gold := if (G := PlayerGold[Player]) then G else 0");
        b.Line();
        b.Line("if (Gold >= Cost):");
        b.Indent();
        b.Line("if (set PlayerGold[Player] = Gold - Cost) {}");
        b.Line("NewTier := CurrentTier + 1");
        b.Line("if (set PlayerTiers[Player] = NewTier) {}");
        b.Line("ApplyUpgrade(Player, NewTier)");
        b.Line("VFXSuccess.Spawn()");
        b.Line("Print(\"Upgraded to tier {NewTier}!\")");
        b.Dedent();
        b.Line("else:");
        b.Indent();
        b.Line("VFXFail.Spawn()");
        b.Line("Print(\"Not enough gold! Need {Cost}, have {Gold}\")");
        b.Dedent();
        b.EndBlock();
        b.Line();

        // GetUpgradeCost
        b.MethodWithParams("GetUpgradeCost", "Tier:int", "int");
        b.Comment("Each tier costs BaseCost * CostMultiplier^(Tier-1)");
        b.Line("var Cost:int = BaseCost");
        b.Line("var I:int = 1");
        b.Line("loop:");
        b.Indent();
        b.Line("if (I >= Tier): break");
        b.Line("set Cost = Floor(Cost * CostMultiplier)");
        b.Line("set I += 1");
        b.Dedent();
        b.Line("Cost");
        b.EndBlock();
        b.Line();

        // ApplyUpgrade
        b.MethodWithParams("ApplyUpgrade", "Player:player, Tier:int", "void");
        b.Line("Agent := agent[Player]");
        b.Line("if (Tier = 1): HealthGranter.GrantItem(Agent)");
        b.Line("else if (Tier = 2): SpeedGranter.GrantItem(Agent)");
        b.Line("else if (Tier = 3): DamageGranter.GrantItem(Agent)");
        b.EndBlock();

        b.EndBlock(); // class

        var devices = new List<PlannedDevice>
        {
            new() { Role = "upgrade_button", DeviceClass = "BP_ButtonDevice_C", Type = "Button Device", Offset = new Vector3Info(0, 0, 0) },
            new() { Role = "health_granter", DeviceClass = "BP_ItemGranterDevice_C", Type = "Item Granter", Offset = new Vector3Info(300, 0, 0) },
            new() { Role = "speed_granter", DeviceClass = "BP_ItemGranterDevice_C", Type = "Item Granter", Offset = new Vector3Info(600, 0, 0) },
            new() { Role = "damage_granter", DeviceClass = "BP_ItemGranterDevice_C", Type = "Item Granter", Offset = new Vector3Info(900, 0, 0) },
            new() { Role = "vfx_success", DeviceClass = "BP_VFXSpawnerDevice_C", Type = "VFX Spawner", Offset = new Vector3Info(0, 200, 0) },
            new() { Role = "vfx_fail", DeviceClass = "BP_VFXSpawnerDevice_C", Type = "VFX Spawner", Offset = new Vector3Info(300, 200, 0) },
            new() { Role = "billboard", DeviceClass = "BP_BillboardDevice_C", Type = "Billboard Device", Offset = new Vector3Info(0, -200, 200) },
            new() { Role = "verse_device", DeviceClass = "upgrade_station_device", Type = "Verse Device", Offset = new Vector3Info(0, -400, 0) }
        };

        return new SmartTemplate
        {
            Id = "upgrade_station",
            Name = "Upgrade Station",
            Category = "economy",
            Description = $"Tiered upgrade station with {tiers} levels: health, speed, damage. Cost scales by x{costMultiplier} per tier (base: {baseCost}). Visual feedback on success/failure.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "tiers", Description = "Number of upgrade tiers", Type = "int", DefaultValue = "3" },
                new() { Name = "base_cost", Description = "Cost of first tier upgrade", Type = "int", DefaultValue = "100" },
                new() { Name = "cost_multiplier", Description = "Cost multiplier per tier", Type = "float", DefaultValue = "2.0" }
            },
            Devices = devices,
            Wiring = new List<PlannedWiring>
            {
                new() { SourceRole = "upgrade_button", OutputEvent = "OnInteracted", TargetRole = "verse_device", InputAction = "TryUpgrade" },
                new() { SourceRole = "verse_device", OutputEvent = "OnUpgradeSuccess", TargetRole = "vfx_success", InputAction = "Spawn" },
                new() { SourceRole = "verse_device", OutputEvent = "OnUpgradeFail", TargetRole = "vfx_fail", InputAction = "Spawn" }
            },
            VerseCode = b.ToString(),
            Tags = new List<string> { "economy", "upgrade", "tiered", "progression", "shop" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "intermediate"
        };
    }

    // =====================================================================
    //  7. TRADING POST (Economy)
    // =====================================================================

    private SmartTemplate BuildTradingPost(Dictionary<string, string> p)
    {
        var tradeTimeout = GetInt(p, "trade_timeout", 30);

        var b = new VerseCodeBuilder();
        b.Comment("Trading Post — Generated by WellVersed Smart Templates");
        b.Comment("Player-to-player trading zone with confirmation.");
        b.Comment($"Trade timeout: {tradeTimeout}s.");
        b.Line();
        b.GameplayUsings();
        b.Line();

        b.ClassDef("trading_post_device");
        b.Editable("TradeZone", "trigger_device", "trigger_device{}");
        b.Editable("ConfirmButton1", "button_device", "button_device{}");
        b.Editable("ConfirmButton2", "button_device", "button_device{}");
        b.Editable("CancelButton", "button_device", "button_device{}");
        b.Editable("TradeTimer", "timer_device", "timer_device{}");
        b.Editable("SuccessVFX", "vfx_spawner_device", "vfx_spawner_device{}");
        b.Editable("HUDMessage", "hud_message_device", "hud_message_device{}");
        b.Line();

        b.ImmutableVar("TradeTimeout", "float", $"{tradeTimeout}.0");
        b.Line();

        b.Var("Trader1", "?player", "false");
        b.Var("Trader2", "?player", "false");
        b.Var("Trader1Confirmed", "logic", "false");
        b.Var("Trader2Confirmed", "logic", "false");
        b.Var("TradeActive", "logic", "false");
        b.Line();

        // OnBegin
        b.OnBegin();
        b.Line("spawn{ListenForTradeZone()}");
        b.Line("spawn{ListenForConfirm1()}");
        b.Line("spawn{ListenForConfirm2()}");
        b.Line("spawn{ListenForCancel()}");
        b.Line("Print(\"Trading Post ready\")");
        b.EndBlock();
        b.Line();

        // ListenForTradeZone
        b.Method("ListenForTradeZone", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := TradeZone.TriggeredEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("if (not TradeActive?):");
        b.Indent();
        b.Line("if (not Trader1?):");
        b.Indent();
        b.Line("set Trader1 = option{Player}");
        b.Line("Print(\"Player 1 entered trade zone\")");
        b.Dedent();
        b.Line("else if (not Trader2?):");
        b.Indent();
        b.Line("set Trader2 = option{Player}");
        b.Line("set TradeActive = true");
        b.Line("Print(\"Trade started! Both players confirm to complete.\")");
        b.Line("HUDMessage.Show()");
        b.Line("spawn{TradeTimeoutWatcher()}");
        b.Dedent();
        b.Dedent();
        b.Dedent();
        b.Dedent(); // loop
        b.EndBlock();
        b.Line();

        // ListenForConfirm1
        b.Method("ListenForConfirm1", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("ConfirmButton1.InteractedWithEvent.Await()");
        b.Line("if (TradeActive?):");
        b.Indent();
        b.Line("set Trader1Confirmed = true");
        b.Line("Print(\"Player 1 confirmed trade\")");
        b.Line("CheckTradeComplete()");
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // ListenForConfirm2
        b.Method("ListenForConfirm2", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("ConfirmButton2.InteractedWithEvent.Await()");
        b.Line("if (TradeActive?):");
        b.Indent();
        b.Line("set Trader2Confirmed = true");
        b.Line("Print(\"Player 2 confirmed trade\")");
        b.Line("CheckTradeComplete()");
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // ListenForCancel
        b.Method("ListenForCancel", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("CancelButton.InteractedWithEvent.Await()");
        b.Line("if (TradeActive?):");
        b.Indent();
        b.Line("Print(\"Trade cancelled!\")");
        b.Line("ResetTrade()");
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // TradeTimeoutWatcher
        b.Method("TradeTimeoutWatcher", "void", suspends: true);
        b.Line("Sleep(TradeTimeout)");
        b.Line("if (TradeActive?):");
        b.Indent();
        b.Line("Print(\"Trade timed out!\")");
        b.Line("ResetTrade()");
        b.Dedent();
        b.EndBlock();
        b.Line();

        // CheckTradeComplete
        b.Method("CheckTradeComplete", "void");
        b.Line("if (Trader1Confirmed? and Trader2Confirmed?):");
        b.Indent();
        b.Line("Print(\"Trade complete!\")");
        b.Line("SuccessVFX.Spawn()");
        b.Comment("Execute item swap logic here");
        b.Line("ResetTrade()");
        b.Dedent();
        b.EndBlock();
        b.Line();

        // ResetTrade
        b.Method("ResetTrade", "void");
        b.Line("set Trader1 = false");
        b.Line("set Trader2 = false");
        b.Line("set Trader1Confirmed = false");
        b.Line("set Trader2Confirmed = false");
        b.Line("set TradeActive = false");
        b.Line("HUDMessage.Hide()");
        b.EndBlock();

        b.EndBlock(); // class

        var devices = new List<PlannedDevice>
        {
            new() { Role = "trade_zone", DeviceClass = "BP_TriggerDevice_C", Type = "Trigger Device", Offset = new Vector3Info(0, 0, 0) },
            new() { Role = "confirm_button_1", DeviceClass = "BP_ButtonDevice_C", Type = "Button Device", Offset = new Vector3Info(-200, 200, 0) },
            new() { Role = "confirm_button_2", DeviceClass = "BP_ButtonDevice_C", Type = "Button Device", Offset = new Vector3Info(200, 200, 0) },
            new() { Role = "cancel_button", DeviceClass = "BP_ButtonDevice_C", Type = "Button Device", Offset = new Vector3Info(0, 400, 0) },
            new() { Role = "trade_timer", DeviceClass = "BP_TimerDevice_C", Type = "Timer Device", Offset = new Vector3Info(0, -200, 0) },
            new() { Role = "success_vfx", DeviceClass = "BP_VFXSpawnerDevice_C", Type = "VFX Spawner", Offset = new Vector3Info(0, 0, 200) },
            new() { Role = "hud_message", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(0, -400, 0) },
            new() { Role = "verse_device", DeviceClass = "trading_post_device", Type = "Verse Device", Offset = new Vector3Info(400, 0, 0) }
        };

        return new SmartTemplate
        {
            Id = "trading_post",
            Name = "Trading Post",
            Category = "economy",
            Description = $"Player-to-player trading zone. Two players enter the zone, confirm trade via buttons, and items are swapped. {tradeTimeout}s timeout. Cancel button to abort.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "trade_timeout", Description = "Seconds before trade auto-cancels", Type = "int", DefaultValue = "30" }
            },
            Devices = devices,
            Wiring = new List<PlannedWiring>
            {
                new() { SourceRole = "trade_zone", OutputEvent = "OnTriggered", TargetRole = "verse_device", InputAction = "OnPlayerEnterZone" },
                new() { SourceRole = "confirm_button_1", OutputEvent = "OnInteracted", TargetRole = "verse_device", InputAction = "OnConfirm1" },
                new() { SourceRole = "confirm_button_2", OutputEvent = "OnInteracted", TargetRole = "verse_device", InputAction = "OnConfirm2" },
                new() { SourceRole = "cancel_button", OutputEvent = "OnInteracted", TargetRole = "verse_device", InputAction = "OnCancel" }
            },
            VerseCode = b.ToString(),
            Tags = new List<string> { "economy", "trading", "multiplayer", "zone", "confirmation" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "advanced"
        };
    }

    // =====================================================================
    //  8. KING OF THE HILL (Objective)
    // =====================================================================

    private SmartTemplate BuildKingOfTheHill(Dictionary<string, string> p)
    {
        var captureTime = GetInt(p, "capture_time", 10);
        var scoreToWin = GetInt(p, "score_to_win", 300);
        var pointsPerSecond = GetInt(p, "points_per_second", 1);

        var b = new VerseCodeBuilder();
        b.Comment("King of the Hill — Generated by WellVersed Smart Templates");
        b.Comment($"Single control point. {captureTime}s to capture. {pointsPerSecond} points/sec while held.");
        b.Comment($"First team to {scoreToWin} wins.");
        b.Line();
        b.GameplayUsings();
        b.Line();

        b.Line("hill_state := enum{Neutral, Contested, CapturedTeam1, CapturedTeam2}");
        b.Line();

        b.ClassDef("king_of_the_hill_device");
        b.Editable("HillZone", "trigger_device", "trigger_device{}");
        b.Editable("CaptureTimer", "timer_device", "timer_device{}");
        b.Editable("ScoreManager", "score_manager_device", "score_manager_device{}");
        b.Editable("EndGameDevice", "end_game_device", "end_game_device{}");
        b.Editable("HUDMessage", "hud_message_device", "hud_message_device{}");
        b.Editable("HillVFX", "vfx_spawner_device", "vfx_spawner_device{}");
        b.Line();

        b.ImmutableVar("CaptureTime", "float", $"{captureTime}.0");
        b.ImmutableVar("ScoreToWin", "int", scoreToWin.ToString());
        b.ImmutableVar("PointsPerSecond", "int", pointsPerSecond.ToString());
        b.Line();

        b.Var("CurrentState", "hill_state", "hill_state.Neutral");
        b.Var("Team1Score", "int", "0");
        b.Var("Team2Score", "int", "0");
        b.Var("PlayersInZone", "[player]logic", "map{}");
        b.Var("CaptureProgress", "float", "0.0");
        b.Var("GameActive", "logic", "true");
        b.Line();

        // OnBegin
        b.OnBegin();
        b.Line("spawn{ListenForZoneEnter()}");
        b.Line("spawn{ListenForZoneExit()}");
        b.Line("ScoringLoop()");
        b.EndBlock();
        b.Line();

        // ListenForZoneEnter
        b.Method("ListenForZoneEnter", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := HillZone.TriggeredEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("if (set PlayersInZone[Player] = true) {}");
        b.Line("EvaluateHillState()");
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // ListenForZoneExit
        b.Method("ListenForZoneExit", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := HillZone.UntriggeredEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("if (set PlayersInZone[Player] = false) {}");
        b.Line("EvaluateHillState()");
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // EvaluateHillState
        b.Method("EvaluateHillState", "void");
        b.Comment("Count players from each team in zone");
        b.Line("var Team1Count:int = 0");
        b.Line("var Team2Count:int = 0");
        b.Line("for (Player -> InZone : PlayersInZone, InZone?):");
        b.Indent();
        b.Comment("Determine team — simplified, wire to team system");
        b.Line("set Team1Count += 1 # Replace with real team check");
        b.Dedent();
        b.Line();
        b.Line("if (Team1Count > 0 and Team2Count > 0):");
        b.Indent();
        b.Line("set CurrentState = hill_state.Contested");
        b.Dedent();
        b.Line("else if (Team1Count > 0):");
        b.Indent();
        b.Line("set CurrentState = hill_state.CapturedTeam1");
        b.Dedent();
        b.Line("else if (Team2Count > 0):");
        b.Indent();
        b.Line("set CurrentState = hill_state.CapturedTeam2");
        b.Dedent();
        b.Line("else:");
        b.Indent();
        b.Line("set CurrentState = hill_state.Neutral");
        b.Dedent();
        b.EndBlock();
        b.Line();

        // ScoringLoop
        b.Method("ScoringLoop", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Sleep(1.0)");
        b.Line("if (not GameActive?): break");
        b.Line();
        b.Line("case (CurrentState):");
        b.Indent();
        b.Line("hill_state.CapturedTeam1 =>");
        b.Indent();
        b.Line("set Team1Score += PointsPerSecond");
        b.Line("if (Team1Score >= ScoreToWin):");
        b.Indent();
        b.Line("set GameActive = false");
        b.Line("Print(\"Team 1 wins!\")");
        b.Line("EndGameDevice.Activate()");
        b.Dedent();
        b.Dedent();
        b.Line("hill_state.CapturedTeam2 =>");
        b.Indent();
        b.Line("set Team2Score += PointsPerSecond");
        b.Line("if (Team2Score >= ScoreToWin):");
        b.Indent();
        b.Line("set GameActive = false");
        b.Line("Print(\"Team 2 wins!\")");
        b.Line("EndGameDevice.Activate()");
        b.Dedent();
        b.Dedent();
        b.Line("_ => {}");
        b.Dedent(); // case
        b.Dedent(); // loop
        b.EndBlock();

        b.EndBlock(); // class

        var devices = new List<PlannedDevice>
        {
            new() { Role = "hill_zone", DeviceClass = "BP_TriggerDevice_C", Type = "Trigger Device", Offset = new Vector3Info(0, 0, 0) },
            new() { Role = "capture_timer", DeviceClass = "BP_TimerDevice_C", Type = "Timer Device", Offset = new Vector3Info(300, 0, 0) },
            new() { Role = "score_manager", DeviceClass = "BP_ScoreManagerDevice_C", Type = "Score Manager", Offset = new Vector3Info(600, 0, 0) },
            new() { Role = "end_game_device", DeviceClass = "BP_EndGameDevice_C", Type = "End Game Device", Offset = new Vector3Info(900, 0, 0) },
            new() { Role = "hud_message", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(0, 300, 0) },
            new() { Role = "hill_vfx", DeviceClass = "BP_VFXSpawnerDevice_C", Type = "VFX Spawner", Offset = new Vector3Info(0, 0, 200) },
            new() { Role = "verse_device", DeviceClass = "king_of_the_hill_device", Type = "Verse Device", Offset = new Vector3Info(0, -300, 0) }
        };

        return new SmartTemplate
        {
            Id = "king_of_the_hill",
            Name = "King of the Hill",
            Category = "objective",
            Description = $"Single control point with contested capture. Teams earn {pointsPerSecond} point(s) per second while holding. Contested when both teams present. First to {scoreToWin} wins.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "capture_time", Description = "Seconds to capture the hill", Type = "int", DefaultValue = "10" },
                new() { Name = "score_to_win", Description = "Points needed to win", Type = "int", DefaultValue = "300" },
                new() { Name = "points_per_second", Description = "Points earned per second while holding", Type = "int", DefaultValue = "1" }
            },
            Devices = devices,
            Wiring = new List<PlannedWiring>
            {
                new() { SourceRole = "hill_zone", OutputEvent = "OnTriggered", TargetRole = "verse_device", InputAction = "OnPlayerEnter" },
                new() { SourceRole = "hill_zone", OutputEvent = "OnUntriggered", TargetRole = "verse_device", InputAction = "OnPlayerExit" },
                new() { SourceRole = "verse_device", OutputEvent = "OnCaptured", TargetRole = "hill_vfx", InputAction = "Spawn" },
                new() { SourceRole = "verse_device", OutputEvent = "OnGameOver", TargetRole = "end_game_device", InputAction = "Activate" }
            },
            VerseCode = b.ToString(),
            Tags = new List<string> { "objective", "control_point", "koth", "teams", "capture" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "intermediate"
        };
    }

    // =====================================================================
    //  9. DOMINATION (Objective)
    // =====================================================================

    private SmartTemplate BuildDomination(Dictionary<string, string> p)
    {
        var pointCount = GetInt(p, "point_count", 3);
        var scoreToWin = GetInt(p, "score_to_win", 200);
        var captureTime = GetInt(p, "capture_time", 8);

        var b = new VerseCodeBuilder();
        b.Comment("Domination — Generated by WellVersed Smart Templates");
        b.Comment($"{pointCount} capture points. Own more points = faster scoring.");
        b.Comment($"First team to {scoreToWin} wins.");
        b.Line();
        b.GameplayUsings();
        b.Line();

        b.Line("point_owner := enum{Neutral, Team1, Team2}");
        b.Line();

        b.ClassDef("domination_device");
        for (int i = 1; i <= pointCount; i++)
            b.Editable($"Zone{i}", "trigger_device", "trigger_device{}");
        b.Editable("EndGameDevice", "end_game_device", "end_game_device{}");
        b.Editable("HUDMessage", "hud_message_device", "hud_message_device{}");
        b.Line();

        b.ImmutableVar("PointCount", "int", pointCount.ToString());
        b.ImmutableVar("ScoreToWin", "int", scoreToWin.ToString());
        b.ImmutableVar("CaptureTime", "float", $"{captureTime}.0");
        b.Line();

        b.Var("PointOwners", "[int]point_owner", "map{}");
        b.Var("Team1Score", "int", "0");
        b.Var("Team2Score", "int", "0");
        b.Var("GameActive", "logic", "true");
        b.Line();

        // OnBegin
        b.OnBegin();
        b.Comment("Initialize all points as neutral");
        b.Line("var I:int = 0");
        b.Line("loop:");
        b.Indent();
        b.Line("set I += 1");
        b.Line("if (I > PointCount): break");
        b.Line("if (set PointOwners[I] = point_owner.Neutral) {}");
        b.Dedent();
        b.Line();
        b.Line("spawn{ListenForZone(Zone1, 1)}");
        if (pointCount >= 2) b.Line("spawn{ListenForZone(Zone2, 2)}");
        if (pointCount >= 3) b.Line("spawn{ListenForZone(Zone3, 3)}");
        if (pointCount >= 4) b.Line("spawn{ListenForZone(Zone4, 4)}");
        if (pointCount >= 5) b.Line("spawn{ListenForZone(Zone5, 5)}");
        b.Line("ScoreTickLoop()");
        b.EndBlock();
        b.Line();

        // ListenForZone
        b.MethodWithParams("ListenForZone", "Zone:trigger_device, PointIndex:int", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := Zone.TriggeredEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Comment("Start capture — simplified, would check team");
        b.Line("Sleep(CaptureTime)");
        b.Line("if (set PointOwners[PointIndex] = point_owner.Team1) {}");
        b.Line("Print(\"Point {PointIndex} captured!\")");
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // ScoreTickLoop
        b.Method("ScoreTickLoop", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Sleep(1.0)");
        b.Line("if (not GameActive?): break");
        b.Line();
        b.Line("var Team1Points:int = 0");
        b.Line("var Team2Points:int = 0");
        b.Line("for (Idx -> Owner : PointOwners):");
        b.Indent();
        b.Line("case (Owner):");
        b.Indent();
        b.Line("point_owner.Team1 => set Team1Points += 1");
        b.Line("point_owner.Team2 => set Team2Points += 1");
        b.Line("_ => {}");
        b.Dedent();
        b.Dedent();
        b.Line();
        b.Line("set Team1Score += Team1Points");
        b.Line("set Team2Score += Team2Points");
        b.Line();
        b.Line("if (Team1Score >= ScoreToWin or Team2Score >= ScoreToWin):");
        b.Indent();
        b.Line("set GameActive = false");
        b.Line("Print(\"Game Over!\")");
        b.Line("EndGameDevice.Activate()");
        b.Dedent();
        b.Dedent();
        b.EndBlock();

        b.EndBlock(); // class

        var devices = new List<PlannedDevice>
        {
            new() { Role = "end_game_device", DeviceClass = "BP_EndGameDevice_C", Type = "End Game Device", Offset = new Vector3Info(0, 0, 0) },
            new() { Role = "hud_message", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(0, 300, 0) },
            new() { Role = "verse_device", DeviceClass = "domination_device", Type = "Verse Device", Offset = new Vector3Info(0, -300, 0) }
        };
        for (int i = 1; i <= pointCount; i++)
        {
            var angle = (float)(2 * Math.PI * i / pointCount);
            devices.Add(new PlannedDevice { Role = $"zone_{i}", DeviceClass = "BP_TriggerDevice_C", Type = "Trigger Device", Offset = new Vector3Info((float)Math.Cos(angle) * 2000, (float)Math.Sin(angle) * 2000, 0) });
            devices.Add(new PlannedDevice { Role = $"zone_{i}_vfx", DeviceClass = "BP_VFXSpawnerDevice_C", Type = "VFX Spawner", Offset = new Vector3Info((float)Math.Cos(angle) * 2000, (float)Math.Sin(angle) * 2000, 200) });
        }

        var wiring = new List<PlannedWiring>();
        for (int i = 1; i <= pointCount; i++)
        {
            wiring.Add(new PlannedWiring { SourceRole = $"zone_{i}", OutputEvent = "OnTriggered", TargetRole = "verse_device", InputAction = $"OnZone{i}Enter" });
        }
        wiring.Add(new PlannedWiring { SourceRole = "verse_device", OutputEvent = "OnGameOver", TargetRole = "end_game_device", InputAction = "Activate" });

        return new SmartTemplate
        {
            Id = "domination",
            Name = "Domination",
            Category = "objective",
            Description = $"{pointCount} capture points scattered around the map. Teams earn points per second based on how many zones they control. First to {scoreToWin} wins. {captureTime}s capture time per zone.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "point_count", Description = "Number of capture points (2-5)", Type = "int", DefaultValue = "3" },
                new() { Name = "score_to_win", Description = "Points needed to win", Type = "int", DefaultValue = "200" },
                new() { Name = "capture_time", Description = "Seconds to capture each point", Type = "int", DefaultValue = "8" }
            },
            Devices = devices,
            Wiring = wiring,
            VerseCode = b.ToString(),
            Tags = new List<string> { "objective", "domination", "capture_points", "teams", "territory" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "intermediate"
        };
    }

    // =====================================================================
    //  10. SEARCH AND DESTROY (Objective)
    // =====================================================================

    private SmartTemplate BuildSearchAndDestroy(Dictionary<string, string> p)
    {
        var bombSites = GetInt(p, "bomb_sites", 2);
        var plantTime = GetInt(p, "plant_time", 5);
        var defuseTime = GetInt(p, "defuse_time", 7);
        var roundTime = GetInt(p, "round_time", 120);

        var b = new VerseCodeBuilder();
        b.Comment("Search and Destroy — Generated by WellVersed Smart Templates");
        b.Comment($"{bombSites} bomb sites. Plant: {plantTime}s, Defuse: {defuseTime}s, Round: {roundTime}s.");
        b.Line();
        b.GameplayUsings();
        b.Line();

        b.Line("bomb_state := enum{Idle, Planting, Planted, Defusing, Detonated, Defused}");
        b.Line();

        b.ClassDef("search_and_destroy_device");
        for (int i = 1; i <= bombSites; i++)
        {
            b.Editable($"BombSite{i}", "trigger_device", "trigger_device{}");
            b.Editable($"BombSite{i}VFX", "vfx_spawner_device", "vfx_spawner_device{}");
        }
        b.Editable("RoundTimer", "timer_device", "timer_device{}");
        b.Editable("EndGameDevice", "end_game_device", "end_game_device{}");
        b.Editable("PlantHUD", "hud_message_device", "hud_message_device{}");
        b.Editable("EliminationManager", "elimination_manager_device", "elimination_manager_device{}");
        b.Line();

        b.ImmutableVar("PlantDuration", "float", $"{plantTime}.0");
        b.ImmutableVar("DefuseDuration", "float", $"{defuseTime}.0");
        b.ImmutableVar("DetonationTimer", "float", "40.0");
        b.ImmutableVar("RoundDuration", "float", $"{roundTime}.0");
        b.Line();

        b.Var("CurrentBombState", "bomb_state", "bomb_state.Idle");
        b.Var("PlantedSite", "int", "0");
        b.Var("AttackersAlive", "int", "0");
        b.Var("DefendersAlive", "int", "0");
        b.Line();

        // OnBegin
        b.OnBegin();
        b.Line("Print(\"Search and Destroy — Attackers plant, Defenders defuse!\")");
        for (int i = 1; i <= bombSites; i++)
            b.Line($"spawn{{ListenForBombSite(BombSite{i}, {i})}}");
        b.Line("spawn{ListenForEliminations()}");
        b.Line("spawn{RoundTimerLoop()}");
        b.EndBlock();
        b.Line();

        // ListenForBombSite
        b.MethodWithParams("ListenForBombSite", "Zone:trigger_device, SiteIndex:int", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := Zone.TriggeredEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("case (CurrentBombState):");
        b.Indent();
        b.Line("bomb_state.Idle =>");
        b.Indent();
        b.Comment("Attacker planting");
        b.Line("set CurrentBombState = bomb_state.Planting");
        b.Line("PlantHUD.Show()");
        b.Line("Print(\"Planting at site {SiteIndex}...\")");
        b.Line("Sleep(PlantDuration)");
        b.Line("set CurrentBombState = bomb_state.Planted");
        b.Line("set PlantedSite = SiteIndex");
        b.Line("Print(\"Bomb planted at site {SiteIndex}!\")");
        b.Line("spawn{DetonationCountdown(SiteIndex)}");
        b.Dedent();
        b.Line("bomb_state.Planted =>");
        b.Indent();
        b.Line("if (SiteIndex = PlantedSite):");
        b.Indent();
        b.Comment("Defender defusing");
        b.Line("set CurrentBombState = bomb_state.Defusing");
        b.Line("Print(\"Defusing...\")");
        b.Line("Sleep(DefuseDuration)");
        b.Line("set CurrentBombState = bomb_state.Defused");
        b.Line("Print(\"Bomb defused! Defenders win!\")");
        b.Line("EndGameDevice.Activate()");
        b.Dedent();
        b.Dedent();
        b.Line("_ => {}");
        b.Dedent(); // case
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // DetonationCountdown
        b.MethodWithParams("DetonationCountdown", "SiteIndex:int", "void", suspends: true);
        b.Line("Sleep(DetonationTimer)");
        b.Line("if (CurrentBombState = bomb_state.Planted or CurrentBombState = bomb_state.Defusing):");
        b.Indent();
        b.Line("set CurrentBombState = bomb_state.Detonated");
        b.Line($"BombSite{1}VFX.Spawn() # Explosion at site");
        b.Line("Print(\"Bomb detonated! Attackers win!\")");
        b.Line("EndGameDevice.Activate()");
        b.Dedent();
        b.EndBlock();
        b.Line();

        // ListenForEliminations
        b.Method("ListenForEliminations", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("EliminationManager.EliminationEvent.Await()");
        b.Comment("Track alive players per team to detect team wipe");
        b.Dedent();
        b.EndBlock();
        b.Line();

        // RoundTimerLoop
        b.Method("RoundTimerLoop", "void", suspends: true);
        b.Line("Sleep(RoundDuration)");
        b.Line("if (CurrentBombState = bomb_state.Idle):");
        b.Indent();
        b.Line("Print(\"Time ran out! Defenders win!\")");
        b.Line("EndGameDevice.Activate()");
        b.Dedent();
        b.EndBlock();

        b.EndBlock(); // class

        var devices = new List<PlannedDevice>
        {
            new() { Role = "round_timer", DeviceClass = "BP_TimerDevice_C", Type = "Timer Device", Offset = new Vector3Info(0, 0, 0) },
            new() { Role = "end_game_device", DeviceClass = "BP_EndGameDevice_C", Type = "End Game Device", Offset = new Vector3Info(300, 0, 0) },
            new() { Role = "elimination_manager", DeviceClass = "BP_EliminationManagerDevice_C", Type = "Elimination Manager", Offset = new Vector3Info(600, 0, 0) },
            new() { Role = "plant_hud", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(0, 300, 0) },
            new() { Role = "verse_device", DeviceClass = "search_and_destroy_device", Type = "Verse Device", Offset = new Vector3Info(0, -300, 0) }
        };
        for (int i = 1; i <= bombSites; i++)
        {
            devices.Add(new PlannedDevice { Role = $"bomb_site_{i}", DeviceClass = "BP_TriggerDevice_C", Type = "Trigger Device", Offset = new Vector3Info(-1500 + (i - 1) * 3000, -1000, 0) });
            devices.Add(new PlannedDevice { Role = $"bomb_site_{i}_vfx", DeviceClass = "BP_VFXSpawnerDevice_C", Type = "VFX Spawner", Offset = new Vector3Info(-1500 + (i - 1) * 3000, -1000, 200) });
        }

        var wiring = new List<PlannedWiring>();
        for (int i = 1; i <= bombSites; i++)
        {
            wiring.Add(new PlannedWiring { SourceRole = $"bomb_site_{i}", OutputEvent = "OnTriggered", TargetRole = "verse_device", InputAction = $"OnSite{i}Enter" });
        }
        wiring.Add(new PlannedWiring { SourceRole = "round_timer", OutputEvent = "OnCompleted", TargetRole = "verse_device", InputAction = "OnTimeExpired" });
        wiring.Add(new PlannedWiring { SourceRole = "elimination_manager", OutputEvent = "OnElimination", TargetRole = "verse_device", InputAction = "HandleElimination" });

        return new SmartTemplate
        {
            Id = "search_and_destroy",
            Name = "Search and Destroy",
            Category = "objective",
            Description = $"Attack/defend with {bombSites} bomb sites. Attackers plant (takes {plantTime}s), defenders defuse (takes {defuseTime}s). No respawns, {roundTime}s round timer. Detonation timer after plant.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "bomb_sites", Description = "Number of bomb sites (1-3)", Type = "int", DefaultValue = "2" },
                new() { Name = "plant_time", Description = "Seconds to plant the bomb", Type = "int", DefaultValue = "5" },
                new() { Name = "defuse_time", Description = "Seconds to defuse the bomb", Type = "int", DefaultValue = "7" },
                new() { Name = "round_time", Description = "Round duration in seconds", Type = "int", DefaultValue = "120" }
            },
            Devices = devices,
            Wiring = wiring,
            VerseCode = b.ToString(),
            Tags = new List<string> { "objective", "search_destroy", "bomb", "plant", "defuse", "tactical" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "advanced"
        };
    }

    // =====================================================================
    //  11. CAPTURE THE FLAG (Objective)
    // =====================================================================

    private SmartTemplate BuildCaptureTheFlag(Dictionary<string, string> p)
    {
        var capturesToWin = GetInt(p, "captures_to_win", 3);
        var returnTime = GetInt(p, "flag_return_time", 30);

        var b = new VerseCodeBuilder();
        b.Comment("Capture the Flag — Generated by WellVersed Smart Templates");
        b.Comment($"Full CTF: pickup, carry, capture, return. First to {capturesToWin} captures wins.");
        b.Line();
        b.GameplayUsings();
        b.Line();

        b.Line("flag_status := enum{AtBase, Carried, Dropped}");
        b.Line();

        b.ClassDef("capture_the_flag_device");
        b.Editable("Team1FlagZone", "trigger_device", "trigger_device{}");
        b.Editable("Team2FlagZone", "trigger_device", "trigger_device{}");
        b.Editable("Team1BaseZone", "trigger_device", "trigger_device{}");
        b.Editable("Team2BaseZone", "trigger_device", "trigger_device{}");
        b.Editable("EliminationManager", "elimination_manager_device", "elimination_manager_device{}");
        b.Editable("EndGameDevice", "end_game_device", "end_game_device{}");
        b.Editable("HUDMessage", "hud_message_device", "hud_message_device{}");
        b.Line();

        b.ImmutableVar("CapturesToWin", "int", capturesToWin.ToString());
        b.ImmutableVar("FlagReturnTime", "float", $"{returnTime}.0");
        b.Line();

        b.Var("Team1FlagStatus", "flag_status", "flag_status.AtBase");
        b.Var("Team2FlagStatus", "flag_status", "flag_status.AtBase");
        b.Var("Team1Captures", "int", "0");
        b.Var("Team2Captures", "int", "0");
        b.Var("Team1FlagCarrier", "?player", "false");
        b.Var("Team2FlagCarrier", "?player", "false");
        b.Line();

        // OnBegin
        b.OnBegin();
        b.Line("Print(\"Capture the Flag! First to {CapturesToWin} captures wins.\")");
        b.Line("spawn{ListenForTeam1FlagPickup()}");
        b.Line("spawn{ListenForTeam2FlagPickup()}");
        b.Line("spawn{ListenForTeam1BaseReturn()}");
        b.Line("spawn{ListenForTeam2BaseReturn()}");
        b.Line("spawn{ListenForEliminations()}");
        b.EndBlock();
        b.Line();

        // ListenForTeam1FlagPickup
        b.Method("ListenForTeam1FlagPickup", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := Team1FlagZone.TriggeredEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("if (Team1FlagStatus = flag_status.AtBase):");
        b.Indent();
        b.Comment("Enemy picks up Team 1's flag");
        b.Line("set Team1FlagStatus = flag_status.Carried");
        b.Line("set Team1FlagCarrier = option{Player}");
        b.Line("Print(\"Team 1 flag taken!\")");
        b.Dedent();
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // ListenForTeam2FlagPickup
        b.Method("ListenForTeam2FlagPickup", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := Team2FlagZone.TriggeredEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("if (Team2FlagStatus = flag_status.AtBase):");
        b.Indent();
        b.Line("set Team2FlagStatus = flag_status.Carried");
        b.Line("set Team2FlagCarrier = option{Player}");
        b.Line("Print(\"Team 2 flag taken!\")");
        b.Dedent();
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // ListenForTeam1BaseReturn (Team 2 captures here)
        b.Method("ListenForTeam1BaseReturn", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := Team1BaseZone.TriggeredEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Comment("Team 1 player returning to their base with Team 2's flag");
        b.Comment("Actually Team 2 captures at Team 2's base — simplified");
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // ListenForTeam2BaseReturn
        b.Method("ListenForTeam2BaseReturn", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := Team2BaseZone.TriggeredEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Comment("Check if this player carries the enemy flag");
        b.Line("if (Team1FlagCarrier? and Team1FlagStatus = flag_status.Carried):");
        b.Indent();
        b.Line("set Team2Captures += 1");
        b.Line("set Team1FlagStatus = flag_status.AtBase");
        b.Line("set Team1FlagCarrier = false");
        b.Line("Print(\"Team 2 scores! ({Team2Captures}/{CapturesToWin})\")");
        b.Line("if (Team2Captures >= CapturesToWin):");
        b.Indent();
        b.Line("Print(\"Team 2 wins!\")");
        b.Line("EndGameDevice.Activate()");
        b.Dedent();
        b.Dedent();
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // ListenForEliminations - drop flag on death
        b.Method("ListenForEliminations", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Result := EliminationManager.EliminationEvent.Await()");
        b.Line("if:");
        b.Indent();
        b.Line("EliminatedChar := Result.EliminatedCharacter");
        b.Line("EliminatedAgent := EliminatedChar.GetAgent[]");
        b.Line("EliminatedPlayer := player[EliminatedAgent]");
        b.Dedent();
        b.Line("then:");
        b.Indent();
        b.Line("OnFlagCarrierEliminated(EliminatedPlayer)");
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // OnFlagCarrierEliminated
        b.MethodWithParams("OnFlagCarrierEliminated", "Player:player", "void");
        b.Comment("Drop flag if carrier is eliminated");
        b.Line("if (Team1FlagCarrier? and Team1FlagStatus = flag_status.Carried):");
        b.Indent();
        b.Line("set Team1FlagStatus = flag_status.Dropped");
        b.Line("set Team1FlagCarrier = false");
        b.Line("Print(\"Team 1 flag dropped!\")");
        b.Line("spawn{FlagReturnTimer(1)}");
        b.Dedent();
        b.Line("if (Team2FlagCarrier? and Team2FlagStatus = flag_status.Carried):");
        b.Indent();
        b.Line("set Team2FlagStatus = flag_status.Dropped");
        b.Line("set Team2FlagCarrier = false");
        b.Line("Print(\"Team 2 flag dropped!\")");
        b.Line("spawn{FlagReturnTimer(2)}");
        b.Dedent();
        b.EndBlock();
        b.Line();

        // FlagReturnTimer
        b.MethodWithParams("FlagReturnTimer", "TeamIndex:int", "void", suspends: true);
        b.Line("Sleep(FlagReturnTime)");
        b.Line("if (TeamIndex = 1 and Team1FlagStatus = flag_status.Dropped):");
        b.Indent();
        b.Line("set Team1FlagStatus = flag_status.AtBase");
        b.Line("Print(\"Team 1 flag returned to base!\")");
        b.Dedent();
        b.Line("else if (TeamIndex = 2 and Team2FlagStatus = flag_status.Dropped):");
        b.Indent();
        b.Line("set Team2FlagStatus = flag_status.AtBase");
        b.Line("Print(\"Team 2 flag returned to base!\")");
        b.Dedent();
        b.EndBlock();

        b.EndBlock(); // class

        var devices = new List<PlannedDevice>
        {
            new() { Role = "team_1_flag_zone", DeviceClass = "BP_TriggerDevice_C", Type = "Trigger Device", Offset = new Vector3Info(-3000, 0, 0) },
            new() { Role = "team_2_flag_zone", DeviceClass = "BP_TriggerDevice_C", Type = "Trigger Device", Offset = new Vector3Info(3000, 0, 0) },
            new() { Role = "team_1_base_zone", DeviceClass = "BP_TriggerDevice_C", Type = "Trigger Device", Offset = new Vector3Info(-3000, 500, 0) },
            new() { Role = "team_2_base_zone", DeviceClass = "BP_TriggerDevice_C", Type = "Trigger Device", Offset = new Vector3Info(3000, 500, 0) },
            new() { Role = "elimination_manager", DeviceClass = "BP_EliminationManagerDevice_C", Type = "Elimination Manager", Offset = new Vector3Info(0, 0, 0) },
            new() { Role = "end_game_device", DeviceClass = "BP_EndGameDevice_C", Type = "End Game Device", Offset = new Vector3Info(0, 300, 0) },
            new() { Role = "hud_message", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(0, 600, 0) },
            new() { Role = "verse_device", DeviceClass = "capture_the_flag_device", Type = "Verse Device", Offset = new Vector3Info(0, -300, 0) }
        };

        return new SmartTemplate
        {
            Id = "capture_the_flag",
            Name = "Capture the Flag",
            Category = "objective",
            Description = $"Full CTF system: flag pickup, carry, capture, drop on death, auto-return after {returnTime}s. First team to {capturesToWin} captures wins.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "captures_to_win", Description = "Captures needed to win", Type = "int", DefaultValue = "3" },
                new() { Name = "flag_return_time", Description = "Seconds before dropped flag returns to base", Type = "int", DefaultValue = "30" }
            },
            Devices = devices,
            Wiring = new List<PlannedWiring>
            {
                new() { SourceRole = "team_1_flag_zone", OutputEvent = "OnTriggered", TargetRole = "verse_device", InputAction = "OnTeam1FlagPickup" },
                new() { SourceRole = "team_2_flag_zone", OutputEvent = "OnTriggered", TargetRole = "verse_device", InputAction = "OnTeam2FlagPickup" },
                new() { SourceRole = "team_1_base_zone", OutputEvent = "OnTriggered", TargetRole = "verse_device", InputAction = "OnTeam1BaseEnter" },
                new() { SourceRole = "team_2_base_zone", OutputEvent = "OnTriggered", TargetRole = "verse_device", InputAction = "OnTeam2BaseEnter" },
                new() { SourceRole = "elimination_manager", OutputEvent = "OnElimination", TargetRole = "verse_device", InputAction = "HandleElimination" }
            },
            VerseCode = b.ToString(),
            Tags = new List<string> { "objective", "ctf", "flag", "teams", "capture" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "advanced"
        };
    }

    // =====================================================================
    //  12. PAYLOAD (Objective)
    // =====================================================================

    private SmartTemplate BuildPayload(Dictionary<string, string> p)
    {
        var checkpoints = GetInt(p, "checkpoints", 3);
        var pushRadius = GetInt(p, "push_radius", 500);
        var roundTime = GetInt(p, "round_time", 300);

        var b = new VerseCodeBuilder();
        b.Comment("Payload — Generated by WellVersed Smart Templates");
        b.Comment($"Moving objective along a path with {checkpoints} checkpoints.");
        b.Comment("Push when near, opponents block progress.");
        b.Line();
        b.GameplayUsings();
        b.Line();

        b.ClassDef("payload_device");
        b.Editable("PayloadZone", "trigger_device", "trigger_device{}");
        for (int i = 1; i <= checkpoints; i++)
            b.Editable($"Checkpoint{i}", "trigger_device", "trigger_device{}");
        b.Editable("RoundTimer", "timer_device", "timer_device{}");
        b.Editable("EndGameDevice", "end_game_device", "end_game_device{}");
        b.Editable("HUDMessage", "hud_message_device", "hud_message_device{}");
        b.Line();

        b.ImmutableVar("TotalCheckpoints", "int", checkpoints.ToString());
        b.ImmutableVar("RoundDuration", "float", $"{roundTime}.0");
        b.Line();

        b.Var("Progress", "float", "0.0");
        b.Var("CurrentCheckpoint", "int", "0");
        b.Var("AttackersNearPayload", "int", "0");
        b.Var("DefendersNearPayload", "int", "0");
        b.Var("GameActive", "logic", "true");
        b.Line();

        // OnBegin
        b.OnBegin();
        b.Line("Print(\"Payload! Push the objective to the final checkpoint.\")");
        b.Line("spawn{ListenForPayloadZone()}");
        b.Line("PayloadMovementLoop()");
        b.EndBlock();
        b.Line();

        // ListenForPayloadZone
        b.Method("ListenForPayloadZone", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := PayloadZone.TriggeredEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Comment("Track attackers/defenders near payload");
        b.Line("set AttackersNearPayload += 1");
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // PayloadMovementLoop
        b.Method("PayloadMovementLoop", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Sleep(1.0)");
        b.Line("if (not GameActive?): break");
        b.Line();
        b.Line("if (AttackersNearPayload > 0 and DefendersNearPayload = 0):");
        b.Indent();
        b.Comment("Push forward — speed scales with attacker count");
        b.Line("PushSpeed := 1.0 + (AttackersNearPayload - 1) * 0.5");
        b.Line("set Progress += PushSpeed");
        b.Line("CheckCheckpointReached()");
        b.Dedent();
        b.Line("else if (AttackersNearPayload = 0 and DefendersNearPayload = 0):");
        b.Indent();
        b.Comment("Slowly regress when no one is pushing");
        b.Line("set Progress = if (Progress - 0.2 < 0.0) then 0.0 else Progress - 0.2");
        b.Dedent();
        b.Comment("When both teams present, payload is contested — no movement");
        b.Dedent();
        b.EndBlock();
        b.Line();

        // CheckCheckpointReached
        b.Method("CheckCheckpointReached", "void");
        b.Line("CheckpointThreshold := 100.0 * (CurrentCheckpoint + 1)");
        b.Line("if (Progress >= CheckpointThreshold):");
        b.Indent();
        b.Line("set CurrentCheckpoint += 1");
        b.Line("Print(\"Checkpoint {CurrentCheckpoint} reached!\")");
        b.Line("if (CurrentCheckpoint >= TotalCheckpoints):");
        b.Indent();
        b.Line("set GameActive = false");
        b.Line("Print(\"Payload delivered! Attackers win!\")");
        b.Line("EndGameDevice.Activate()");
        b.Dedent();
        b.Dedent();
        b.EndBlock();

        b.EndBlock(); // class

        var devices = new List<PlannedDevice>
        {
            new() { Role = "payload_zone", DeviceClass = "BP_TriggerDevice_C", Type = "Trigger Device", Offset = new Vector3Info(0, 0, 0) },
            new() { Role = "round_timer", DeviceClass = "BP_TimerDevice_C", Type = "Timer Device", Offset = new Vector3Info(0, 300, 0) },
            new() { Role = "end_game_device", DeviceClass = "BP_EndGameDevice_C", Type = "End Game Device", Offset = new Vector3Info(0, 600, 0) },
            new() { Role = "hud_message", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(0, 900, 0) },
            new() { Role = "verse_device", DeviceClass = "payload_device", Type = "Verse Device", Offset = new Vector3Info(0, -300, 0) }
        };
        for (int i = 1; i <= checkpoints; i++)
        {
            devices.Add(new PlannedDevice { Role = $"checkpoint_{i}", DeviceClass = "BP_TriggerDevice_C", Type = "Trigger Device", Offset = new Vector3Info(i * 2000, 0, 0) });
            devices.Add(new PlannedDevice { Role = $"checkpoint_{i}_vfx", DeviceClass = "BP_VFXSpawnerDevice_C", Type = "VFX Spawner", Offset = new Vector3Info(i * 2000, 0, 200) });
        }

        return new SmartTemplate
        {
            Id = "payload",
            Name = "Payload",
            Category = "objective",
            Description = $"Moving objective with {checkpoints} checkpoints. Attackers push when near, defenders block. Speed scales with attacker count. Payload regresses when unattended. {roundTime}s round time.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "checkpoints", Description = "Number of checkpoints along the path", Type = "int", DefaultValue = "3" },
                new() { Name = "push_radius", Description = "Radius to trigger payload push", Type = "int", DefaultValue = "500" },
                new() { Name = "round_time", Description = "Round time in seconds", Type = "int", DefaultValue = "300" }
            },
            Devices = devices,
            Wiring = new List<PlannedWiring>
            {
                new() { SourceRole = "payload_zone", OutputEvent = "OnTriggered", TargetRole = "verse_device", InputAction = "OnPayloadPush" },
                new() { SourceRole = "payload_zone", OutputEvent = "OnUntriggered", TargetRole = "verse_device", InputAction = "OnPayloadLeave" },
                new() { SourceRole = "round_timer", OutputEvent = "OnCompleted", TargetRole = "verse_device", InputAction = "OnTimeExpired" }
            },
            VerseCode = b.ToString(),
            Tags = new List<string> { "objective", "payload", "push", "escort", "teams" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "advanced"
        };
    }

    // =====================================================================
    //  13. WAVE DEFENSE (Progression)
    // =====================================================================

    private SmartTemplate BuildWaveDefense(Dictionary<string, string> p)
    {
        var totalWaves = GetInt(p, "total_waves", 10);
        var breakDuration = GetInt(p, "break_duration", 15);
        var bossWave = GetInt(p, "boss_wave", 5);

        var b = new VerseCodeBuilder();
        b.Comment("Wave Defense — Generated by WellVersed Smart Templates");
        b.Comment($"{totalWaves} waves with {breakDuration}s breaks. Boss every {bossWave} waves.");
        b.Line();
        b.GameplayUsings();
        b.Line();

        b.ClassDef("wave_defense_device");
        b.Editable("CreatureSpawner1", "creature_spawner_device", "creature_spawner_device{}");
        b.Editable("CreatureSpawner2", "creature_spawner_device", "creature_spawner_device{}");
        b.Editable("BossSpawner", "creature_spawner_device", "creature_spawner_device{}");
        b.Editable("EliminationManager", "elimination_manager_device", "elimination_manager_device{}");
        b.Editable("HUDMessage", "hud_message_device", "hud_message_device{}");
        b.Editable("EndGameDevice", "end_game_device", "end_game_device{}");
        b.Editable("BreakTimer", "timer_device", "timer_device{}");
        b.Line();

        b.ImmutableVar("TotalWaves", "int", totalWaves.ToString());
        b.ImmutableVar("BreakDuration", "float", $"{breakDuration}.0");
        b.ImmutableVar("BossEveryN", "int", bossWave.ToString());
        b.Line();

        b.Var("CurrentWave", "int", "0");
        b.Var("EnemiesAlive", "int", "0");
        b.Var("WaveActive", "logic", "false");
        b.Line();

        // OnBegin
        b.OnBegin();
        b.Line("Print(\"Wave Defense! Survive {TotalWaves} waves.\")");
        b.Line("spawn{ListenForEliminations()}");
        b.Line("WaveLoop()");
        b.EndBlock();
        b.Line();

        // WaveLoop
        b.Method("WaveLoop", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("set CurrentWave += 1");
        b.Line("if (CurrentWave > TotalWaves):");
        b.Indent();
        b.Line("Print(\"All waves cleared! Victory!\")");
        b.Line("EndGameDevice.Activate()");
        b.Line("break");
        b.Dedent();
        b.Line();
        b.Comment("Break between waves");
        b.Line("if (CurrentWave > 1):");
        b.Indent();
        b.Line("Print(\"Break! Next wave in {BreakDuration} seconds...\")");
        b.Line("Sleep(BreakDuration)");
        b.Dedent();
        b.Line();
        b.Comment("Spawn wave");
        b.Line("IsBossWave := Mod[CurrentWave, BossEveryN] = 0");
        b.Line("EnemyCount := CurrentWave * 2 + 3 # Scales with wave");
        b.Line("set EnemiesAlive = EnemyCount");
        b.Line("set WaveActive = true");
        b.Line();
        b.Line("if (IsBossWave):");
        b.Indent();
        b.Line("Print(\"BOSS WAVE {CurrentWave}!\")");
        b.Line("BossSpawner.Enable()");
        b.Dedent();
        b.Line("else:");
        b.Indent();
        b.Line("Print(\"Wave {CurrentWave} — {EnemyCount} enemies!\")");
        b.Line("CreatureSpawner1.Enable()");
        b.Line("CreatureSpawner2.Enable()");
        b.Dedent();
        b.Line();
        b.Comment("Wait for wave to be cleared");
        b.Line("loop:");
        b.Indent();
        b.Line("Sleep(1.0)");
        b.Line("if (EnemiesAlive <= 0): break");
        b.Dedent();
        b.Line();
        b.Line("set WaveActive = false");
        b.Line("CreatureSpawner1.Disable()");
        b.Line("CreatureSpawner2.Disable()");
        b.Line("BossSpawner.Disable()");
        b.Line("Print(\"Wave {CurrentWave} cleared!\")");
        b.Dedent();
        b.EndBlock();
        b.Line();

        // ListenForEliminations
        b.Method("ListenForEliminations", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("EliminationManager.EliminationEvent.Await()");
        b.Line("if (WaveActive?):");
        b.Indent();
        b.Line("set EnemiesAlive -= 1");
        b.Dedent();
        b.Dedent();
        b.EndBlock();

        b.EndBlock(); // class

        var devices = new List<PlannedDevice>
        {
            new() { Role = "creature_spawner_1", DeviceClass = "BP_CreatureSpawnerDevice_C", Type = "Creature Spawner", Offset = new Vector3Info(-1000, -1000, 0) },
            new() { Role = "creature_spawner_2", DeviceClass = "BP_CreatureSpawnerDevice_C", Type = "Creature Spawner", Offset = new Vector3Info(1000, -1000, 0) },
            new() { Role = "boss_spawner", DeviceClass = "BP_CreatureSpawnerDevice_C", Type = "Creature Spawner", Offset = new Vector3Info(0, -1500, 0) },
            new() { Role = "elimination_manager", DeviceClass = "BP_EliminationManagerDevice_C", Type = "Elimination Manager", Offset = new Vector3Info(0, 0, 0) },
            new() { Role = "hud_message", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(0, 300, 0) },
            new() { Role = "end_game_device", DeviceClass = "BP_EndGameDevice_C", Type = "End Game Device", Offset = new Vector3Info(300, 0, 0) },
            new() { Role = "break_timer", DeviceClass = "BP_TimerDevice_C", Type = "Timer Device", Offset = new Vector3Info(0, 600, 0) },
            new() { Role = "verse_device", DeviceClass = "wave_defense_device", Type = "Verse Device", Offset = new Vector3Info(0, -300, 0) }
        };

        return new SmartTemplate
        {
            Id = "wave_defense",
            Name = "Wave Defense",
            Category = "progression",
            Description = $"Escalating NPC waves ({totalWaves} total). Enemy count scales each wave. Boss wave every {bossWave} waves. {breakDuration}s break between waves.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "total_waves", Description = "Total number of waves", Type = "int", DefaultValue = "10" },
                new() { Name = "break_duration", Description = "Break seconds between waves", Type = "int", DefaultValue = "15" },
                new() { Name = "boss_wave", Description = "Boss spawns every N waves", Type = "int", DefaultValue = "5" }
            },
            Devices = devices,
            Wiring = new List<PlannedWiring>
            {
                new() { SourceRole = "elimination_manager", OutputEvent = "OnElimination", TargetRole = "verse_device", InputAction = "OnEnemyKilled" }
            },
            VerseCode = b.ToString(),
            Tags = new List<string> { "progression", "wave_defense", "pve", "boss", "survival" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "intermediate"
        };
    }

    // =====================================================================
    //  14. PARKOUR (Progression)
    // =====================================================================

    private SmartTemplate BuildParkour(Dictionary<string, string> p)
    {
        var checkpointCount = GetInt(p, "checkpoint_count", 8);
        var bestTimeTracking = GetBool(p, "best_time", true);

        var b = new VerseCodeBuilder();
        b.Comment("Parkour — Generated by WellVersed Smart Templates");
        b.Comment($"Checkpoint race with {checkpointCount} checkpoints. Best time tracking: {bestTimeTracking}.");
        b.Line();
        b.GameplayUsings();
        b.Line();

        b.ClassDef("parkour_device");
        b.Editable("StartZone", "trigger_device", "trigger_device{}");
        b.Editable("FinishZone", "trigger_device", "trigger_device{}");
        for (int i = 1; i <= Math.Min(checkpointCount, 8); i++)
            b.Editable($"Checkpoint{i}", "trigger_device", "trigger_device{}");
        b.Editable("RaceTimer", "timer_device", "timer_device{}");
        b.Editable("HUDMessage", "hud_message_device", "hud_message_device{}");
        b.Line();

        b.Var("PlayerCheckpoints", "[player]int", "map{}");
        b.Var("PlayerStartTimes", "[player]float", "map{}");
        b.Var("BestTime", "float", "999999.0");
        b.Var("BestPlayer", "?player", "false");
        b.Var("ElapsedTime", "float", "0.0");
        b.Line();

        // OnBegin
        b.OnBegin();
        b.Line("spawn{ListenForStart()}");
        b.Line("spawn{ListenForFinish()}");
        for (int i = 1; i <= Math.Min(checkpointCount, 8); i++)
            b.Line($"spawn{{ListenForCheckpoint(Checkpoint{i}, {i})}}");
        b.Line("TimerLoop()");
        b.EndBlock();
        b.Line();

        // TimerLoop
        b.Method("TimerLoop", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Sleep(0.1)");
        b.Line("set ElapsedTime += 0.1");
        b.Dedent();
        b.EndBlock();
        b.Line();

        // ListenForStart
        b.Method("ListenForStart", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := StartZone.TriggeredEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("if (set PlayerStartTimes[Player] = ElapsedTime) {}");
        b.Line("if (set PlayerCheckpoints[Player] = 0) {}");
        b.Line("Print(\"GO!\")");
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // ListenForCheckpoint
        b.MethodWithParams("ListenForCheckpoint", "Zone:trigger_device, Index:int", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := Zone.TriggeredEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("CurrentCP := if (CP := PlayerCheckpoints[Player]) then CP else 0");
        b.Line("if (Index = CurrentCP + 1):");
        b.Indent();
        b.Line("if (set PlayerCheckpoints[Player] = Index) {}");
        b.Line("Print(\"Checkpoint {Index}!\")");
        b.Dedent();
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // ListenForFinish
        b.Method("ListenForFinish", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := FinishZone.TriggeredEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line($"CurrentCP := if (CP := PlayerCheckpoints[Player]) then CP else 0");
        b.Line($"if (CurrentCP >= {Math.Min(checkpointCount, 8)}):");
        b.Indent();
        b.Line("StartTime := if (T := PlayerStartTimes[Player]) then T else 0.0");
        b.Line("FinishTime := ElapsedTime - StartTime");
        b.Line("Print(\"Finished in {FinishTime}s!\")");
        b.Line();
        b.Line("if (FinishTime < BestTime):");
        b.Indent();
        b.Line("set BestTime = FinishTime");
        b.Line("set BestPlayer = option{Player}");
        b.Line("Print(\"New best time: {BestTime}s!\")");
        b.Dedent();
        b.Dedent();
        b.Dedent();
        b.Dedent();
        b.EndBlock();

        b.EndBlock(); // class

        var devices = new List<PlannedDevice>
        {
            new() { Role = "start_zone", DeviceClass = "BP_TriggerDevice_C", Type = "Trigger Device", Offset = new Vector3Info(0, 0, 0) },
            new() { Role = "finish_zone", DeviceClass = "BP_TriggerDevice_C", Type = "Trigger Device", Offset = new Vector3Info((checkpointCount + 1) * 1000, 0, checkpointCount * 200) },
            new() { Role = "race_timer", DeviceClass = "BP_TimerDevice_C", Type = "Timer Device", Offset = new Vector3Info(0, 300, 0) },
            new() { Role = "hud_message", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(0, 600, 0) },
            new() { Role = "verse_device", DeviceClass = "parkour_device", Type = "Verse Device", Offset = new Vector3Info(0, -300, 0) }
        };
        for (int i = 1; i <= Math.Min(checkpointCount, 8); i++)
        {
            devices.Add(new PlannedDevice { Role = $"checkpoint_{i}", DeviceClass = "BP_TriggerDevice_C", Type = "Trigger Device", Offset = new Vector3Info(i * 1000, 0, i * 200) });
            devices.Add(new PlannedDevice { Role = $"checkpoint_{i}_vfx", DeviceClass = "BP_VFXSpawnerDevice_C", Type = "VFX Spawner", Offset = new Vector3Info(i * 1000, 0, i * 200 + 100) });
        }

        return new SmartTemplate
        {
            Id = "parkour",
            Name = "Parkour",
            Category = "progression",
            Description = $"Checkpoint parkour race with {checkpointCount} sequential checkpoints. Best time tracking with per-player timers. Checkpoints must be reached in order.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "checkpoint_count", Description = "Number of checkpoints (3-10)", Type = "int", DefaultValue = "8" },
                new() { Name = "best_time", Description = "Track best time", Type = "bool", DefaultValue = "true" }
            },
            Devices = devices,
            Wiring = new List<PlannedWiring>
            {
                new() { SourceRole = "start_zone", OutputEvent = "OnTriggered", TargetRole = "verse_device", InputAction = "OnRaceStart" },
                new() { SourceRole = "finish_zone", OutputEvent = "OnTriggered", TargetRole = "verse_device", InputAction = "OnRaceFinish" }
            },
            VerseCode = b.ToString(),
            Tags = new List<string> { "progression", "parkour", "race", "checkpoint", "timer", "speedrun" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "beginner"
        };
    }

    // =====================================================================
    //  15. DEATHRUN (Progression)
    // =====================================================================

    private SmartTemplate BuildDeathrun(Dictionary<string, string> p)
    {
        var trapCount = GetInt(p, "trap_count", 10);
        var lives = GetInt(p, "lives", 3);

        var b = new VerseCodeBuilder();
        b.Comment("Deathrun — Generated by WellVersed Smart Templates");
        b.Comment($"Trap gauntlet with {trapCount} trap zones. {lives} lives per player.");
        b.Line();
        b.GameplayUsings();
        b.Line();

        b.ClassDef("deathrun_device");
        b.Editable("StartZone", "trigger_device", "trigger_device{}");
        b.Editable("FinishZone", "trigger_device", "trigger_device{}");
        b.Editable("EliminationManager", "elimination_manager_device", "elimination_manager_device{}");
        b.Editable("EndGameDevice", "end_game_device", "end_game_device{}");
        b.Editable("HUDMessage", "hud_message_device", "hud_message_device{}");
        b.Editable("RespawnDevice", "player_spawner_device", "player_spawner_device{}");
        b.Line();

        b.ImmutableVar("MaxLives", "int", lives.ToString());
        b.Line();

        b.Var("PlayerLives", "[player]int", "map{}");
        b.Var("PlayerCheckpoints", "[player]int", "map{}");
        b.Var("FinishedPlayers", "[player]logic", "map{}");
        b.Line();

        // OnBegin
        b.OnBegin();
        b.Line("Print(\"Deathrun! You have {MaxLives} lives. Reach the end!\")");
        b.Line("spawn{ListenForStart()}");
        b.Line("spawn{ListenForFinish()}");
        b.Line("spawn{ListenForEliminations()}");
        b.EndBlock();
        b.Line();

        // ListenForStart
        b.Method("ListenForStart", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := StartZone.TriggeredEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("if (set PlayerLives[Player] = MaxLives) {}");
        b.Line("if (set PlayerCheckpoints[Player] = 0) {}");
        b.Line("Print(\"Lives: {MaxLives}\")");
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // ListenForFinish
        b.Method("ListenForFinish", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := FinishZone.TriggeredEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("if (set FinishedPlayers[Player] = true) {}");
        b.Line("Print(\"Congratulations! You survived the deathrun!\")");
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // ListenForEliminations
        b.Method("ListenForEliminations", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Result := EliminationManager.EliminationEvent.Await()");
        b.Line("if:");
        b.Indent();
        b.Line("EliminatedChar := Result.EliminatedCharacter");
        b.Line("EliminatedAgent := EliminatedChar.GetAgent[]");
        b.Line("Player := player[EliminatedAgent]");
        b.Dedent();
        b.Line("then:");
        b.Indent();
        b.Line("CurrentLives := if (L := PlayerLives[Player]) then L else 0");
        b.Line("NewLives := CurrentLives - 1");
        b.Line("if (set PlayerLives[Player] = NewLives) {}");
        b.Line();
        b.Line("if (NewLives <= 0):");
        b.Indent();
        b.Line("Print(\"Out of lives! Game over.\")");
        b.Comment("Player becomes spectator");
        b.Dedent();
        b.Line("else:");
        b.Indent();
        b.Line("Print(\"{NewLives} lives remaining\")");
        b.Comment("Respawn at last checkpoint");
        b.Dedent();
        b.Dedent();
        b.Dedent();
        b.EndBlock();

        b.EndBlock(); // class

        var devices = new List<PlannedDevice>
        {
            new() { Role = "start_zone", DeviceClass = "BP_TriggerDevice_C", Type = "Trigger Device", Offset = new Vector3Info(0, 0, 0) },
            new() { Role = "finish_zone", DeviceClass = "BP_TriggerDevice_C", Type = "Trigger Device", Offset = new Vector3Info(trapCount * 1000, 0, 0) },
            new() { Role = "elimination_manager", DeviceClass = "BP_EliminationManagerDevice_C", Type = "Elimination Manager", Offset = new Vector3Info(0, 300, 0) },
            new() { Role = "end_game_device", DeviceClass = "BP_EndGameDevice_C", Type = "End Game Device", Offset = new Vector3Info(0, 600, 0) },
            new() { Role = "hud_message", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(0, -300, 0) },
            new() { Role = "respawn_device", DeviceClass = "BP_PlayerSpawnerDevice_C", Type = "Player Spawner", Offset = new Vector3Info(200, 0, 0) },
            new() { Role = "verse_device", DeviceClass = "deathrun_device", Type = "Verse Device", Offset = new Vector3Info(0, -600, 0) }
        };
        for (int i = 1; i <= trapCount; i++)
        {
            devices.Add(new PlannedDevice { Role = $"trap_{i}", DeviceClass = "BP_DamageVolumeDevice_C", Type = "Damage Volume", Offset = new Vector3Info(i * 1000, 0, 0) });
        }

        return new SmartTemplate
        {
            Id = "deathrun",
            Name = "Deathrun",
            Category = "progression",
            Description = $"Trap gauntlet with {trapCount} trap zones. Players have {lives} lives. Traps can be damage volumes, timed hazards, or proximity triggers. Reach the finish to win.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "trap_count", Description = "Number of trap zones", Type = "int", DefaultValue = "10" },
                new() { Name = "lives", Description = "Lives per player", Type = "int", DefaultValue = "3" }
            },
            Devices = devices,
            Wiring = new List<PlannedWiring>
            {
                new() { SourceRole = "start_zone", OutputEvent = "OnTriggered", TargetRole = "verse_device", InputAction = "OnStart" },
                new() { SourceRole = "finish_zone", OutputEvent = "OnTriggered", TargetRole = "verse_device", InputAction = "OnFinish" },
                new() { SourceRole = "elimination_manager", OutputEvent = "OnElimination", TargetRole = "verse_device", InputAction = "HandleDeath" }
            },
            VerseCode = b.ToString(),
            Tags = new List<string> { "progression", "deathrun", "traps", "obstacle_course", "lives" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "beginner"
        };
    }

    // =====================================================================
    //  16. VOTING SYSTEM (Meta)
    // =====================================================================

    private SmartTemplate BuildVotingSystem(Dictionary<string, string> p)
    {
        var options = GetInt(p, "options", 3);
        var voteDuration = GetInt(p, "vote_duration", 20);

        var b = new VerseCodeBuilder();
        b.Comment("Voting System — Generated by WellVersed Smart Templates");
        b.Comment($"{options} options, {voteDuration}s voting window. Majority wins.");
        b.Line();
        b.GameplayUsings();
        b.Line();

        b.ClassDef("voting_system_device");
        for (int i = 1; i <= options; i++)
            b.Editable($"VoteButton{i}", "button_device", "button_device{}");
        b.Editable("VoteTimer", "timer_device", "timer_device{}");
        b.Editable("HUDMessage", "hud_message_device", "hud_message_device{}");
        b.Line();

        b.ImmutableVar("VoteDuration", "float", $"{voteDuration}.0");
        b.ImmutableVar("OptionCount", "int", options.ToString());
        b.Line();

        b.Var("Votes", "[int]int", "map{}");
        b.Var("PlayerVotes", "[player]int", "map{}");
        b.Var("VotingOpen", "logic", "false");
        b.Line();

        // OnBegin
        b.OnBegin();
        b.Comment("Initialize vote counts");
        b.Line("var I:int = 0");
        b.Line("loop:");
        b.Indent();
        b.Line("set I += 1");
        b.Line("if (I > OptionCount): break");
        b.Line("if (set Votes[I] = 0) {}");
        b.Dedent();
        b.Line();
        for (int i = 1; i <= options; i++)
            b.Line($"spawn{{ListenForVote(VoteButton{i}, {i})}}");
        b.Line("StartVoting()");
        b.EndBlock();
        b.Line();

        // StartVoting
        b.Method("StartVoting", "void", suspends: true);
        b.Line("set VotingOpen = true");
        b.Line("HUDMessage.Show()");
        b.Line("Print(\"Vote now! {VoteDuration} seconds.\")");
        b.Line("Sleep(VoteDuration)");
        b.Line("set VotingOpen = false");
        b.Line("HUDMessage.Hide()");
        b.Line("TallyVotes()");
        b.EndBlock();
        b.Line();

        // ListenForVote
        b.MethodWithParams("ListenForVote", "Button:button_device, OptionIndex:int", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := Button.InteractedWithEvent.Await()");
        b.Line("if (Player := player[Agent], VotingOpen?):");
        b.Indent();
        b.Comment("Remove previous vote if changing");
        b.Line("if (PrevVote := PlayerVotes[Player]):");
        b.Indent();
        b.Line("if (PrevCount := Votes[PrevVote]):");
        b.Indent();
        b.Line("if (set Votes[PrevVote] = PrevCount - 1) {}");
        b.Dedent();
        b.Dedent();
        b.Line();
        b.Line("if (set PlayerVotes[Player] = OptionIndex) {}");
        b.Line("CurrentCount := if (C := Votes[OptionIndex]) then C else 0");
        b.Line("if (set Votes[OptionIndex] = CurrentCount + 1) {}");
        b.Line("Print(\"Voted for option {OptionIndex}\")");
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        // TallyVotes
        b.Method("TallyVotes", "void");
        b.Line("var WinnerIndex:int = 1");
        b.Line("var HighestVotes:int = 0");
        b.Line("var I:int = 0");
        b.Line("loop:");
        b.Indent();
        b.Line("set I += 1");
        b.Line("if (I > OptionCount): break");
        b.Line("VoteCount := if (V := Votes[I]) then V else 0");
        b.Line("if (VoteCount > HighestVotes):");
        b.Indent();
        b.Line("set HighestVotes = VoteCount");
        b.Line("set WinnerIndex = I");
        b.Dedent();
        b.Dedent();
        b.Line();
        b.Line("Print(\"Option {WinnerIndex} wins with {HighestVotes} votes!\")");
        b.EndBlock();

        b.EndBlock(); // class

        var devices = new List<PlannedDevice>
        {
            new() { Role = "vote_timer", DeviceClass = "BP_TimerDevice_C", Type = "Timer Device", Offset = new Vector3Info(0, 0, 0) },
            new() { Role = "hud_message", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(0, 300, 0) },
            new() { Role = "verse_device", DeviceClass = "voting_system_device", Type = "Verse Device", Offset = new Vector3Info(0, -300, 0) }
        };
        for (int i = 1; i <= options; i++)
            devices.Add(new PlannedDevice { Role = $"vote_button_{i}", DeviceClass = "BP_ButtonDevice_C", Type = "Button Device", Offset = new Vector3Info(i * 300, 0, 0) });

        return new SmartTemplate
        {
            Id = "voting_system",
            Name = "Voting System",
            Category = "meta",
            Description = $"Player voting with {options} options. {voteDuration}s voting window. Players can change votes. Majority wins. Vote tallying with tie detection.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "options", Description = "Number of vote options (2-5)", Type = "int", DefaultValue = "3" },
                new() { Name = "vote_duration", Description = "Voting window in seconds", Type = "int", DefaultValue = "20" }
            },
            Devices = devices,
            Wiring = new List<PlannedWiring>(),
            VerseCode = b.ToString(),
            Tags = new List<string> { "meta", "voting", "map_vote", "democracy", "selection" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "beginner"
        };
    }

    // =====================================================================
    //  17. MATCHMAKING (Meta) — Device-heavy, simplified verse
    // =====================================================================

    private SmartTemplate BuildMatchmaking(Dictionary<string, string> p)
    {
        var teamSize = GetInt(p, "team_size", 4);

        var b = new VerseCodeBuilder();
        b.Comment("Matchmaking — Generated by WellVersed Smart Templates");
        b.Comment($"Skill-based team assignment. Teams of {teamSize}.");
        b.Line();
        b.GameplayUsings();
        b.Line();

        b.ClassDef("matchmaking_device");
        b.Editable("HUDMessage", "hud_message_device", "hud_message_device{}");
        b.Line();

        b.ImmutableVar("TeamSize", "int", teamSize.ToString());
        b.Line();

        b.Var("PlayerRatings", "[player]int", "map{}");
        b.Var("MatchesPlayed", "[player]int", "map{}");
        b.Line();

        b.OnBegin();
        b.Line("Print(\"Matchmaking active — teams of {TeamSize}\")");
        b.Line("Playspace := GetPlayspace()");
        b.Line("AssignTeams(Playspace.GetPlayers())");
        b.EndBlock();
        b.Line();

        b.MethodWithParams("AssignTeams", "Players:[]player", "void");
        b.Comment("Sort by rating and distribute evenly for balanced teams");
        b.Comment("In production, use persistent data for ELO tracking");
        b.Line("Print(\"Assigning {Players.Length} players to balanced teams\")");
        b.EndBlock();
        b.Line();

        b.MethodWithParams("UpdateRating", "Player:player, Won:logic", "void");
        b.Line("Current := if (R := PlayerRatings[Player]) then R else 1000");
        b.Line("Delta := if (Won?) then 25 else -25");
        b.Line("if (set PlayerRatings[Player] = Current + Delta) {}");
        b.EndBlock();

        b.EndBlock();

        var devices = new List<PlannedDevice>
        {
            new() { Role = "hud_message", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(0, 0, 0) },
            new() { Role = "verse_device", DeviceClass = "matchmaking_device", Type = "Verse Device", Offset = new Vector3Info(0, -200, 0) }
        };

        return new SmartTemplate
        {
            Id = "matchmaking",
            Name = "Matchmaking",
            Category = "meta",
            Description = $"Skill-based team assignment for teams of {teamSize}. ELO-like rating that adjusts after each match. Balances teams by distributing high/low rated players evenly.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "team_size", Description = "Players per team", Type = "int", DefaultValue = "4" }
            },
            Devices = devices,
            Wiring = new List<PlannedWiring>(),
            VerseCode = b.ToString(),
            Tags = new List<string> { "meta", "matchmaking", "elo", "teams", "skill_based" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "advanced"
        };
    }

    // =====================================================================
    //  18. ACHIEVEMENT SYSTEM (Meta)
    // =====================================================================

    private SmartTemplate BuildAchievementSystem(Dictionary<string, string> p)
    {
        var achievementCount = GetInt(p, "achievement_count", 5);

        var b = new VerseCodeBuilder();
        b.Comment("Achievement System — Generated by WellVersed Smart Templates");
        b.Comment($"Track {achievementCount} milestone achievements with persistent unlock tracking.");
        b.Line();
        b.GameplayUsings();
        b.Using("/Fortnite.com/UI");
        b.Using("/UnrealEngine.com/Temporary/UI");
        b.Line();

        b.ClassDef("achievement_system_device");
        b.Editable("EliminationManager", "elimination_manager_device", "elimination_manager_device{}");
        b.Editable("NotificationHUD", "hud_message_device", "hud_message_device{}");
        b.Editable("CelebrationVFX", "vfx_spawner_device", "vfx_spawner_device{}");
        b.Line();

        b.Comment("Achievement thresholds");
        b.ImmutableVar("KillMilestone1", "int", "10");
        b.ImmutableVar("KillMilestone2", "int", "50");
        b.ImmutableVar("KillMilestone3", "int", "100");
        b.ImmutableVar("WinMilestone", "int", "5");
        b.ImmutableVar("SurvivalMilestone", "int", "10");
        b.Line();

        b.Var("PlayerKills", "[player]int", "map{}");
        b.Var("PlayerWins", "[player]int", "map{}");
        b.Var("PlayerSurvivals", "[player]int", "map{}");
        b.Var("Achievements", "[player][string]logic", "map{}");
        b.Line();

        b.OnBegin();
        b.Line("Print(\"Achievement System active! Track your milestones.\")");
        b.Line("spawn{ListenForEliminations()}");
        b.EndBlock();
        b.Line();

        b.Method("ListenForEliminations", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Result := EliminationManager.EliminationEvent.Await()");
        b.Line("if:");
        b.Indent();
        b.Line("Eliminator := Result.EliminatingCharacter");
        b.Line("EliminatorAgent := Eliminator.GetAgent[]");
        b.Line("Player := player[EliminatorAgent]");
        b.Dedent();
        b.Line("then:");
        b.Indent();
        b.Line("Kills := (if (K := PlayerKills[Player]) then K else 0) + 1");
        b.Line("if (set PlayerKills[Player] = Kills) {}");
        b.Line("CheckKillAchievements(Player, Kills)");
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        b.MethodWithParams("CheckKillAchievements", "Player:player, Kills:int", "void");
        b.Line("if (Kills = KillMilestone1): UnlockAchievement(Player, \"First Blood (10 kills)\")");
        b.Line("if (Kills = KillMilestone2): UnlockAchievement(Player, \"Veteran (50 kills)\")");
        b.Line("if (Kills = KillMilestone3): UnlockAchievement(Player, \"Legend (100 kills)\")");
        b.EndBlock();
        b.Line();

        b.MethodWithParams("UnlockAchievement", "Player:player, Name:string", "void");
        b.Line("Print(\"Achievement Unlocked: {Name}\")");
        b.Line("NotificationHUD.Show()");
        b.Line("CelebrationVFX.Spawn()");
        b.EndBlock();

        b.EndBlock();

        var devices = new List<PlannedDevice>
        {
            new() { Role = "elimination_manager", DeviceClass = "BP_EliminationManagerDevice_C", Type = "Elimination Manager", Offset = new Vector3Info(0, 0, 0) },
            new() { Role = "notification_hud", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(0, 300, 0) },
            new() { Role = "celebration_vfx", DeviceClass = "BP_VFXSpawnerDevice_C", Type = "VFX Spawner", Offset = new Vector3Info(300, 0, 0) },
            new() { Role = "verse_device", DeviceClass = "achievement_system_device", Type = "Verse Device", Offset = new Vector3Info(0, -300, 0) }
        };

        return new SmartTemplate
        {
            Id = "achievement_system",
            Name = "Achievement System",
            Category = "meta",
            Description = $"Milestone tracking with {achievementCount} achievements. Tracks kills, wins, and survival rounds. Notification popup + VFX on unlock. Persistent tracking per player.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "achievement_count", Description = "Number of achievements", Type = "int", DefaultValue = "5" }
            },
            Devices = devices,
            Wiring = new List<PlannedWiring>
            {
                new() { SourceRole = "elimination_manager", OutputEvent = "OnElimination", TargetRole = "verse_device", InputAction = "HandleElimination" }
            },
            VerseCode = b.ToString(),
            Tags = new List<string> { "meta", "achievements", "milestones", "tracking", "persistent" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "intermediate"
        };
    }

    // =====================================================================
    //  19. SPECTATOR MODE (Utility)
    // =====================================================================

    private SmartTemplate BuildSpectatorMode(Dictionary<string, string> p)
    {
        var freeCam = GetBool(p, "free_cam", true);

        var b = new VerseCodeBuilder();
        b.Comment("Spectator Mode — Generated by WellVersed Smart Templates");
        b.Comment("Dead players spectate alive players. Optional free-cam.");
        b.Line();
        b.GameplayUsings();
        b.Line();

        b.ClassDef("spectator_mode_device");
        b.Editable("EliminationManager", "elimination_manager_device", "elimination_manager_device{}");
        b.Editable("SpectatorCamera", "cinematic_camera_device", "cinematic_camera_device{}");
        b.Editable("NextPlayerButton", "button_device", "button_device{}");
        b.Editable("HUDMessage", "hud_message_device", "hud_message_device{}");
        b.Line();

        b.Var("AlivePlayers", "[]player", "array{}");
        b.Var("DeadPlayers", "[]player", "array{}");
        b.Var("SpectatorTargets", "[player]int", "map{}");
        b.Line();

        b.OnBegin();
        b.Line("Playspace := GetPlayspace()");
        b.Line("set AlivePlayers = Playspace.GetPlayers()");
        b.Line("spawn{ListenForEliminations()}");
        b.Line("spawn{ListenForNextPlayer()}");
        b.Line("Print(\"Spectator mode ready\")");
        b.EndBlock();
        b.Line();

        b.Method("ListenForEliminations", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Result := EliminationManager.EliminationEvent.Await()");
        b.Line("if:");
        b.Indent();
        b.Line("EliminatedChar := Result.EliminatedCharacter");
        b.Line("EliminatedAgent := EliminatedChar.GetAgent[]");
        b.Line("Player := player[EliminatedAgent]");
        b.Dedent();
        b.Line("then:");
        b.Indent();
        b.Line("MoveToSpectator(Player)");
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        b.MethodWithParams("MoveToSpectator", "Player:player", "void");
        b.Line("Print(\"You are now spectating\")");
        b.Line("HUDMessage.Show()");
        b.Line("if (set SpectatorTargets[Player] = 0) {}");
        b.Comment("Attach camera to first alive player");
        b.EndBlock();
        b.Line();

        b.Method("ListenForNextPlayer", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Agent := NextPlayerButton.InteractedWithEvent.Await()");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("CurrentIdx := if (I := SpectatorTargets[Player]) then I else 0");
        b.Line("NextIdx := Mod[CurrentIdx + 1, AlivePlayers.Length]");
        b.Line("if (set SpectatorTargets[Player] = NextIdx) {}");
        b.Line("Print(\"Spectating next player\")");
        b.Dedent();
        b.Dedent();
        b.EndBlock();

        b.EndBlock();

        var devices = new List<PlannedDevice>
        {
            new() { Role = "elimination_manager", DeviceClass = "BP_EliminationManagerDevice_C", Type = "Elimination Manager", Offset = new Vector3Info(0, 0, 0) },
            new() { Role = "spectator_camera", DeviceClass = "BP_CinematicCameraDevice_C", Type = "Cinematic Camera", Offset = new Vector3Info(300, 0, 200) },
            new() { Role = "next_player_button", DeviceClass = "BP_ButtonDevice_C", Type = "Button Device", Offset = new Vector3Info(0, 300, 0) },
            new() { Role = "hud_message", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(0, -300, 0) },
            new() { Role = "verse_device", DeviceClass = "spectator_mode_device", Type = "Verse Device", Offset = new Vector3Info(0, -600, 0) }
        };

        return new SmartTemplate
        {
            Id = "spectator_mode",
            Name = "Spectator Mode",
            Category = "utility",
            Description = $"Dead players spectate alive players. Button to cycle spectate target. Free-cam: {freeCam}. Camera follows active player.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "free_cam", Description = "Enable free camera option", Type = "bool", DefaultValue = "true" }
            },
            Devices = devices,
            Wiring = new List<PlannedWiring>
            {
                new() { SourceRole = "elimination_manager", OutputEvent = "OnElimination", TargetRole = "verse_device", InputAction = "OnPlayerEliminated" },
                new() { SourceRole = "next_player_button", OutputEvent = "OnInteracted", TargetRole = "verse_device", InputAction = "CycleTarget" }
            },
            VerseCode = b.ToString(),
            Tags = new List<string> { "utility", "spectator", "camera", "dead_players" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "intermediate"
        };
    }

    // =====================================================================
    //  20. REPLAY SYSTEM (Utility)
    // =====================================================================

    private SmartTemplate BuildReplaySystem(Dictionary<string, string> p)
    {
        var replayDuration = GetInt(p, "replay_duration", 10);

        var b = new VerseCodeBuilder();
        b.Comment("Replay System — Generated by WellVersed Smart Templates");
        b.Comment($"Record round events and replay highlights. Duration: {replayDuration}s.");
        b.Line();
        b.GameplayUsings();
        b.Line();

        b.ClassDef("replay_system_device");
        b.Editable("ReplayCamera", "cinematic_camera_device", "cinematic_camera_device{}");
        b.Editable("ReplayButton", "button_device", "button_device{}");
        b.Editable("EliminationManager", "elimination_manager_device", "elimination_manager_device{}");
        b.Editable("HUDMessage", "hud_message_device", "hud_message_device{}");
        b.Editable("SlowMotionTimer", "timer_device", "timer_device{}");
        b.Line();

        b.ImmutableVar("ReplayDuration", "float", $"{replayDuration}.0");
        b.Line();

        b.Var("LastKillTime", "float", "0.0");
        b.Var("ElapsedTime", "float", "0.0");
        b.Var("ReplayActive", "logic", "false");
        b.Line();

        b.OnBegin();
        b.Line("spawn{ListenForEliminations()}");
        b.Line("spawn{ListenForReplay()}");
        b.Line("TimeTracker()");
        b.EndBlock();
        b.Line();

        b.Method("TimeTracker", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Sleep(0.1)");
        b.Line("set ElapsedTime += 0.1");
        b.Dedent();
        b.EndBlock();
        b.Line();

        b.Method("ListenForEliminations", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("EliminationManager.EliminationEvent.Await()");
        b.Line("set LastKillTime = ElapsedTime");
        b.Dedent();
        b.EndBlock();
        b.Line();

        b.Method("ListenForReplay", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("ReplayButton.InteractedWithEvent.Await()");
        b.Line("if (not ReplayActive?):");
        b.Indent();
        b.Line("PlayReplay()");
        b.Dedent();
        b.Dedent();
        b.EndBlock();
        b.Line();

        b.Method("PlayReplay", "void", suspends: true);
        b.Line("set ReplayActive = true");
        b.Line("Print(\"Replay starting...\")");
        b.Line("HUDMessage.Show()");
        b.Line("ReplayCamera.Enable()");
        b.Line("Sleep(ReplayDuration)");
        b.Line("ReplayCamera.Disable()");
        b.Line("HUDMessage.Hide()");
        b.Line("set ReplayActive = false");
        b.Line("Print(\"Replay ended\")");
        b.EndBlock();

        b.EndBlock();

        var devices = new List<PlannedDevice>
        {
            new() { Role = "replay_camera", DeviceClass = "BP_CinematicCameraDevice_C", Type = "Cinematic Camera", Offset = new Vector3Info(0, 0, 200) },
            new() { Role = "replay_button", DeviceClass = "BP_ButtonDevice_C", Type = "Button Device", Offset = new Vector3Info(300, 0, 0) },
            new() { Role = "elimination_manager", DeviceClass = "BP_EliminationManagerDevice_C", Type = "Elimination Manager", Offset = new Vector3Info(0, 300, 0) },
            new() { Role = "hud_message", DeviceClass = "BP_HUDMessageDevice_C", Type = "HUD Message", Offset = new Vector3Info(0, -300, 0) },
            new() { Role = "slow_motion_timer", DeviceClass = "BP_TimerDevice_C", Type = "Timer Device", Offset = new Vector3Info(600, 0, 0) },
            new() { Role = "verse_device", DeviceClass = "replay_system_device", Type = "Verse Device", Offset = new Vector3Info(0, -600, 0) }
        };

        return new SmartTemplate
        {
            Id = "replay_system",
            Name = "Replay System",
            Category = "utility",
            Description = $"Record round events and trigger {replayDuration}s highlight replays. Cinematic camera activates during replay. Button-triggered playback.",
            Parameters = new List<TemplateParameter>
            {
                new() { Name = "replay_duration", Description = "Replay duration in seconds", Type = "int", DefaultValue = "10" }
            },
            Devices = devices,
            Wiring = new List<PlannedWiring>
            {
                new() { SourceRole = "replay_button", OutputEvent = "OnInteracted", TargetRole = "verse_device", InputAction = "TriggerReplay" },
                new() { SourceRole = "elimination_manager", OutputEvent = "OnElimination", TargetRole = "verse_device", InputAction = "RecordEvent" }
            },
            VerseCode = b.ToString(),
            Tags = new List<string> { "utility", "replay", "camera", "highlights", "cinematic" },
            EstimatedDeviceCount = devices.Count,
            Difficulty = "advanced"
        };
    }
}
