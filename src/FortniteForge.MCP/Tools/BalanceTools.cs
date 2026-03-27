using WellVersed.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace WellVersed.MCP.Tools;

/// <summary>
/// MCP tools for game balance analysis — spawn fairness, loot distribution,
/// flow analysis, timer/scoring viability, and actionable improvement suggestions.
/// </summary>
[McpServerToolType]
public class BalanceTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Analyzes a UEFN level's game balance across five dimensions: spawn fairness, " +
        "loot distribution, player flow (chokepoints and dead zones), timer/scoring viability, " +
        "and map complexity. Returns a comprehensive report with an overall grade (A+ through F) " +
        "and actionable suggestions. Use this to identify balance issues before publishing.")]
    public string analyze_game_balance(
        GameBalanceAnalyzer analyzer,
        [Description("Path to the .umap level file to analyze")] string levelPath)
    {
        var report = analyzer.AnalyzeBalance(levelPath);

        return JsonSerializer.Serialize(new
        {
            report.LevelName,
            report.LevelPath,
            overall = new
            {
                score = report.OverallBalanceScore,
                grade = report.OverallGrade,
                complexity = report.ComplexityScore,
                complexityRating = report.ComplexityRating
            },
            spawnBalance = new
            {
                fair = report.SpawnBalance.IsFair,
                totalSpawns = report.SpawnBalance.TotalSpawnDevices,
                spawnsPerTeam = report.SpawnBalance.SpawnsPerTeam,
                distanceToObjective = report.SpawnBalance.AvgDistanceToObjective,
                distanceImbalance = $"{report.SpawnBalance.DistanceImbalancePercent:P0}",
                protectionPerTeam = report.SpawnBalance.ProtectionPerTeam,
                issues = report.SpawnBalance.Issues
            },
            lootBalance = new
            {
                balanced = report.LootBalance.IsBalanced,
                totalLoot = report.LootBalance.TotalLootDevices,
                lootPerZone = report.LootBalance.LootPerZone,
                deserts = report.LootBalance.LootDeserts,
                floods = report.LootBalance.LootFloods,
                averageSpacing = $"{report.LootBalance.AverageSpacing:N0} UU",
                issues = report.LootBalance.Issues
            },
            flow = new
            {
                gridCells = report.Flow.GridCellCount,
                averageDensity = report.Flow.AverageDensity,
                densityStdDev = report.Flow.DensityStdDev,
                chokepoints = report.Flow.Chokepoints.Select(c => new
                {
                    zone = c.ZoneName,
                    devices = c.DeviceCount,
                    c.Description
                }),
                deadZones = report.Flow.DeadZones.Select(d => new
                {
                    zone = d.ZoneName,
                    d.Description
                }),
                issues = report.Flow.Issues
            },
            timerScoring = new
            {
                viable = report.TimerScoring.IsViable,
                timerDevices = report.TimerScoring.TimerDeviceCount,
                scoringDevices = report.TimerScoring.ScoringDeviceCount,
                detectedTimers = report.TimerScoring.DetectedTimerValues,
                detectedScoreTargets = report.TimerScoring.DetectedScoreTargets,
                detectedPointsPerAction = report.TimerScoring.DetectedPointsPerAction,
                maxSpawnToObjectiveDistance = $"{report.TimerScoring.MaxSpawnToObjectiveDistance:N0} UU",
                issues = report.TimerScoring.Issues
            },
            suggestions = report.Suggestions.Select(s => new
            {
                s.Priority,
                s.Category,
                s.Description,
                s.Fix,
                affectedDevices = s.AffectedDevices
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Analyzes a level's game balance and returns only the actionable improvement suggestions. " +
        "Each suggestion has a priority (Critical/Recommended/Nice), category, description, and proposed fix. " +
        "Use this for a quick fix list without the full balance report.")]
    public string suggest_balance_improvements(
        GameBalanceAnalyzer analyzer,
        [Description("Path to the .umap level file to analyze")] string levelPath)
    {
        var report = analyzer.AnalyzeBalance(levelPath);
        var suggestions = report.Suggestions;

        if (suggestions.Count == 0)
        {
            return $"Level \"{report.LevelName}\" scored {report.OverallGrade} " +
                   $"({report.OverallBalanceScore:N0}/100). No balance issues detected.";
        }

        var critical = suggestions.Where(s => s.Priority == "Critical").ToList();
        var recommended = suggestions.Where(s => s.Priority == "Recommended").ToList();
        var nice = suggestions.Where(s => s.Priority == "Nice").ToList();

        return JsonSerializer.Serialize(new
        {
            report.LevelName,
            grade = report.OverallGrade,
            score = report.OverallBalanceScore,
            summary = $"{critical.Count} critical, {recommended.Count} recommended, {nice.Count} nice-to-have",
            critical = critical.Select(s => new { s.Category, s.Description, s.Fix }),
            recommended = recommended.Select(s => new { s.Category, s.Description, s.Fix }),
            niceToHave = nice.Select(s => new { s.Category, s.Description, s.Fix })
        }, JsonOpts);
    }
}
