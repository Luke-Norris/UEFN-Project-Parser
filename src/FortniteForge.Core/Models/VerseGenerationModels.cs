namespace FortniteForge.Core.Models;

/// <summary>
/// Request to generate a Verse source file from a template.
/// </summary>
public class VerseFileRequest
{
    /// <summary>File name without extension (e.g., "my_hud").</summary>
    public string FileName { get; set; } = "";

    /// <summary>Output directory for the .verse file. If null, uses project Verse path.</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>Which template to use.</summary>
    public VerseTemplateType TemplateType { get; set; }

    /// <summary>The Verse class name (e.g., "my_hud_controller").</summary>
    public string ClassName { get; set; } = "";

    /// <summary>Template-specific parameters.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new();
}

public enum VerseTemplateType
{
    // UI Templates
    HudOverlay,
    Scoreboard,
    InteractionMenu,
    NotificationPopup,
    ItemTracker,
    ProgressBar,
    CustomWidget,

    // Device Templates
    GameManager,
    TimerController,
    TeamManager,
    EliminationTracker,
    CollectibleSystem,
    ZoneController,
    MovementMutator,
    EmptyDevice
}

/// <summary>
/// Result of generating a Verse file.
/// </summary>
public class VerseFileResult
{
    public bool Success { get; set; }
    public string FilePath { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string SourceCode { get; set; } = "";
    public string? Error { get; set; }
    public List<string> Notes { get; set; } = new();
}

/// <summary>
/// Describes an available template and its parameters.
/// </summary>
public class VerseTemplateInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public VerseTemplateType Type { get; set; }
    public string Category { get; set; } = "";
    public List<VerseParameterInfo> Parameters { get; set; } = new();
}

/// <summary>
/// Describes a configurable parameter on a template.
/// </summary>
public class VerseParameterInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string DefaultValue { get; set; } = "";
    public bool Required { get; set; }
}

/// <summary>
/// Level analytics result — performance and complexity analysis.
/// </summary>
public class LevelAnalytics
{
    public string LevelPath { get; set; } = "";
    public string LevelName { get; set; } = "";
    public int TotalActors { get; set; }
    public int TotalExports { get; set; }
    public int TotalImports { get; set; }
    public long FileSizeBytes { get; set; }
    public Dictionary<string, int> ActorsByClass { get; set; } = new();
    public Dictionary<string, int> ActorsByCategory { get; set; } = new();
    public int DeviceCount { get; set; }
    public int UniqueDeviceTypes { get; set; }
    public List<DuplicateGroup> Duplicates { get; set; } = new();
    public BoundsInfo WorldBounds { get; set; } = new();
    public PerformanceEstimate Performance { get; set; } = new();
    public WiringAnalysis Wiring { get; set; } = new();
}

public class DuplicateGroup
{
    public string ClassName { get; set; } = "";
    public int Count { get; set; }
    public List<string> ActorNames { get; set; } = new();
    public string Suggestion { get; set; } = "";
}

public class BoundsInfo
{
    public Vector3Info Min { get; set; } = new();
    public Vector3Info Max { get; set; } = new();
    public Vector3Info Size { get; set; } = new();
    public Vector3Info Center { get; set; } = new();
}

public class PerformanceEstimate
{
    /// <summary>Simple 1-10 score where 10 is most complex.</summary>
    public int ComplexityScore { get; set; }
    public string Rating { get; set; } = "";
    public List<string> Concerns { get; set; } = new();
    public List<string> Optimizations { get; set; } = new();
    public int EstimatedMemoryMB { get; set; }
}

public class WiringAnalysis
{
    public int TotalConnections { get; set; }
    public int UnwiredDevices { get; set; }
    public int DevicesWithOutput { get; set; }
    public int DevicesWithInput { get; set; }
    public List<string> IsolatedDevices { get; set; } = new();
}
