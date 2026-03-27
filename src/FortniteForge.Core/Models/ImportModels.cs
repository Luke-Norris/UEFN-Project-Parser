namespace WellVersed.Core.Models;

/// <summary>
/// Preview of what a system import will do before executing.
/// Returned by SystemImporter.PreviewImport so the user can review
/// device placements, wiring, and missing dependencies before committing.
/// </summary>
public class ImportPreview
{
    /// <summary>
    /// Name of the system being imported (e.g., "Trigger + Timer + Score Manager").
    /// </summary>
    public string SystemName { get; set; } = "";

    /// <summary>
    /// Source level the system was extracted from.
    /// </summary>
    public string SourceLevel { get; set; } = "";

    /// <summary>
    /// Target level where devices will be placed.
    /// </summary>
    public string TargetLevel { get; set; } = "";

    /// <summary>
    /// Number of devices in the system.
    /// </summary>
    public int DeviceCount { get; set; }

    /// <summary>
    /// Number of wiring connections to recreate.
    /// </summary>
    public int WiringCount { get; set; }

    /// <summary>
    /// Whether the extracted system included verse code.
    /// </summary>
    public bool HasVerse { get; set; }

    /// <summary>
    /// Device classes that have no matching template actor in the target level.
    /// Import cannot proceed while this list is non-empty.
    /// </summary>
    public List<string> MissingTemplates { get; set; } = new();

    /// <summary>
    /// Non-fatal warnings (e.g., property types that may not transfer cleanly).
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Whether the import can proceed. False when MissingTemplates is non-empty.
    /// </summary>
    public bool CanImport { get; set; }

    /// <summary>
    /// Details of each device that will be placed.
    /// </summary>
    public List<ImportDevicePreview> Devices { get; set; } = new();
}

/// <summary>
/// Preview details for a single device within an import.
/// </summary>
public class ImportDevicePreview
{
    public string DeviceClass { get; set; } = "";
    public string Role { get; set; } = "";
    public string TemplateActor { get; set; } = "";
    public Vector3Info PlacementPosition { get; set; } = new();
    public int PropertyOverrideCount { get; set; }
}

/// <summary>
/// Result of an executed system import.
/// </summary>
public class ImportResult
{
    /// <summary>
    /// Whether the import completed without fatal errors.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Summary message describing what happened.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Number of devices successfully placed in the target level.
    /// </summary>
    public int DevicesPlaced { get; set; }

    /// <summary>
    /// Number of wiring connections created between placed devices.
    /// </summary>
    public int WiresCreated { get; set; }

    /// <summary>
    /// Path to the generated verse file, if the system included verse code.
    /// </summary>
    public string? VerseFilePath { get; set; }

    /// <summary>
    /// Errors encountered during import (device placement failures, wiring issues, etc.).
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Path to the backup created before modifying the target level.
    /// </summary>
    public string? BackupPath { get; set; }

    /// <summary>
    /// Names of the actors created in the target level.
    /// </summary>
    public List<string> CreatedActors { get; set; } = new();
}
