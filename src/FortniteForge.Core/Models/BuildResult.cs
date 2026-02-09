namespace FortniteForge.Core.Models;

/// <summary>
/// Result of a UEFN build operation.
/// </summary>
public class BuildResult
{
    public bool Success { get; set; }

    /// <summary>
    /// Overall build status.
    /// </summary>
    public BuildStatus Status { get; set; }

    /// <summary>
    /// Build duration in seconds.
    /// </summary>
    public double DurationSeconds { get; set; }

    /// <summary>
    /// Build errors.
    /// </summary>
    public List<BuildMessage> Errors { get; set; } = new();

    /// <summary>
    /// Build warnings.
    /// </summary>
    public List<BuildMessage> Warnings { get; set; } = new();

    /// <summary>
    /// Raw build output (last N lines).
    /// </summary>
    public string RawOutput { get; set; } = "";

    /// <summary>
    /// Path to the full build log file.
    /// </summary>
    public string? LogFilePath { get; set; }

    /// <summary>
    /// Verse compilation errors (subset of Errors, extracted for convenience).
    /// </summary>
    public List<VerseError> VerseErrors { get; set; } = new();

    public string Summary => Status switch
    {
        BuildStatus.Success => "Build succeeded.",
        BuildStatus.SuccessWithWarnings => $"Build succeeded with {Warnings.Count} warning(s).",
        BuildStatus.Failed => $"Build failed with {Errors.Count} error(s).",
        BuildStatus.Timeout => "Build timed out.",
        BuildStatus.NotStarted => "Build was not started.",
        _ => "Unknown build status."
    };
}

public class BuildMessage
{
    public string Message { get; set; } = "";
    public string? File { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public string? Code { get; set; }
}

public class VerseError
{
    public string Message { get; set; } = "";
    public string? FilePath { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public string? ErrorCode { get; set; }
    public string? Snippet { get; set; }
}

public enum BuildStatus
{
    NotStarted,
    InProgress,
    Success,
    SuccessWithWarnings,
    Failed,
    Timeout
}
