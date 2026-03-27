namespace WellVersed.Core.Models;

/// <summary>
/// Directed graph of all devices and their signal wiring in a level.
/// </summary>
public class DeviceGraph
{
    /// <summary>
    /// All device nodes in the graph.
    /// </summary>
    public List<DeviceNode> Nodes { get; set; } = new();

    /// <summary>
    /// Directed edges representing signal wiring between devices.
    /// </summary>
    public List<DeviceEdge> Edges { get; set; } = new();

    /// <summary>
    /// Total number of devices discovered.
    /// </summary>
    public int TotalDevices { get; set; }

    /// <summary>
    /// Total number of wiring connections discovered.
    /// </summary>
    public int TotalConnections { get; set; }
}

/// <summary>
/// A single device in the wiring graph.
/// </summary>
public class DeviceNode
{
    /// <summary>
    /// The actor/object name in the level (e.g., "TriggerDevice_3").
    /// </summary>
    public string ActorName { get; set; } = "";

    /// <summary>
    /// The UE class type (e.g., "BP_TriggerDevice_C").
    /// </summary>
    public string DeviceClass { get; set; } = "";

    /// <summary>
    /// Friendly device type name (e.g., "Trigger Device").
    /// </summary>
    public string DeviceType { get; set; } = "";

    /// <summary>
    /// World position of the device.
    /// </summary>
    public Vector3Info Position { get; set; } = new();

    /// <summary>
    /// Events this device can fire (e.g., OnTriggered, OnComplete).
    /// </summary>
    public List<string> OutputEvents { get; set; } = new();

    /// <summary>
    /// Actions this device can receive (e.g., Enable, Disable, Trigger).
    /// </summary>
    public List<string> InputActions { get; set; } = new();

    /// <summary>
    /// Number of non-default properties on this device instance.
    /// </summary>
    public int PropertyCount { get; set; }

    /// <summary>
    /// Whether this is a Verse-scripted device.
    /// </summary>
    public bool IsVerseDevice { get; set; }
}

/// <summary>
/// A directed edge in the device wiring graph: source fires an output event
/// that triggers an input action on the target.
/// </summary>
public class DeviceEdge
{
    /// <summary>
    /// Actor name of the device that fires the event.
    /// </summary>
    public string SourceActor { get; set; } = "";

    /// <summary>
    /// The output event on the source device.
    /// </summary>
    public string OutputEvent { get; set; } = "";

    /// <summary>
    /// Actor name of the device that receives the signal.
    /// </summary>
    public string TargetActor { get; set; } = "";

    /// <summary>
    /// The input action on the target device.
    /// </summary>
    public string InputAction { get; set; } = "";
}

/// <summary>
/// A single issue found by the QA logic tracer.
/// </summary>
public class QAIssue
{
    /// <summary>
    /// Unique identifier for this issue type (e.g., "DEAD_001").
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// How severe this issue is.
    /// </summary>
    public QASeverity Severity { get; set; }

    /// <summary>
    /// Category slug: dead_device, orphaned_output, missing_win, timing, spawn, loop, team, verse, property, duplicate.
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// Short human-readable title.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Detailed description of the issue.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// The device actor name most directly involved (if applicable).
    /// </summary>
    public string? AffectedDevice { get; set; }

    /// <summary>
    /// Actionable suggestion for fixing the issue.
    /// </summary>
    public string? SuggestedFix { get; set; }
}

/// <summary>
/// Severity levels for QA issues.
/// </summary>
public enum QASeverity
{
    /// <summary>Game-breaking problem that must be fixed.</summary>
    Critical,
    /// <summary>Likely a bug or logic error.</summary>
    Warning,
    /// <summary>Suggestion or minor concern.</summary>
    Info
}

/// <summary>
/// Complete QA report for a level, aggregating all analysis passes.
/// </summary>
public class QAReport
{
    /// <summary>
    /// Path to the level that was analyzed.
    /// </summary>
    public string LevelPath { get; set; } = "";

    /// <summary>
    /// Total number of devices found in the level.
    /// </summary>
    public int TotalDevices { get; set; }

    /// <summary>
    /// Total number of wiring connections found.
    /// </summary>
    public int TotalConnections { get; set; }

    /// <summary>
    /// Number of critical (game-breaking) issues.
    /// </summary>
    public int CriticalCount { get; set; }

    /// <summary>
    /// Number of warning-level issues.
    /// </summary>
    public int WarningCount { get; set; }

    /// <summary>
    /// Number of informational issues.
    /// </summary>
    public int InfoCount { get; set; }

    /// <summary>
    /// True if there are zero critical issues.
    /// </summary>
    public bool PassesQA { get; set; }

    /// <summary>
    /// All issues found across all analysis passes.
    /// </summary>
    public List<QAIssue> Issues { get; set; } = new();

    /// <summary>
    /// Human-readable summary of the QA results.
    /// </summary>
    public string Summary { get; set; } = "";
}
