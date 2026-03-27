namespace WellVersed.Core.Models;

/// <summary>
/// A point-in-time snapshot of a UEFN project's file state.
/// Stores file hashes, timestamps, and metadata for diffing.
/// </summary>
public class ProjectSnapshot
{
    /// <summary>Unique snapshot identifier (timestamp-based).</summary>
    public string Id { get; set; } = "";

    /// <summary>Human-readable description (e.g. "Before terrain rework").</summary>
    public string Description { get; set; } = "";

    /// <summary>When this snapshot was taken.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Absolute path to the project root.</summary>
    public string ProjectPath { get; set; } = "";

    /// <summary>Project name derived from the directory.</summary>
    public string ProjectName { get; set; } = "";

    /// <summary>All tracked files in this snapshot.</summary>
    public List<SnapshotFileEntry> Files { get; set; } = new();

    /// <summary>Total .uasset file count at snapshot time.</summary>
    public int UassetCount { get; set; }

    /// <summary>Total .verse file count at snapshot time.</summary>
    public int VerseCount { get; set; }

    /// <summary>Path where this snapshot JSON is stored.</summary>
    public string? SnapshotPath { get; set; }
}

/// <summary>
/// A single file's state within a snapshot.
/// </summary>
public class SnapshotFileEntry
{
    /// <summary>Path relative to the project's Content directory.</summary>
    public string RelativePath { get; set; } = "";

    /// <summary>SHA-256 hash of the file contents.</summary>
    public string Hash { get; set; } = "";

    /// <summary>File size in bytes.</summary>
    public long FileSize { get; set; }

    /// <summary>Last modified time on disk.</summary>
    public DateTime LastModified { get; set; }

    /// <summary>File extension (.uasset, .verse, .umap).</summary>
    public string Extension { get; set; } = "";

    // ─── Device metadata (for .uasset files in __ExternalActors__) ───

    /// <summary>Actor name extracted from the export, if applicable.</summary>
    public string? ActorName { get; set; }

    /// <summary>Actor class (e.g. "trigger_device"), if applicable.</summary>
    public string? ActorClass { get; set; }

    /// <summary>Actor position as "X,Y,Z" string, if applicable.</summary>
    public string? Position { get; set; }

    /// <summary>Number of non-default properties on this actor.</summary>
    public int? PropertyCount { get; set; }

    // ─── Verse metadata ───

    /// <summary>Line count for .verse files.</summary>
    public int? LineCount { get; set; }
}

/// <summary>
/// The result of comparing two project states — what changed between them.
/// </summary>
public class ProjectDiff
{
    /// <summary>Human-readable summary (e.g. "3 files added, 2 modified, 1 deleted").</summary>
    public string Description { get; set; } = "";

    /// <summary>Timestamp of the older state.</summary>
    public DateTime OlderTimestamp { get; set; }

    /// <summary>Timestamp of the newer state.</summary>
    public DateTime NewerTimestamp { get; set; }

    /// <summary>Individual file-level changes.</summary>
    public List<FileDiff> Changes { get; set; } = new();

    /// <summary>Number of files added.</summary>
    public int AddedCount => Changes.Count(c => c.Type == DiffType.Added);

    /// <summary>Number of files modified.</summary>
    public int ModifiedCount => Changes.Count(c => c.Type == DiffType.Modified);

    /// <summary>Number of files deleted.</summary>
    public int DeletedCount => Changes.Count(c => c.Type == DiffType.Deleted);

    /// <summary>Total property-level changes across all modified .uasset files.</summary>
    public int TotalPropertyChanges => Changes.Sum(c => c.PropertyChanges?.Count ?? 0);
}

/// <summary>
/// Type of change detected for a file.
/// </summary>
public enum DiffType
{
    Unchanged,
    Added,
    Modified,
    Deleted
}

/// <summary>
/// A single file's diff between two states.
/// </summary>
public class FileDiff
{
    /// <summary>Relative path within the project Content directory.</summary>
    public string FilePath { get; set; } = "";

    /// <summary>What kind of change this file underwent.</summary>
    public DiffType Type { get; set; }

    /// <summary>Hash from the older snapshot (null if Added).</summary>
    public string? OldHash { get; set; }

    /// <summary>Hash from the newer state (null if Deleted).</summary>
    public string? NewHash { get; set; }

    /// <summary>File size in the older state.</summary>
    public long? OldSize { get; set; }

    /// <summary>File size in the newer state.</summary>
    public long? NewSize { get; set; }

    // ─── .uasset property-level diffs ───

    /// <summary>Property-level changes for modified .uasset files.</summary>
    public List<PropertyDiff>? PropertyChanges { get; set; }

    /// <summary>Actor class, if this is an external actor file.</summary>
    public string? ActorClass { get; set; }

    /// <summary>Actor name, if this is an external actor file.</summary>
    public string? ActorName { get; set; }

    // ─── .verse line-level diffs ───

    /// <summary>Lines added (for .verse files).</summary>
    public int? LinesAdded { get; set; }

    /// <summary>Lines removed (for .verse files).</summary>
    public int? LinesRemoved { get; set; }
}

/// <summary>
/// A single property value change within a .uasset file.
/// </summary>
public class PropertyDiff
{
    /// <summary>Actor or export name containing this property.</summary>
    public string ActorName { get; set; } = "";

    /// <summary>Property name that changed.</summary>
    public string PropertyName { get; set; } = "";

    /// <summary>Previous value (null if property was added).</summary>
    public string? OldValue { get; set; }

    /// <summary>New value (null if property was removed).</summary>
    public string? NewValue { get; set; }
}

/// <summary>
/// Summary info for listing available snapshots.
/// </summary>
public class SnapshotSummary
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string ProjectName { get; set; } = "";
    public int FileCount { get; set; }
    public int UassetCount { get; set; }
    public int VerseCount { get; set; }
    public string SnapshotPath { get; set; } = "";
}
