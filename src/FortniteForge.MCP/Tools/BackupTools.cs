using FortniteForge.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace FortniteForge.MCP.Tools;

/// <summary>
/// MCP tools for managing asset backups.
/// Backups are created automatically before modifications,
/// but these tools allow manual backup management.
/// </summary>
[McpServerToolType]
public class BackupTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Creates a manual backup of an asset file. " +
        "Backups are also created automatically before any modification.")]
    public string backup_asset(
        BackupService backupService,
        [Description("Path to the asset file to backup")] string assetPath)
    {
        try
        {
            var backupPath = backupService.CreateBackup(assetPath);
            return JsonSerializer.Serialize(new
            {
                success = true,
                originalFile = assetPath,
                backupPath,
                message = "Backup created successfully."
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            }, JsonOpts);
        }
    }

    [McpServerTool, Description(
        "Restores an asset from a backup file. " +
        "Creates a safety backup of the current file before restoring.")]
    public string restore_asset(
        BackupService backupService,
        [Description("Path to the backup file to restore from")] string backupPath,
        [Description("Path to the target file to overwrite")] string targetPath)
    {
        try
        {
            backupService.RestoreBackup(backupPath, targetPath);
            return JsonSerializer.Serialize(new
            {
                success = true,
                restored = targetPath,
                fromBackup = backupPath,
                message = "Asset restored from backup. A safety backup of the previous version was created."
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            }, JsonOpts);
        }
    }

    [McpServerTool, Description(
        "Lists all available backups. Optionally filter by asset path.")]
    public string list_backups(
        BackupService backupService,
        [Description("Optional asset path to filter backups for a specific file")] string? assetPath = null)
    {
        var backups = backupService.ListBackups(assetPath);

        if (backups.Count == 0)
            return assetPath != null
                ? $"No backups found for '{assetPath}'."
                : "No backups found.";

        return JsonSerializer.Serialize(new
        {
            count = backups.Count,
            backups = backups.Select(b => new
            {
                b.BackupPath,
                b.OriginalFileName,
                b.Timestamp,
                size = $"{b.FileSize / 1024.0:F1} KB"
            })
        }, JsonOpts);
    }
}
