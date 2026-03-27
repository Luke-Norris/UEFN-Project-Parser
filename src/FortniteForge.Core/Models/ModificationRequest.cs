namespace WellVersed.Core.Models;

/// <summary>
/// Describes a requested modification to an asset.
/// Used for both dry-run previews and actual application.
/// </summary>
public class ModificationRequest
{
    /// <summary>
    /// Unique ID for tracking this modification through preview → apply.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// The type of modification to perform.
    /// </summary>
    public ModificationType Type { get; set; }

    /// <summary>
    /// Path to the asset file being modified.
    /// </summary>
    public string AssetPath { get; set; } = "";

    /// <summary>
    /// For property changes: the export/actor name containing the property.
    /// </summary>
    public string? TargetObject { get; set; }

    /// <summary>
    /// For property changes: the property name to modify.
    /// </summary>
    public string? PropertyName { get; set; }

    /// <summary>
    /// For property changes: the new value to set.
    /// </summary>
    public string? NewValue { get; set; }

    /// <summary>
    /// For wiring: the source device actor name.
    /// </summary>
    public string? SourceDevice { get; set; }

    /// <summary>
    /// For wiring: the output event name.
    /// </summary>
    public string? OutputEvent { get; set; }

    /// <summary>
    /// For wiring: the target device actor name.
    /// </summary>
    public string? TargetDevice { get; set; }

    /// <summary>
    /// For wiring: the input action name.
    /// </summary>
    public string? InputAction { get; set; }

    /// <summary>
    /// For adding devices: the device class to add.
    /// </summary>
    public string? DeviceClass { get; set; }

    /// <summary>
    /// For adding devices: initial property values.
    /// </summary>
    public Dictionary<string, string>? InitialProperties { get; set; }

    /// <summary>
    /// For adding devices: world location.
    /// </summary>
    public Vector3Info? Location { get; set; }

    /// <summary>
    /// Timestamp when this request was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Result of a modification preview (dry-run).
/// </summary>
public class ModificationPreview
{
    /// <summary>
    /// The request ID this preview corresponds to.
    /// </summary>
    public string RequestId { get; set; } = "";

    /// <summary>
    /// Whether the modification is safe to apply.
    /// </summary>
    public bool IsSafe { get; set; }

    /// <summary>
    /// Human-readable description of what will change.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// The file(s) that will be modified.
    /// </summary>
    public List<string> AffectedFiles { get; set; } = new();

    /// <summary>
    /// Specific changes that will be made.
    /// </summary>
    public List<ChangeDetail> Changes { get; set; } = new();

    /// <summary>
    /// Any safety warnings about this modification.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Reasons the modification was blocked (if IsSafe is false).
    /// </summary>
    public List<string> BlockReasons { get; set; } = new();
}

public class ChangeDetail
{
    public string What { get; set; } = "";
    public string? OldValue { get; set; }
    public string NewValue { get; set; } = "";
}

/// <summary>
/// Result after a modification has been applied.
/// </summary>
public class ModificationResult
{
    public string RequestId { get; set; } = "";
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? BackupPath { get; set; }
    public List<string> ModifiedFiles { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public enum ModificationType
{
    SetProperty,
    AddDevice,
    RemoveDevice,
    WireDevices,
    UnwireDevices,
    DuplicateDevice
}
