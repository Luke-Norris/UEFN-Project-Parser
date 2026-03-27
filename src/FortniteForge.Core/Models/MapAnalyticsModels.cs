namespace WellVersed.Core.Models;

/// <summary>
/// Comprehensive profile of a single UEFN map project.
/// </summary>
public class MapProfile
{
    public string ProjectName { get; set; } = "";
    public string ProjectPath { get; set; } = "";

    /// <summary>
    /// Classified genre: "Battle Royale", "Deathmatch", "Tycoon", "Parkour", "Horror", "Racing", "Puzzle", etc.
    /// </summary>
    public string MapClassification { get; set; } = "Unknown";

    public int TotalDevices { get; set; }
    public int TotalActors { get; set; }
    public int TotalWirings { get; set; }
    public int VerseFileCount { get; set; }
    public int VerseTotalLines { get; set; }
    public int WidgetCount { get; set; }
    public int LevelCount { get; set; }
    public int UserAssetCount { get; set; }

    /// <summary>Device class name to count.</summary>
    public Dictionary<string, int> DevicesByType { get; set; } = new();

    /// <summary>Actor category (Device, StaticMesh, Light, etc.) to count.</summary>
    public Dictionary<string, int> ActorsByCategory { get; set; } = new();

    /// <summary>Verse class names found in the project.</summary>
    public List<string> VerseClasses { get; set; } = new();

    /// <summary>0-100 score: how much logic, wiring, and verse code is present.</summary>
    public int ComplexityScore { get; set; }

    /// <summary>0-100 score: how finished/polished the map appears (labels, widgets, verse quality).</summary>
    public int PolishScore { get; set; }

    /// <summary>0-100 score: variety of device types and gameplay systems.</summary>
    public int GameplayVarietyScore { get; set; }

    /// <summary>Letter rating: S, A, B, C, D.</summary>
    public string OverallRating { get; set; } = "";
}

/// <summary>
/// Comparison of one map against the full reference library.
/// </summary>
public class LibraryComparison
{
    public string ProjectName { get; set; } = "";

    /// <summary>Percentage of library maps this map has more devices than.</summary>
    public int DeviceCountPercentile { get; set; }

    /// <summary>Percentage of library maps this map has more wiring than.</summary>
    public int WiringComplexityPercentile { get; set; }

    /// <summary>Percentage of library maps this map uses more verse than.</summary>
    public int VerseUsagePercentile { get; set; }

    /// <summary>Percentage of library maps this map has more actors than.</summary>
    public int ActorCountPercentile { get; set; }

    /// <summary>Percentage of library maps this map has more widget blueprints than.</summary>
    public int WidgetUsagePercentile { get; set; }

    /// <summary>Name of the most similar map by device composition.</summary>
    public string MostSimilarMap { get; set; } = "";

    /// <summary>Cosine similarity with the most similar map (0.0 - 1.0).</summary>
    public float SimilarityScore { get; set; }

    /// <summary>Common gameplay features present in many library maps but absent here.</summary>
    public List<string> MissingFeatures { get; set; } = new();

    /// <summary>Things this map does well relative to the library.</summary>
    public List<string> Strengths { get; set; } = new();
}

/// <summary>
/// Aggregate statistics across all projects in the reference library.
/// </summary>
public class LibraryInsights
{
    public int TotalProjects { get; set; }
    public DateTime AnalyzedAt { get; set; }

    public double AverageDeviceCount { get; set; }
    public double AverageActorCount { get; set; }
    public double AverageVerseFileCount { get; set; }
    public double AverageVerseLines { get; set; }
    public double AverageWiringCount { get; set; }

    public int MedianDeviceCount { get; set; }
    public int MaxDeviceCount { get; set; }
    public string MaxDeviceProject { get; set; } = "";

    /// <summary>Top N most-used device classes across all projects.</summary>
    public List<DeviceUsageStat> TopDeviceTypes { get; set; } = new();

    /// <summary>What percentage of maps use each common feature/device category.</summary>
    public Dictionary<string, int> FeatureAdoption { get; set; } = new();

    /// <summary>Genre distribution across the library.</summary>
    public Dictionary<string, int> GenreDistribution { get; set; } = new();

    /// <summary>Percentage of maps that have any verse code.</summary>
    public int PercentWithVerse { get; set; }

    /// <summary>Percentage of maps that have widget blueprints.</summary>
    public int PercentWithWidgets { get; set; }
}

public class DeviceUsageStat
{
    public string DeviceClass { get; set; } = "";
    public string DisplayName { get; set; } = "";

    /// <summary>Total instances across all library projects.</summary>
    public int TotalInstances { get; set; }

    /// <summary>How many distinct projects use this device.</summary>
    public int ProjectCount { get; set; }
}

/// <summary>
/// Structured report combining profile, library comparison, and suggestions.
/// </summary>
public class MapReport
{
    public MapProfile Profile { get; set; } = new();
    public LibraryComparison? Comparison { get; set; }

    public List<MapReportSection> Sections { get; set; } = new();
}

public class MapReportSection
{
    public string Title { get; set; } = "";
    public List<string> Lines { get; set; } = new();
}

/// <summary>
/// A library map ranked by similarity to a target map.
/// </summary>
public class SimilarMap
{
    public string ProjectName { get; set; } = "";
    public string ProjectPath { get; set; } = "";

    /// <summary>Cosine similarity (0.0 - 1.0).</summary>
    public float SimilarityScore { get; set; }

    /// <summary>Which device types are shared.</summary>
    public List<string> SharedDeviceTypes { get; set; } = new();

    /// <summary>Device types the similar map has that the target does not.</summary>
    public List<string> UniqueDeviceTypes { get; set; } = new();

    public int DeviceCount { get; set; }
    public int VerseFileCount { get; set; }
    public string Classification { get; set; } = "";
}
