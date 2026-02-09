using FortniteForge.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace FortniteForge.MCP.Tools;

/// <summary>
/// MCP tools for auditing UEFN project configurations.
/// Helps Claude identify issues with device setups, wiring, and references.
/// </summary>
[McpServerToolType]
public class AuditTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Audits an entire level for common issues: unwired devices, default configs, " +
        "broken references, unconfigured spawners, and more. " +
        "Start here when debugging level behavior.")]
    public string audit_level(
        AuditService auditService,
        [Description("Path to the .umap level file to audit")] string levelPath)
    {
        var result = auditService.AuditLevel(levelPath);
        return JsonSerializer.Serialize(new
        {
            target = result.Target,
            status = result.Status.ToString(),
            summary = result.Summary,
            findings = result.Findings.Select(f => new
            {
                severity = f.Severity.ToString(),
                f.Category,
                f.Message,
                f.Location,
                f.Suggestion
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Audits a specific device for configuration issues. " +
        "Checks properties against the device schema and validates wiring.")]
    public string audit_device(
        AuditService auditService,
        [Description("Path to the .umap level file")] string levelPath,
        [Description("Actor name of the device to audit")] string deviceName)
    {
        var result = auditService.AuditDevice(levelPath, deviceName);
        return JsonSerializer.Serialize(new
        {
            target = result.Target,
            status = result.Status.ToString(),
            summary = result.Summary,
            findings = result.Findings.Select(f => new
            {
                severity = f.Severity.ToString(),
                f.Category,
                f.Message,
                f.Location,
                f.Suggestion
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Runs a full audit across the entire project — all levels, all devices. " +
        "Can be slow for large projects. Use audit_level for targeted checks.")]
    public string audit_project(AuditService auditService)
    {
        var result = auditService.AuditProject();
        return JsonSerializer.Serialize(new
        {
            target = result.Target,
            status = result.Status.ToString(),
            summary = result.Summary,
            errorCount = result.Findings.Count(f => f.Severity == Core.Models.AuditSeverity.Error),
            warningCount = result.Findings.Count(f => f.Severity == Core.Models.AuditSeverity.Warning),
            infoCount = result.Findings.Count(f => f.Severity == Core.Models.AuditSeverity.Info),
            findings = result.Findings.Select(f => new
            {
                severity = f.Severity.ToString(),
                f.Category,
                f.Message,
                f.Location,
                f.Suggestion
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Validates a property value against the device schema before modifying it. " +
        "Checks if the property exists and is editable on the given device type.")]
    public string validate_property(
        DigestService digestService,
        [Description("Device class name (e.g., 'trigger_device')")] string deviceType,
        [Description("Property name to validate")] string propertyName)
    {
        var result = digestService.ValidateProperty(deviceType, propertyName);
        return JsonSerializer.Serialize(result, JsonOpts);
    }
}
