namespace WellVersed.Core.Models;

/// <summary>
/// A structured plan for generating a game system — devices, wiring, and verse code.
/// Produced by GameDesigner.DesignToDevicePlan() and consumed by ProjectAssembler.
/// Also used by GenerationTools for the existing system generation pipeline.
/// </summary>
public class GenerationPlan
{
    public string Description { get; set; } = "";
    public string SystemName { get; set; } = "";
    public string Category { get; set; } = "";
    public string? LevelPath { get; set; }
    public Vector3Info Center { get; set; } = new();
    public bool IsCustom { get; set; }
    public string? CustomInstructions { get; set; }
    public string? ExistingContext { get; set; }
    public List<PlannedDevice> Devices { get; set; } = new();
    public List<PlannedWiring> Wiring { get; set; } = new();
    public string? VerseCode { get; set; }
    public List<ExecutionStep> Steps { get; set; } = new();
}

/// <summary>
/// A device to be placed as part of a generation plan.
/// </summary>
public class PlannedDevice
{
    public string Role { get; set; } = "";
    public string DeviceClass { get; set; } = "";
    public string Type { get; set; } = "";
    public Vector3Info Offset { get; set; } = new();
}

/// <summary>
/// A wiring connection between two planned devices.
/// </summary>
public class PlannedWiring
{
    public string SourceRole { get; set; } = "";
    public string OutputEvent { get; set; } = "";
    public string TargetRole { get; set; } = "";
    public string InputAction { get; set; } = "";
}

/// <summary>
/// A step in the execution plan for building a system.
/// </summary>
public class ExecutionStep
{
    public int Order { get; set; }
    public string Tool { get; set; } = "";
    public string Description { get; set; } = "";
    public Dictionary<string, string>? Parameters { get; set; }
}
