using WellVersed.Core.Config;
using WellVersed.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace WellVersed.MCP.Tools;

/// <summary>
/// MCP tools for project diffing — snapshots and change tracking.
/// UEFN has no version control. These tools let Claude see exactly what
/// changed between saves.
/// </summary>
[McpServerToolType]
public class DiffTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Static field to store the path of the last auto-snapshot for show_project_changes.
    /// </summary>
    private static string? _lastAutoSnapshotPath;

    [McpServerTool, Description(
        "Takes a snapshot of the current project state. " +
        "Records every file's hash, size, and metadata. " +
        "For device files, also captures actor name, class, position, and property count. " +
        "Use this before making changes so you can diff later.")]
    public string take_project_snapshot(
        ProjectDiffService diffService,
        WellVersedConfig config,
        [Description("Optional description for this snapshot (e.g. 'Before terrain rework')")] string? description = null)
    {
        if (string.IsNullOrEmpty(config.ProjectPath))
            return JsonSerializer.Serialize(new { error = "No project configured" }, JsonOpts);

        var snapshot = diffService.TakeSnapshot(config.ProjectPath, description);

        return JsonSerializer.Serialize(new
        {
            id = snapshot.Id,
            description = snapshot.Description,
            timestamp = snapshot.Timestamp,
            fileCount = snapshot.Files.Count,
            uassetCount = snapshot.UassetCount,
            verseCount = snapshot.VerseCount,
            snapshotPath = snapshot.SnapshotPath,
            message = $"Snapshot saved with {snapshot.Files.Count} files tracked"
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Lists all available project snapshots, sorted newest first. " +
        "Shows snapshot ID, description, timestamp, and file counts.")]
    public string list_snapshots(
        ProjectDiffService diffService,
        WellVersedConfig config)
    {
        if (string.IsNullOrEmpty(config.ProjectPath))
            return JsonSerializer.Serialize(new { error = "No project configured" }, JsonOpts);

        var snapshots = diffService.ListSnapshots(config.ProjectPath);

        return JsonSerializer.Serialize(new
        {
            count = snapshots.Count,
            snapshots = snapshots.Select(s => new
            {
                s.Id,
                s.Description,
                s.Timestamp,
                s.ProjectName,
                s.FileCount,
                s.UassetCount,
                s.VerseCount,
                s.SnapshotPath
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Compares the current project state against a saved snapshot. " +
        "Shows which files were added, modified, or deleted. " +
        "For modified .uasset files, shows property-level changes. " +
        "For modified .verse files, shows line count changes.")]
    public string compare_to_snapshot(
        ProjectDiffService diffService,
        WellVersedConfig config,
        [Description("Snapshot ID or full path to the snapshot JSON file")] string snapshotId)
    {
        if (string.IsNullOrEmpty(config.ProjectPath))
            return JsonSerializer.Serialize(new { error = "No project configured" }, JsonOpts);

        // Resolve snapshot ID to path
        var snapshotPath = ResolveSnapshotPath(diffService, config.ProjectPath, snapshotId);
        if (snapshotPath == null)
            return JsonSerializer.Serialize(new { error = $"Snapshot not found: {snapshotId}" }, JsonOpts);

        var diff = diffService.CompareToSnapshot(config.ProjectPath, snapshotPath);

        return JsonSerializer.Serialize(new
        {
            diff.Description,
            olderTimestamp = diff.OlderTimestamp,
            newerTimestamp = diff.NewerTimestamp,
            summary = new
            {
                added = diff.AddedCount,
                modified = diff.ModifiedCount,
                deleted = diff.DeletedCount,
                totalPropertyChanges = diff.TotalPropertyChanges,
            },
            changes = diff.Changes.Select(c => new
            {
                filePath = c.FilePath,
                type = c.Type.ToString(),
                c.ActorClass,
                c.ActorName,
                oldSize = c.OldSize,
                newSize = c.NewSize,
                linesAdded = c.LinesAdded,
                linesRemoved = c.LinesRemoved,
                propertyChanges = c.PropertyChanges?.Select(p => new
                {
                    p.ActorName,
                    p.PropertyName,
                    p.OldValue,
                    p.NewValue
                })
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Quick change tracker: on first call, auto-snapshots the project. " +
        "On subsequent calls, shows what changed since the auto-snapshot. " +
        "Perfect for monitoring a UEFN editing session.")]
    public string show_project_changes(
        ProjectDiffService diffService,
        WellVersedConfig config)
    {
        if (string.IsNullOrEmpty(config.ProjectPath))
            return JsonSerializer.Serialize(new { error = "No project configured" }, JsonOpts);

        if (_lastAutoSnapshotPath == null || !File.Exists(_lastAutoSnapshotPath))
        {
            // First call — take a baseline snapshot
            var snapshot = diffService.TakeSnapshot(config.ProjectPath, "Auto-baseline for change tracking");
            _lastAutoSnapshotPath = snapshot.SnapshotPath;

            return JsonSerializer.Serialize(new
            {
                status = "baseline_created",
                snapshotId = snapshot.Id,
                fileCount = snapshot.Files.Count,
                message = $"Baseline snapshot created with {snapshot.Files.Count} files. " +
                          "Call this tool again after making changes in UEFN to see what changed."
            }, JsonOpts);
        }

        // Subsequent calls — diff against baseline
        var diff = diffService.CompareToSnapshot(config.ProjectPath, _lastAutoSnapshotPath);

        if (diff.Changes.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                status = "no_changes",
                message = "No changes detected since baseline snapshot."
            }, JsonOpts);
        }

        return JsonSerializer.Serialize(new
        {
            status = "changes_detected",
            diff.Description,
            summary = new
            {
                added = diff.AddedCount,
                modified = diff.ModifiedCount,
                deleted = diff.DeletedCount,
                totalPropertyChanges = diff.TotalPropertyChanges,
            },
            changes = diff.Changes.Select(c => new
            {
                filePath = c.FilePath,
                type = c.Type.ToString(),
                c.ActorClass,
                c.ActorName,
                linesAdded = c.LinesAdded,
                linesRemoved = c.LinesRemoved,
                propertyChanges = c.PropertyChanges?.Select(p => new
                {
                    p.ActorName,
                    p.PropertyName,
                    p.OldValue,
                    p.NewValue
                })
            })
        }, JsonOpts);
    }

    /// <summary>
    /// Resolves a snapshot ID or path to a full file path.
    /// </summary>
    private static string? ResolveSnapshotPath(
        ProjectDiffService diffService, string projectPath, string snapshotId)
    {
        // If it's already a full path
        if (File.Exists(snapshotId))
            return snapshotId;

        // Search by ID
        var snapshots = diffService.ListSnapshots(projectPath);
        var match = snapshots.FirstOrDefault(s =>
            s.Id.Equals(snapshotId, StringComparison.OrdinalIgnoreCase));

        return match?.SnapshotPath;
    }
}
