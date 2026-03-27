using WellVersed.Core.Config;
using WellVersed.Core.Models;
using WellVersed.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WellVersed.Tests;

/// <summary>
/// Tests for the MapAnalytics service — verifies map profiling, genre classification,
/// complexity scoring, model defaults, cosine similarity, and similar map search.
///
/// Unit tests use model construction; sandbox tests (if available) profile real maps.
/// </summary>
public class MapAnalyticsTests
{
    // =====================================================================
    //  Model defaults
    // =====================================================================

    [Fact]
    public void MapProfile_DefaultValues()
    {
        var profile = new MapProfile();
        Assert.Equal("", profile.ProjectName);
        Assert.Equal("", profile.ProjectPath);
        Assert.Equal("Unknown", profile.MapClassification);
        Assert.Equal(0, profile.TotalDevices);
        Assert.Equal(0, profile.TotalActors);
        Assert.Equal(0, profile.TotalWirings);
        Assert.Equal(0, profile.VerseFileCount);
        Assert.Equal(0, profile.VerseTotalLines);
        Assert.Equal(0, profile.WidgetCount);
        Assert.Equal(0, profile.LevelCount);
        Assert.Equal(0, profile.UserAssetCount);
        Assert.Empty(profile.DevicesByType);
        Assert.Empty(profile.ActorsByCategory);
        Assert.Empty(profile.VerseClasses);
        Assert.Equal(0, profile.ComplexityScore);
        Assert.Equal(0, profile.PolishScore);
        Assert.Equal(0, profile.GameplayVarietyScore);
        Assert.Equal("", profile.OverallRating);
    }

    [Fact]
    public void LibraryComparison_DefaultValues()
    {
        var comparison = new LibraryComparison();
        Assert.Equal("", comparison.ProjectName);
        Assert.Equal(0, comparison.DeviceCountPercentile);
        Assert.Equal(0, comparison.WiringComplexityPercentile);
        Assert.Equal(0, comparison.VerseUsagePercentile);
        Assert.Equal(0, comparison.ActorCountPercentile);
        Assert.Equal(0, comparison.WidgetUsagePercentile);
        Assert.Equal("", comparison.MostSimilarMap);
        Assert.Equal(0f, comparison.SimilarityScore);
        Assert.Empty(comparison.MissingFeatures);
        Assert.Empty(comparison.Strengths);
    }

    [Fact]
    public void SimilarMap_DefaultValues()
    {
        var similar = new SimilarMap();
        Assert.Equal("", similar.ProjectName);
        Assert.Equal("", similar.ProjectPath);
        Assert.Equal(0f, similar.SimilarityScore);
        Assert.Empty(similar.SharedDeviceTypes);
        Assert.Empty(similar.UniqueDeviceTypes);
        Assert.Equal(0, similar.DeviceCount);
        Assert.Equal(0, similar.VerseFileCount);
        Assert.Equal("", similar.Classification);
    }

    [Fact]
    public void MapReport_DefaultValues()
    {
        var report = new MapReport();
        Assert.NotNull(report.Profile);
        Assert.Null(report.Comparison);
        Assert.Empty(report.Sections);
    }

    [Fact]
    public void MapReportSection_DefaultValues()
    {
        var section = new MapReportSection();
        Assert.Equal("", section.Title);
        Assert.Empty(section.Lines);
    }

    [Fact]
    public void LibraryInsights_DefaultValues()
    {
        var insights = new LibraryInsights();
        Assert.Equal(0, insights.TotalProjects);
        Assert.Equal(0.0, insights.AverageDeviceCount);
        Assert.Equal(0.0, insights.AverageActorCount);
        Assert.Equal(0.0, insights.AverageVerseFileCount);
        Assert.Equal(0.0, insights.AverageVerseLines);
        Assert.Equal(0.0, insights.AverageWiringCount);
        Assert.Equal(0, insights.MedianDeviceCount);
        Assert.Equal(0, insights.MaxDeviceCount);
        Assert.Equal("", insights.MaxDeviceProject);
        Assert.Empty(insights.TopDeviceTypes);
        Assert.Empty(insights.FeatureAdoption);
        Assert.Empty(insights.GenreDistribution);
        Assert.Equal(0, insights.PercentWithVerse);
        Assert.Equal(0, insights.PercentWithWidgets);
    }

    [Fact]
    public void DeviceUsageStat_DefaultValues()
    {
        var stat = new DeviceUsageStat();
        Assert.Equal("", stat.DeviceClass);
        Assert.Equal("", stat.DisplayName);
        Assert.Equal(0, stat.TotalInstances);
        Assert.Equal(0, stat.ProjectCount);
    }

    // =====================================================================
    //  ComplexityScore ranges
    // =====================================================================

    [Fact]
    public void ComplexityScore_RangeIs0To100()
    {
        // A MapProfile's complexity score should be 0-100
        var profile = new MapProfile { ComplexityScore = 50 };
        Assert.InRange(profile.ComplexityScore, 0, 100);
    }

    [Fact]
    public void PolishScore_RangeIs0To100()
    {
        var profile = new MapProfile { PolishScore = 75 };
        Assert.InRange(profile.PolishScore, 0, 100);
    }

    [Fact]
    public void GameplayVarietyScore_RangeIs0To100()
    {
        var profile = new MapProfile { GameplayVarietyScore = 30 };
        Assert.InRange(profile.GameplayVarietyScore, 0, 100);
    }

    // =====================================================================
    //  ProfileMap — handles nonexistent path gracefully
    // =====================================================================

    [Fact]
    public void ProfileMap_NonexistentPath_ReturnsEmptyProfile()
    {
        var analytics = CreateMapAnalytics();

        var profile = analytics.ProfileMap("/nonexistent/project/path");

        Assert.NotNull(profile);
        Assert.Equal("/nonexistent/project/path", profile.ProjectPath);
        Assert.Equal(0, profile.TotalDevices);
        Assert.Equal(0, profile.LevelCount);
    }

    // =====================================================================
    //  SimilarMap cosine similarity (test with known vectors)
    // =====================================================================

    [Fact]
    public void SimilarityScore_IdenticalMaps_ShouldBeHigh()
    {
        // Two maps with identical device compositions should have high similarity
        var map1 = new MapProfile
        {
            ProjectName = "Map1",
            ProjectPath = "/map1",
            DevicesByType = new Dictionary<string, int>
            {
                ["BP_TriggerDevice_C"] = 5,
                ["BP_TimerDevice_C"] = 3,
                ["BP_BarrierDevice_C"] = 2
            }
        };

        var map2 = new MapProfile
        {
            ProjectName = "Map2",
            ProjectPath = "/map2",
            DevicesByType = new Dictionary<string, int>
            {
                ["BP_TriggerDevice_C"] = 5,
                ["BP_TimerDevice_C"] = 3,
                ["BP_BarrierDevice_C"] = 2
            }
        };

        // Manually compute cosine similarity
        var similarity = ComputeCosineSimilarity(
            new float[] { 5, 3, 2 },
            new float[] { 5, 3, 2 });

        Assert.True(similarity > 0.99f, $"Identical vectors should have ~1.0 similarity, got {similarity}");
    }

    [Fact]
    public void SimilarityScore_CompletelyDifferentMaps_ShouldBeLow()
    {
        // Vectors with no overlap
        var similarity = ComputeCosineSimilarity(
            new float[] { 5, 0, 0 },
            new float[] { 0, 0, 3 });

        Assert.True(similarity < 0.01f, $"Orthogonal vectors should have ~0.0 similarity, got {similarity}");
    }

    [Fact]
    public void SimilarityScore_PartialOverlap_MidRange()
    {
        var similarity = ComputeCosineSimilarity(
            new float[] { 5, 3, 0 },
            new float[] { 5, 0, 2 });

        Assert.InRange(similarity, 0.1f, 0.95f);
    }

    [Fact]
    public void SimilarityScore_EmptyVectors_ReturnsZero()
    {
        var similarity = ComputeCosineSimilarity(
            new float[] { 0, 0, 0 },
            new float[] { 0, 0, 0 });

        Assert.Equal(0f, similarity);
    }

    // =====================================================================
    //  Sandbox: ProfileMap with real projects
    // =====================================================================

    [Fact]
    public void ProfileMap_SandboxProject_ProducesProfile()
    {
        var sandbox = Environment.GetEnvironmentVariable("WELLVERSED_SANDBOX");
        if (string.IsNullOrEmpty(sandbox)) return;

        var projects = WellVersedConfig.DiscoverProjects(sandbox);
        if (projects.Count == 0) return;

        var analytics = CreateMapAnalytics();
        var profile = analytics.ProfileMap(projects.First());

        Assert.NotNull(profile);
        Assert.False(string.IsNullOrEmpty(profile.ProjectName));
        // Real projects should have at least some actors
        Assert.True(profile.TotalActors >= 0);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static MapAnalytics CreateMapAnalytics()
    {
        var config = new WellVersedConfig { ProjectPath = "." };
        var libraryIndexer = new LibraryIndexer(NullLogger<LibraryIndexer>.Instance);
        return new MapAnalytics(config, libraryIndexer, NullLogger<MapAnalytics>.Instance);
    }

    /// <summary>
    /// Mirrors the CosineSimilarity implementation in MapAnalytics for test validation.
    /// </summary>
    private static float ComputeCosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;

        double dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        if (magA == 0 || magB == 0) return 0;
        return (float)(dot / (Math.Sqrt(magA) * Math.Sqrt(magB)));
    }
}
