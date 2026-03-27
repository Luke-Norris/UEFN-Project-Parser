using System.Text.RegularExpressions;
using WellVersed.Core.Models;
using Microsoft.Extensions.Logging;

namespace WellVersed.Core.Services;

/// <summary>
/// Layer 1: Game Designer — takes a natural language description and produces
/// a structured GameDesign. Uses pattern matching to extract game mode, player
/// counts, round structure, scoring, objectives, economy, and UI needs.
///
/// Also converts a GameDesign into a GenerationPlan (device placement, wiring,
/// and verse code) that the ProjectAssembler can execute.
/// </summary>
public class GameDesigner
{
    private readonly ILogger<GameDesigner> _logger;

    public GameDesigner(ILogger<GameDesigner> logger)
    {
        _logger = logger;
    }

    // =========================================================================
    //  TEMPLATES
    // =========================================================================

    private static readonly List<GameTemplate> Templates = new()
    {
        // 1. Team Deathmatch
        new GameTemplate
        {
            Id = "team_deathmatch",
            Name = "Team Deathmatch",
            Description = "Classic 2-team elimination combat with round-based scoring and respawns.",
            Category = "Combat",
            Tags = new() { "teams", "elimination", "rounds", "respawn" },
            Design = new GameDesign
            {
                Name = "Team Deathmatch",
                Description = "Two teams compete to reach the elimination target within the time limit. " +
                    "Players respawn after elimination. Most team eliminations wins the round.",
                GameMode = GameModeType.TeamBased,
                PlayerCount = 16,
                TeamCount = 2,
                Rounds = new RoundConfig
                {
                    RoundCount = 3,
                    RoundDurationSeconds = 300,
                    WarmupSeconds = 10,
                    AutoRestart = true
                },
                Scoring = new ScoringConfig
                {
                    WinCondition = "Most eliminations when time expires or first to target score",
                    WinScore = 50,
                    TrackKills = true,
                    TrackObjectives = false
                },
                SpawnAreas = new()
                {
                    new SpawnConfig { TeamName = "Team A", SpawnCount = 4, HasProtection = true, ProtectionDuration = 5 },
                    new SpawnConfig { TeamName = "Team B", SpawnCount = 4, HasProtection = true, ProtectionDuration = 5 }
                },
                UINeeds = new()
                {
                    new UIRequirement { Type = "HUD", Description = "Kill count, team scores, timer" },
                    new UIRequirement { Type = "Scoreboard", Description = "Team-based scoreboard with K/D" },
                    new UIRequirement { Type = "Notification", Description = "Kill feed and round transitions" }
                },
                SpecialMechanics = new() { "respawn_on_elimination", "team_scoring" }
            }
        },

        // 2. Capture the Flag
        new GameTemplate
        {
            Id = "capture_the_flag",
            Name = "Capture the Flag",
            Description = "Two teams compete to capture the enemy flag and return it to base for points.",
            Category = "Objective",
            Tags = new() { "teams", "objective", "capture", "flag" },
            Design = new GameDesign
            {
                Name = "Capture the Flag",
                Description = "Two teams each defend a flag at their base. Grab the enemy flag and bring it " +
                    "back to your own base to score. First team to the capture limit wins.",
                GameMode = GameModeType.TeamBased,
                PlayerCount = 16,
                TeamCount = 2,
                Rounds = new RoundConfig
                {
                    RoundCount = 2,
                    RoundDurationSeconds = 600,
                    WarmupSeconds = 15,
                    AutoRestart = true
                },
                Scoring = new ScoringConfig
                {
                    WinCondition = "First team to capture limit or most captures at time",
                    WinScore = 3,
                    TrackKills = true,
                    TrackObjectives = true
                },
                Objectives = new()
                {
                    new ObjectiveConfig { Type = "capture", Description = "Team A flag — defend and return", Radius = 200 },
                    new ObjectiveConfig { Type = "capture", Description = "Team B flag — defend and return", Radius = 200 },
                    new ObjectiveConfig { Type = "return_zone", Description = "Team A return zone", Radius = 300 },
                    new ObjectiveConfig { Type = "return_zone", Description = "Team B return zone", Radius = 300 }
                },
                SpawnAreas = new()
                {
                    new SpawnConfig { TeamName = "Team A", SpawnCount = 4, HasProtection = true, ProtectionDuration = 5 },
                    new SpawnConfig { TeamName = "Team B", SpawnCount = 4, HasProtection = true, ProtectionDuration = 5 }
                },
                UINeeds = new()
                {
                    new UIRequirement { Type = "HUD", Description = "Flag status indicators, team scores, timer" },
                    new UIRequirement { Type = "Scoreboard", Description = "Captures, kills, assists per player" },
                    new UIRequirement { Type = "Notification", Description = "Flag picked up/dropped/captured/returned alerts" }
                },
                SpecialMechanics = new() { "flag_pickup", "flag_drop_on_elimination", "flag_return_timer", "spawn_protection" }
            }
        },

        // 3. Free For All
        new GameTemplate
        {
            Id = "free_for_all",
            Name = "Free For All",
            Description = "Every player for themselves. Eliminate opponents to score. First to target wins.",
            Category = "Combat",
            Tags = new() { "ffa", "elimination", "solo" },
            Design = new GameDesign
            {
                Name = "Free For All",
                Description = "16-player free-for-all elimination. Score points for each elimination. " +
                    "First player to reach the target score or most points at time wins.",
                GameMode = GameModeType.FreeForAll,
                PlayerCount = 16,
                TeamCount = 0,
                Rounds = new RoundConfig
                {
                    RoundCount = 1,
                    RoundDurationSeconds = 600,
                    WarmupSeconds = 10,
                    AutoRestart = false
                },
                Scoring = new ScoringConfig
                {
                    WinCondition = "First to target score or most eliminations at time",
                    WinScore = 25,
                    TrackKills = true,
                    TrackObjectives = false
                },
                SpawnAreas = new()
                {
                    new SpawnConfig { TeamName = "FFA", SpawnCount = 16, HasProtection = true, ProtectionDuration = 3 }
                },
                UINeeds = new()
                {
                    new UIRequirement { Type = "HUD", Description = "Personal kill count, timer, position" },
                    new UIRequirement { Type = "Scoreboard", Description = "All-player leaderboard" },
                    new UIRequirement { Type = "Notification", Description = "Kill feed" }
                },
                SpecialMechanics = new() { "instant_respawn", "random_spawn_selection" }
            }
        },

        // 4. Tycoon
        new GameTemplate
        {
            Id = "tycoon",
            Name = "Tycoon",
            Description = "Build and upgrade your empire with passive income, upgradeable stations, and unlockable areas.",
            Category = "Economy",
            Tags = new() { "economy", "upgrades", "passive_income", "solo" },
            Design = new GameDesign
            {
                Name = "Tycoon",
                Description = "Start with a small base. Earn passive income over time. Buy upgrades to " +
                    "unlock new areas, increase income rate, and expand your empire.",
                GameMode = GameModeType.Tycoon,
                PlayerCount = 1,
                TeamCount = 0,
                Scoring = new ScoringConfig
                {
                    WinCondition = "Unlock all upgrades and reach max tier",
                    WinScore = 0,
                    TrackKills = false,
                    TrackObjectives = true
                },
                Economy = new EconomyConfig
                {
                    HasCurrency = true,
                    CurrencyName = "Gold",
                    StartingAmount = 100,
                    PassiveIncome = true,
                    Purchasables = new() { "Speed Boost", "Income Multiplier", "Area Unlock Tier 1", "Area Unlock Tier 2", "Area Unlock Tier 3" }
                },
                UINeeds = new()
                {
                    new UIRequirement { Type = "HUD", Description = "Currency balance, income rate, tier progress" },
                    new UIRequirement { Type = "ShopUI", Description = "Upgrade purchase menu with costs and descriptions" },
                    new UIRequirement { Type = "Notification", Description = "Purchase confirmations and unlock alerts" }
                },
                SpecialMechanics = new() { "passive_income_timer", "tiered_unlocks", "persistent_progress" }
            }
        },

        // 5. Zone Wars
        new GameTemplate
        {
            Id = "zone_wars",
            Name = "Zone Wars",
            Description = "FFA storm-closing combat. Zone shrinks over time. Last player standing wins the round.",
            Category = "Combat",
            Tags = new() { "ffa", "storm", "last_standing", "building" },
            Design = new GameDesign
            {
                Name = "Zone Wars",
                Description = "Free-for-all with a shrinking storm zone. Players compete to be the last " +
                    "one standing as the safe zone closes in. Multiple quick rounds.",
                GameMode = GameModeType.Rounds,
                PlayerCount = 16,
                TeamCount = 0,
                Rounds = new RoundConfig
                {
                    RoundCount = 10,
                    RoundDurationSeconds = 120,
                    WarmupSeconds = 5,
                    AutoRestart = true
                },
                Scoring = new ScoringConfig
                {
                    WinCondition = "Last player standing wins round, most round wins overall",
                    WinScore = 5,
                    TrackKills = true,
                    TrackObjectives = false
                },
                Objectives = new()
                {
                    new ObjectiveConfig { Type = "storm_zone", Description = "Shrinking safe zone", Radius = 5000 }
                },
                SpawnAreas = new()
                {
                    new SpawnConfig { TeamName = "FFA", SpawnCount = 16, HasProtection = false, ProtectionDuration = 0 }
                },
                UINeeds = new()
                {
                    new UIRequirement { Type = "HUD", Description = "Storm timer, players alive, round counter" },
                    new UIRequirement { Type = "Scoreboard", Description = "Round wins per player" },
                    new UIRequirement { Type = "Notification", Description = "Storm phase changes, eliminations" }
                },
                SpecialMechanics = new() { "storm_phases", "no_respawn_in_round", "round_reset" }
            }
        },

        // 6. Gun Game
        new GameTemplate
        {
            Id = "gun_game",
            Name = "Gun Game",
            Description = "FFA with weapon progression. Each elimination advances to the next weapon. First to complete the weapon ladder wins.",
            Category = "Combat",
            Tags = new() { "ffa", "weapon_progression", "elimination" },
            Design = new GameDesign
            {
                Name = "Gun Game",
                Description = "Every player starts with the same weapon. Each elimination upgrades to the " +
                    "next weapon in the ladder. First player to get an elimination with every weapon wins.",
                GameMode = GameModeType.Progressive,
                PlayerCount = 16,
                TeamCount = 0,
                Rounds = new RoundConfig
                {
                    RoundCount = 1,
                    RoundDurationSeconds = 900,
                    WarmupSeconds = 10,
                    AutoRestart = false
                },
                Scoring = new ScoringConfig
                {
                    WinCondition = "First to complete the weapon ladder",
                    WinScore = 20,
                    TrackKills = true,
                    TrackObjectives = false
                },
                SpawnAreas = new()
                {
                    new SpawnConfig { TeamName = "FFA", SpawnCount = 16, HasProtection = true, ProtectionDuration = 2 }
                },
                UINeeds = new()
                {
                    new UIRequirement { Type = "HUD", Description = "Current weapon tier, progress bar, leader info" },
                    new UIRequirement { Type = "Scoreboard", Description = "Player weapon tiers and kill counts" },
                    new UIRequirement { Type = "Notification", Description = "Weapon upgrade notifications, leader alerts" }
                },
                SpecialMechanics = new() { "weapon_progression_on_kill", "instant_respawn", "weapon_swap_on_advance", "demotion_on_melee_kill" }
            }
        }
    };

    // =========================================================================
    //  DESIGN FROM DESCRIPTION
    // =========================================================================

    /// <summary>
    /// Takes a natural language description and produces a structured GameDesign.
    /// Uses pattern matching to identify game mode, counts, mechanics, and UI needs.
    /// </summary>
    public GameDesign DesignGame(string description, int? playerCount = null, int? teamCount = null)
    {
        var lower = description.ToLower();
        var design = new GameDesign
        {
            Description = description,
            PlayerCount = playerCount ?? ExtractPlayerCount(lower),
            TeamCount = teamCount ?? ExtractTeamCount(lower)
        };

        // Classify game mode
        design.GameMode = ClassifyGameMode(lower, design.TeamCount);

        // Generate a name from the description
        design.Name = GenerateName(lower, design.GameMode);

        // Extract round structure
        design.Rounds = ExtractRounds(lower, design.GameMode);

        // Extract scoring
        design.Scoring = ExtractScoring(lower, design.GameMode);

        // Extract objectives
        design.Objectives = ExtractObjectives(lower);

        // Build spawn areas
        design.SpawnAreas = BuildSpawnAreas(design);

        // Extract economy
        design.Economy = ExtractEconomy(lower);

        // Extract special mechanics
        design.SpecialMechanics = ExtractSpecialMechanics(lower);

        // Infer UI needs from mechanics
        design.UINeeds = InferUINeeds(design);

        _logger.LogInformation("Designed game: {Name} ({Mode}, {Players}p, {Teams}t)",
            design.Name, design.GameMode, design.PlayerCount, design.TeamCount);

        return design;
    }

    // =========================================================================
    //  DESIGN → GENERATION PLAN
    // =========================================================================

    /// <summary>
    /// Converts a GameDesign into a GenerationPlan with concrete devices, wiring,
    /// and verse code specifications. Produces COMPLETE device lists with meaningful
    /// spatial offsets and all required wiring connections.
    /// </summary>
    public GenerationPlan DesignToDevicePlan(GameDesign design)
    {
        var plan = new GenerationPlan
        {
            Description = design.Description,
            SystemName = design.Name,
            Category = design.GameMode.ToString().ToLower()
        };

        // ── Spatial layout zones ────────────────────────────────────────
        // Spawns:     perimeter ring (radius ~3000-5000)
        // Objectives: center or strategic positions
        // Scoring:    management area (elevated, Y offset -2000)
        // Economy:    shop cluster (separate area, Y offset -4000)
        // Rounds:     management area near scoring
        // HUD/Mgmt:   elevated (Z=500) near center

        // ── 1. Spawn areas → Player Spawner + Barrier + Protection Timer ─
        AddSpawnDevices(plan, design);

        // ── 2. Objectives → Trigger + Timer + Score wiring ──────────────
        AddObjectiveDevices(plan, design);

        // ── 3. Scoring → Score Manager + Elimination Manager + HUD ──────
        AddScoringDevices(plan, design);

        // ── 4. Rounds → Timer + Round Settings + VFX announcements ──────
        AddRoundDevices(plan, design);

        // ── 5. Economy → Vending Machines + Item Granter + Income ───────
        AddEconomyDevices(plan, design);

        // ── 6. Game-mode-specific devices ───────────────────────────────
        AddGameModeDevices(plan, design);

        // ── 7. Wire scoring to end-game ─────────────────────────────────
        WireEndGameConditions(plan, design);

        // Build verse code needs
        plan.VerseCode = BuildVerseCodePlan(design);

        return plan;
    }

    private static void AddSpawnDevices(GenerationPlan plan, GameDesign design)
    {
        foreach (var spawn in design.SpawnAreas)
        {
            var teamIdx = design.SpawnAreas.IndexOf(spawn);
            for (int i = 0; i < spawn.SpawnCount; i++)
            {
                var offset = CalculateSpawnOffset(spawn.TeamName, i, spawn.SpawnCount, teamIdx);
                plan.Devices.Add(new PlannedDevice
                {
                    Role = $"spawn_{SanitizeName(spawn.TeamName)}_{i + 1}",
                    DeviceClass = "BP_PlayerSpawnerDevice_C",
                    Type = "Player Spawner",
                    Offset = offset
                });
            }

            if (spawn.HasProtection)
            {
                var barrierOffset = CalculateSpawnOffset(spawn.TeamName, 0, 1, teamIdx);
                plan.Devices.Add(new PlannedDevice
                {
                    Role = $"barrier_{SanitizeName(spawn.TeamName)}",
                    DeviceClass = "BP_BarrierDevice_C",
                    Type = "Barrier Device",
                    Offset = barrierOffset
                });

                var timerRole = $"protection_timer_{SanitizeName(spawn.TeamName)}";
                plan.Devices.Add(new PlannedDevice
                {
                    Role = timerRole,
                    DeviceClass = "BP_TimerDevice_C",
                    Type = "Timer Device",
                    Offset = new Vector3Info(barrierOffset.X, barrierOffset.Y + 200, barrierOffset.Z)
                });

                plan.Wiring.Add(new PlannedWiring
                {
                    SourceRole = timerRole,
                    OutputEvent = "OnCompleted",
                    TargetRole = $"barrier_{SanitizeName(spawn.TeamName)}",
                    InputAction = "Disable"
                });
            }
        }
    }

    private static void AddObjectiveDevices(GenerationPlan plan, GameDesign design)
    {
        int objIndex = 0;
        var totalObj = Math.Max(design.Objectives.Count, 1);

        foreach (var obj in design.Objectives)
        {
            // Distribute objectives around the center in a circle
            var angle = (2.0 * Math.PI * objIndex) / totalObj;
            var objRadius = 1500f;
            var objX = (float)(Math.Cos(angle) * objRadius);
            var objY = (float)(Math.Sin(angle) * objRadius);

            switch (obj.Type.ToLower())
            {
                case "capture":
                case "control":
                case "zone":
                    plan.Devices.Add(new PlannedDevice
                    {
                        Role = $"objective_trigger_{objIndex}",
                        DeviceClass = "BP_TriggerDevice_C",
                        Type = "Trigger Device",
                        Offset = new Vector3Info(objX, objY, 0)
                    });
                    plan.Devices.Add(new PlannedDevice
                    {
                        Role = $"objective_timer_{objIndex}",
                        DeviceClass = "BP_TimerDevice_C",
                        Type = "Timer Device",
                        Offset = new Vector3Info(objX + 200, objY, 0)
                    });
                    // Billboard to mark the objective visually
                    plan.Devices.Add(new PlannedDevice
                    {
                        Role = $"objective_marker_{objIndex}",
                        DeviceClass = "BP_BillboardDevice_C",
                        Type = "Billboard",
                        Offset = new Vector3Info(objX, objY, 300)
                    });
                    plan.Wiring.Add(new PlannedWiring
                    {
                        SourceRole = $"objective_trigger_{objIndex}",
                        OutputEvent = "OnTriggered",
                        TargetRole = $"objective_timer_{objIndex}",
                        InputAction = "Start"
                    });
                    plan.Wiring.Add(new PlannedWiring
                    {
                        SourceRole = $"objective_trigger_{objIndex}",
                        OutputEvent = "OnUntriggered",
                        TargetRole = $"objective_timer_{objIndex}",
                        InputAction = "Pause"
                    });
                    // Objective complete → increment score
                    if (plan.Devices.Any(d => d.Role == "score_manager") ||
                        design.Scoring is { TrackObjectives: true })
                    {
                        plan.Wiring.Add(new PlannedWiring
                        {
                            SourceRole = $"objective_timer_{objIndex}",
                            OutputEvent = "OnCompleted",
                            TargetRole = "score_manager",
                            InputAction = "IncrementScore"
                        });
                    }
                    break;

                case "return_zone":
                    // Return zones placed near team bases (offset from spawn side)
                    var returnSide = objIndex % 2 == 0 ? -1 : 1;
                    plan.Devices.Add(new PlannedDevice
                    {
                        Role = $"return_zone_{objIndex}",
                        DeviceClass = "BP_TriggerDevice_C",
                        Type = "Trigger Device",
                        Offset = new Vector3Info(returnSide * 2500f, objY, 0)
                    });
                    break;

                case "storm_zone":
                    // Storm controller at the map center, elevated
                    plan.Devices.Add(new PlannedDevice
                    {
                        Role = $"storm_controller_{objIndex}",
                        DeviceClass = "BP_StormControllerDevice_C",
                        Type = "Storm Controller",
                        Offset = new Vector3Info(0, 0, 500)
                    });
                    break;

                default:
                    plan.Devices.Add(new PlannedDevice
                    {
                        Role = $"objective_{objIndex}",
                        DeviceClass = "BP_TriggerDevice_C",
                        Type = "Trigger Device",
                        Offset = new Vector3Info(objX, objY, 0)
                    });
                    break;
            }
            objIndex++;
        }
    }

    private static void AddScoringDevices(GenerationPlan plan, GameDesign design)
    {
        if (design.Scoring == null)
            return;

        // Management area: offset south from center, elevated
        var mgmtY = -2000f;
        var mgmtZ = 500f;

        plan.Devices.Add(new PlannedDevice
        {
            Role = "score_manager",
            DeviceClass = "BP_ScoreManagerDevice_C",
            Type = "Score Manager",
            Offset = new Vector3Info(0, mgmtY, mgmtZ)
        });

        if (design.Scoring.TrackKills)
        {
            plan.Devices.Add(new PlannedDevice
            {
                Role = "elimination_manager",
                DeviceClass = "BP_EliminationManagerDevice_C",
                Type = "Elimination Manager",
                Offset = new Vector3Info(400, mgmtY, mgmtZ)
            });
            plan.Wiring.Add(new PlannedWiring
            {
                SourceRole = "elimination_manager",
                OutputEvent = "OnElimination",
                TargetRole = "score_manager",
                InputAction = "IncrementScore"
            });
        }

        // HUD message device for in-game announcements
        plan.Devices.Add(new PlannedDevice
        {
            Role = "hud_message",
            DeviceClass = "BP_HUDMessageDevice_C",
            Type = "HUD Message",
            Offset = new Vector3Info(800, mgmtY, mgmtZ)
        });

        // Wire score changes to HUD
        plan.Wiring.Add(new PlannedWiring
        {
            SourceRole = "score_manager",
            OutputEvent = "OnScoreReached",
            TargetRole = "hud_message",
            InputAction = "Show"
        });

        // VFX for score events
        plan.Devices.Add(new PlannedDevice
        {
            Role = "score_vfx",
            DeviceClass = "BP_VFXSpawnerDevice_C",
            Type = "VFX Spawner",
            Offset = new Vector3Info(0, mgmtY + 200, mgmtZ + 200)
        });
        plan.Wiring.Add(new PlannedWiring
        {
            SourceRole = "score_manager",
            OutputEvent = "OnScoreReached",
            TargetRole = "score_vfx",
            InputAction = "Activate"
        });
    }

    private static void AddRoundDevices(GenerationPlan plan, GameDesign design)
    {
        if (design.Rounds == null)
            return;

        // Round management area: near scoring, offset east
        var roundY = -2000f;
        var roundX = -800f;
        var roundZ = 500f;

        plan.Devices.Add(new PlannedDevice
        {
            Role = "round_timer",
            DeviceClass = "BP_TimerDevice_C",
            Type = "Timer Device",
            Offset = new Vector3Info(roundX, roundY, roundZ)
        });

        if (design.Rounds.RoundCount > 1)
        {
            plan.Devices.Add(new PlannedDevice
            {
                Role = "round_settings",
                DeviceClass = "BP_RoundSettingsDevice_C",
                Type = "Round Settings",
                Offset = new Vector3Info(roundX - 400, roundY, roundZ)
            });

            // Wire round timer to round settings
            plan.Wiring.Add(new PlannedWiring
            {
                SourceRole = "round_timer",
                OutputEvent = "OnTimerComplete",
                TargetRole = "round_settings",
                InputAction = "EndRound"
            });

            // Round start → start the round timer
            plan.Wiring.Add(new PlannedWiring
            {
                SourceRole = "round_settings",
                OutputEvent = "OnRoundStart",
                TargetRole = "round_timer",
                InputAction = "Start"
            });

            // Round end announcement
            if (plan.Devices.Any(d => d.Role == "hud_message"))
            {
                plan.Wiring.Add(new PlannedWiring
                {
                    SourceRole = "round_settings",
                    OutputEvent = "OnRoundEnd",
                    TargetRole = "hud_message",
                    InputAction = "Show"
                });
            }
        }

        // Round transition VFX
        plan.Devices.Add(new PlannedDevice
        {
            Role = "round_vfx",
            DeviceClass = "BP_VFXSpawnerDevice_C",
            Type = "VFX Spawner",
            Offset = new Vector3Info(roundX, roundY + 200, roundZ + 200)
        });
    }

    private static void AddEconomyDevices(GenerationPlan plan, GameDesign design)
    {
        if (design.Economy is not { HasCurrency: true })
            return;

        // Shop area: grouped cluster, offset from main play area
        var shopBaseX = -2000f;
        var shopBaseY = -4000f;
        var purchaseCount = Math.Min(design.Economy.Purchasables.Count, 6);

        for (int i = 0; i < Math.Max(purchaseCount, 1); i++)
        {
            // Arrange vendors in an arc
            var vendorAngle = (Math.PI * i) / Math.Max(purchaseCount - 1, 1);
            var vendorX = shopBaseX + (float)(Math.Cos(vendorAngle) * 600);
            var vendorY = shopBaseY + (float)(Math.Sin(vendorAngle) * 400);

            plan.Devices.Add(new PlannedDevice
            {
                Role = $"vendor_{i + 1}",
                DeviceClass = "BP_VendingMachineDevice_C",
                Type = "Vending Machine",
                Offset = new Vector3Info(vendorX, vendorY, 0)
            });
        }

        // Item granter at the center of the shop area
        plan.Devices.Add(new PlannedDevice
        {
            Role = "item_granter",
            DeviceClass = "BP_ItemGranterDevice_C",
            Type = "Item Granter",
            Offset = new Vector3Info(shopBaseX, shopBaseY - 300, 0)
        });

        // Wire all vendors to item granter
        for (int i = 0; i < Math.Max(purchaseCount, 1); i++)
        {
            plan.Wiring.Add(new PlannedWiring
            {
                SourceRole = $"vendor_{i + 1}",
                OutputEvent = "OnItemPurchased",
                TargetRole = "item_granter",
                InputAction = "GrantItem"
            });
        }

        // Shop billboard sign
        plan.Devices.Add(new PlannedDevice
        {
            Role = "shop_sign",
            DeviceClass = "BP_BillboardDevice_C",
            Type = "Billboard",
            Offset = new Vector3Info(shopBaseX, shopBaseY + 400, 400)
        });

        if (design.Economy.PassiveIncome)
        {
            plan.Devices.Add(new PlannedDevice
            {
                Role = "income_timer",
                DeviceClass = "BP_TimerDevice_C",
                Type = "Timer Device",
                Offset = new Vector3Info(shopBaseX + 800, shopBaseY, 0)
            });
            // Self-looping timer for passive income
            plan.Wiring.Add(new PlannedWiring
            {
                SourceRole = "income_timer",
                OutputEvent = "OnCompleted",
                TargetRole = "income_timer",
                InputAction = "Start"
            });
        }
    }

    private static void AddGameModeDevices(GenerationPlan plan, GameDesign design)
    {
        // Add mode-specific devices that aren't covered by the generic sections above
        switch (design.GameMode)
        {
            case GameModeType.TeamBased:
                // Team settings device
                plan.Devices.Add(new PlannedDevice
                {
                    Role = "team_settings",
                    DeviceClass = "BP_TeamSettingsDevice_C",
                    Type = "Team Settings",
                    Offset = new Vector3Info(0, -2500, 500)
                });
                break;

            case GameModeType.Progressive:
                // Weapon granters for gun-game weapon ladder
                for (int tier = 0; tier < 5; tier++)
                {
                    plan.Devices.Add(new PlannedDevice
                    {
                        Role = $"weapon_granter_tier_{tier + 1}",
                        DeviceClass = "BP_ItemGranterDevice_C",
                        Type = "Item Granter",
                        Offset = new Vector3Info(1500 + tier * 300, -2000, 500)
                    });
                }
                break;

            case GameModeType.Rounds:
                // Storm-related: if storm objectives exist, add phase timer
                if (design.Objectives.Any(o => o.Type.Equals("storm_zone", StringComparison.OrdinalIgnoreCase)))
                {
                    plan.Devices.Add(new PlannedDevice
                    {
                        Role = "storm_phase_timer",
                        DeviceClass = "BP_TimerDevice_C",
                        Type = "Timer Device",
                        Offset = new Vector3Info(400, 0, 500)
                    });
                    if (plan.Devices.Any(d => d.Role.StartsWith("storm_controller")))
                    {
                        plan.Wiring.Add(new PlannedWiring
                        {
                            SourceRole = "storm_phase_timer",
                            OutputEvent = "OnTimerComplete",
                            TargetRole = plan.Devices.First(d => d.Role.StartsWith("storm_controller")).Role,
                            InputAction = "Activate"
                        });
                    }
                }
                break;

            case GameModeType.Tycoon:
                // Button devices for tier unlocks
                for (int tier = 0; tier < 3; tier++)
                {
                    var unlockX = -2000f + tier * 600;
                    plan.Devices.Add(new PlannedDevice
                    {
                        Role = $"unlock_button_tier_{tier + 1}",
                        DeviceClass = "BP_ButtonDevice_C",
                        Type = "Button Device",
                        Offset = new Vector3Info(unlockX, -5000, 0)
                    });
                    plan.Devices.Add(new PlannedDevice
                    {
                        Role = $"unlock_barrier_tier_{tier + 1}",
                        DeviceClass = "BP_BarrierDevice_C",
                        Type = "Barrier Device",
                        Offset = new Vector3Info(unlockX, -5500, 0)
                    });
                    plan.Wiring.Add(new PlannedWiring
                    {
                        SourceRole = $"unlock_button_tier_{tier + 1}",
                        OutputEvent = "OnButtonPressed",
                        TargetRole = $"unlock_barrier_tier_{tier + 1}",
                        InputAction = "Disable"
                    });
                }
                break;
        }
    }

    private static void WireEndGameConditions(GenerationPlan plan, GameDesign design)
    {
        // If scoring exists and we have round settings or a win score, wire them together
        if (design.Scoring == null) return;

        bool hasRoundSettings = plan.Devices.Any(d => d.Role == "round_settings");
        bool hasScoreManager = plan.Devices.Any(d => d.Role == "score_manager");

        // Score threshold → end game
        if (hasScoreManager && design.Scoring.WinScore > 0)
        {
            if (hasRoundSettings)
            {
                plan.Wiring.Add(new PlannedWiring
                {
                    SourceRole = "score_manager",
                    OutputEvent = "OnScoreReached",
                    TargetRole = "round_settings",
                    InputAction = "EndRound"
                });
            }
        }

        // Round timer expiration → end round (if not already wired)
        bool hasRoundTimerToSettings = plan.Wiring.Any(w =>
            w.SourceRole == "round_timer" && w.TargetRole == "round_settings");
        if (!hasRoundTimerToSettings && hasRoundSettings && plan.Devices.Any(d => d.Role == "round_timer"))
        {
            plan.Wiring.Add(new PlannedWiring
            {
                SourceRole = "round_timer",
                OutputEvent = "OnTimerComplete",
                TargetRole = "round_settings",
                InputAction = "EndRound"
            });
        }
    }

    // =========================================================================
    //  TEMPLATE ACCESS
    // =========================================================================

    /// <summary>
    /// Returns all pre-built game templates.
    /// </summary>
    public static List<GameTemplate> GetTemplates() => Templates;

    /// <summary>
    /// Gets a specific template by ID.
    /// </summary>
    public static GameTemplate? GetTemplate(string id) =>
        Templates.Find(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    // =========================================================================
    //  PATTERN MATCHING — GAME MODE
    // =========================================================================

    private static GameModeType ClassifyGameMode(string lower, int teamCount)
    {
        if (lower.Contains("tycoon") || lower.Contains("idle") || lower.Contains("clicker"))
            return GameModeType.Tycoon;
        if (lower.Contains("survival") || lower.Contains("survive") || lower.Contains("pve"))
            return GameModeType.Survival;
        if (lower.Contains("gun game") || lower.Contains("weapon progress") || lower.Contains("arms race"))
            return GameModeType.Progressive;
        if (lower.Contains("zone war") || lower.Contains("storm") && lower.Contains("shrink"))
            return GameModeType.Rounds;
        if (lower.Contains("round") || lower.Contains("best of"))
            return GameModeType.Rounds;
        if (teamCount > 0 || lower.Contains("team") || lower.Contains("capture the flag") ||
            lower.Contains("ctf") || lower.Contains("vs"))
            return GameModeType.TeamBased;
        if (lower.Contains("free for all") || lower.Contains("ffa") || lower.Contains("deathmatch") ||
            lower.Contains("elimination"))
            return GameModeType.FreeForAll;

        return GameModeType.Custom;
    }

    // =========================================================================
    //  PATTERN MATCHING — EXTRACTION
    // =========================================================================

    private static int ExtractPlayerCount(string lower)
    {
        // Look for "N-player", "N players", "Nv", "NvN"
        var match = Regex.Match(lower, @"(\d+)\s*[-]?\s*player");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var pc))
            return Math.Clamp(pc, 1, 100);

        match = Regex.Match(lower, @"(\d+)\s*v\s*(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var a) && int.TryParse(match.Groups[2].Value, out var b))
            return a + b;

        if (lower.Contains("solo") || lower.Contains("single"))
            return 1;

        return 16; // default
    }

    private static int ExtractTeamCount(string lower)
    {
        var match = Regex.Match(lower, @"(\d+)\s*team");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var tc))
            return Math.Clamp(tc, 0, 16);

        // NvN patterns
        match = Regex.Match(lower, @"(\d+)\s*v\s*(\d+)\s*v\s*(\d+)\s*v\s*(\d+)");
        if (match.Success) return 4;
        match = Regex.Match(lower, @"(\d+)\s*v\s*(\d+)\s*v\s*(\d+)");
        if (match.Success) return 3;
        match = Regex.Match(lower, @"(\d+)\s*v\s*(\d+)");
        if (match.Success) return 2;

        if (lower.Contains("team") || lower.Contains("capture the flag") || lower.Contains("ctf"))
            return 2;
        if (lower.Contains("ffa") || lower.Contains("free for all") || lower.Contains("solo"))
            return 0;

        return 0; // default FFA
    }

    private static RoundConfig? ExtractRounds(string lower, GameModeType mode)
    {
        var config = new RoundConfig
        {
            WarmupSeconds = 10,
            AutoRestart = true
        };

        // Extract round count
        var match = Regex.Match(lower, @"(\d+)\s*round");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var rc))
            config.RoundCount = Math.Clamp(rc, 1, 50);
        else if (lower.Contains("best of 3")) config.RoundCount = 3;
        else if (lower.Contains("best of 5")) config.RoundCount = 5;
        else
        {
            config.RoundCount = mode switch
            {
                GameModeType.Rounds => 5,
                GameModeType.TeamBased => 3,
                _ => 1
            };
        }

        // Extract round duration
        match = Regex.Match(lower, @"(\d+)\s*(?:second|sec|s)\s*(?:round|match|per round)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var secs))
            config.RoundDurationSeconds = secs;
        else
        {
            match = Regex.Match(lower, @"(\d+)\s*(?:minute|min|m)\s*(?:round|match|per round|each)?");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var mins))
                config.RoundDurationSeconds = mins * 60;
            else
                config.RoundDurationSeconds = mode switch
                {
                    GameModeType.Rounds => 120,
                    GameModeType.TeamBased => 300,
                    GameModeType.FreeForAll => 600,
                    _ => 300
                };
        }

        // Extract warmup
        match = Regex.Match(lower, @"(\d+)\s*(?:second|sec|s)\s*warmup");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var warmup))
            config.WarmupSeconds = warmup;

        if (lower.Contains("no warmup"))
            config.WarmupSeconds = 0;

        return config;
    }

    private static ScoringConfig? ExtractScoring(string lower, GameModeType mode)
    {
        var config = new ScoringConfig();

        // Track kills if elimination-related
        config.TrackKills = lower.Contains("kill") || lower.Contains("elim") ||
            lower.Contains("deathmatch") || lower.Contains("combat") ||
            mode is GameModeType.FreeForAll or GameModeType.TeamBased or GameModeType.Progressive or GameModeType.Rounds;

        // Track objectives
        config.TrackObjectives = lower.Contains("capture") || lower.Contains("collect") ||
            lower.Contains("objective") || lower.Contains("flag") || lower.Contains("zone") ||
            mode is GameModeType.Tycoon;

        // Win score
        var match = Regex.Match(lower, @"(?:first to|win at|target)\s*(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var ws))
            config.WinScore = ws;
        else
        {
            config.WinScore = mode switch
            {
                GameModeType.FreeForAll => 25,
                GameModeType.TeamBased => 50,
                GameModeType.Progressive => 20,
                GameModeType.Rounds => 5,
                _ => 100
            };
        }

        // Win condition
        config.WinCondition = mode switch
        {
            GameModeType.FreeForAll => "First to target score or most kills at time",
            GameModeType.TeamBased => lower.Contains("capture") || lower.Contains("flag")
                ? "Most objective completions or first to target"
                : "Most team eliminations or first to target score",
            GameModeType.Progressive => "First to complete progression ladder",
            GameModeType.Rounds => "Most round wins",
            GameModeType.Tycoon => "Complete all upgrades",
            GameModeType.Survival => "Survive the longest",
            _ => "Highest score wins"
        };

        return config;
    }

    private static List<ObjectiveConfig> ExtractObjectives(string lower)
    {
        var objectives = new List<ObjectiveConfig>();

        if (lower.Contains("capture the flag") || lower.Contains("ctf"))
        {
            objectives.Add(new ObjectiveConfig { Type = "capture", Description = "Team A flag", Radius = 200 });
            objectives.Add(new ObjectiveConfig { Type = "capture", Description = "Team B flag", Radius = 200 });
            objectives.Add(new ObjectiveConfig { Type = "return_zone", Description = "Team A return zone", Radius = 300 });
            objectives.Add(new ObjectiveConfig { Type = "return_zone", Description = "Team B return zone", Radius = 300 });
        }
        else if (lower.Contains("capture") || lower.Contains("control point") || lower.Contains("domination"))
        {
            var match = Regex.Match(lower, @"(\d+)\s*(?:capture|control|zone|point)");
            var count = match.Success && int.TryParse(match.Groups[1].Value, out var c) ? c : 3;
            for (int i = 0; i < count; i++)
                objectives.Add(new ObjectiveConfig { Type = "capture", Description = $"Control Point {(char)('A' + i)}", Radius = 500 });
        }
        else if (lower.Contains("collect"))
        {
            objectives.Add(new ObjectiveConfig { Type = "collect", Description = "Collectible items", Radius = 100 });
        }

        if (lower.Contains("storm") || lower.Contains("zone war"))
        {
            objectives.Add(new ObjectiveConfig { Type = "storm_zone", Description = "Shrinking safe zone", Radius = 5000 });
        }

        return objectives;
    }

    private static List<SpawnConfig> BuildSpawnAreas(GameDesign design)
    {
        var spawns = new List<SpawnConfig>();

        if (design.TeamCount > 0)
        {
            var playersPerTeam = design.PlayerCount / design.TeamCount;
            var spawnsPerTeam = Math.Max(2, Math.Min(playersPerTeam, 8));
            for (int t = 0; t < design.TeamCount; t++)
            {
                spawns.Add(new SpawnConfig
                {
                    TeamName = $"Team {(char)('A' + t)}",
                    SpawnCount = spawnsPerTeam,
                    HasProtection = true,
                    ProtectionDuration = 5
                });
            }
        }
        else
        {
            var spawnCount = Math.Max(4, Math.Min(design.PlayerCount, 16));
            spawns.Add(new SpawnConfig
            {
                TeamName = "FFA",
                SpawnCount = spawnCount,
                HasProtection = design.GameMode != GameModeType.Tycoon,
                ProtectionDuration = 3
            });
        }

        return spawns;
    }

    private static EconomyConfig? ExtractEconomy(string lower)
    {
        if (!lower.Contains("currency") && !lower.Contains("gold") && !lower.Contains("coins") &&
            !lower.Contains("buy") && !lower.Contains("shop") && !lower.Contains("purchase") &&
            !lower.Contains("economy") && !lower.Contains("tycoon") && !lower.Contains("vending"))
            return null;

        var config = new EconomyConfig { HasCurrency = true };

        // Currency name
        if (lower.Contains("gold")) config.CurrencyName = "Gold";
        else if (lower.Contains("coin")) config.CurrencyName = "Coins";
        else if (lower.Contains("gem")) config.CurrencyName = "Gems";
        else if (lower.Contains("credit")) config.CurrencyName = "Credits";
        else config.CurrencyName = "Gold";

        // Starting amount
        var match = Regex.Match(lower, @"start(?:ing)?\s*(?:with)?\s*(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var sa))
            config.StartingAmount = sa;
        else
            config.StartingAmount = 100;

        // Passive income
        config.PassiveIncome = lower.Contains("passive") || lower.Contains("income") ||
            lower.Contains("idle") || lower.Contains("tycoon");

        // Purchasables
        config.Purchasables = new List<string> { "Upgrade 1", "Upgrade 2", "Upgrade 3" };

        return config;
    }

    private static List<string> ExtractSpecialMechanics(string lower)
    {
        var mechanics = new List<string>();

        if (lower.Contains("respawn")) mechanics.Add("respawn_on_elimination");
        if (lower.Contains("no respawn") || lower.Contains("perma") || lower.Contains("last standing"))
            mechanics.Add("no_respawn");
        if (lower.Contains("storm") || lower.Contains("zone war"))
            mechanics.Add("storm_phases");
        if (lower.Contains("gun game") || lower.Contains("weapon progress"))
            mechanics.Add("weapon_progression_on_kill");
        if (lower.Contains("build")) mechanics.Add("building_enabled");
        if (lower.Contains("no build")) mechanics.Add("building_disabled");
        if (lower.Contains("low grav")) mechanics.Add("low_gravity");
        if (lower.Contains("speed boost")) mechanics.Add("speed_boost");
        if (lower.Contains("loadout")) mechanics.Add("custom_loadout");
        if (lower.Contains("loot") || lower.Contains("chest"))
            mechanics.Add("loot_spawns");

        return mechanics;
    }

    private static List<UIRequirement> InferUINeeds(GameDesign design)
    {
        var needs = new List<UIRequirement>();

        // Scoring → HUD + Scoreboard
        if (design.Scoring != null)
        {
            needs.Add(new UIRequirement { Type = "HUD", Description = "Score display and game timer" });

            if (design.TeamCount > 0)
                needs.Add(new UIRequirement { Type = "Scoreboard", Description = "Team-based scoreboard" });
            else
                needs.Add(new UIRequirement { Type = "Scoreboard", Description = "Player leaderboard" });
        }

        // Economy → ShopUI
        if (design.Economy is { HasCurrency: true })
        {
            needs.Add(new UIRequirement { Type = "ShopUI", Description = $"{design.Economy.CurrencyName} balance and purchase menu" });
        }

        // Rounds → Timer
        if (design.Rounds != null && design.Rounds.RoundCount > 1)
        {
            needs.Add(new UIRequirement { Type = "Timer", Description = "Round timer and round counter" });
        }

        // Always need notifications for feedback
        needs.Add(new UIRequirement { Type = "Notification", Description = "Game event notifications" });

        return needs;
    }

    private static string GenerateName(string lower, GameModeType mode)
    {
        // Try to extract a specific name from quotes
        var match = Regex.Match(lower, @"""([^""]+)""");
        if (match.Success) return match.Groups[1].Value;

        match = Regex.Match(lower, @"'([^']+)'");
        if (match.Success) return match.Groups[1].Value;

        // Generate from mode
        return mode switch
        {
            GameModeType.FreeForAll => "Free For All Arena",
            GameModeType.TeamBased when lower.Contains("capture the flag") || lower.Contains("ctf") => "Capture the Flag",
            GameModeType.TeamBased when lower.Contains("deathmatch") => "Team Deathmatch",
            GameModeType.TeamBased => "Team Battle",
            GameModeType.Rounds when lower.Contains("zone") || lower.Contains("storm") => "Zone Wars",
            GameModeType.Rounds => "Round Battle",
            GameModeType.Progressive => "Gun Game",
            GameModeType.Tycoon => "Tycoon Empire",
            GameModeType.Survival => "Survival Arena",
            _ => "Custom Game"
        };
    }

    // =========================================================================
    //  VERSE CODE PLAN
    // =========================================================================

    private static string? BuildVerseCodePlan(GameDesign design)
    {
        // Build a description of what verse files are needed
        var parts = new List<string>();

        parts.Add($"# Verse code needed for: {design.Name}");
        parts.Add($"# Game mode: {design.GameMode}, Players: {design.PlayerCount}, Teams: {design.TeamCount}");

        if (design.Rounds != null && design.Rounds.RoundCount > 1)
            parts.Add($"# Round manager: {design.Rounds.RoundCount} rounds, {design.Rounds.RoundDurationSeconds}s each");

        if (design.Scoring is { TrackKills: true })
            parts.Add("# Elimination tracking and score management");

        if (design.Economy is { HasCurrency: true })
            parts.Add($"# Economy system: {design.Economy.CurrencyName} currency" +
                (design.Economy.PassiveIncome ? " with passive income" : ""));

        if (design.Objectives.Count > 0)
            parts.Add($"# Objective tracking: {design.Objectives.Count} objectives");

        return string.Join("\n", parts);
    }

    // =========================================================================
    //  HELPERS
    // =========================================================================

    private static Vector3Info CalculateSpawnOffset(string teamName, int index, int total, int teamIndex)
    {
        // Spread spawns in a line per team, teams on opposite sides
        var teamSide = teamIndex % 2 == 0 ? -1 : 1;
        var teamDistance = 3000f;
        var spacing = 500f;

        var x = teamSide * teamDistance;
        var y = (index - total / 2f) * spacing;

        return new Vector3Info(x, y, 0);
    }

    private static string SanitizeName(string name) =>
        Regex.Replace(name.ToLower().Replace(" ", "_"), @"[^a-z0-9_]", "");
}
