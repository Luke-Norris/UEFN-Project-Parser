using System.Security.Cryptography;
using System.Text.Json;
using WellVersed.Core.Config;
using WellVersed.Core.Models;
using Microsoft.Extensions.Logging;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace WellVersed.Core.Services;

/// <summary>
/// Snapshots a UEFN project's file state and diffs between snapshots.
/// UEFN has no version control — this service fills that gap by tracking
/// exactly what changed between saves.
///
/// All .uasset reads go through SafeFileAccess (copy-on-read) to avoid
/// file locking conflicts with UEFN.
/// </summary>
public class ProjectDiffService
{
    private readonly WellVersedConfig _config;
    private readonly SafeFileAccess _fileAccess;
    private readonly ILogger<ProjectDiffService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ProjectDiffService(
        WellVersedConfig config,
        SafeFileAccess fileAccess,
        ILogger<ProjectDiffService> logger)
    {
        _config = config;
        _fileAccess = fileAccess;
        _logger = logger;
    }

    /// <summary>
    /// Takes a snapshot of the current project state.
    /// Records every .uasset, .umap, and .verse file with hashes and metadata.
    /// For external actor files, also records actor name, class, position, and property count.
    /// Saves the snapshot as JSON to .wellversed/snapshots/.
    /// </summary>
    public ProjectSnapshot TakeSnapshot(string projectPath, string? description = null)
    {
        var config = new WellVersedConfig { ProjectPath = projectPath };
        var contentPath = config.ContentPath;

        if (!Directory.Exists(contentPath))
            throw new DirectoryNotFoundException($"Content directory not found: {contentPath}");

        var snapshot = new ProjectSnapshot
        {
            Id = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"),
            Description = description ?? $"Snapshot at {DateTime.Now:g}",
            Timestamp = DateTime.UtcNow,
            ProjectPath = projectPath,
            ProjectName = config.ProjectName,
        };

        // Enumerate all tracked file types
        var trackedExtensions = new[] { "*.uasset", "*.umap", "*.verse" };
        var allFiles = trackedExtensions
            .SelectMany(ext => Directory.EnumerateFiles(contentPath, ext, SearchOption.AllDirectories))
            .ToList();

        int uassetCount = 0, verseCount = 0;

        foreach (var filePath in allFiles)
        {
            try
            {
                var entry = BuildFileEntry(filePath, contentPath);
                snapshot.Files.Add(entry);

                if (entry.Extension is ".uasset" or ".umap") uassetCount++;
                if (entry.Extension == ".verse") verseCount++;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipping file during snapshot: {Path}", filePath);
            }
        }

        snapshot.UassetCount = uassetCount;
        snapshot.VerseCount = verseCount;

        // Save to .wellversed/snapshots/
        var snapshotDir = GetSnapshotDirectory(projectPath);
        Directory.CreateDirectory(snapshotDir);

        var snapshotPath = Path.Combine(snapshotDir, $"{snapshot.Id}.json");
        var json = JsonSerializer.Serialize(snapshot, JsonOpts);
        File.WriteAllText(snapshotPath, json);

        snapshot.SnapshotPath = snapshotPath;
        _logger.LogInformation(
            "Snapshot taken: {Id} — {FileCount} files ({Uasset} assets, {Verse} verse)",
            snapshot.Id, snapshot.Files.Count, uassetCount, verseCount);

        return snapshot;
    }

    /// <summary>
    /// Compares the current project state against a saved snapshot.
    /// For modified .uasset files, parses both versions and diffs properties.
    /// </summary>
    public ProjectDiff CompareToSnapshot(string projectPath, string snapshotPath)
    {
        var snapshot = LoadSnapshot(snapshotPath);
        var config = new WellVersedConfig { ProjectPath = projectPath };
        var contentPath = config.ContentPath;

        if (!Directory.Exists(contentPath))
            throw new DirectoryNotFoundException($"Content directory not found: {contentPath}");

        // Build the current state
        var currentFiles = new Dictionary<string, SnapshotFileEntry>(StringComparer.OrdinalIgnoreCase);
        var trackedExtensions = new[] { "*.uasset", "*.umap", "*.verse" };
        var allFiles = trackedExtensions
            .SelectMany(ext => Directory.EnumerateFiles(contentPath, ext, SearchOption.AllDirectories))
            .ToList();

        foreach (var filePath in allFiles)
        {
            try
            {
                var entry = BuildFileEntry(filePath, contentPath);
                currentFiles[entry.RelativePath] = entry;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipping file during comparison: {Path}", filePath);
            }
        }

        // Build the old state lookup
        var oldFiles = new Dictionary<string, SnapshotFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot.Files)
            oldFiles[entry.RelativePath] = entry;

        // Compute diffs
        var changes = new List<FileDiff>();

        // Check for modified and deleted files
        foreach (var (relativePath, oldEntry) in oldFiles)
        {
            if (currentFiles.TryGetValue(relativePath, out var currentEntry))
            {
                if (oldEntry.Hash != currentEntry.Hash)
                {
                    var diff = new FileDiff
                    {
                        FilePath = relativePath,
                        Type = DiffType.Modified,
                        OldHash = oldEntry.Hash,
                        NewHash = currentEntry.Hash,
                        OldSize = oldEntry.FileSize,
                        NewSize = currentEntry.FileSize,
                        ActorClass = currentEntry.ActorClass ?? oldEntry.ActorClass,
                        ActorName = currentEntry.ActorName ?? oldEntry.ActorName,
                    };

                    // Property-level diff for .uasset files
                    if (relativePath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                    {
                        var fullPath = Path.Combine(contentPath, relativePath);
                        diff.PropertyChanges = DiffAssetProperties(snapshot, relativePath, fullPath);
                    }

                    // Line diff for .verse files
                    if (relativePath.EndsWith(".verse", StringComparison.OrdinalIgnoreCase))
                    {
                        ComputeVerseDiff(diff, oldEntry, currentEntry, contentPath);
                    }

                    changes.Add(diff);
                }
                // else: unchanged, skip
            }
            else
            {
                // File existed in old snapshot but not now — deleted
                changes.Add(new FileDiff
                {
                    FilePath = relativePath,
                    Type = DiffType.Deleted,
                    OldHash = oldEntry.Hash,
                    OldSize = oldEntry.FileSize,
                    ActorClass = oldEntry.ActorClass,
                    ActorName = oldEntry.ActorName,
                });
            }
        }

        // Check for added files
        foreach (var (relativePath, currentEntry) in currentFiles)
        {
            if (!oldFiles.ContainsKey(relativePath))
            {
                changes.Add(new FileDiff
                {
                    FilePath = relativePath,
                    Type = DiffType.Added,
                    NewHash = currentEntry.Hash,
                    NewSize = currentEntry.FileSize,
                    ActorClass = currentEntry.ActorClass,
                    ActorName = currentEntry.ActorName,
                });
            }
        }

        var diff_result = new ProjectDiff
        {
            OlderTimestamp = snapshot.Timestamp,
            NewerTimestamp = DateTime.UtcNow,
            Changes = changes.OrderBy(c => c.Type).ThenBy(c => c.FilePath).ToList(),
        };

        diff_result.Description = BuildDiffDescription(diff_result);
        return diff_result;
    }

    /// <summary>
    /// Compares two saved snapshots without needing the project files on disk.
    /// Only produces file-level diffs (no property-level diffing since we
    /// only have hashes, not the actual files).
    /// </summary>
    public ProjectDiff CompareSnapshots(string snapshotPathA, string snapshotPathB)
    {
        var snapshotA = LoadSnapshot(snapshotPathA);
        var snapshotB = LoadSnapshot(snapshotPathB);

        // Ensure A is older than B
        if (snapshotA.Timestamp > snapshotB.Timestamp)
            (snapshotA, snapshotB) = (snapshotB, snapshotA);

        var oldFiles = new Dictionary<string, SnapshotFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshotA.Files)
            oldFiles[entry.RelativePath] = entry;

        var newFiles = new Dictionary<string, SnapshotFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshotB.Files)
            newFiles[entry.RelativePath] = entry;

        var changes = new List<FileDiff>();

        foreach (var (relativePath, oldEntry) in oldFiles)
        {
            if (newFiles.TryGetValue(relativePath, out var newEntry))
            {
                if (oldEntry.Hash != newEntry.Hash)
                {
                    changes.Add(new FileDiff
                    {
                        FilePath = relativePath,
                        Type = DiffType.Modified,
                        OldHash = oldEntry.Hash,
                        NewHash = newEntry.Hash,
                        OldSize = oldEntry.FileSize,
                        NewSize = newEntry.FileSize,
                        ActorClass = newEntry.ActorClass ?? oldEntry.ActorClass,
                        ActorName = newEntry.ActorName ?? oldEntry.ActorName,
                    });
                }
            }
            else
            {
                changes.Add(new FileDiff
                {
                    FilePath = relativePath,
                    Type = DiffType.Deleted,
                    OldHash = oldEntry.Hash,
                    OldSize = oldEntry.FileSize,
                    ActorClass = oldEntry.ActorClass,
                    ActorName = oldEntry.ActorName,
                });
            }
        }

        foreach (var (relativePath, newEntry) in newFiles)
        {
            if (!oldFiles.ContainsKey(relativePath))
            {
                changes.Add(new FileDiff
                {
                    FilePath = relativePath,
                    Type = DiffType.Added,
                    NewHash = newEntry.Hash,
                    NewSize = newEntry.FileSize,
                    ActorClass = newEntry.ActorClass,
                    ActorName = newEntry.ActorName,
                });
            }
        }

        var result = new ProjectDiff
        {
            OlderTimestamp = snapshotA.Timestamp,
            NewerTimestamp = snapshotB.Timestamp,
            Changes = changes.OrderBy(c => c.Type).ThenBy(c => c.FilePath).ToList(),
        };

        result.Description = BuildDiffDescription(result);
        return result;
    }

    /// <summary>
    /// Compares two live project directories — e.g., main copy vs dev copy.
    /// Takes a snapshot of each project on the fly and diffs them.
    /// Returns file-level changes: what was added, modified, or deleted in projectB relative to projectA.
    /// </summary>
    public ProjectDiff CompareProjects(string projectPathA, string projectPathB)
    {
        _logger.LogInformation("Comparing projects: {A} vs {B}",
            Path.GetFileName(projectPathA), Path.GetFileName(projectPathB));

        // Take ephemeral snapshots of both (not saved to disk)
        var snapshotA = TakeEphemeralSnapshot(projectPathA, "main");
        var snapshotB = TakeEphemeralSnapshot(projectPathB, "dev");

        // Diff by relative path
        var aFiles = new Dictionary<string, SnapshotFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshotA.Files)
            aFiles[entry.RelativePath] = entry;

        var bFiles = new Dictionary<string, SnapshotFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshotB.Files)
            bFiles[entry.RelativePath] = entry;

        var changes = new List<FileDiff>();

        // Files in A but not B (deleted in dev copy)
        foreach (var (relativePath, aEntry) in aFiles)
        {
            if (!bFiles.ContainsKey(relativePath))
            {
                changes.Add(new FileDiff
                {
                    FilePath = relativePath,
                    Type = DiffType.Deleted,
                    OldHash = aEntry.Hash,
                    OldSize = aEntry.FileSize,
                    ActorClass = aEntry.ActorClass,
                    ActorName = aEntry.ActorName,
                });
            }
        }

        // Files in both — check for modifications
        foreach (var (relativePath, aEntry) in aFiles)
        {
            if (bFiles.TryGetValue(relativePath, out var bEntry))
            {
                if (aEntry.Hash != bEntry.Hash)
                {
                    changes.Add(new FileDiff
                    {
                        FilePath = relativePath,
                        Type = DiffType.Modified,
                        OldHash = aEntry.Hash,
                        NewHash = bEntry.Hash,
                        OldSize = aEntry.FileSize,
                        NewSize = bEntry.FileSize,
                        ActorClass = bEntry.ActorClass ?? aEntry.ActorClass,
                        ActorName = bEntry.ActorName ?? aEntry.ActorName,
                    });
                }
            }
        }

        // Files in B but not A (added in dev copy)
        foreach (var (relativePath, bEntry) in bFiles)
        {
            if (!aFiles.ContainsKey(relativePath))
            {
                // Skip wellversed bridge files — those are expected
                if (relativePath.Contains("Python/wellversed", StringComparison.OrdinalIgnoreCase) ||
                    relativePath.Contains("init_unreal.py", StringComparison.OrdinalIgnoreCase) ||
                    relativePath.Contains(".wellversed", StringComparison.OrdinalIgnoreCase))
                    continue;

                changes.Add(new FileDiff
                {
                    FilePath = relativePath,
                    Type = DiffType.Added,
                    NewHash = bEntry.Hash,
                    NewSize = bEntry.FileSize,
                    ActorClass = bEntry.ActorClass,
                    ActorName = bEntry.ActorName,
                });
            }
        }

        var result = new ProjectDiff
        {
            OlderTimestamp = DateTime.UtcNow,
            NewerTimestamp = DateTime.UtcNow,
            Changes = changes.OrderBy(c => c.Type).ThenBy(c => c.FilePath).ToList(),
        };
        result.Description = $"Main vs Dev: {BuildDiffDescription(result)}";
        return result;
    }

    /// <summary>
    /// Takes a snapshot without saving to disk — for ephemeral comparisons.
    /// </summary>
    private ProjectSnapshot TakeEphemeralSnapshot(string projectPath, string label)
    {
        var config = new WellVersedConfig { ProjectPath = projectPath };
        var contentPath = config.ContentPath;

        var snapshot = new ProjectSnapshot
        {
            Id = label,
            Description = $"Ephemeral: {label}",
            Timestamp = DateTime.UtcNow,
            ProjectPath = projectPath,
        };

        if (!Directory.Exists(contentPath))
            return snapshot;

        var extensions = new[] { ".uasset", ".umap", ".verse", ".uexp" };
        foreach (var file in Directory.EnumerateFiles(contentPath, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (!extensions.Contains(ext)) continue;

            var relativePath = Path.GetRelativePath(contentPath, file);

            try
            {
                var fileInfo = new FileInfo(file);
                using var stream = File.OpenRead(file);
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();

                var entry = new SnapshotFileEntry
                {
                    RelativePath = relativePath,
                    Hash = hash,
                    FileSize = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    Extension = ext,
                };

                if (ext == ".verse")
                {
                    entry.LineCount = File.ReadAllLines(file).Length;
                    snapshot.VerseCount++;
                }
                else if (ext == ".uasset")
                    snapshot.UassetCount++;

                snapshot.Files.Add(entry);
            }
            catch { /* skip unreadable files */ }
        }

        return snapshot;
    }

    /// <summary>
    /// Lists all available snapshots for a project, sorted newest first.
    /// </summary>
    public List<SnapshotSummary> ListSnapshots(string projectPath)
    {
        var snapshotDir = GetSnapshotDirectory(projectPath);
        if (!Directory.Exists(snapshotDir))
            return new List<SnapshotSummary>();

        var summaries = new List<SnapshotSummary>();
        foreach (var file in Directory.EnumerateFiles(snapshotDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var snapshot = JsonSerializer.Deserialize<ProjectSnapshot>(json, JsonOpts);
                if (snapshot == null) continue;

                summaries.Add(new SnapshotSummary
                {
                    Id = snapshot.Id,
                    Description = snapshot.Description,
                    Timestamp = snapshot.Timestamp,
                    ProjectName = snapshot.ProjectName,
                    FileCount = snapshot.Files.Count,
                    UassetCount = snapshot.UassetCount,
                    VerseCount = snapshot.VerseCount,
                    SnapshotPath = file,
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipping unparseable snapshot: {Path}", file);
            }
        }

        return summaries.OrderByDescending(s => s.Timestamp).ToList();
    }

    /// <summary>
    /// Takes an automatic snapshot before a modification.
    /// Uses a standardized description format.
    /// </summary>
    public ProjectSnapshot AutoSnapshot(string projectPath)
    {
        return TakeSnapshot(projectPath, "Auto-snapshot (pre-modification)");
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private SnapshotFileEntry BuildFileEntry(string filePath, string contentPath)
    {
        var fileInfo = new FileInfo(filePath);
        var relativePath = Path.GetRelativePath(contentPath, filePath);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        var entry = new SnapshotFileEntry
        {
            RelativePath = relativePath,
            Hash = ComputeFileHash(filePath),
            FileSize = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
            Extension = extension,
        };

        // Extract device metadata for external actor .uasset files
        if (extension == ".uasset" && relativePath.Contains("__ExternalActors__"))
        {
            try
            {
                ExtractActorMetadata(filePath, entry);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not extract actor metadata from: {Path}", filePath);
            }
        }

        // Line count for .verse files
        if (extension == ".verse")
        {
            try
            {
                entry.LineCount = File.ReadLines(filePath).Count();
            }
            catch { /* non-fatal */ }
        }

        return entry;
    }

    private void ExtractActorMetadata(string filePath, SnapshotFileEntry entry)
    {
        var asset = _fileAccess.OpenForRead(filePath);

        foreach (var export in asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;

            var className = normalExport.GetExportClassType()?.ToString() ?? "";
            var objName = normalExport.ObjectName?.ToString() ?? "";

            // Skip components, metadata, etc. — find the primary actor
            if (className.Contains("Component") || className.Contains("Model")
                || className.Contains("HLODLayer") || className.Contains("MetaData")
                || className.Contains("Level") || className.Contains("Brush"))
                continue;

            entry.ActorClass = className;
            entry.ActorName = objName;
            entry.PropertyCount = normalExport.Data.Count;

            // Try to extract position from properties
            foreach (var prop in normalExport.Data)
            {
                if (prop.Name?.ToString() == "RelativeLocation" && prop is StructPropertyData structProp)
                {
                    var values = structProp.Value?.Select(p => FormatPropertyValue(p)).ToList();
                    if (values != null && values.Count >= 3)
                        entry.Position = $"{values[0]},{values[1]},{values[2]}";
                }
            }

            break; // Only care about the first real actor
        }
    }

    /// <summary>
    /// Diffs the export properties of a modified .uasset file against the snapshot.
    /// We need to re-parse both versions to compare property values.
    /// </summary>
    private List<PropertyDiff>? DiffAssetProperties(
        ProjectSnapshot oldSnapshot, string relativePath, string currentFilePath)
    {
        try
        {
            // Parse the current version
            var currentAsset = _fileAccess.OpenForRead(currentFilePath);
            var currentProps = ExtractAllProperties(currentAsset);

            // For the old version, we only have the hash — we can't re-parse it.
            // But we stored the actor metadata in the snapshot entry.
            // Property-level diff is only possible when we compare to current disk state.
            // The snapshot only stores hashes + metadata, not full file contents.
            // So we compare the current properties against the snapshot's metadata.

            var oldEntry = oldSnapshot.Files.FirstOrDefault(
                f => f.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));

            if (oldEntry == null) return null;

            // If property count changed, report that
            var diffs = new List<PropertyDiff>();

            if (oldEntry.PropertyCount.HasValue)
            {
                var currentPropCount = currentProps.Sum(kvp => kvp.Value.Count);
                if (currentPropCount != oldEntry.PropertyCount.Value)
                {
                    diffs.Add(new PropertyDiff
                    {
                        ActorName = oldEntry.ActorName ?? Path.GetFileNameWithoutExtension(relativePath),
                        PropertyName = "(property count)",
                        OldValue = oldEntry.PropertyCount.Value.ToString(),
                        NewValue = currentPropCount.ToString(),
                    });
                }
            }

            // If position changed, report that
            if (relativePath.Contains("__ExternalActors__"))
            {
                string? currentPosition = null;
                foreach (var (exportName, props) in currentProps)
                {
                    if (props.TryGetValue("RelativeLocation", out var locVal))
                    {
                        currentPosition = locVal;
                        break;
                    }
                }

                if (oldEntry.Position != null && currentPosition != null
                    && oldEntry.Position != currentPosition)
                {
                    diffs.Add(new PropertyDiff
                    {
                        ActorName = oldEntry.ActorName ?? Path.GetFileNameWithoutExtension(relativePath),
                        PropertyName = "RelativeLocation",
                        OldValue = oldEntry.Position,
                        NewValue = currentPosition,
                    });
                }
            }

            return diffs.Count > 0 ? diffs : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not diff properties for: {Path}", relativePath);
            return null;
        }
    }

    /// <summary>
    /// Extracts all properties from all exports in a UAsset as export-name -> property-name -> value.
    /// </summary>
    private Dictionary<string, Dictionary<string, string>> ExtractAllProperties(UAsset asset)
    {
        var result = new Dictionary<string, Dictionary<string, string>>();

        foreach (var export in asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;

            var exportName = normalExport.ObjectName?.ToString() ?? "Unknown";
            var props = new Dictionary<string, string>();

            foreach (var prop in normalExport.Data)
            {
                var name = prop.Name?.ToString() ?? "Unknown";
                var value = FormatPropertyValue(prop);
                props[name] = value;
            }

            result[exportName] = props;
        }

        return result;
    }

    private void ComputeVerseDiff(FileDiff diff, SnapshotFileEntry oldEntry,
        SnapshotFileEntry currentEntry, string contentPath)
    {
        try
        {
            var currentPath = Path.Combine(contentPath, currentEntry.RelativePath);
            var currentLines = File.ReadAllLines(currentPath);
            var oldLineCount = oldEntry.LineCount ?? 0;
            var newLineCount = currentLines.Length;

            if (newLineCount > oldLineCount)
            {
                diff.LinesAdded = newLineCount - oldLineCount;
                diff.LinesRemoved = 0;
            }
            else if (newLineCount < oldLineCount)
            {
                diff.LinesAdded = 0;
                diff.LinesRemoved = oldLineCount - newLineCount;
            }
            else
            {
                // Same count but different hash — content changed
                diff.LinesAdded = 0;
                diff.LinesRemoved = 0;
            }
        }
        catch { /* non-fatal */ }
    }

    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string FormatPropertyValue(PropertyData prop)
    {
        try
        {
            return prop switch
            {
                BoolPropertyData b => b.Value.ToString(),
                IntPropertyData i => i.Value.ToString(),
                FloatPropertyData f => f.Value.ToString(),
                StrPropertyData s => s.Value?.ToString() ?? "null",
                NamePropertyData n => n.Value?.ToString() ?? "null",
                TextPropertyData t => t.Value?.ToString() ?? "null",
                ObjectPropertyData o => o.Value?.ToString() ?? "null",
                EnumPropertyData e => e.Value?.ToString() ?? "null",
                StructPropertyData st => FormatStructValue(st),
                ArrayPropertyData a => $"[{a.Value?.Length ?? 0} elements]",
                _ => prop.ToString() ?? prop.GetType().Name,
            };
        }
        catch
        {
            return $"[{prop.GetType().Name}]";
        }
    }

    private static string FormatStructValue(StructPropertyData structProp)
    {
        if (structProp.Value == null || structProp.Value.Count == 0) return "{}";

        var structType = structProp.StructType?.ToString() ?? "";
        if (structType is "Vector" or "Rotator" && structProp.Value.Count >= 3)
        {
            var values = structProp.Value.Select(p => FormatPropertyValue(p)).ToList();
            return $"({string.Join(", ", values)})";
        }

        var props = structProp.Value.Select(p => $"{p.Name}={FormatPropertyValue(p)}");
        return $"{{{string.Join(", ", props)}}}";
    }

    private static string BuildDiffDescription(ProjectDiff diff)
    {
        var parts = new List<string>();
        if (diff.AddedCount > 0)
            parts.Add($"{diff.AddedCount} file{(diff.AddedCount == 1 ? "" : "s")} added");
        if (diff.ModifiedCount > 0)
            parts.Add($"{diff.ModifiedCount} file{(diff.ModifiedCount == 1 ? "" : "s")} modified");
        if (diff.DeletedCount > 0)
            parts.Add($"{diff.DeletedCount} file{(diff.DeletedCount == 1 ? "" : "s")} deleted");

        if (parts.Count == 0) return "No changes detected";
        return string.Join(", ", parts);
    }

    private ProjectSnapshot LoadSnapshot(string snapshotPath)
    {
        if (!File.Exists(snapshotPath))
            throw new FileNotFoundException($"Snapshot file not found: {snapshotPath}");

        var json = File.ReadAllText(snapshotPath);
        var snapshot = JsonSerializer.Deserialize<ProjectSnapshot>(json, JsonOpts)
            ?? throw new InvalidOperationException($"Could not parse snapshot: {snapshotPath}");

        snapshot.SnapshotPath = snapshotPath;
        return snapshot;
    }

    private static string GetSnapshotDirectory(string projectPath)
    {
        return Path.Combine(projectPath, ".wellversed", "snapshots");
    }
}
