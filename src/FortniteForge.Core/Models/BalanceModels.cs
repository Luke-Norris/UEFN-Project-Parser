namespace WellVersed.Core.Models;

/// <summary>
/// Complete balance analysis report for a UEFN level.
/// Covers spawn fairness, loot distribution, player flow,
/// timer/scoring viability, and overall complexity.
/// </summary>
public class BalanceReport
{
    public string LevelPath { get; set; } = "";
    public string LevelName { get; set; } = "";
    public SpawnFairness SpawnBalance { get; set; } = new();
    public LootDistribution LootBalance { get; set; } = new();
    public FlowAnalysis Flow { get; set; } = new();
    public TimerScoringBalance TimerScoring { get; set; } = new();

    /// <summary>Device count, wiring depth, verse device count rolled into a 0-100 score.</summary>
    public int ComplexityScore { get; set; }

    /// <summary>"Minimal", "Simple", "Moderate", "Complex", or "Dense".</summary>
    public string ComplexityRating { get; set; } = "";

    /// <summary>Weighted composite of all dimensions, 0-100.</summary>
    public float OverallBalanceScore { get; set; }

    /// <summary>Letter grade from A+ through F.</summary>
    public string OverallGrade { get; set; } = "";

    public List<BalanceSuggestion> Suggestions { get; set; } = new();
}

/// <summary>
/// Evaluates whether all teams have equal opportunity at spawn.
/// </summary>
public class SpawnFairness
{
    /// <summary>Number of spawn-related devices found (player spawners, spawn pads, etc.).</summary>
    public int TotalSpawnDevices { get; set; }

    /// <summary>Spawn device count per detected team/group.</summary>
    public Dictionary<string, int> SpawnsPerTeam { get; set; } = new();

    /// <summary>Average distance from each team's spawns to the nearest objective device.</summary>
    public Dictionary<string, float> AvgDistanceToObjective { get; set; } = new();

    /// <summary>Number of barrier/shield devices near each team's spawns.</summary>
    public Dictionary<string, int> ProtectionPerTeam { get; set; } = new();

    /// <summary>
    /// Max percentage difference in spawn-to-objective distance between any two teams.
    /// Above 15% is considered imbalanced.
    /// </summary>
    public float DistanceImbalancePercent { get; set; }

    /// <summary>Whether spawn counts differ between teams.</summary>
    public bool SpawnCountImbalanced { get; set; }

    /// <summary>True if all spawn metrics are within acceptable thresholds.</summary>
    public bool IsFair { get; set; } = true;

    public List<string> Issues { get; set; } = new();
}

/// <summary>
/// Analyzes how loot (item granters, vending machines, chests) is distributed across the map.
/// </summary>
public class LootDistribution
{
    /// <summary>Total loot-dispensing devices found.</summary>
    public int TotalLootDevices { get; set; }

    /// <summary>Loot device count per spatial zone (quadrant or named area).</summary>
    public Dictionary<string, int> LootPerZone { get; set; } = new();

    /// <summary>Zones with no loot devices at all.</summary>
    public List<string> LootDeserts { get; set; } = new();

    /// <summary>Zones with an excessive concentration of loot devices.</summary>
    public List<string> LootFloods { get; set; } = new();

    /// <summary>Average spacing between loot devices in Unreal units.</summary>
    public float AverageSpacing { get; set; }

    /// <summary>True if loot is reasonably distributed.</summary>
    public bool IsBalanced { get; set; } = true;

    public List<string> Issues { get; set; } = new();
}

/// <summary>
/// Analyzes player movement flow: chokepoints, dead zones, device density heat map.
/// </summary>
public class FlowAnalysis
{
    /// <summary>Regions where device density is unusually high (potential chokepoints).</summary>
    public List<FlowHotspot> Chokepoints { get; set; } = new();

    /// <summary>Large areas with no gameplay-relevant devices.</summary>
    public List<FlowHotspot> DeadZones { get; set; } = new();

    /// <summary>Device count per spatial grid cell (zone name to count).</summary>
    public Dictionary<string, int> DensityMap { get; set; } = new();

    /// <summary>Total number of spatial grid cells analyzed.</summary>
    public int GridCellCount { get; set; }

    /// <summary>Average devices per grid cell.</summary>
    public float AverageDensity { get; set; }

    /// <summary>Standard deviation of device density across cells.</summary>
    public float DensityStdDev { get; set; }

    public List<string> Issues { get; set; } = new();
}

/// <summary>
/// A spatial region flagged during flow analysis.
/// </summary>
public class FlowHotspot
{
    public string ZoneName { get; set; } = "";
    public Vector3Info Center { get; set; } = new();
    public int DeviceCount { get; set; }
    public string Description { get; set; } = "";
}

/// <summary>
/// Checks whether timer and scoring configurations allow winnable, balanced matches.
/// </summary>
public class TimerScoringBalance
{
    /// <summary>Timer-related devices found (round timers, countdown devices, etc.).</summary>
    public int TimerDeviceCount { get; set; }

    /// <summary>Scoring-related devices found (score managers, score granters, etc.).</summary>
    public int ScoringDeviceCount { get; set; }

    /// <summary>Detected round timer values in seconds (from device properties).</summary>
    public List<float> DetectedTimerValues { get; set; } = new();

    /// <summary>Detected score-to-win values.</summary>
    public List<float> DetectedScoreTargets { get; set; } = new();

    /// <summary>Detected points-per-action values.</summary>
    public List<float> DetectedPointsPerAction { get; set; } = new();

    /// <summary>Estimated maximum distance from any spawn to any objective, in Unreal units.</summary>
    public float MaxSpawnToObjectiveDistance { get; set; }

    /// <summary>Potential impossible or trivially easy win conditions detected.</summary>
    public List<string> Issues { get; set; } = new();

    /// <summary>True if no win-condition issues were found.</summary>
    public bool IsViable { get; set; } = true;
}

/// <summary>
/// An actionable suggestion for improving game balance.
/// </summary>
public class BalanceSuggestion
{
    /// <summary>"Critical", "Recommended", or "Nice".</summary>
    public string Priority { get; set; } = "";

    /// <summary>Which balance dimension this addresses (Spawn, Loot, Flow, Timer, Complexity).</summary>
    public string Category { get; set; } = "";

    /// <summary>Human-readable description of the imbalance.</summary>
    public string Description { get; set; } = "";

    /// <summary>Proposed fix action.</summary>
    public string Fix { get; set; } = "";

    /// <summary>Actor names of devices involved (if applicable).</summary>
    public List<string> AffectedDevices { get; set; } = new();
}
