namespace WellVersed.Core.Models;

/// <summary>
/// Full semantic analysis of a .verse file — classes, functions, patterns, anti-patterns.
/// </summary>
public class VerseAnalysis
{
    public string FilePath { get; set; } = "";
    public int LineCount { get; set; }
    public List<string> Classes { get; set; } = new();
    public List<string> Functions { get; set; } = new();
    public List<string> EditableProperties { get; set; } = new();
    public List<string> EventSubscriptions { get; set; } = new();
    public List<string> Variables { get; set; } = new();

    /// <summary>
    /// High-level patterns detected: "timer_loop", "elimination_handler",
    /// "score_tracking", "ui_update", "spawn_system", "round_management", etc.
    /// </summary>
    public List<string> DetectedPatterns { get; set; } = new();

    public List<VerseAntiPattern> AntiPatterns { get; set; } = new();

    /// <summary>
    /// 1-10 complexity score based on class count, nesting depth, device references,
    /// and control flow complexity.
    /// </summary>
    public int ComplexityScore { get; set; }
}

/// <summary>
/// A code quality issue or common mistake found in a .verse file.
/// </summary>
public class VerseAntiPattern
{
    /// <summary>Machine-readable ID, e.g. "sleep_without_suspends".</summary>
    public string Id { get; set; } = "";

    /// <summary>1-based line number where the issue was detected.</summary>
    public int Line { get; set; }

    /// <summary>"error", "warning", or "info".</summary>
    public string Severity { get; set; } = "warning";

    /// <summary>Short human-readable title.</summary>
    public string Title { get; set; } = "";

    /// <summary>Detailed explanation of why this is a problem.</summary>
    public string Description { get; set; } = "";

    /// <summary>What the developer should do to fix it.</summary>
    public string Fix { get; set; } = "";

    /// <summary>The problematic code snippet (if applicable).</summary>
    public string? CodeBefore { get; set; }

    /// <summary>The corrected code snippet (if applicable).</summary>
    public string? CodeAfter { get; set; }
}

/// <summary>
/// An @editable device reference extracted from Verse source,
/// cross-referenced against placed devices in the level.
/// </summary>
public class VerseDeviceReference
{
    /// <summary>Variable name in Verse (e.g. "MyTimer").</summary>
    public string VariableName { get; set; } = "";

    /// <summary>Verse type (e.g. "timer_device").</summary>
    public string VerseType { get; set; } = "";

    /// <summary>Line number of the @editable declaration.</summary>
    public int Line { get; set; }

    /// <summary>Whether a matching device was found in the level.</summary>
    public bool FoundInLevel { get; set; }

    /// <summary>The matched actor name in the level (if found).</summary>
    public string? MatchedActorName { get; set; }

    /// <summary>The matched device class (if found).</summary>
    public string? MatchedDeviceClass { get; set; }
}

/// <summary>
/// Result of cross-referencing Verse @editable declarations against placed level devices.
/// </summary>
public class DeviceMismatchReport
{
    /// <summary>Path to the .verse file analyzed.</summary>
    public string VerseFilePath { get; set; } = "";

    /// <summary>Path to the level analyzed.</summary>
    public string LevelPath { get; set; } = "";

    /// <summary>All @editable device references found in the Verse file.</summary>
    public List<VerseDeviceReference> VerseReferences { get; set; } = new();

    /// <summary>Devices placed in the level that have no corresponding @editable in any Verse file.</summary>
    public List<string> UnreferencedDevices { get; set; } = new();

    /// <summary>@editable references that don't match any placed device type in the level.</summary>
    public List<string> MissingDevices { get; set; } = new();

    /// <summary>Total placed devices in the level.</summary>
    public int TotalLevelDevices { get; set; }
}

/// <summary>
/// A suggested code improvement with before/after examples.
/// </summary>
public class RefactoringSuggestion
{
    /// <summary>Machine-readable category, e.g. "extract_function", "add_editable".</summary>
    public string Category { get; set; } = "";

    /// <summary>Human-readable description of what to do.</summary>
    public string Description { get; set; } = "";

    /// <summary>Starting line of the code to refactor.</summary>
    public int LineStart { get; set; }

    /// <summary>Ending line of the code to refactor.</summary>
    public int LineEnd { get; set; }

    /// <summary>The current code.</summary>
    public string CodeBefore { get; set; } = "";

    /// <summary>The suggested replacement.</summary>
    public string CodeAfter { get; set; } = "";

    /// <summary>"low", "medium", or "high" — how impactful the refactoring would be.</summary>
    public string Impact { get; set; } = "medium";
}
