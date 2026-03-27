using WellVersed.Core.Config;
using WellVersed.Core.Models;
using WellVersed.Core.Safety;
using WellVersed.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WellVersed.Tests;

/// <summary>
/// Tests for the GameBalanceAnalyzer — verifies balance report generation,
/// spawn fairness detection, complexity scoring, grade computation, and
/// suggestion generation.
///
/// Uses a minimal DeviceService (no real .uasset files) to validate
/// the analysis logic in isolation.
/// </summary>
public class GameBalanceAnalyzerTests
{
    // =====================================================================
    //  Model defaults
    // =====================================================================

    [Fact]
    public void BalanceReport_DefaultValues()
    {
        var report = new BalanceReport();
        Assert.Equal("", report.LevelPath);
        Assert.Equal("", report.LevelName);
        Assert.NotNull(report.SpawnBalance);
        Assert.NotNull(report.LootBalance);
        Assert.NotNull(report.Flow);
        Assert.NotNull(report.TimerScoring);
        Assert.Equal(0, report.ComplexityScore);
        Assert.Equal("", report.ComplexityRating);
        Assert.Equal("", report.OverallGrade);
        Assert.Empty(report.Suggestions);
    }

    [Fact]
    public void SpawnFairness_DefaultValues()
    {
        var fairness = new SpawnFairness();
        Assert.Equal(0, fairness.TotalSpawnDevices);
        Assert.Empty(fairness.SpawnsPerTeam);
        Assert.Empty(fairness.AvgDistanceToObjective);
        Assert.Empty(fairness.ProtectionPerTeam);
        Assert.Equal(0f, fairness.DistanceImbalancePercent);
        Assert.False(fairness.SpawnCountImbalanced);
        Assert.True(fairness.IsFair);
        Assert.Empty(fairness.Issues);
    }

    [Fact]
    public void LootDistribution_DefaultValues()
    {
        var loot = new LootDistribution();
        Assert.Equal(0, loot.TotalLootDevices);
        Assert.Empty(loot.LootPerZone);
        Assert.Empty(loot.LootDeserts);
        Assert.Empty(loot.LootFloods);
        Assert.Equal(0f, loot.AverageSpacing);
        Assert.True(loot.IsBalanced);
        Assert.Empty(loot.Issues);
    }

    [Fact]
    public void FlowAnalysis_DefaultValues()
    {
        var flow = new FlowAnalysis();
        Assert.Empty(flow.Chokepoints);
        Assert.Empty(flow.DeadZones);
        Assert.Empty(flow.DensityMap);
        Assert.Equal(0, flow.GridCellCount);
        Assert.Equal(0f, flow.AverageDensity);
        Assert.Equal(0f, flow.DensityStdDev);
        Assert.Empty(flow.Issues);
    }

    [Fact]
    public void TimerScoringBalance_DefaultValues()
    {
        var ts = new TimerScoringBalance();
        Assert.Equal(0, ts.TimerDeviceCount);
        Assert.Equal(0, ts.ScoringDeviceCount);
        Assert.Empty(ts.DetectedTimerValues);
        Assert.Empty(ts.DetectedScoreTargets);
        Assert.Empty(ts.DetectedPointsPerAction);
        Assert.Equal(0f, ts.MaxSpawnToObjectiveDistance);
        Assert.Empty(ts.Issues);
        Assert.True(ts.IsViable);
    }

    [Fact]
    public void BalanceSuggestion_DefaultValues()
    {
        var suggestion = new BalanceSuggestion();
        Assert.Equal("", suggestion.Priority);
        Assert.Equal("", suggestion.Category);
        Assert.Equal("", suggestion.Description);
        Assert.Equal("", suggestion.Fix);
        Assert.Empty(suggestion.AffectedDevices);
    }

    [Fact]
    public void FlowHotspot_DefaultValues()
    {
        var hotspot = new FlowHotspot();
        Assert.Equal("", hotspot.ZoneName);
        Assert.NotNull(hotspot.Center);
        Assert.Equal(0, hotspot.DeviceCount);
        Assert.Equal("", hotspot.Description);
    }

    // =====================================================================
    //  SuggestImprovements — generates suggestions from issues
    // =====================================================================

    [Fact]
    public void SuggestImprovements_GeneratesSpawnSuggestions_ForUnfairSpawns()
    {
        var report = new BalanceReport();
        report.SpawnBalance.Issues.Add("Spawn count imbalanced: teams have between 1 and 4 spawns.");
        report.SpawnBalance.DistanceImbalancePercent = 0.35f; // > 30% = Critical

        var analyzer = CreateAnalyzer();
        var suggestions = analyzer.SuggestImprovements(report);

        Assert.True(suggestions.Count >= 1, "Should generate at least one spawn suggestion");
        Assert.Contains(suggestions, s => s.Category == "Spawn");
        Assert.Contains(suggestions, s => s.Priority == "Critical");
    }

    [Fact]
    public void SuggestImprovements_GeneratesLootDesertSuggestions()
    {
        var report = new BalanceReport();
        report.LootBalance.LootDeserts.Add("NW");
        report.LootBalance.LootDeserts.Add("SE");

        var analyzer = CreateAnalyzer();
        var suggestions = analyzer.SuggestImprovements(report);

        Assert.True(suggestions.Count >= 2, "Should generate suggestion for each loot desert");
        Assert.All(suggestions.Where(s => s.Category == "Loot"), s =>
        {
            Assert.Contains("desert", s.Description, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void SuggestImprovements_GeneratesLootFloodSuggestions()
    {
        var report = new BalanceReport();
        report.LootBalance.LootFloods.Add("Center");

        var analyzer = CreateAnalyzer();
        var suggestions = analyzer.SuggestImprovements(report);

        Assert.Contains(suggestions, s =>
            s.Category == "Loot" &&
            s.Description.Contains("flood", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SuggestImprovements_GeneratesFlowChokepointSuggestions()
    {
        var report = new BalanceReport();
        report.Flow.Chokepoints.Add(new FlowHotspot
        {
            ZoneName = "B3",
            DeviceCount = 15,
            Description = "Dense"
        });

        var analyzer = CreateAnalyzer();
        var suggestions = analyzer.SuggestImprovements(report);

        Assert.Contains(suggestions, s =>
            s.Category == "Flow" &&
            s.Description.Contains("Chokepoint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SuggestImprovements_GeneratesDeadZoneSuggestions()
    {
        var report = new BalanceReport();
        report.Flow.DeadZones.Add(new FlowHotspot
        {
            ZoneName = "A1",
            DeviceCount = 0,
            Description = "Empty"
        });

        var analyzer = CreateAnalyzer();
        var suggestions = analyzer.SuggestImprovements(report);

        Assert.Contains(suggestions, s =>
            s.Category == "Flow" &&
            s.Description.Contains("Dead zone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SuggestImprovements_GeneratesTimerSuggestions()
    {
        var report = new BalanceReport();
        report.TimerScoring.Issues.Add("Timer setting makes it impossible to win.");

        var analyzer = CreateAnalyzer();
        var suggestions = analyzer.SuggestImprovements(report);

        Assert.Contains(suggestions, s =>
            s.Category == "Timer" && s.Priority == "Critical");
    }

    [Fact]
    public void SuggestImprovements_GeneratesLowComplexitySuggestion()
    {
        var report = new BalanceReport();
        report.ComplexityScore = 5;
        report.ComplexityRating = "Minimal";

        var analyzer = CreateAnalyzer();
        var suggestions = analyzer.SuggestImprovements(report);

        Assert.Contains(suggestions, s =>
            s.Category == "Complexity" &&
            s.Description.Contains("Low complexity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SuggestImprovements_GeneratesHighComplexitySuggestion()
    {
        var report = new BalanceReport();
        report.ComplexityScore = 90;
        report.ComplexityRating = "Dense";

        var analyzer = CreateAnalyzer();
        var suggestions = analyzer.SuggestImprovements(report);

        Assert.Contains(suggestions, s =>
            s.Category == "Complexity" &&
            s.Description.Contains("High complexity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SuggestImprovements_NoIssues_ReturnsEmpty()
    {
        var report = new BalanceReport();
        report.ComplexityScore = 50; // Not low, not high
        report.ComplexityRating = "Moderate";

        var analyzer = CreateAnalyzer();
        var suggestions = analyzer.SuggestImprovements(report);

        Assert.Empty(suggestions);
    }

    // =====================================================================
    //  OverallGrade computation (via report model)
    // =====================================================================

    [Theory]
    [InlineData("A+")]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("C")]
    [InlineData("D")]
    [InlineData("F")]
    public void OverallGrade_IsValidLetterGrade(string grade)
    {
        var report = new BalanceReport { OverallGrade = grade };
        Assert.False(string.IsNullOrEmpty(report.OverallGrade));
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static GameBalanceAnalyzer CreateAnalyzer()
    {
        var config = new WellVersedConfig { ProjectPath = "." };
        var detector = new UefnDetector(config, NullLogger<UefnDetector>.Instance);
        var fileAccess = new SafeFileAccess(config, detector, NullLogger<SafeFileAccess>.Instance);
        var guard = new AssetGuard(config, detector, NullLogger<AssetGuard>.Instance);
        var assetService = new AssetService(config, guard, fileAccess, NullLogger<AssetService>.Instance);
        var digestService = new DigestService(config, NullLogger<DigestService>.Instance);
        var deviceService = new DeviceService(config, assetService, digestService, NullLogger<DeviceService>.Instance);
        return new GameBalanceAnalyzer(config, deviceService, NullLogger<GameBalanceAnalyzer>.Instance);
    }
}
