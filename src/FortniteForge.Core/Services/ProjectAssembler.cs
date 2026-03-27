using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WellVersed.Core.Config;
using WellVersed.Core.Models;
using WellVersed.Core.Services.VerseGeneration;
using Microsoft.Extensions.Logging;

namespace WellVersed.Core.Services;

/// <summary>
/// Layer 2: Project Assembler — takes a GameDesign + GenerationPlan and creates
/// a complete UEFN project folder structure with all files.
///
/// Output:
///   outputPath/
///   ├── [ProjectName].uefnproject
///   ├── Config/
///   │   └── DefaultEngine.ini
///   ├── Content/
///   │   ├── [GameName]_manager.verse
///   │   ├── [GameName]_ui.verse (if UI needed)
///   │   └── [Additional].verse
///   └── Plugins/
///       └── [ProjectName]/
///           └── Content/
///               └── [Widgets].json (WidgetSpec)
/// </summary>
public class ProjectAssembler
{
    private readonly VerseDeviceGenerator _verseDeviceGen;
    private readonly VerseUIGenerator _verseUIGen;
    private readonly ILogger<ProjectAssembler> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ProjectAssembler(
        VerseDeviceGenerator verseDeviceGen,
        VerseUIGenerator verseUIGen,
        ILogger<ProjectAssembler> logger)
    {
        _verseDeviceGen = verseDeviceGen;
        _verseUIGen = verseUIGen;
        _logger = logger;
    }

    /// <summary>
    /// Assembles a complete UEFN project folder from a GameDesign and GenerationPlan.
    /// </summary>
    public AssemblyResult AssembleProject(GameDesign design, GenerationPlan plan, string outputPath)
    {
        var projectName = SanitizeProjectName(design.Name);
        var result = new AssemblyResult
        {
            OutputPath = outputPath,
            ProjectName = projectName,
            Design = design
        };

        try
        {
            // Create directory structure
            Directory.CreateDirectory(outputPath);
            Directory.CreateDirectory(Path.Combine(outputPath, "Config"));
            Directory.CreateDirectory(Path.Combine(outputPath, "Content"));
            Directory.CreateDirectory(Path.Combine(outputPath, "Plugins", projectName, "Content"));

            // 1. .uefnproject file
            var uefnProjectPath = Path.Combine(outputPath, $"{projectName}.uefnproject");
            WriteUefnProject(uefnProjectPath);
            result.GeneratedFiles.Add(uefnProjectPath);

            // 2. DefaultEngine.ini
            var engineIniPath = Path.Combine(outputPath, "Config", "DefaultEngine.ini");
            WriteDefaultEngineIni(engineIniPath, design);
            result.GeneratedFiles.Add(engineIniPath);

            // 3. Main game manager verse file
            var managerVerseFile = GenerateManagerVerse(design, plan, outputPath);
            if (managerVerseFile != null)
                result.GeneratedFiles.Add(managerVerseFile);

            // 4. UI controller verse file (if UI needed)
            if (design.UINeeds.Count > 0)
            {
                var uiVerseFile = GenerateUIVerse(design, outputPath);
                if (uiVerseFile != null)
                    result.GeneratedFiles.Add(uiVerseFile);
            }

            // 5. Economy verse file (if economy system)
            if (design.Economy is { HasCurrency: true })
            {
                var economyVerseFile = GenerateEconomyVerse(design, outputPath);
                if (economyVerseFile != null)
                    result.GeneratedFiles.Add(economyVerseFile);
            }

            // 6. Per-mechanic verse files
            foreach (var mechanic in design.SpecialMechanics)
            {
                var mechanicFile = GenerateMechanicVerse(mechanic, design, outputPath);
                if (mechanicFile != null)
                    result.GeneratedFiles.Add(mechanicFile);
            }

            // 7. Widget specs (as JSON)
            foreach (var uiNeed in design.UINeeds.Where(u => u.Type is "HUD" or "Scoreboard" or "ShopUI"))
            {
                var widgetFile = GenerateWidgetSpec(uiNeed, design, outputPath, projectName);
                if (widgetFile != null)
                    result.GeneratedFiles.Add(widgetFile);
            }

            // 8. Device placement manifest (reference for placing in UEFN)
            var manifestPath = Path.Combine(outputPath, "Content", "device_manifest.json");
            WriteDeviceManifest(manifestPath, plan);
            result.GeneratedFiles.Add(manifestPath);

            // Build plan summary
            result.Plan = new GenerationPlanSummary
            {
                DeviceCount = plan.Devices.Count,
                WiringCount = plan.Wiring.Count,
                VerseFileCount = result.GeneratedFiles.Count(f => f.EndsWith(".verse")),
                WidgetCount = result.GeneratedFiles.Count(f => f.EndsWith(".widget.json")),
                DeviceTypes = plan.Devices.Select(d => d.Type).Distinct().ToList()
            };

            result.Success = true;
            _logger.LogInformation("Assembled project '{Name}' at {Path} — {Files} files",
                projectName, outputPath, result.GeneratedFiles.Count);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger.LogError(ex, "Failed to assemble project '{Name}'", projectName);
        }

        return result;
    }

    // =========================================================================
    //  FILE GENERATORS
    // =========================================================================

    private static void WriteUefnProject(string path)
    {
        var content = JsonSerializer.Serialize(new
        {
            EngineAssociation = "5.4",
            Category = "",
            Description = ""
        }, JsonOpts);
        File.WriteAllText(path, content);
    }

    private static void WriteDefaultEngineIni(string path, GameDesign design)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[/Script/EngineSettings.GameMapsSettings]");
        sb.AppendLine();
        sb.AppendLine("[/Script/Engine.Engine]");
        sb.AppendLine();
        sb.AppendLine($"; Generated by WellVersed for: {design.Name}");
        sb.AppendLine($"; Game Mode: {design.GameMode}");
        sb.AppendLine($"; Players: {design.PlayerCount}");
        if (design.TeamCount > 0)
            sb.AppendLine($"; Teams: {design.TeamCount}");

        File.WriteAllText(path, sb.ToString());
    }

    private string? GenerateManagerVerse(GameDesign design, GenerationPlan plan, string outputPath)
    {
        var className = SanitizeClassName(design.Name) + "_manager";
        var builder = new VerseCodeBuilder();

        builder.Comment($"{design.Name} — Game Manager");
        builder.Comment("Generated by WellVersed");
        builder.Line();
        builder.GameplayUsings();
        builder.Line();

        builder.ClassDef(className);

        // Editable device references from the plan
        var deviceTypes = plan.Devices
            .GroupBy(d => d.Type)
            .Where(g => g.Count() <= 4) // Only reference devices with manageable counts
            .ToList();

        foreach (var group in deviceTypes)
        {
            var verseType = DeviceClassToVerseType(group.First().DeviceClass);
            if (group.Count() == 1)
            {
                builder.Editable(SanitizeVerseName(group.First().Role), verseType, $"{verseType}{{}}");
            }
            else
            {
                int idx = 0;
                foreach (var dev in group)
                {
                    builder.Editable(SanitizeVerseName(dev.Role), verseType, $"{verseType}{{}}");
                    idx++;
                }
            }
        }
        builder.Line();

        // State variables
        if (design.Rounds != null)
        {
            builder.Var("CurrentRound", "int", "0");
            builder.ImmutableVar("TotalRounds", "int", design.Rounds.RoundCount.ToString());
            builder.ImmutableVar("RoundDuration", "float", $"{design.Rounds.RoundDurationSeconds}.0");
        }

        if (design.Scoring != null)
        {
            builder.Var("GameActive", "logic", "false");
            if (design.Scoring.WinScore > 0)
                builder.ImmutableVar("WinScore", "int", design.Scoring.WinScore.ToString());
        }

        if (design.Economy is { HasCurrency: true })
        {
            builder.Var("PlayerBalances", "[player]int", "map{}");
            builder.ImmutableVar("StartingBalance", "int", design.Economy.StartingAmount.ToString());
        }
        builder.Line();

        // OnBegin
        builder.OnBegin();
        if (design.Rounds is { WarmupSeconds: > 0 })
        {
            builder.Line($"Print(\"{design.Name} starting...\")");
            builder.Line($"Sleep({design.Rounds.WarmupSeconds}.0)");
        }

        if (design.Rounds is { RoundCount: > 1 })
        {
            builder.Line("GameLoop()");
        }
        else
        {
            builder.Line("StartGame()");
        }
        builder.EndBlock();

        // Game loop (if rounds)
        if (design.Rounds is { RoundCount: > 1 })
        {
            builder.Line();
            builder.Method("GameLoop", "void", suspends: true);
            builder.Line("loop:");
            builder.Indent();
            builder.Line("set CurrentRound += 1");
            builder.Line("if (CurrentRound > TotalRounds):");
            builder.Indent();
            builder.Line("EndGame()");
            builder.Line("break");
            builder.Dedent();
            builder.Line("StartRound()");
            builder.Line("Sleep(RoundDuration)");
            builder.Line("EndRound()");
            builder.Line("Sleep(5.0) # Brief pause between rounds");
            builder.Dedent();
            builder.EndBlock();
        }

        // StartGame / StartRound
        builder.Line();
        if (design.Rounds is { RoundCount: > 1 })
        {
            builder.Method("StartRound", "void");
            builder.Line("set GameActive = true");
            builder.Line("Print(\"Round {CurrentRound} of {TotalRounds} — FIGHT!\")");
            builder.EndBlock();

            builder.Line();
            builder.Method("EndRound", "void");
            builder.Line("set GameActive = false");
            builder.Line("Print(\"Round {CurrentRound} complete.\")");
            builder.EndBlock();
        }
        else
        {
            builder.Method("StartGame", "void", suspends: true);
            builder.Line("set GameActive = true");
            builder.Line($"Print(\"{design.Name} has begun!\")");
            if (design.Rounds != null)
            {
                builder.Line("Sleep(RoundDuration)");
                builder.Line("EndGame()");
            }
            builder.EndBlock();
        }

        builder.Line();
        builder.Method("EndGame", "void");
        builder.Line("set GameActive = false");
        builder.Line($"Print(\"{design.Name} — Game Over!\")");
        builder.Line("Playspace := GetPlayspace()");
        builder.Line("AllPlayers := Playspace.GetPlayers()");
        builder.Line("if (Winner := AllPlayers[0]):");
        builder.Indent();
        builder.Line("Print(\"Winner determined!\")");
        builder.Dedent();
        builder.EndBlock();

        // Close class
        builder.EndBlock();

        var filePath = Path.Combine(outputPath, "Content", $"{className}.verse");
        File.WriteAllText(filePath, builder.ToString());
        return filePath;
    }

    private string? GenerateUIVerse(GameDesign design, string outputPath)
    {
        var className = SanitizeClassName(design.Name) + "_ui";
        var builder = new VerseCodeBuilder();

        builder.Comment($"{design.Name} — UI Controller");
        builder.Comment("Generated by WellVersed");
        builder.Line();
        builder.UIUsings();
        builder.Line();

        builder.ClassDef(className);

        // Editable UI device references
        builder.Editable("HUDDevice", "hud_message_device", "hud_message_device{}");
        builder.Line();

        // State
        if (design.Scoring != null)
        {
            builder.Var("DisplayedScore", "int", "0");
        }
        if (design.Rounds != null)
        {
            builder.Var("TimeRemaining", "float", $"{design.Rounds.RoundDurationSeconds}.0");
        }
        builder.Line();

        // OnBegin
        builder.OnBegin();
        builder.Line("Print(\"UI Controller initialized\")");
        if (design.Rounds != null)
        {
            builder.Line("UpdateTimerLoop()");
        }
        builder.EndBlock();

        // Timer loop
        if (design.Rounds != null)
        {
            builder.Line();
            builder.Method("UpdateTimerLoop", "void", suspends: true);
            builder.Line("loop:");
            builder.Indent();
            builder.Line("if (TimeRemaining <= 0.0):");
            builder.Indent();
            builder.Line("break");
            builder.Dedent();
            builder.Line("set TimeRemaining -= 1.0");
            builder.Line("Minutes := Floor(TimeRemaining / 60.0)");
            builder.Line("Seconds := Floor(Mod(TimeRemaining, 60.0))");
            builder.Line("HUDDevice.SetText(\"Time: {Minutes}:{Seconds}\")");
            builder.Line("HUDDevice.Show()");
            builder.Line("Sleep(1.0)");
            builder.Dedent();
            builder.EndBlock();
        }

        // UpdateScore
        if (design.Scoring != null)
        {
            builder.Line();
            builder.MethodWithParams("UpdateScore", "NewScore:int", "void");
            builder.Line("set DisplayedScore = NewScore");
            builder.Line("HUDDevice.SetText(\"Score: {DisplayedScore}\")");
            builder.Line("HUDDevice.Show()");
            builder.EndBlock();
        }

        // ShowNotification
        builder.Line();
        builder.MethodWithParams("ShowNotification", "Message:string", "void");
        builder.Line("Print(Message)");
        builder.Line("HUDDevice.SetText(Message)");
        builder.Line("HUDDevice.Show()");
        builder.EndBlock();

        // Close class
        builder.EndBlock();

        var filePath = Path.Combine(outputPath, "Content", $"{className}.verse");
        File.WriteAllText(filePath, builder.ToString());
        return filePath;
    }

    private string? GenerateEconomyVerse(GameDesign design, string outputPath)
    {
        if (design.Economy == null) return null;

        var className = SanitizeClassName(design.Name) + "_economy";
        var builder = new VerseCodeBuilder();

        builder.Comment($"{design.Name} — Economy System");
        builder.Comment("Generated by WellVersed");
        builder.Line();
        builder.GameplayUsings();
        builder.Line();

        builder.ClassDef(className);

        builder.ImmutableVar("CurrencyName", "string", $"\"{design.Economy.CurrencyName}\"");
        builder.ImmutableVar("StartingAmount", "int", design.Economy.StartingAmount.ToString());
        builder.Var("PlayerBalances", "[player]int", "map{}");
        builder.Line();

        if (design.Economy.PassiveIncome)
        {
            builder.Editable("IncomeTimer", "timer_device", "timer_device{}");
            builder.ImmutableVar("PassiveIncomeAmount", "int", "10");
            builder.ImmutableVar("PassiveIncomeInterval", "float", "30.0");
            builder.Line();
        }

        // OnBegin
        builder.OnBegin();
        builder.Line("Print(\"Economy system initialized\")");
        if (design.Economy.PassiveIncome)
        {
            builder.Line("PassiveIncomeLoop()");
        }
        builder.EndBlock();

        // InitializePlayer
        builder.Line();
        builder.MethodWithParams("InitializePlayer", "Player:player", "void");
        builder.Line("if (set PlayerBalances[Player] = StartingAmount):");
        builder.Indent();
        builder.Line("Print(\"Player initialized with {StartingAmount} {CurrencyName}\")");
        builder.Dedent();
        builder.EndBlock();

        // AddCurrency
        builder.Line();
        builder.MethodWithParams("AddCurrency", "Player:player, Amount:int", "void");
        builder.Line("if (CurrentBalance := PlayerBalances[Player]):");
        builder.Indent();
        builder.Line("if (set PlayerBalances[Player] = CurrentBalance + Amount):");
        builder.Indent();
        builder.Line("Print(\"Added {Amount} {CurrencyName}\")");
        builder.Dedent();
        builder.Dedent();
        builder.EndBlock();

        // CanAfford
        builder.Line();
        builder.MethodWithParams("CanAfford", "Player:player, Cost:int", "logic");
        builder.Line("if (CurrentBalance := PlayerBalances[Player], CurrentBalance >= Cost):");
        builder.Indent();
        builder.Line("return true");
        builder.Dedent();
        builder.Line("return false");
        builder.EndBlock();

        // SpendCurrency
        builder.Line();
        builder.MethodWithParams("SpendCurrency", "Player:player, Cost:int", "logic");
        builder.Line("if (CurrentBalance := PlayerBalances[Player], CurrentBalance >= Cost):");
        builder.Indent();
        builder.Line("if (set PlayerBalances[Player] = CurrentBalance - Cost):");
        builder.Indent();
        builder.Line("Print(\"Spent {Cost} {CurrencyName}\")");
        builder.Line("return true");
        builder.Dedent();
        builder.Dedent();
        builder.Line("return false");
        builder.EndBlock();

        // Passive income loop
        if (design.Economy.PassiveIncome)
        {
            builder.Line();
            builder.Method("PassiveIncomeLoop", "void", suspends: true);
            builder.Line("loop:");
            builder.Indent();
            builder.Line("Sleep(PassiveIncomeInterval)");
            builder.Line("for (Key -> Balance : PlayerBalances):");
            builder.Indent();
            builder.Line("if (set PlayerBalances[Key] = Balance + PassiveIncomeAmount):");
            builder.Indent();
            builder.Line("Print(\"Granted {PassiveIncomeAmount} {CurrencyName} passive income\")");
            builder.Dedent();
            builder.Dedent();
            builder.Dedent();
            builder.EndBlock();
        }

        // Close class
        builder.EndBlock();

        var filePath = Path.Combine(outputPath, "Content", $"{className}.verse");
        File.WriteAllText(filePath, builder.ToString());
        return filePath;
    }

    private static string? GenerateMechanicVerse(string mechanic, GameDesign design, string outputPath)
    {
        // Only generate separate verse files for mechanics that need their own device
        var builder = new VerseCodeBuilder();
        string? className = null;

        switch (mechanic)
        {
            case "weapon_progression_on_kill":
                className = SanitizeClassName(design.Name) + "_weapon_ladder";
                builder.Comment($"{design.Name} — Weapon Progression");
                builder.Comment("Generated by WellVersed");
                builder.Line();
                builder.GameplayUsings();
                builder.Line();

                builder.ClassDef(className);

                // Editable weapon granters for each tier
                builder.Comment("Weapon granters — assign one per tier in UEFN");
                builder.Editable("WeaponGranterTier1", "item_granter_device", "item_granter_device{}");
                builder.Editable("WeaponGranterTier2", "item_granter_device", "item_granter_device{}");
                builder.Editable("WeaponGranterTier3", "item_granter_device", "item_granter_device{}");
                builder.Editable("WeaponGranterTier4", "item_granter_device", "item_granter_device{}");
                builder.Editable("WeaponGranterTier5", "item_granter_device", "item_granter_device{}");
                builder.Line();
                builder.Var("WeaponGranters", "[]item_granter_device", "array{}");
                builder.Var("PlayerWeaponTier", "[player]int", "map{}");
                builder.ImmutableVar("MaxTier", "int", "5");
                builder.Line();

                builder.OnBegin();
                builder.Line("set WeaponGranters = array{WeaponGranterTier1, WeaponGranterTier2, WeaponGranterTier3, WeaponGranterTier4, WeaponGranterTier5}");
                builder.Line("Print(\"Weapon ladder initialized\")");
                builder.EndBlock();

                builder.Line();
                builder.MethodWithParams("OnPlayerElimination", "Eliminator:player", "void");
                builder.Line("if (CurrentTier := PlayerWeaponTier[Eliminator]):");
                builder.Indent();
                builder.Line("NextTier := CurrentTier + 1");
                builder.Line("if (NextTier > MaxTier):");
                builder.Indent();
                builder.Line("Print(\"Player completed the weapon ladder!\")");
                builder.Dedent();
                builder.Line("else:");
                builder.Indent();
                builder.Line("if (set PlayerWeaponTier[Eliminator] = NextTier):");
                builder.Indent();
                builder.Line("GrantWeaponForTier(Eliminator, NextTier)");
                builder.Line("Print(\"Advanced to weapon tier {NextTier}\")");
                builder.Dedent();
                builder.Dedent();
                builder.Dedent();
                builder.EndBlock();

                builder.Line();
                builder.MethodWithParams("GrantWeaponForTier", "Player:player, Tier:int", "void");
                builder.Line("TierIndex := Tier - 1");
                builder.Line("if (Granter := WeaponGranters[TierIndex]):");
                builder.Indent();
                builder.Line("Granter.GrantItem(Player)");
                builder.Line("Print(\"Granted tier {Tier} weapon\")");
                builder.Dedent();
                builder.EndBlock();

                builder.EndBlock(); // class
                break;

            case "storm_phases":
                className = SanitizeClassName(design.Name) + "_storm_controller";
                builder.Comment($"{design.Name} — Storm Controller");
                builder.Comment("Generated by WellVersed");
                builder.Line();
                builder.StandardUsings();
                builder.Line();

                builder.ClassDef(className);
                builder.Editable("StormDevice", "storm_controller_device", "storm_controller_device{}");
                builder.Editable("PhaseHUD", "hud_message_device", "hud_message_device{}");
                builder.Var("CurrentPhase", "int", "0");
                builder.ImmutableVar("TotalPhases", "int", "5");
                builder.Comment("Phase durations in seconds — shrinks faster each phase");
                builder.ImmutableVar("PhaseDurations", "[]float", "array{60.0, 45.0, 30.0, 20.0, 15.0}");
                builder.Line();

                builder.OnBegin();
                builder.Line("Print(\"Storm controller active — {TotalPhases} phases\")");
                builder.Line("Sleep(10.0) # Initial grace period");
                builder.Line("StormLoop()");
                builder.EndBlock();

                builder.Line();
                builder.Method("StormLoop", "void", suspends: true);
                builder.Line("loop:");
                builder.Indent();
                builder.Line("set CurrentPhase += 1");
                builder.Line("if (CurrentPhase > TotalPhases):");
                builder.Indent();
                builder.Line("Print(\"Final storm phase complete\")");
                builder.Line("break");
                builder.Dedent();
                builder.Line("Print(\"Storm phase {CurrentPhase} of {TotalPhases}\")");
                builder.Line("PhaseHUD.SetText(\"Storm Phase {CurrentPhase}/{TotalPhases}\")");
                builder.Line("PhaseHUD.Show()");
                builder.Line("StormDevice.Activate()");
                builder.Comment("Wait for this phase's duration before advancing");
                builder.Line("PhaseDuration := PhaseDurations[CurrentPhase - 1] or 30.0");
                builder.Line("Sleep(PhaseDuration)");
                builder.Dedent();
                builder.EndBlock();

                builder.EndBlock(); // class
                break;

            default:
                // Other mechanics don't need separate files
                return null;
        }

        if (className == null) return null;

        var filePath = Path.Combine(outputPath, "Content", $"{className}.verse");
        File.WriteAllText(filePath, builder.ToString());
        return filePath;
    }

    private static string? GenerateWidgetSpec(UIRequirement uiNeed, GameDesign design, string outputPath, string projectName)
    {
        var widgetName = $"{SanitizeClassName(design.Name)}_{uiNeed.Type}";
        var spec = new
        {
            schema = "widget-spec-v1",
            name = widgetName,
            width = 1920,
            height = 1080,
            description = uiNeed.Description,
            root = BuildWidgetTree(uiNeed, design)
        };

        var filePath = Path.Combine(outputPath, "Plugins", projectName, "Content", $"{widgetName}.widget.json");
        File.WriteAllText(filePath, JsonSerializer.Serialize(spec, JsonOpts));
        return filePath;
    }

    private static object BuildWidgetTree(UIRequirement uiNeed, GameDesign design)
    {
        return uiNeed.Type switch
        {
            "HUD" => new
            {
                type = "CanvasPanel",
                name = "HUD_Root",
                children = new object[]
                {
                    new { type = "TextBlock", name = "ScoreText", properties = new { text = "Score: 0", fontSize = 24 } },
                    new { type = "TextBlock", name = "TimerText", properties = new { text = "5:00", fontSize = 32 } },
                    design.TeamCount > 0
                        ? new { type = "TextBlock", name = "TeamScoreText", properties = new { text = "Team A: 0 | Team B: 0", fontSize = 20 } }
                        : (object)new { type = "TextBlock", name = "PositionText", properties = new { text = "#1 / 16", fontSize = 20 } }
                }
            },
            "Scoreboard" => new
            {
                type = "CanvasPanel",
                name = "Scoreboard_Root",
                children = new object[]
                {
                    new { type = "TextBlock", name = "HeaderText", properties = new { text = design.Name, fontSize = 28 } },
                    new { type = "VerticalBox", name = "PlayerList", children = Array.Empty<object>() }
                }
            },
            "ShopUI" => new
            {
                type = "CanvasPanel",
                name = "Shop_Root",
                children = new object[]
                {
                    new { type = "TextBlock", name = "ShopTitle", properties = new { text = "Shop", fontSize = 28 } },
                    new
                    {
                        type = "TextBlock", name = "BalanceText",
                        properties = new { text = $"{design.Economy?.CurrencyName ?? "Gold"}: 0", fontSize = 22 }
                    },
                    new { type = "VerticalBox", name = "ItemList", children = Array.Empty<object>() }
                }
            },
            _ => new
            {
                type = "CanvasPanel",
                name = $"{uiNeed.Type}_Root",
                children = new object[]
                {
                    new { type = "TextBlock", name = "Label", properties = new { text = uiNeed.Description, fontSize = 20 } }
                }
            }
        };
    }

    private static void WriteDeviceManifest(string path, GenerationPlan plan)
    {
        var manifest = new
        {
            description = "Device placement manifest — reference for placing devices in UEFN",
            note = "Place these devices in your level at the listed positions relative to center",
            devices = plan.Devices.Select(d => new
            {
                role = d.Role,
                type = d.Type,
                deviceClass = d.DeviceClass,
                offset = new { x = d.Offset.X, y = d.Offset.Y, z = d.Offset.Z }
            }),
            wiring = plan.Wiring.Select(w => new
            {
                source = w.SourceRole,
                outputEvent = w.OutputEvent,
                target = w.TargetRole,
                inputAction = w.InputAction
            })
        };

        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOpts));
    }

    // =========================================================================
    //  HELPERS
    // =========================================================================

    private static string SanitizeProjectName(string name) =>
        Regex.Replace(name.Replace(" ", "_"), @"[^a-zA-Z0-9_]", "");

    private static string SanitizeClassName(string name) =>
        Regex.Replace(name.ToLower().Replace(" ", "_"), @"[^a-z0-9_]", "");

    private static string SanitizeVerseName(string name) =>
        Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");

    private static string DeviceClassToVerseType(string deviceClass) => deviceClass switch
    {
        "BP_PlayerSpawnerDevice_C" => "player_spawner_device",
        "BP_TriggerDevice_C" => "trigger_device",
        "BP_TimerDevice_C" => "timer_device",
        "BP_ScoreManagerDevice_C" => "score_manager_device",
        "BP_EliminationManagerDevice_C" => "elimination_manager_device",
        "BP_HUDMessageDevice_C" => "hud_message_device",
        "BP_BarrierDevice_C" => "barrier_device",
        "BP_VendingMachineDevice_C" => "vending_machine_device",
        "BP_ItemGranterDevice_C" => "item_granter_device",
        "BP_VFXSpawnerDevice_C" => "vfx_spawner_device",
        "BP_ButtonDevice_C" => "button_device",
        "BP_BillboardDevice_C" => "billboard_device",
        "BP_RoundSettingsDevice_C" => "round_settings_device",
        "BP_StormControllerDevice_C" => "storm_controller_device",
        _ => "creative_device"
    };
}
