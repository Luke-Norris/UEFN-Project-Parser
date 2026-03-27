using System.Diagnostics;
using WellVersed.Core.Config;
using Microsoft.Extensions.Logging;

namespace WellVersed.Core.Services;

/// <summary>
/// Detects whether UEFN is running and provides project status information.
/// Used to determine safe operation modes (read-only vs staged vs direct).
/// </summary>
public class UefnDetector
{
    private readonly WellVersedConfig _config;
    private readonly ILogger<UefnDetector> _logger;

    private static readonly string[] UefnProcessNames =
    {
        "UnrealEditor-FortniteGame",
        "FortniteGame",
        "UnrealEditor"
    };

    public UefnDetector(WellVersedConfig config, ILogger<UefnDetector> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Returns full status of the UEFN environment.
    /// </summary>
    public UefnStatus GetStatus()
    {
        var status = new UefnStatus();

        // Check if UEFN process is running
        var (isRunning, pid, processName) = CheckUefnProcess();
        status.IsUefnRunning = isRunning;
        status.UefnPid = pid;
        status.UefnProcessName = processName;

        // Check for URC (Unreal Revision Control) directory
        if (!string.IsNullOrEmpty(_config.ProjectPath) && Directory.Exists(_config.ProjectPath))
        {
            var urcPath = Path.Combine(_config.ProjectPath, ".urc");
            status.HasUrc = Directory.Exists(urcPath);

            if (status.HasUrc)
            {
                var journalPath = Path.Combine(urcPath, "urc.sqlite-journal");
                status.UrcActive = File.Exists(journalPath);
            }
        }

        // Determine operation mode
        if (_config.ReadOnly)
        {
            status.Mode = OperationMode.ReadOnly;
            status.ModeReason = "Config: readOnly=true";
        }
        else if (status.IsUefnRunning)
        {
            status.Mode = OperationMode.Staged;
            status.ModeReason = $"UEFN is running (PID {status.UefnPid})";
        }
        else if (status.UrcActive)
        {
            status.Mode = OperationMode.Staged;
            status.ModeReason = "URC has active journal (UEFN may have been open recently)";
        }
        else
        {
            status.Mode = OperationMode.Direct;
            status.ModeReason = "UEFN not detected, direct writes allowed";
        }

        // Count staged files
        var stagingDir = _config.StagingDirectory;
        if (!string.IsNullOrEmpty(stagingDir))
        {
            var fullStagingPath = Path.IsPathRooted(stagingDir)
                ? stagingDir
                : Path.Combine(_config.ProjectPath, stagingDir);

            if (Directory.Exists(fullStagingPath))
            {
                status.StagedFileCount = Directory.EnumerateFiles(fullStagingPath, "*.*", SearchOption.AllDirectories)
                    .Count(f => f.EndsWith(".uasset") || f.EndsWith(".umap") || f.EndsWith(".uexp"));
            }
        }

        // Project info
        status.ProjectName = DetectProjectName();

        return status;
    }

    /// <summary>
    /// Quick check if UEFN is running — use this for fast decisions.
    /// </summary>
    public bool IsUefnRunning()
    {
        return CheckUefnProcess().IsRunning;
    }

    private (bool IsRunning, int? Pid, string? ProcessName) CheckUefnProcess()
    {
        try
        {
            foreach (var name in UefnProcessNames)
            {
                var processes = Process.GetProcessesByName(name);
                if (processes.Length > 0)
                {
                    var proc = processes[0];
                    var pid = proc.Id;
                    var procName = proc.ProcessName;

                    foreach (var p in processes) p.Dispose();

                    return (true, pid, procName);
                }

                foreach (var p in processes) p.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not check UEFN process status");
        }

        return (false, null, null);
    }

    private string DetectProjectName()
    {
        if (string.IsNullOrEmpty(_config.ProjectPath) || !Directory.Exists(_config.ProjectPath))
            return "Unknown";

        // Check for .uefnproject file
        var uefnProjects = Directory.GetFiles(_config.ProjectPath, "*.uefnproject");
        if (uefnProjects.Length > 0)
            return Path.GetFileNameWithoutExtension(uefnProjects[0]);

        // Check for .uproject file
        var uprojects = Directory.GetFiles(_config.ProjectPath, "*.uproject");
        if (uprojects.Length > 0)
            return Path.GetFileNameWithoutExtension(uprojects[0]);

        // Fall back to directory name
        return Path.GetFileName(_config.ProjectPath);
    }
}

public class UefnStatus
{
    public bool IsUefnRunning { get; set; }
    public int? UefnPid { get; set; }
    public string? UefnProcessName { get; set; }
    public bool HasUrc { get; set; }
    public bool UrcActive { get; set; }
    public OperationMode Mode { get; set; }
    public string ModeReason { get; set; } = "";
    public int StagedFileCount { get; set; }
    public string ProjectName { get; set; } = "";
}

public enum OperationMode
{
    /// <summary>All writes blocked.</summary>
    ReadOnly,
    /// <summary>Writes go to staging directory, not source files.</summary>
    Staged,
    /// <summary>Direct writes with backup (UEFN not running).</summary>
    Direct
}
