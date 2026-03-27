using WellVersed.Core.Config;
using WellVersed.Core.Models;
using Microsoft.Extensions.Logging;

namespace WellVersed.Core.Services.VerseGeneration;

/// <summary>
/// Generates Verse source files for game logic devices — round managers,
/// scoring systems, timers, elimination trackers, and more.
/// </summary>
public class VerseDeviceGenerator
{
    private readonly WellVersedConfig _config;
    private readonly ILogger<VerseDeviceGenerator> _logger;

    public VerseDeviceGenerator(WellVersedConfig config, ILogger<VerseDeviceGenerator> logger)
    {
        _config = config;
        _logger = logger;
    }

    public VerseFileResult Generate(VerseFileRequest request)
    {
        var result = new VerseFileResult { ClassName = request.ClassName };

        try
        {
            var code = request.TemplateType switch
            {
                VerseTemplateType.GameManager => GenerateGameManager(request),
                VerseTemplateType.TimerController => GenerateTimerController(request),
                VerseTemplateType.TeamManager => GenerateTeamManager(request),
                VerseTemplateType.EliminationTracker => GenerateEliminationTracker(request),
                VerseTemplateType.CollectibleSystem => GenerateCollectibleSystem(request),
                VerseTemplateType.ZoneController => GenerateZoneController(request),
                VerseTemplateType.MovementMutator => GenerateMovementMutator(request),
                VerseTemplateType.EmptyDevice => GenerateEmptyDevice(request),
                _ => throw new ArgumentException($"Template type '{request.TemplateType}' is not a device template.")
            };

            result.SourceCode = code;
            result.Success = true;

            var outputDir = request.OutputDirectory
                ?? Path.Combine(_config.ProjectPath, "Content");
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
                var fileName = string.IsNullOrEmpty(request.FileName)
                    ? request.ClassName
                    : request.FileName;
                result.FilePath = Path.Combine(outputDir, $"{fileName}.verse");
                File.WriteAllText(result.FilePath, code);
                result.Notes.Add($"File written to {result.FilePath}");
            }

            result.Notes.Add("Compile in UEFN, then place the device in your level.");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger.LogError(ex, "Failed to generate device template {Type}", request.TemplateType);
        }

        return result;
    }

    public static List<VerseTemplateInfo> GetDeviceTemplates()
    {
        return new List<VerseTemplateInfo>
        {
            new()
            {
                Name = "Game Manager",
                Description = "Round-based game flow controller with warmup, active, and end phases. " +
                              "Manages scoring, round transitions, and win conditions.",
                Type = VerseTemplateType.GameManager,
                Category = "Device",
                Parameters = new()
                {
                    new() { Name = "round_count", Description = "Number of rounds", DefaultValue = "3" },
                    new() { Name = "round_duration", Description = "Seconds per round", DefaultValue = "180" },
                    new() { Name = "warmup_duration", Description = "Warmup seconds before round", DefaultValue = "10" },
                    new() { Name = "win_score", Description = "Score needed to win", DefaultValue = "100" }
                }
            },
            new()
            {
                Name = "Timer Controller",
                Description = "Versatile countdown/count-up timer with configurable events at intervals. " +
                              "Wire to HUD devices or Verse UI for display.",
                Type = VerseTemplateType.TimerController,
                Category = "Device",
                Parameters = new()
                {
                    new() { Name = "duration", Description = "Timer duration in seconds", DefaultValue = "300" },
                    new() { Name = "count_down", Description = "Count down (true) or up (false)", DefaultValue = "true" },
                    new() { Name = "warning_time", Description = "Seconds remaining to trigger warning", DefaultValue = "30" }
                }
            },
            new()
            {
                Name = "Team Manager",
                Description = "Team assignment, balancing, and scoring system. " +
                              "Handles team creation, player assignment, and team-based scoring.",
                Type = VerseTemplateType.TeamManager,
                Category = "Device",
                Parameters = new()
                {
                    new() { Name = "team_count", Description = "Number of teams", DefaultValue = "2" },
                    new() { Name = "auto_balance", Description = "Auto-balance teams", DefaultValue = "true" }
                }
            },
            new()
            {
                Name = "Elimination Tracker",
                Description = "Tracks player eliminations with kill streaks, multi-kills, and leaderboard. " +
                              "Awards points and triggers events on milestones.",
                Type = VerseTemplateType.EliminationTracker,
                Category = "Device",
                Parameters = new()
                {
                    new() { Name = "points_per_elim", Description = "Score per elimination", DefaultValue = "10" },
                    new() { Name = "streak_bonus", Description = "Extra points per streak kill", DefaultValue = "5" },
                    new() { Name = "streak_threshold", Description = "Kills for streak bonus", DefaultValue = "3" }
                }
            },
            new()
            {
                Name = "Collectible System",
                Description = "Item collection game with spawning, tracking, and completion logic. " +
                              "Supports multiple collectible types and respawning.",
                Type = VerseTemplateType.CollectibleSystem,
                Category = "Device",
                Parameters = new()
                {
                    new() { Name = "total_collectibles", Description = "Total items to collect", DefaultValue = "20" },
                    new() { Name = "respawn_time", Description = "Respawn delay in seconds (0 = no respawn)", DefaultValue = "0" },
                    new() { Name = "item_name", Description = "Collectible name", DefaultValue = "Star" }
                }
            },
            new()
            {
                Name = "Zone Controller",
                Description = "Area capture/control point system. Players score by standing in zones. " +
                              "Supports contested, capturing, and captured states.",
                Type = VerseTemplateType.ZoneController,
                Category = "Device",
                Parameters = new()
                {
                    new() { Name = "capture_time", Description = "Seconds to capture zone", DefaultValue = "10" },
                    new() { Name = "points_per_second", Description = "Score per second while holding", DefaultValue = "1" },
                    new() { Name = "contested_pause", Description = "Pause capture when contested", DefaultValue = "true" }
                }
            },
            new()
            {
                Name = "Movement Mutator",
                Description = "Modifies player movement — speed boost, low gravity, dash ability. " +
                              "Apply globally or per-zone with trigger devices.",
                Type = VerseTemplateType.MovementMutator,
                Category = "Device",
                Parameters = new()
                {
                    new() { Name = "speed_multiplier", Description = "Movement speed multiplier", DefaultValue = "1.5" },
                    new() { Name = "gravity_scale", Description = "Gravity multiplier (0.5 = low grav)", DefaultValue = "1.0" },
                    new() { Name = "enable_dash", Description = "Enable dash ability", DefaultValue = "false" }
                }
            },
            new()
            {
                Name = "Empty Device",
                Description = "Minimal creative_device with OnBegin and a helper method. " +
                              "Starting point for custom Verse devices.",
                Type = VerseTemplateType.EmptyDevice,
                Category = "Device",
                Parameters = new()
            }
        };
    }

    // =====================================================================
    //  GAME MANAGER
    // =====================================================================

    private string GenerateGameManager(VerseFileRequest request)
    {
        var p = request.Parameters;
        var roundCount = GetInt(p, "round_count", 3);
        var roundDuration = GetInt(p, "round_duration", 180);
        var warmupDuration = GetInt(p, "warmup_duration", 10);
        var winScore = GetInt(p, "win_score", 100);

        var b = new VerseCodeBuilder();
        b.Comment("Game Manager — Generated by WellVersed");
        b.Comment($"Manages {roundCount} rounds of {roundDuration}s each with {warmupDuration}s warmup.");
        b.Comment($"First to {winScore} points wins.");
        b.Line();
        b.GameplayUsings();
        b.UIUsings();
        b.Line();

        b.Line("game_phase := enum{Warmup, Active, RoundEnd, GameOver}");
        b.Line();

        b.ClassDef(request.ClassName);
        b.Editable("RoundEndDevice", "round_settings_device", "round_settings_device{}");
        b.Editable("EndGameDevice", "end_game_device", "end_game_device{}");
        b.Editable("HUDMessage", "hud_message_device", "hud_message_device{}");
        b.Line();

        b.ImmutableVar("TotalRounds", "int", roundCount.ToString());
        b.ImmutableVar("RoundDuration", "float", $"{roundDuration}.0");
        b.ImmutableVar("WarmupDuration", "float", $"{warmupDuration}.0");
        b.ImmutableVar("WinScore", "int", winScore.ToString());
        b.Line();

        b.Var("CurrentRound", "int", "0");
        b.Var("CurrentPhase", "game_phase", "game_phase.Warmup");
        b.Var("Scores", "[player]int", "map{}");
        b.Line();

        b.OnBegin();
        b.Line("GameLoop()");
        b.EndBlock();

        // GameLoop
        b.Line();
        b.Method("GameLoop", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("set CurrentRound += 1");
        b.Line("if (CurrentRound > TotalRounds):");
        b.Indent();
        b.Line("EndGame()");
        b.Line("break");
        b.Dedent();
        b.Line();
        b.Comment("Warmup phase");
        b.Line("set CurrentPhase = game_phase.Warmup");
        b.Line("HUDMessage.Show()");
        b.Line($"Print(\"Round {{CurrentRound}} starting in {warmupDuration} seconds...\")");
        b.Line("Sleep(WarmupDuration)");
        b.Line();
        b.Comment("Active phase");
        b.Line("set CurrentPhase = game_phase.Active");
        b.Line("HUDMessage.Hide()");
        b.Line("Print(\"Round {CurrentRound} — FIGHT!\")");
        b.Line("Sleep(RoundDuration)");
        b.Line();
        b.Comment("Round end");
        b.Line("set CurrentPhase = game_phase.RoundEnd");
        b.Line("OnRoundEnd()");
        b.Line("Sleep(5.0)");
        b.Dedent(); // loop
        b.EndBlock();

        // OnRoundEnd
        b.Line();
        b.Method("OnRoundEnd", "void");
        b.Line("Print(\"Round {CurrentRound} complete!\")");
        b.Line("RoundEndDevice.EndRound()");
        b.EndBlock();

        // EndGame
        b.Line();
        b.Method("EndGame", "void");
        b.Line("set CurrentPhase = game_phase.GameOver");
        b.Line("Print(\"Game Over!\")");
        b.Line("EndGameDevice.Activate()");
        b.EndBlock();

        // AddScore
        b.Line();
        b.MethodWithParams("AddScore", "Player:player, Amount:int", "void");
        b.Line("CurrentPlayerScore := if (S := Scores[Player]) then S else 0");
        b.Line("NewScore := CurrentPlayerScore + Amount");
        b.Line("if (set Scores[Player] = NewScore) {}");
        b.Line();
        b.Line("if (NewScore >= WinScore):");
        b.Indent();
        b.Line("Print(\"Player reached win score!\")");
        b.Line("EndGame()");
        b.Dedent();
        b.EndBlock();

        // GetScore
        b.Line();
        b.MethodWithParams("GetScore", "Player:player", "int");
        b.Line("if (S := Scores[Player]) then S else 0");
        b.EndBlock();

        b.EndBlock(); // class
        return b.ToString();
    }

    // =====================================================================
    //  TIMER CONTROLLER
    // =====================================================================

    private string GenerateTimerController(VerseFileRequest request)
    {
        var p = request.Parameters;
        var duration = GetInt(p, "duration", 300);
        var countDown = GetBool(p, "count_down", true);
        var warningTime = GetInt(p, "warning_time", 30);

        var b = new VerseCodeBuilder();
        b.Comment("Timer Controller — Generated by WellVersed");
        b.Comment($"{(countDown ? "Countdown" : "Count-up")} timer: {duration}s, warning at {warningTime}s.");
        b.Line();
        b.StandardUsings();
        b.Using("/Fortnite.com/Devices");
        b.Line();

        b.ClassDef(request.ClassName);
        b.Editable("OnCompleteTrigger", "trigger_device", "trigger_device{}");
        b.Editable("OnWarningTrigger", "trigger_device", "trigger_device{}");
        b.Editable("StartButton", "button_device", "button_device{}");
        b.Editable("TimerDisplay", "hud_message_device", "hud_message_device{}");
        b.Line();

        b.ImmutableVar("Duration", "float", $"{duration}.0");
        b.ImmutableVar("WarningThreshold", "float", $"{warningTime}.0");
        b.Line();
        b.Var("Elapsed", "float", "0.0");
        b.Var("IsRunning", "logic", "false");
        b.Var("WarningFired", "logic", "false");
        b.Line();

        b.OnBegin();
        b.Line("StartButton.InteractedWithEvent.Subscribe(OnStart)");
        b.EndBlock();

        b.Line();
        b.MethodWithParams("OnStart", "Agent:agent", "void");
        b.Line("if (not IsRunning?):");
        b.Indent();
        b.Line("set IsRunning = true");
        b.Line("set Elapsed = 0.0");
        b.Line("set WarningFired = false");
        b.Line("spawn{TimerTick()}");
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.Method("TimerTick", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Sleep(1.0)");
        b.Line("set Elapsed += 1.0");
        b.Line();

        if (countDown)
        {
            b.Line("Remaining := Duration - Elapsed");
            b.Line("Minutes := Floor(Remaining / 60.0)");
            b.Line("Seconds := Floor(Mod(Remaining, 60.0))");
            b.Line("Print(\"{Minutes}:{Seconds}\")");
            b.Line();
            b.Line("# Warning event");
            b.Line("if (Remaining <= WarningThreshold, not WarningFired?):");
            b.Indent();
            b.Line("set WarningFired = true");
            b.Line("OnWarningTrigger.Trigger()");
            b.Dedent();
            b.Line();
            b.Line("# Timer complete");
            b.Line("if (Remaining <= 0.0):");
        }
        else
        {
            b.Line("Print(\"Time: {Elapsed}\")");
            b.Line();
            b.Line("if (Elapsed >= WarningThreshold, not WarningFired?):");
            b.Indent();
            b.Line("set WarningFired = true");
            b.Line("OnWarningTrigger.Trigger()");
            b.Dedent();
            b.Line();
            b.Line("if (Elapsed >= Duration):");
        }

        b.Indent();
        b.Line("set IsRunning = false");
        b.Line("OnCompleteTrigger.Trigger()");
        b.Line("Print(\"Timer complete!\")");
        b.Line("break");
        b.Dedent();
        b.Dedent(); // loop
        b.EndBlock();

        b.Line();
        b.Method("Stop", "void");
        b.Line("set IsRunning = false");
        b.EndBlock();

        b.Line();
        b.Method("Reset", "void");
        b.Line("set IsRunning = false");
        b.Line("set Elapsed = 0.0");
        b.Line("set WarningFired = false");
        b.EndBlock();

        b.EndBlock(); // class
        return b.ToString();
    }

    // =====================================================================
    //  TEAM MANAGER
    // =====================================================================

    private string GenerateTeamManager(VerseFileRequest request)
    {
        var p = request.Parameters;
        var teamCount = GetInt(p, "team_count", 2);
        var autoBalance = GetBool(p, "auto_balance", true);

        var b = new VerseCodeBuilder();
        b.Comment("Team Manager — Generated by WellVersed");
        b.Comment($"Manages {teamCount} teams with {(autoBalance ? "auto-balancing" : "manual assignment")}.");
        b.Line();
        b.GameplayUsings();
        b.Line();

        b.ClassDef(request.ClassName);
        b.Editable("TeamSwitcher", "class_and_team_selector_device", "class_and_team_selector_device{}");
        b.Line();

        b.ImmutableVar("TeamCount", "int", teamCount.ToString());
        b.Var("TeamScores", "[]int", $"array{{{string.Join(", ", Enumerable.Repeat("0", teamCount))}}}");
        b.Var("TeamPlayers", "[][]player", $"array{{{string.Join(", ", Enumerable.Repeat("array{}", teamCount))}}}");
        b.Line();

        b.OnBegin();
        b.Line("GetPlayspace().PlayerAddedEvent().Subscribe(OnPlayerJoined)");
        if (autoBalance)
        {
            b.Line("GetPlayspace().PlayerRemovedEvent().Subscribe(OnPlayerLeft)");
        }
        b.EndBlock();

        b.Line();
        b.MethodWithParams("OnPlayerJoined", "Player:player", "void");
        if (autoBalance)
        {
            b.Line("# Assign to team with fewest players");
            b.Line("var SmallestTeam : int = 0");
            b.Line("var SmallestCount : int = 999");
            b.Line("for (TeamIdx := 0..TeamCount - 1):");
            b.Indent();
            b.Line("if (Team := TeamPlayers[TeamIdx]):");
            b.Indent();
            b.Line("if (Team.Length < SmallestCount):");
            b.Indent();
            b.Line("set SmallestTeam = TeamIdx");
            b.Line("set SmallestCount = Team.Length");
            b.Dedent();
            b.Dedent();
            b.Dedent();
            b.Line("AssignToTeam(Player, SmallestTeam)");
        }
        else
        {
            b.Line("# Manual team assignment — wire to team selector device");
            b.Line("Print(\"Player joined — assign to team manually\")");
        }
        b.EndBlock();

        if (autoBalance)
        {
            b.Line();
            b.MethodWithParams("OnPlayerLeft", "Player:player", "void");
            b.Line("for (TeamIdx := 0..TeamCount - 1):");
            b.Indent();
            b.Line("if (Team := TeamPlayers[TeamIdx]):");
            b.Indent();
            b.Line("# Remove player from their team");
            b.Line("var NewTeam : []player = array{}");
            b.Line("for (P : Team, P <> Player):");
            b.Indent();
            b.Line("set NewTeam += array{P}");
            b.Dedent();
            b.Line("if (set TeamPlayers[TeamIdx] = NewTeam) {}");
            b.Dedent();
            b.Dedent();
            b.EndBlock();
        }

        b.Line();
        b.MethodWithParams("AssignToTeam", "Player:player, TeamIndex:int", "void");
        b.Line("if (Team := TeamPlayers[TeamIndex]):");
        b.Indent();
        b.Line("if (set TeamPlayers[TeamIndex] = Team + array{Player}) {}");
        b.Line("Print(\"Player assigned to Team {TeamIndex + 1}\")");
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.MethodWithParams("AddTeamScore", "TeamIndex:int, Amount:int", "void");
        b.Line("if (Current := TeamScores[TeamIndex]):");
        b.Indent();
        b.Line("if (set TeamScores[TeamIndex] = Current + Amount) {}");
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.MethodWithParams("GetTeamScore", "TeamIndex:int", "int");
        b.Line("if (S := TeamScores[TeamIndex]) then S else 0");
        b.EndBlock();

        b.Line();
        b.Method("GetWinningTeam", "int");
        b.Line("var BestTeam : int = 0");
        b.Line("var BestScore : int = -1");
        b.Line("for (I := 0..TeamCount - 1):");
        b.Indent();
        b.Line("if (S := TeamScores[I], S > BestScore):");
        b.Indent();
        b.Line("set BestTeam = I");
        b.Line("set BestScore = S");
        b.Dedent();
        b.Dedent();
        b.Line("BestTeam");
        b.EndBlock();

        b.EndBlock(); // class
        return b.ToString();
    }

    // =====================================================================
    //  ELIMINATION TRACKER
    // =====================================================================

    private string GenerateEliminationTracker(VerseFileRequest request)
    {
        var p = request.Parameters;
        var pointsPerElim = GetInt(p, "points_per_elim", 10);
        var streakBonus = GetInt(p, "streak_bonus", 5);
        var streakThreshold = GetInt(p, "streak_threshold", 3);

        var b = new VerseCodeBuilder();
        b.Comment("Elimination Tracker — Generated by WellVersed");
        b.Comment($"{pointsPerElim} pts/elim, {streakBonus} streak bonus at {streakThreshold}+ streak.");
        b.Line();
        b.GameplayUsings();
        b.Using("/Fortnite.com/Devices");
        b.Line();

        b.ClassDef(request.ClassName);
        b.Editable("EliminationManager", "elimination_manager_device", "elimination_manager_device{}");
        b.Editable("ScoreManager", "score_manager_device", "score_manager_device{}");
        b.Editable("StreakAnnouncer", "hud_message_device", "hud_message_device{}");
        b.Line();

        b.ImmutableVar("PointsPerElim", "int", pointsPerElim.ToString());
        b.ImmutableVar("StreakBonus", "int", streakBonus.ToString());
        b.ImmutableVar("StreakThreshold", "int", streakThreshold.ToString());
        b.Line();
        b.Var("KillStreaks", "[player]int", "map{}");
        b.Var("TotalElims", "[player]int", "map{}");
        b.Line();

        b.OnBegin();
        b.Line("EliminationManager.EliminationEvent.Subscribe(OnElimination)");
        b.EndBlock();

        b.Line();
        b.MethodWithParams("OnElimination", "Result:elimination_result", "void");
        b.Line("if (Eliminator := Result.EliminatingCharacter?, Player := player[Eliminator]):");
        b.Indent();
        b.Line("# Update totals");
        b.Line("CurrentElims := if (E := TotalElims[Player]) then E else 0");
        b.Line("if (set TotalElims[Player] = CurrentElims + 1) {}");
        b.Line();
        b.Line("# Update streak");
        b.Line("CurrentStreak := if (S := KillStreaks[Player]) then S else 0");
        b.Line("NewStreak := CurrentStreak + 1");
        b.Line("if (set KillStreaks[Player] = NewStreak) {}");
        b.Line();
        b.Line("# Calculate points");
        b.Line("var Points : int = PointsPerElim");
        b.Line("if (NewStreak >= StreakThreshold):");
        b.Indent();
        b.Line("set Points += StreakBonus");
        b.Line("StreakAnnouncer.Show()");
        b.Line("Print(\"{NewStreak} kill streak! +{StreakBonus} bonus\")");
        b.Dedent();
        b.Line();
        b.Line("ScoreManager.Activate(Player)");
        b.Line("Print(\"Elimination! +{Points} points\")");
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.Comment("Reset streak when a player is eliminated");
        b.MethodWithParams("OnPlayerEliminated", "Player:player", "void");
        b.Line("if (set KillStreaks[Player] = 0) {}");
        b.EndBlock();

        b.Line();
        b.MethodWithParams("GetEliminations", "Player:player", "int");
        b.Line("if (E := TotalElims[Player]) then E else 0");
        b.EndBlock();

        b.Line();
        b.MethodWithParams("GetStreak", "Player:player", "int");
        b.Line("if (S := KillStreaks[Player]) then S else 0");
        b.EndBlock();

        b.EndBlock(); // class
        return b.ToString();
    }

    // =====================================================================
    //  COLLECTIBLE SYSTEM
    // =====================================================================

    private string GenerateCollectibleSystem(VerseFileRequest request)
    {
        var p = request.Parameters;
        var totalCollectibles = GetInt(p, "total_collectibles", 20);
        var respawnTime = GetInt(p, "respawn_time", 0);
        var itemName = GetString(p, "item_name", "Star");

        var b = new VerseCodeBuilder();
        b.Comment("Collectible System — Generated by WellVersed");
        b.Comment($"Collect {totalCollectibles} {itemName}s. {(respawnTime > 0 ? $"Respawn after {respawnTime}s." : "No respawn.")}");
        b.Line();
        b.StandardUsings();
        b.Using("/Fortnite.com/Devices");
        b.Line();

        b.ClassDef(request.ClassName);
        b.Editable("ItemGranter", "item_granter_device", "item_granter_device{}");
        b.Editable("CompletionDevice", "trigger_device", "trigger_device{}");
        b.Editable("CollectSound", "audio_player_device", "audio_player_device{}");
        b.Line();

        b.ImmutableVar("TotalRequired", "int", totalCollectibles.ToString());
        b.Var("Collected", "[player]int", "map{}");
        b.Line();

        b.OnBegin();
        b.Line("ItemGranter.ItemGrantedEvent.Subscribe(OnItemCollected)");
        b.EndBlock();

        b.Line();
        b.MethodWithParams("OnItemCollected", "Agent:agent", "void");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("Current := if (C := Collected[Player]) then C else 0");
        b.Line("NewCount := Current + 1");
        b.Line("if (set Collected[Player] = NewCount) {}");
        b.Line();
        b.Line("CollectSound.Play()");
        b.Line($"Print(\"{itemName} {{NewCount}}/{{TotalRequired}}\")");
        b.Line();
        b.Line("if (NewCount >= TotalRequired):");
        b.Indent();
        b.Line("OnCollectionComplete(Player)");
        b.Dedent();
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.MethodWithParams("OnCollectionComplete", "Player:player", "void");
        b.Line($"Print(\"All {itemName}s collected!\")");
        b.Line("CompletionDevice.Trigger(Player)");
        b.EndBlock();

        b.Line();
        b.MethodWithParams("GetProgress", "Player:player", "int");
        b.Line("if (C := Collected[Player]) then C else 0");
        b.EndBlock();

        b.Line();
        b.MethodWithParams("ResetProgress", "Player:player", "void");
        b.Line("if (set Collected[Player] = 0) {}");
        b.EndBlock();

        b.EndBlock(); // class
        return b.ToString();
    }

    // =====================================================================
    //  ZONE CONTROLLER
    // =====================================================================

    private string GenerateZoneController(VerseFileRequest request)
    {
        var p = request.Parameters;
        var captureTime = GetInt(p, "capture_time", 10);
        var pointsPerSecond = GetInt(p, "points_per_second", 1);
        var contestedPause = GetBool(p, "contested_pause", true);

        var b = new VerseCodeBuilder();
        b.Comment("Zone Controller — Generated by WellVersed");
        b.Comment($"Capture point: {captureTime}s to capture, {pointsPerSecond} pts/sec while holding.");
        b.Line();
        b.StandardUsings();
        b.Using("/Fortnite.com/Devices");
        b.Line();

        b.Line("zone_state := enum{Neutral, Capturing, Captured, Contested}");
        b.Line();

        b.ClassDef(request.ClassName);
        b.Editable("CaptureZone", "capture_area_device", "capture_area_device{}");
        b.Editable("CapturedIndicator", "light_device", "light_device{}");
        b.Editable("ScoreGranter", "score_manager_device", "score_manager_device{}");
        b.Line();

        b.ImmutableVar("CaptureTime", "float", $"{captureTime}.0");
        b.ImmutableVar("PointsPerSecond", "int", pointsPerSecond.ToString());
        b.Line();
        b.Var("State", "zone_state", "zone_state.Neutral");
        b.Var("CaptureProgress", "float", "0.0");
        b.Var("PlayersInZone", "[]player", "array{}");
        b.Var("ControllingPlayer", "?player", "false");
        b.Line();

        b.OnBegin();
        b.Line("CaptureZone.AgentEntersEvent.Subscribe(OnPlayerEnter)");
        b.Line("CaptureZone.AgentExitsEvent.Subscribe(OnPlayerExit)");
        b.Line("spawn{ZoneUpdateLoop()}");
        b.EndBlock();

        b.Line();
        b.MethodWithParams("OnPlayerEnter", "Agent:agent", "void");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("set PlayersInZone += array{Player}");
        b.Line("Print(\"Player entered zone\")");
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.MethodWithParams("OnPlayerExit", "Agent:agent", "void");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("var NewList : []player = array{}");
        b.Line("for (P : PlayersInZone, P <> Player):");
        b.Indent();
        b.Line("set NewList += array{P}");
        b.Dedent();
        b.Line("set PlayersInZone = NewList");
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.Method("ZoneUpdateLoop", "void", suspends: true);
        b.Line("loop:");
        b.Indent();
        b.Line("Sleep(1.0)");
        b.Line();
        b.Line("if (PlayersInZone.Length = 0):");
        b.Indent();
        b.Comment("Nobody in zone — slowly decay progress");
        b.Line("set CaptureProgress = Max(CaptureProgress - 0.5, 0.0)");
        b.Line("if (CaptureProgress <= 0.0). set State = zone_state.Neutral");
        b.Dedent();
        b.Line("else if (PlayersInZone.Length = 1):");
        b.Indent();
        b.Comment("Single player — advance capture");
        b.Line("set CaptureProgress += 1.0");
        b.Line("set State = zone_state.Capturing");
        b.Line("if (CaptureProgress >= CaptureTime):");
        b.Indent();
        b.Line("set State = zone_state.Captured");
        b.Line("set ControllingPlayer = option{PlayersInZone[0]}");
        b.Line("CapturedIndicator.TurnOn()");
        b.Line("ScoreGranter.Activate(PlayersInZone[0])");
        b.Dedent();
        b.Dedent();

        if (contestedPause)
        {
            b.Line("else:");
            b.Indent();
            b.Comment("Multiple players — contested, pause capture");
            b.Line("set State = zone_state.Contested");
            b.Dedent();
        }

        b.Dedent(); // loop
        b.EndBlock();

        b.EndBlock(); // class
        return b.ToString();
    }

    // =====================================================================
    //  MOVEMENT MUTATOR
    // =====================================================================

    private string GenerateMovementMutator(VerseFileRequest request)
    {
        var p = request.Parameters;
        var speedMulti = GetString(p, "speed_multiplier", "1.5");
        var gravScale = GetString(p, "gravity_scale", "1.0");
        var enableDash = GetBool(p, "enable_dash", false);

        var b = new VerseCodeBuilder();
        b.Comment("Movement Mutator — Generated by WellVersed");
        b.Comment($"Speed: {speedMulti}x, Gravity: {gravScale}x{(enableDash ? ", Dash enabled" : "")}.");
        b.Line();
        b.StandardUsings();
        b.Using("/Fortnite.com/Devices");
        b.Using("/Fortnite.com/Characters");
        b.Line();

        b.ClassDef(request.ClassName);
        b.Editable("ActivateTrigger", "trigger_device", "trigger_device{}");
        b.Editable("DeactivateTrigger", "trigger_device", "trigger_device{}");
        b.Editable("SpeedModifier", "movement_modulator_device", "movement_modulator_device{}");
        b.Line();
        b.Var("IsActive", "logic", "false");
        b.Var("AffectedPlayers", "[]player", "array{}");
        b.Line();

        b.OnBegin();
        b.Line("ActivateTrigger.TriggeredEvent.Subscribe(OnActivate)");
        b.Line("DeactivateTrigger.TriggeredEvent.Subscribe(OnDeactivate)");
        b.EndBlock();

        b.Line();
        b.MethodWithParams("OnActivate", "MaybeAgent:?agent", "void");
        b.Line("if (Agent := MaybeAgent?, Player := player[Agent]):");
        b.Indent();
        b.Line("ApplyMutator(Player)");
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.MethodWithParams("OnDeactivate", "MaybeAgent:?agent", "void");
        b.Line("if (Agent := MaybeAgent?, Player := player[Agent]):");
        b.Indent();
        b.Line("RemoveMutator(Player)");
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.MethodWithParams("ApplyMutator", "Player:player", "void");
        b.Line("SpeedModifier.Activate(Player)");
        b.Line("set AffectedPlayers += array{Player}");
        b.Line("set IsActive = true");
        b.Line($"Print(\"Movement mutator applied: {speedMulti}x speed\")");
        b.EndBlock();

        b.Line();
        b.MethodWithParams("RemoveMutator", "Player:player", "void");
        b.Line("SpeedModifier.Deactivate(Player)");
        b.Line("var NewList : []player = array{}");
        b.Line("for (P : AffectedPlayers, P <> Player):");
        b.Indent();
        b.Line("set NewList += array{P}");
        b.Dedent();
        b.Line("set AffectedPlayers = NewList");
        b.Line("Print(\"Movement mutator removed\")");
        b.EndBlock();

        b.EndBlock(); // class
        return b.ToString();
    }

    // =====================================================================
    //  EMPTY DEVICE
    // =====================================================================

    private string GenerateEmptyDevice(VerseFileRequest request)
    {
        var b = new VerseCodeBuilder();
        b.Comment($"{request.ClassName} — Generated by WellVersed");
        b.Comment("Minimal creative_device. Add your logic here.");
        b.Line();
        b.StandardUsings();
        b.Line();

        b.ClassDef(request.ClassName);
        b.Comment("Add @editable properties here for devices you want to reference:");
        b.Comment("@editable");
        b.Comment("MyTrigger : trigger_device = trigger_device{}");
        b.Line();

        b.OnBegin();
        b.Line("Print(\"{request.ClassName} started!\")");
        b.EndBlock();

        b.EndBlock(); // class
        return b.ToString();
    }

    // =====================================================================
    //  HELPERS
    // =====================================================================

    private static bool GetBool(Dictionary<string, string> p, string key, bool def)
        => p.TryGetValue(key, out var v) ? bool.TryParse(v, out var b) && b : def;

    private static int GetInt(Dictionary<string, string> p, string key, int def)
        => p.TryGetValue(key, out var v) ? int.TryParse(v, out var i) ? i : def : def;

    private static string GetString(Dictionary<string, string> p, string key, string def)
        => p.TryGetValue(key, out var v) ? v : def;
}
