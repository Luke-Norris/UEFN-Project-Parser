using WellVersed.Core.Config;
using WellVersed.Core.Models;
using Microsoft.Extensions.Logging;

namespace WellVersed.Core.Services;

/// <summary>
/// Analyzes a UEFN level's device layout for game balance issues and suggests improvements.
/// Examines spawn fairness, loot distribution, player flow, timer/scoring viability,
/// and overall complexity — dimensions no other UEFN tool evaluates.
/// </summary>
public class GameBalanceAnalyzer
{
    private readonly WellVersedConfig _config;
    private readonly DeviceService _deviceService;
    private readonly ILogger<GameBalanceAnalyzer> _logger;

    // Grid cell size for spatial analysis (Unreal units). 5000 ≈ 50m in UE scale.
    private const float GridCellSize = 5000f;

    // Imbalance thresholds
    private const float SpawnDistanceImbalanceThreshold = 0.15f; // 15%
    private const int LootFloodThreshold = 5;    // devices per grid cell
    private const float LootDesertRadiusThreshold = 15000f; // UU with no loot

    // Device class keyword matching (case-insensitive substring checks)
    private static readonly string[] SpawnKeywords =
        { "spawn", "spawnpad", "player_spawn", "playerspawn", "respawn", "startingpoint" };
    private static readonly string[] ObjectiveKeywords =
        { "objective", "capture", "flag", "payload", "control", "checkpoint", "goal" };
    private static readonly string[] LootKeywords =
        { "granter", "itemgranter", "vending", "chest", "pickup", "loot", "supply" };
    private static readonly string[] BarrierKeywords =
        { "barrier", "shield", "wall", "protection" };
    private static readonly string[] TimerKeywords =
        { "timer", "countdown", "round", "clock" };
    private static readonly string[] ScoringKeywords =
        { "score", "scoring", "point", "elimination", "leaderboard" };

    public GameBalanceAnalyzer(
        WellVersedConfig config,
        DeviceService deviceService,
        ILogger<GameBalanceAnalyzer> logger)
    {
        _config = config;
        _deviceService = deviceService;
        _logger = logger;
    }

    /// <summary>
    /// Performs a full balance analysis on the given level, including spawn fairness,
    /// loot distribution, flow analysis, timer/scoring viability, and complexity scoring.
    /// </summary>
    public BalanceReport AnalyzeBalance(string levelPath)
    {
        var report = new BalanceReport
        {
            LevelPath = levelPath,
            LevelName = Path.GetFileNameWithoutExtension(levelPath)
        };

        // Gather all devices from the level (handles both inline and external actors)
        List<DeviceInfo> devices;
        try
        {
            devices = CollectAllDevices(levelPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect devices from level {Path}", levelPath);
            report.Suggestions.Add(new BalanceSuggestion
            {
                Priority = "Critical",
                Category = "Analysis",
                Description = $"Could not parse level: {ex.Message}",
                Fix = "Ensure the level file is a valid .umap and the project path is correct."
            });
            report.OverallGrade = "F";
            return report;
        }

        _logger.LogInformation("Analyzing balance for {Level}: {Count} devices found",
            report.LevelName, devices.Count);

        // Classify devices by role
        var spawns = devices.Where(d => MatchesAny(d, SpawnKeywords)).ToList();
        var objectives = devices.Where(d => MatchesAny(d, ObjectiveKeywords)).ToList();
        var loot = devices.Where(d => MatchesAny(d, LootKeywords)).ToList();
        var barriers = devices.Where(d => MatchesAny(d, BarrierKeywords)).ToList();
        var timers = devices.Where(d => MatchesAny(d, TimerKeywords)).ToList();
        var scoring = devices.Where(d => MatchesAny(d, ScoringKeywords)).ToList();

        // Run each analysis dimension
        report.SpawnBalance = AnalyzeSpawnFairness(spawns, objectives, barriers);
        report.LootBalance = AnalyzeLootDistribution(loot, devices);
        report.Flow = AnalyzeFlow(devices);
        report.TimerScoring = AnalyzeTimerScoring(timers, scoring, spawns, objectives);
        AnalyzeComplexity(report, devices);

        // Calculate overall score and grade
        CalculateOverallScore(report);

        // Generate suggestions for all detected issues
        report.Suggestions = SuggestImprovements(report);

        return report;
    }

    /// <summary>
    /// Generates actionable fix suggestions from an existing balance report.
    /// Each suggestion includes a priority, description, and proposed fix.
    /// </summary>
    public List<BalanceSuggestion> SuggestImprovements(BalanceReport report)
    {
        var suggestions = new List<BalanceSuggestion>();

        // Spawn fairness suggestions
        foreach (var issue in report.SpawnBalance.Issues)
        {
            suggestions.Add(new BalanceSuggestion
            {
                Priority = report.SpawnBalance.DistanceImbalancePercent > 0.30f ? "Critical" : "Recommended",
                Category = "Spawn",
                Description = issue,
                Fix = DeriveSpawnFix(issue, report.SpawnBalance)
            });
        }

        // Loot distribution suggestions
        foreach (var desert in report.LootBalance.LootDeserts)
        {
            suggestions.Add(new BalanceSuggestion
            {
                Priority = "Recommended",
                Category = "Loot",
                Description = $"Loot desert detected in zone {desert} — no pickups available.",
                Fix = $"Add 1-2 item granters or a vending machine in the {desert} area."
            });
        }

        foreach (var flood in report.LootBalance.LootFloods)
        {
            suggestions.Add(new BalanceSuggestion
            {
                Priority = "Recommended",
                Category = "Loot",
                Description = $"Loot flood detected in zone {flood} — too many pickups concentrated here.",
                Fix = $"Redistribute some loot devices from {flood} to under-served areas."
            });
        }

        foreach (var issue in report.LootBalance.Issues)
        {
            if (!suggestions.Any(s => s.Category == "Loot" && s.Description == issue))
            {
                suggestions.Add(new BalanceSuggestion
                {
                    Priority = "Nice",
                    Category = "Loot",
                    Description = issue,
                    Fix = "Review loot placement for even coverage across the map."
                });
            }
        }

        // Flow suggestions
        foreach (var choke in report.Flow.Chokepoints)
        {
            suggestions.Add(new BalanceSuggestion
            {
                Priority = "Recommended",
                Category = "Flow",
                Description = $"Chokepoint at {choke.ZoneName}: {choke.DeviceCount} devices packed into a small area.",
                Fix = "Add an alternate path or spread devices to adjacent zones to reduce bottleneck risk."
            });
        }

        foreach (var dead in report.Flow.DeadZones)
        {
            suggestions.Add(new BalanceSuggestion
            {
                Priority = "Nice",
                Category = "Flow",
                Description = $"Dead zone at {dead.ZoneName} — no gameplay devices, players have no reason to go here.",
                Fix = "Add a pickup, trigger, or environmental hazard to give this area purpose."
            });
        }

        foreach (var issue in report.Flow.Issues)
        {
            if (!suggestions.Any(s => s.Category == "Flow" && s.Description == issue))
            {
                suggestions.Add(new BalanceSuggestion
                {
                    Priority = "Nice",
                    Category = "Flow",
                    Description = issue,
                    Fix = "Adjust device placement to improve player flow across the map."
                });
            }
        }

        // Timer/scoring suggestions
        foreach (var issue in report.TimerScoring.Issues)
        {
            var isCritical = issue.Contains("impossible", StringComparison.OrdinalIgnoreCase)
                          || issue.Contains("unreachable", StringComparison.OrdinalIgnoreCase);
            suggestions.Add(new BalanceSuggestion
            {
                Priority = isCritical ? "Critical" : "Recommended",
                Category = "Timer",
                Description = issue,
                Fix = DeriveTimerFix(issue)
            });
        }

        // Complexity suggestions
        if (report.ComplexityScore < 10)
        {
            suggestions.Add(new BalanceSuggestion
            {
                Priority = "Nice",
                Category = "Complexity",
                Description = $"Low complexity ({report.ComplexityScore}/100, rated {report.ComplexityRating}). " +
                              "Most successful maps use 15-30 devices with varied types.",
                Fix = "Add more gameplay devices (triggers, zones, scoring) to create engaging mechanics."
            });
        }
        else if (report.ComplexityScore > 85)
        {
            suggestions.Add(new BalanceSuggestion
            {
                Priority = "Recommended",
                Category = "Complexity",
                Description = $"High complexity ({report.ComplexityScore}/100, rated {report.ComplexityRating}). " +
                              "Too many devices can hurt performance and confuse players.",
                Fix = "Consider consolidating devices or removing redundant ones to simplify gameplay."
            });
        }

        return suggestions;
    }

    // ────────────────────────────────────────────────────────────
    // Device collection
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Collects devices from both the .umap inline actors and the __ExternalActors__ folder.
    /// </summary>
    private List<DeviceInfo> CollectAllDevices(string levelPath)
    {
        var devices = new List<DeviceInfo>();

        // Inline actors from the .umap
        try
        {
            devices.AddRange(_deviceService.ListDevicesInLevel(levelPath));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not parse inline actors from {Path}", levelPath);
        }

        // External actors
        var contentDir = Path.GetDirectoryName(levelPath);
        if (contentDir == null) return devices;

        var levelName = Path.GetFileNameWithoutExtension(levelPath);
        var externalActorsDir = Path.Combine(contentDir, "__ExternalActors__", levelName);

        if (Directory.Exists(externalActorsDir))
        {
            foreach (var extAsset in Directory.EnumerateFiles(externalActorsDir, "*.uasset", SearchOption.AllDirectories))
            {
                try
                {
                    var extDevices = _deviceService.ListDevicesInLevel(extAsset);
                    devices.AddRange(extDevices);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not parse external actor {Path}", extAsset);
                }
            }
        }

        return devices;
    }

    // ────────────────────────────────────────────────────────────
    // Spawn fairness
    // ────────────────────────────────────────────────────────────

    private SpawnFairness AnalyzeSpawnFairness(
        List<DeviceInfo> spawns,
        List<DeviceInfo> objectives,
        List<DeviceInfo> barriers)
    {
        var result = new SpawnFairness
        {
            TotalSpawnDevices = spawns.Count
        };

        if (spawns.Count == 0)
        {
            result.IsFair = false;
            result.Issues.Add("No spawn devices found in level. Players need at least one spawn point.");
            return result;
        }

        // Group spawns by team affinity. Use the device label, actor name, or
        // property hints containing "Team" to bucket them.
        var teamGroups = GroupByTeam(spawns);
        foreach (var (team, members) in teamGroups)
            result.SpawnsPerTeam[team] = members.Count;

        // Check spawn count balance across teams
        if (teamGroups.Count > 1)
        {
            var counts = teamGroups.Values.Select(g => g.Count).ToList();
            if (counts.Max() > counts.Min() * 2)
            {
                result.SpawnCountImbalanced = true;
                result.IsFair = false;
                result.Issues.Add(
                    $"Spawn count imbalanced: teams have between {counts.Min()} and {counts.Max()} spawns.");
            }
        }

        // Measure distance from each team's spawn centroid to the nearest objective
        if (objectives.Count > 0)
        {
            foreach (var (team, members) in teamGroups)
            {
                var centroid = Centroid(members);
                var nearest = objectives.Min(o => Distance(centroid, o.Location));
                result.AvgDistanceToObjective[team] = nearest;
            }

            if (result.AvgDistanceToObjective.Count >= 2)
            {
                var distances = result.AvgDistanceToObjective.Values.ToList();
                var minDist = distances.Min();
                var maxDist = distances.Max();
                result.DistanceImbalancePercent = minDist > 0
                    ? (maxDist - minDist) / minDist
                    : 0;

                if (result.DistanceImbalancePercent > SpawnDistanceImbalanceThreshold)
                {
                    result.IsFair = false;
                    result.Issues.Add(
                        $"Spawn-to-objective distance differs by {result.DistanceImbalancePercent:P0} between teams " +
                        $"(closest {minDist:N0} UU, farthest {maxDist:N0} UU). Threshold is {SpawnDistanceImbalanceThreshold:P0}.");
                }
            }
        }
        else
        {
            result.Issues.Add("No objective devices found — cannot measure spawn-to-objective distance.");
        }

        // Barrier protection per team
        foreach (var (team, members) in teamGroups)
        {
            var centroid = Centroid(members);
            var nearbyBarriers = barriers.Count(b => Distance(centroid, b.Location) < 3000f);
            result.ProtectionPerTeam[team] = nearbyBarriers;
        }

        if (result.ProtectionPerTeam.Count >= 2)
        {
            var protCounts = result.ProtectionPerTeam.Values.ToList();
            if (protCounts.Max() > 0 && protCounts.Min() == 0)
            {
                result.IsFair = false;
                result.Issues.Add("Some teams have barrier protection near spawn while others have none.");
            }
        }

        return result;
    }

    // ────────────────────────────────────────────────────────────
    // Loot distribution
    // ────────────────────────────────────────────────────────────

    private LootDistribution AnalyzeLootDistribution(List<DeviceInfo> lootDevices, List<DeviceInfo> allDevices)
    {
        var result = new LootDistribution
        {
            TotalLootDevices = lootDevices.Count
        };

        if (lootDevices.Count == 0)
        {
            result.IsBalanced = true; // No loot is not necessarily imbalanced — could be PvP-only
            result.Issues.Add("No loot devices found. If this is a loot-based mode, add item granters or vending machines.");
            return result;
        }

        // Compute map bounds from all devices
        var bounds = ComputeBounds(allDevices);
        var zones = BuildGrid(bounds);

        // Assign each loot device to a grid zone
        foreach (var device in lootDevices)
        {
            var zone = GetZoneName(device.Location, bounds);
            result.LootPerZone.TryGetValue(zone, out var count);
            result.LootPerZone[zone] = count + 1;
        }

        // Identify deserts and floods
        foreach (var zone in zones)
        {
            result.LootPerZone.TryGetValue(zone, out var count);
            if (count == 0)
                result.LootDeserts.Add(zone);
            else if (count >= LootFloodThreshold)
                result.LootFloods.Add(zone);
        }

        // Average spacing between loot devices
        if (lootDevices.Count >= 2)
        {
            var totalDist = 0f;
            var pairs = 0;
            for (int i = 0; i < lootDevices.Count; i++)
            {
                for (int j = i + 1; j < lootDevices.Count; j++)
                {
                    totalDist += Distance(lootDevices[i].Location, lootDevices[j].Location);
                    pairs++;
                }
            }
            result.AverageSpacing = pairs > 0 ? totalDist / pairs : 0;
        }

        if (result.LootDeserts.Count > 0 || result.LootFloods.Count > 0)
            result.IsBalanced = false;

        if (result.LootDeserts.Count > zones.Count / 2)
            result.Issues.Add($"Loot covers only {zones.Count - result.LootDeserts.Count}/{zones.Count} zones. " +
                              "Large portions of the map have no pickups.");

        return result;
    }

    // ────────────────────────────────────────────────────────────
    // Flow analysis
    // ────────────────────────────────────────────────────────────

    private FlowAnalysis AnalyzeFlow(List<DeviceInfo> allDevices)
    {
        var result = new FlowAnalysis();

        if (allDevices.Count == 0)
        {
            result.Issues.Add("No devices found — cannot analyze player flow.");
            return result;
        }

        var bounds = ComputeBounds(allDevices);
        var zones = BuildGrid(bounds);
        result.GridCellCount = zones.Count;

        // Count devices per cell
        foreach (var device in allDevices)
        {
            var zone = GetZoneName(device.Location, bounds);
            result.DensityMap.TryGetValue(zone, out var count);
            result.DensityMap[zone] = count + 1;
        }

        // Fill in empty zones
        foreach (var zone in zones)
        {
            if (!result.DensityMap.ContainsKey(zone))
                result.DensityMap[zone] = 0;
        }

        // Statistics
        var values = result.DensityMap.Values.Select(v => (float)v).ToList();
        result.AverageDensity = values.Count > 0 ? values.Average() : 0;
        result.DensityStdDev = StdDev(values);

        // Identify chokepoints (density > mean + 2*stddev) and dead zones (density == 0)
        var threshold = result.AverageDensity + 2 * result.DensityStdDev;

        foreach (var (zone, count) in result.DensityMap)
        {
            if (count > threshold && threshold > 0)
            {
                result.Chokepoints.Add(new FlowHotspot
                {
                    ZoneName = zone,
                    Center = ZoneCenter(zone, bounds),
                    DeviceCount = count,
                    Description = $"{count} devices in a single grid cell — potential chokepoint."
                });
            }
            else if (count == 0)
            {
                result.DeadZones.Add(new FlowHotspot
                {
                    ZoneName = zone,
                    Center = ZoneCenter(zone, bounds),
                    DeviceCount = 0,
                    Description = "No gameplay devices — players have no reason to visit this area."
                });
            }
        }

        if (result.Chokepoints.Count > 0)
            result.Issues.Add($"{result.Chokepoints.Count} chokepoint(s) detected where devices are overly concentrated.");
        if (result.DeadZones.Count > zones.Count / 2)
            result.Issues.Add($"{result.DeadZones.Count}/{zones.Count} zones are dead — over half the map has no devices.");

        return result;
    }

    // ────────────────────────────────────────────────────────────
    // Timer / scoring viability
    // ────────────────────────────────────────────────────────────

    private TimerScoringBalance AnalyzeTimerScoring(
        List<DeviceInfo> timers,
        List<DeviceInfo> scoring,
        List<DeviceInfo> spawns,
        List<DeviceInfo> objectives)
    {
        var result = new TimerScoringBalance
        {
            TimerDeviceCount = timers.Count,
            ScoringDeviceCount = scoring.Count
        };

        // Extract numeric property values that look like timer or score settings
        foreach (var device in timers)
        {
            foreach (var prop in device.Properties)
            {
                var name = prop.Name.ToLowerInvariant();
                if ((name.Contains("time") || name.Contains("duration") || name.Contains("round"))
                    && float.TryParse(prop.Value, out var val) && val > 0)
                {
                    result.DetectedTimerValues.Add(val);
                }
            }
        }

        foreach (var device in scoring)
        {
            foreach (var prop in device.Properties)
            {
                var name = prop.Name.ToLowerInvariant();
                if ((name.Contains("score") || name.Contains("target") || name.Contains("win"))
                    && float.TryParse(prop.Value, out var val) && val > 0)
                {
                    result.DetectedScoreTargets.Add(val);
                }
                if ((name.Contains("point") || name.Contains("reward") || name.Contains("amount"))
                    && float.TryParse(prop.Value, out var pts) && pts > 0)
                {
                    result.DetectedPointsPerAction.Add(pts);
                }
            }
        }

        // Estimate max spawn-to-objective distance
        if (spawns.Count > 0 && objectives.Count > 0)
        {
            result.MaxSpawnToObjectiveDistance = spawns
                .SelectMany(s => objectives.Select(o => Distance(s.Location, o.Location)))
                .Max();
        }

        // Check for impossible win conditions
        if (result.DetectedScoreTargets.Count > 0 && result.DetectedPointsPerAction.Count > 0)
        {
            var maxTarget = result.DetectedScoreTargets.Max();
            var maxPts = result.DetectedPointsPerAction.Max();
            if (maxPts > 0)
            {
                var actionsNeeded = maxTarget / maxPts;
                if (result.DetectedTimerValues.Count > 0)
                {
                    var maxTime = result.DetectedTimerValues.Max();
                    // Assume ~3 seconds per scoring action as an optimistic floor
                    var possibleActions = maxTime / 3f;
                    if (actionsNeeded > possibleActions)
                    {
                        result.IsViable = false;
                        result.Issues.Add(
                            $"Potentially impossible win condition: need {actionsNeeded:N0} scoring actions " +
                            $"but timer ({maxTime:N0}s) only allows ~{possibleActions:N0} at best pace.");
                    }
                }
            }
        }

        // Check if timer is too short to reach objectives
        if (result.DetectedTimerValues.Count > 0 && result.MaxSpawnToObjectiveDistance > 0)
        {
            var minTimer = result.DetectedTimerValues.Min();
            // Assume ~600 UU/s player movement speed (approximate UEFN sprint speed)
            var timeToReach = result.MaxSpawnToObjectiveDistance / 600f;
            if (timeToReach > minTimer * 0.8f)
            {
                result.IsViable = false;
                result.Issues.Add(
                    $"Timer may be too short: farthest spawn is ~{timeToReach:N0}s from objective " +
                    $"but shortest timer is {minTimer:N0}s.");
            }
        }

        if (timers.Count == 0 && scoring.Count == 0)
        {
            result.Issues.Add("No timer or scoring devices detected. Consider adding a win condition.");
        }

        return result;
    }

    // ────────────────────────────────────────────────────────────
    // Complexity scoring
    // ────────────────────────────────────────────────────────────

    private void AnalyzeComplexity(BalanceReport report, List<DeviceInfo> devices)
    {
        var totalDevices = devices.Count;
        var wiredDevices = devices.Count(d => d.Wiring.Count > 0);
        var verseDevices = devices.Count(d => d.IsVerseDevice);
        var uniqueTypes = devices.Select(d => d.DeviceClass).Distinct().Count();

        // Score: weight device count (40%), wiring depth (30%), type variety (20%), verse (10%)
        var deviceScore = Math.Clamp(totalDevices / 50f * 40f, 0, 40);
        var wiringScore = totalDevices > 0
            ? Math.Clamp(wiredDevices / (float)totalDevices * 30f, 0, 30)
            : 0;
        var varietyScore = Math.Clamp(uniqueTypes / 15f * 20f, 0, 20);
        var verseScore = Math.Clamp(verseDevices / 5f * 10f, 0, 10);

        report.ComplexityScore = (int)Math.Round(deviceScore + wiringScore + varietyScore + verseScore);
        report.ComplexityScore = Math.Clamp(report.ComplexityScore, 0, 100);

        report.ComplexityRating = report.ComplexityScore switch
        {
            <= 10 => "Minimal",
            <= 30 => "Simple",
            <= 55 => "Moderate",
            <= 80 => "Complex",
            _ => "Dense"
        };
    }

    // ────────────────────────────────────────────────────────────
    // Overall scoring and grading
    // ────────────────────────────────────────────────────────────

    private void CalculateOverallScore(BalanceReport report)
    {
        // Weighted composite: Spawn 30%, Loot 20%, Flow 20%, Timer 15%, Complexity 15%
        var spawnScore = report.SpawnBalance.IsFair ? 100f : Math.Max(0f, 100f - report.SpawnBalance.Issues.Count * 25f);
        var lootScore = report.LootBalance.IsBalanced ? 100f : Math.Max(0f, 100f - report.LootBalance.Issues.Count * 20f);
        var flowScore = CalculateFlowScore(report.Flow);
        var timerScore = report.TimerScoring.IsViable ? 100f : Math.Max(0f, 100f - report.TimerScoring.Issues.Count * 30f);

        // Complexity: best score when Moderate (40-60), penalize extremes
        var complexPenalty = Math.Abs(report.ComplexityScore - 50) / 50f * 40f;
        var complexityScore = Math.Max(0f, 100f - complexPenalty);

        report.OverallBalanceScore = (float)Math.Round(
            spawnScore * 0.30f +
            lootScore * 0.20f +
            flowScore * 0.20f +
            timerScore * 0.15f +
            complexityScore * 0.15f,
            1);

        report.OverallBalanceScore = Math.Clamp(report.OverallBalanceScore, 0f, 100f);

        report.OverallGrade = report.OverallBalanceScore switch
        {
            >= 95 => "A+",
            >= 90 => "A",
            >= 85 => "A-",
            >= 80 => "B+",
            >= 75 => "B",
            >= 70 => "B-",
            >= 65 => "C+",
            >= 60 => "C",
            >= 55 => "C-",
            >= 50 => "D+",
            >= 45 => "D",
            >= 40 => "D-",
            _ => "F"
        };
    }

    private static float CalculateFlowScore(FlowAnalysis flow)
    {
        if (flow.GridCellCount == 0) return 50f; // No data, neutral
        var deadRatio = flow.DeadZones.Count / (float)flow.GridCellCount;
        var chokeRatio = flow.Chokepoints.Count / (float)flow.GridCellCount;

        // Penalize for dead zones and chokepoints
        var score = 100f - deadRatio * 100f - chokeRatio * 80f - flow.Issues.Count * 10f;
        return Math.Clamp(score, 0f, 100f);
    }

    // ────────────────────────────────────────────────────────────
    // Suggestion helpers
    // ────────────────────────────────────────────────────────────

    private static string DeriveSpawnFix(string issue, SpawnFairness spawnData)
    {
        if (issue.Contains("No spawn"))
            return "Place at least one Player Spawn Pad device in the level.";
        if (issue.Contains("count imbalanced"))
            return "Equalize spawn pad counts — each team should have the same number.";
        if (issue.Contains("distance differs"))
            return "Move spawn locations so all teams are roughly equidistant from the objective.";
        if (issue.Contains("barrier"))
            return "Add barrier/shield devices near unprotected spawns, or remove them from all spawns for equality.";
        if (issue.Contains("No objective"))
            return "Add an objective device (capture zone, flag, checkpoint) so spawn distance can be evaluated.";
        return "Review spawn placement for fairness across teams.";
    }

    private static string DeriveTimerFix(string issue)
    {
        if (issue.Contains("impossible win"))
            return "Either increase the round timer, lower the score target, or increase points per action.";
        if (issue.Contains("too short"))
            return "Increase the round timer or move spawns closer to the objective.";
        if (issue.Contains("No timer"))
            return "Add a Round Settings or Timer device to define a win condition.";
        return "Review timer and scoring device settings for viability.";
    }

    // ────────────────────────────────────────────────────────────
    // Device classification helpers
    // ────────────────────────────────────────────────────────────

    private static bool MatchesAny(DeviceInfo device, string[] keywords)
    {
        var className = device.DeviceClass.ToLowerInvariant();
        var typeName = device.DeviceType.ToLowerInvariant();
        var label = device.Label.ToLowerInvariant();
        var actorName = device.ActorName.ToLowerInvariant();

        return keywords.Any(kw =>
            className.Contains(kw) ||
            typeName.Contains(kw) ||
            label.Contains(kw) ||
            actorName.Contains(kw));
    }

    /// <summary>
    /// Groups devices by detected team affinity. Uses label or property hints
    /// containing team indicators. Falls back to spatial clustering for unlabeled spawns.
    /// </summary>
    private static Dictionary<string, List<DeviceInfo>> GroupByTeam(List<DeviceInfo> devices)
    {
        var groups = new Dictionary<string, List<DeviceInfo>>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in devices)
        {
            var team = DetectTeam(device);
            if (!groups.ContainsKey(team))
                groups[team] = new List<DeviceInfo>();
            groups[team].Add(device);
        }

        // If everything ended up in "Default", try spatial clustering (2 groups)
        if (groups.Count == 1 && groups.ContainsKey("Default") && devices.Count >= 2)
        {
            var clustered = SpatialCluster(devices, 2);
            if (clustered.Count > 1)
                return clustered;
        }

        return groups;
    }

    private static string DetectTeam(DeviceInfo device)
    {
        // Check label and properties for team hints
        var searchables = new List<string> { device.Label, device.ActorName };
        searchables.AddRange(device.Properties
            .Where(p => p.Name.Contains("Team", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Value));

        foreach (var text in searchables)
        {
            var lower = text.ToLowerInvariant();
            if (lower.Contains("team1") || lower.Contains("team 1") || lower.Contains("team_1") || lower.Contains("teama"))
                return "Team1";
            if (lower.Contains("team2") || lower.Contains("team 2") || lower.Contains("team_2") || lower.Contains("teamb"))
                return "Team2";
            if (lower.Contains("team3") || lower.Contains("team 3") || lower.Contains("team_3") || lower.Contains("teamc"))
                return "Team3";
            if (lower.Contains("team4") || lower.Contains("team 4") || lower.Contains("team_4") || lower.Contains("teamd"))
                return "Team4";
        }

        return "Default";
    }

    /// <summary>
    /// Simple k-means-style spatial clustering for unlabeled devices.
    /// </summary>
    private static Dictionary<string, List<DeviceInfo>> SpatialCluster(List<DeviceInfo> devices, int k)
    {
        if (devices.Count < k)
        {
            return new Dictionary<string, List<DeviceInfo>>
            {
                ["Default"] = devices.ToList()
            };
        }

        // Initialize centroids by picking the two most distant devices
        var sorted = devices.OrderBy(d => d.Location.X + d.Location.Y).ToList();
        var centroids = new Vector3Info[k];
        centroids[0] = Clone(sorted.First().Location);
        centroids[1] = Clone(sorted.Last().Location);

        var assignments = new int[devices.Count];

        // Run a few iterations of k-means
        for (int iter = 0; iter < 10; iter++)
        {
            // Assign each device to nearest centroid
            for (int i = 0; i < devices.Count; i++)
            {
                var minDist = float.MaxValue;
                for (int c = 0; c < k; c++)
                {
                    var d = Distance(devices[i].Location, centroids[c]);
                    if (d < minDist)
                    {
                        minDist = d;
                        assignments[i] = c;
                    }
                }
            }

            // Recompute centroids
            for (int c = 0; c < k; c++)
            {
                var members = Enumerable.Range(0, devices.Count)
                    .Where(i => assignments[i] == c)
                    .Select(i => devices[i])
                    .ToList();
                if (members.Count > 0)
                    centroids[c] = Centroid(members);
            }
        }

        var result = new Dictionary<string, List<DeviceInfo>>();
        for (int i = 0; i < devices.Count; i++)
        {
            var team = $"Team{assignments[i] + 1}";
            if (!result.ContainsKey(team))
                result[team] = new List<DeviceInfo>();
            result[team].Add(devices[i]);
        }

        return result;
    }

    // ────────────────────────────────────────────────────────────
    // Spatial math helpers
    // ────────────────────────────────────────────────────────────

    private static float Distance(Vector3Info a, Vector3Info b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static Vector3Info Centroid(List<DeviceInfo> devices)
    {
        if (devices.Count == 0) return new Vector3Info();
        return new Vector3Info(
            devices.Average(d => d.Location.X),
            devices.Average(d => d.Location.Y),
            devices.Average(d => d.Location.Z));
    }

    private static Vector3Info Clone(Vector3Info v) => new(v.X, v.Y, v.Z);

    private static float StdDev(List<float> values)
    {
        if (values.Count <= 1) return 0;
        var mean = values.Average();
        var sumSq = values.Sum(v => (v - mean) * (v - mean));
        return MathF.Sqrt(sumSq / values.Count);
    }

    /// <summary>
    /// Computes the axis-aligned bounding box of all device positions.
    /// </summary>
    private static (Vector3Info Min, Vector3Info Max) ComputeBounds(List<DeviceInfo> devices)
    {
        if (devices.Count == 0)
            return (new Vector3Info(), new Vector3Info());

        var minX = devices.Min(d => d.Location.X);
        var minY = devices.Min(d => d.Location.Y);
        var minZ = devices.Min(d => d.Location.Z);
        var maxX = devices.Max(d => d.Location.X);
        var maxY = devices.Max(d => d.Location.Y);
        var maxZ = devices.Max(d => d.Location.Z);

        return (new Vector3Info(minX, minY, minZ), new Vector3Info(maxX, maxY, maxZ));
    }

    /// <summary>
    /// Builds a flat list of grid zone names that cover the bounding box.
    /// Zone names encode their grid coordinates (e.g., "R2C3" for row 2, column 3).
    /// </summary>
    private static List<string> BuildGrid((Vector3Info Min, Vector3Info Max) bounds)
    {
        var sizeX = bounds.Max.X - bounds.Min.X;
        var sizeY = bounds.Max.Y - bounds.Min.Y;

        var cols = Math.Max(1, (int)MathF.Ceiling(sizeX / GridCellSize));
        var rows = Math.Max(1, (int)MathF.Ceiling(sizeY / GridCellSize));

        // Cap to a reasonable grid to avoid huge allocations on very large maps
        cols = Math.Min(cols, 20);
        rows = Math.Min(rows, 20);

        var zones = new List<string>();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                zones.Add($"R{r}C{c}");

        return zones;
    }

    /// <summary>
    /// Maps a world position to a grid zone name.
    /// </summary>
    private static string GetZoneName(Vector3Info pos, (Vector3Info Min, Vector3Info Max) bounds)
    {
        var sizeX = bounds.Max.X - bounds.Min.X;
        var sizeY = bounds.Max.Y - bounds.Min.Y;

        var cols = Math.Max(1, (int)MathF.Ceiling(sizeX / GridCellSize));
        var rows = Math.Max(1, (int)MathF.Ceiling(sizeY / GridCellSize));
        cols = Math.Min(cols, 20);
        rows = Math.Min(rows, 20);

        var col = sizeX > 0
            ? Math.Clamp((int)((pos.X - bounds.Min.X) / GridCellSize), 0, cols - 1)
            : 0;
        var row = sizeY > 0
            ? Math.Clamp((int)((pos.Y - bounds.Min.Y) / GridCellSize), 0, rows - 1)
            : 0;

        return $"R{row}C{col}";
    }

    /// <summary>
    /// Recovers the world-space center of a grid zone from its name and the bounds.
    /// </summary>
    private static Vector3Info ZoneCenter(string zoneName, (Vector3Info Min, Vector3Info Max) bounds)
    {
        // Parse "R{row}C{col}"
        var parts = zoneName.Split('C');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0].TrimStart('R'), out var row) ||
            !int.TryParse(parts[1], out var col))
        {
            return new Vector3Info();
        }

        var cx = bounds.Min.X + (col + 0.5f) * GridCellSize;
        var cy = bounds.Min.Y + (row + 0.5f) * GridCellSize;
        var cz = (bounds.Min.Z + bounds.Max.Z) / 2f;

        return new Vector3Info(cx, cy, cz);
    }
}
