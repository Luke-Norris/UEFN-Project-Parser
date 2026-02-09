namespace FortniteForge.Core.Models;

/// <summary>
/// Lightweight summary of a parsed .uasset file — what Claude sees first.
/// </summary>
public class AssetInfo
{
    /// <summary>
    /// Absolute path to the .uasset file on disk.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Path relative to the project's Content/ directory.
    /// </summary>
    public string RelativePath { get; set; } = "";

    /// <summary>
    /// Asset name (filename without extension).
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The primary class of this asset (e.g., "Blueprint", "World", "StaticMesh").
    /// </summary>
    public string AssetClass { get; set; } = "";

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Whether this asset contains cooked data (Epic's — read-only).
    /// </summary>
    public bool IsCooked { get; set; }

    /// <summary>
    /// Whether this asset is safe to modify based on config and cooked status.
    /// </summary>
    public bool IsModifiable { get; set; }

    /// <summary>
    /// Number of exports in the asset.
    /// </summary>
    public int ExportCount { get; set; }

    /// <summary>
    /// Number of imports in the asset.
    /// </summary>
    public int ImportCount { get; set; }

    /// <summary>
    /// Last modified timestamp.
    /// </summary>
    public DateTime LastModified { get; set; }

    /// <summary>
    /// The Unreal Engine version this asset targets.
    /// </summary>
    public string EngineVersion { get; set; } = "";

    /// <summary>
    /// Quick summary of what this asset contains (e.g., "Blueprint with 3 components").
    /// </summary>
    public string Summary { get; set; } = "";
}

/// <summary>
/// Detailed view of an asset — returned when Claude drills into a specific asset.
/// </summary>
public class AssetDetail : AssetInfo
{
    /// <summary>
    /// All exports in this asset.
    /// </summary>
    public List<ExportInfo> Exports { get; set; } = new();

    /// <summary>
    /// All imports (external references) in this asset.
    /// </summary>
    public List<ImportInfo> Imports { get; set; } = new();

    /// <summary>
    /// Package flags from the asset header.
    /// </summary>
    public uint PackageFlags { get; set; }

    /// <summary>
    /// Custom version information.
    /// </summary>
    public Dictionary<string, int> CustomVersions { get; set; } = new();
}

/// <summary>
/// Information about a single export within an asset.
/// </summary>
public class ExportInfo
{
    public int Index { get; set; }
    public string ObjectName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string SuperClass { get; set; } = "";
    public long SerialSize { get; set; }

    /// <summary>
    /// Key properties of this export — simplified name:value pairs.
    /// Large/binary properties are summarized rather than included in full.
    /// </summary>
    public List<PropertyInfo> Properties { get; set; } = new();
}

/// <summary>
/// Information about an import (external dependency) in an asset.
/// </summary>
public class ImportInfo
{
    public int Index { get; set; }
    public string ObjectName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string PackageName { get; set; } = "";
}

/// <summary>
/// Simplified representation of a UProperty.
/// </summary>
public class PropertyInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Value { get; set; } = "";
    public int ArrayIndex { get; set; }

    /// <summary>
    /// Whether this property is user-configurable (editable in UEFN details panel).
    /// </summary>
    public bool IsConfigurable { get; set; } = true;
}
