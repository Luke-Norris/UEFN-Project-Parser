namespace FortniteForge.Core.Models;

/// <summary>
/// Result of an audit operation.
/// </summary>
public class AuditResult
{
    /// <summary>
    /// What was audited (asset path, device name, level, etc.).
    /// </summary>
    public string Target { get; set; } = "";

    /// <summary>
    /// Overall audit status.
    /// </summary>
    public AuditStatus Status { get; set; } = AuditStatus.Pass;

    /// <summary>
    /// Individual findings from the audit.
    /// </summary>
    public List<AuditFinding> Findings { get; set; } = new();

    /// <summary>
    /// Summary of the audit (human readable).
    /// </summary>
    public string Summary => Findings.Count == 0
        ? "No issues found."
        : $"{Findings.Count(f => f.Severity == AuditSeverity.Error)} errors, " +
          $"{Findings.Count(f => f.Severity == AuditSeverity.Warning)} warnings, " +
          $"{Findings.Count(f => f.Severity == AuditSeverity.Info)} info items.";
}

public class AuditFinding
{
    /// <summary>
    /// Severity of this finding.
    /// </summary>
    public AuditSeverity Severity { get; set; }

    /// <summary>
    /// Category of the finding (e.g., "Wiring", "Property", "Reference").
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// What the issue is.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// The specific device/asset/property involved.
    /// </summary>
    public string Location { get; set; } = "";

    /// <summary>
    /// Suggested fix, if any.
    /// </summary>
    public string? Suggestion { get; set; }
}

public enum AuditStatus
{
    Pass,
    Warning,
    Fail
}

public enum AuditSeverity
{
    Info,
    Warning,
    Error
}
