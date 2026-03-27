namespace WellVersed.Core.Models;

/// <summary>
/// Structured game design describing WHAT a UEFN experience is — game mode,
/// objectives, scoring, economy, UI needs — independent of HOW it's implemented.
/// This is the output of Layer 1 (Game Designer) and input to Layer 2 (Project Assembler).
/// </summary>
public class GameDesign
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public GameModeType GameMode { get; set; }
    public int PlayerCount { get; set; } = 16;
    public int TeamCount { get; set; } // 0 = FFA
    public RoundConfig? Rounds { get; set; }
    public ScoringConfig? Scoring { get; set; }
    public List<ObjectiveConfig> Objectives { get; set; } = new();
    public List<SpawnConfig> SpawnAreas { get; set; } = new();
    public EconomyConfig? Economy { get; set; }
    public List<UIRequirement> UINeeds { get; set; } = new();
    public List<string> SpecialMechanics { get; set; } = new();
}

/// <summary>
/// High-level game mode classification.
/// </summary>
public enum GameModeType
{
    FreeForAll,
    TeamBased,
    Rounds,
    Progressive,
    Tycoon,
    Survival,
    Custom
}

/// <summary>
/// Round/match structure configuration.
/// </summary>
public class RoundConfig
{
    public int RoundCount { get; set; }
    public float RoundDurationSeconds { get; set; }
    public float WarmupSeconds { get; set; }
    public bool AutoRestart { get; set; }
}

/// <summary>
/// Scoring and win condition configuration.
/// </summary>
public class ScoringConfig
{
    public string WinCondition { get; set; } = "";
    public int WinScore { get; set; }
    public bool TrackKills { get; set; }
    public bool TrackObjectives { get; set; }
}

/// <summary>
/// A gameplay objective (capture, defend, collect, etc.).
/// </summary>
public class ObjectiveConfig
{
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public Vector3Info? Location { get; set; }
    public float? Radius { get; set; }
}

/// <summary>
/// Spawn area configuration for a team or FFA.
/// </summary>
public class SpawnConfig
{
    public string TeamName { get; set; } = "";
    public int SpawnCount { get; set; }
    public bool HasProtection { get; set; }
    public float ProtectionDuration { get; set; }
}

/// <summary>
/// In-game economy/currency system.
/// </summary>
public class EconomyConfig
{
    public bool HasCurrency { get; set; }
    public string CurrencyName { get; set; } = "";
    public int StartingAmount { get; set; }
    public bool PassiveIncome { get; set; }
    public List<string> Purchasables { get; set; } = new();
}

/// <summary>
/// A UI element the game requires (HUD, Scoreboard, ShopUI, etc.).
/// </summary>
public class UIRequirement
{
    /// <summary>Type: HUD, Scoreboard, ShopUI, Notification, Timer</summary>
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>
/// Result of assembling a complete UEFN project from a GameDesign.
/// </summary>
public class AssemblyResult
{
    public bool Success { get; set; }
    public string OutputPath { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public List<string> GeneratedFiles { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string? Error { get; set; }
    public GameDesign? Design { get; set; }
    public GenerationPlanSummary? Plan { get; set; }
}

/// <summary>
/// Lightweight summary of the generation plan included in assembly results.
/// </summary>
public class GenerationPlanSummary
{
    public int DeviceCount { get; set; }
    public int WiringCount { get; set; }
    public int VerseFileCount { get; set; }
    public int WidgetCount { get; set; }
    public List<string> DeviceTypes { get; set; } = new();
}

/// <summary>
/// Pre-built game mode template with a complete GameDesign.
/// </summary>
public class GameTemplate
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public GameDesign Design { get; set; } = new();
}
