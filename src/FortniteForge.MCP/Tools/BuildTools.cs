using FortniteForge.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace FortniteForge.MCP.Tools;

/// <summary>
/// MCP tools for UEFN build operations — triggering builds,
/// reading logs, and extracting errors.
/// </summary>
[McpServerToolType]
public class BuildTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Triggers a UEFN build and returns the result. " +
        "Requires build.buildCommand to be configured in forge.config.json.")]
    public async Task<string> trigger_build(BuildService buildService)
    {
        var result = await buildService.TriggerBuildAsync();
        return JsonSerializer.Serialize(new
        {
            result.Success,
            status = result.Status.ToString(),
            result.DurationSeconds,
            summary = result.Summary,
            errorCount = result.Errors.Count,
            warningCount = result.Warnings.Count,
            verseErrorCount = result.VerseErrors.Count,
            result.LogFilePath,
            errors = result.Errors.Select(e => new { e.Message, e.File, e.Line, e.Code }),
            verseErrors = result.VerseErrors.Select(e => new { e.Message, e.FilePath, e.Line, e.ErrorCode }),
            warnings = result.Warnings.Take(10).Select(w => new { w.Message, w.File, w.Line }),
            note = result.Warnings.Count > 10 ? $"Showing 10 of {result.Warnings.Count} warnings." : null
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Reads and parses the most recent build log. " +
        "Use this to check build results without triggering a new build.")]
    public string get_build_log(BuildService buildService)
    {
        var result = buildService.ReadLatestBuildLog();
        return JsonSerializer.Serialize(new
        {
            result.Success,
            status = result.Status.ToString(),
            result.LogFilePath,
            summary = result.Summary,
            errorCount = result.Errors.Count,
            warningCount = result.Warnings.Count,
            verseErrorCount = result.VerseErrors.Count,
            errors = result.Errors.Select(e => new { e.Message, e.File, e.Line, e.Code }),
            verseErrors = result.VerseErrors.Select(e => new { e.Message, e.FilePath, e.Line, e.ErrorCode }),
            warnings = result.Warnings.Take(10).Select(w => new { w.Message })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Gets a concise build summary optimized for quick reading. " +
        "Shows status, error count, and key error messages.")]
    public string build_summary(BuildService buildService)
    {
        return buildService.GetBuildSummary();
    }

    [McpServerTool, Description(
        "Gets Verse compilation errors from the build log. " +
        "Returns structured error data including file paths, line numbers, and messages. " +
        "Useful for forwarding to a Verse-focused debugging session.")]
    public string get_verse_errors(
        BuildService buildService,
        [Description("Optional path to a specific log file. If not provided, reads the latest log.")] string? logFilePath = null)
    {
        var errors = buildService.GetVerseErrors(logFilePath);

        if (errors.Count == 0)
            return "No Verse compilation errors found.";

        return JsonSerializer.Serialize(new
        {
            count = errors.Count,
            errors = errors.Select(e => new
            {
                e.Message,
                e.FilePath,
                e.Line,
                e.Column,
                e.ErrorCode,
                e.Snippet
            })
        }, JsonOpts);
    }
}
