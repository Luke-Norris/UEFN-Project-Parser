using WellVersed.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace WellVersed.MCP.Tools;

/// <summary>
/// MCP tools for the Verse Intelligence Engine — semantic analysis, anti-pattern
/// detection, refactoring suggestions, device cross-referencing, and scaffolding.
/// </summary>
[McpServerToolType]
public class VerseIntelligenceTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Performs full semantic analysis of a .verse file. Extracts classes, functions, " +
        "@editable properties, event subscriptions, detected gameplay patterns " +
        "(timer_loop, elimination_handler, score_tracking, etc.), anti-patterns, " +
        "and a 1-10 complexity score. Use this to understand what a Verse file does.")]
    public string analyze_verse(
        VerseIntelligence intelligence,
        [Description("Full path to the .verse file to analyze")] string filePath)
    {
        try
        {
            var analysis = intelligence.AnalyzeVerseFile(filePath);

            return JsonSerializer.Serialize(new
            {
                analysis.FilePath,
                analysis.LineCount,
                analysis.ComplexityScore,
                analysis.Classes,
                analysis.Functions,
                analysis.EditableProperties,
                analysis.Variables,
                analysis.EventSubscriptions,
                analysis.DetectedPatterns,
                antiPatternCount = analysis.AntiPatterns.Count,
                antiPatterns = analysis.AntiPatterns.Select(ap => new
                {
                    ap.Id,
                    ap.Line,
                    ap.Severity,
                    ap.Title
                })
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
        "Cross-references @editable device declarations in a Verse file against " +
        "devices actually placed in the project's levels. Flags: devices referenced " +
        "in Verse but missing from the level, and placed devices with no Verse reference. " +
        "Use this after placing devices to ensure your bindings match.")]
    public string find_verse_device_mismatches(
        VerseIntelligence intelligence,
        DeviceService deviceService,
        [Description("Full path to the .verse file containing @editable device references")] string filePath,
        [Description("Optional: path to a specific .umap level file. If omitted, searches all project levels.")] string? levelPath = null)
    {
        try
        {
            var references = intelligence.FindDeviceReferences(filePath);

            // Gather level devices for the mismatch report
            var levelDevices = new List<WellVersed.Core.Models.DeviceInfo>();
            var usedLevelPath = levelPath ?? "(all project levels)";

            if (!string.IsNullOrEmpty(levelPath))
            {
                levelDevices.AddRange(deviceService.ListDevicesInLevel(levelPath));
            }
            else
            {
                foreach (var level in deviceService.FindLevels())
                {
                    try { levelDevices.AddRange(deviceService.ListDevicesInLevel(level)); }
                    catch { /* skip unparseable */ }
                }
            }

            // Find placed devices not referenced in this Verse file
            var referencedTypes = references
                .Select(r => r.VerseType.ToLowerInvariant())
                .ToHashSet();

            var unreferenced = levelDevices
                .Where(d =>
                {
                    var verseType = MapDeviceClassToVerseType(d.DeviceClass);
                    return verseType != null && !referencedTypes.Contains(verseType.ToLowerInvariant());
                })
                .Select(d => $"{d.ActorName} ({d.DeviceType})")
                .Distinct()
                .ToList();

            // Find @editable references with no matching device in level
            var missing = references
                .Where(r => !r.FoundInLevel)
                .Select(r => $"{r.VariableName}: {r.VerseType} (line {r.Line})")
                .ToList();

            return JsonSerializer.Serialize(new
            {
                verseFile = filePath,
                level = usedLevelPath,
                totalVerseReferences = references.Count,
                totalLevelDevices = levelDevices.Count,
                matched = references.Count(r => r.FoundInLevel),
                references = references.Select(r => new
                {
                    r.VariableName,
                    r.VerseType,
                    r.Line,
                    r.FoundInLevel,
                    r.MatchedActorName,
                    r.MatchedDeviceClass
                }),
                missingFromLevel = missing,
                unreferencedInVerse = unreferenced.Take(30)
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
        "Detects common Verse mistakes and anti-patterns in a file. Checks for: " +
        "Sleep() without <suspends>, set on immutable, missing failable guards, " +
        "infinite loops without Sleep, large persistent arrays, missing optional checks, " +
        "orphaned event subscriptions, redundant <decides>, and unused variables.")]
    public string detect_verse_antipatterns(
        VerseIntelligence intelligence,
        [Description("Full path to the .verse file to check")] string filePath)
    {
        try
        {
            var issues = intelligence.DetectAntiPatterns(filePath);

            var grouped = issues
                .GroupBy(i => i.Severity)
                .ToDictionary(g => g.Key, g => g.Count());

            return JsonSerializer.Serialize(new
            {
                filePath,
                totalIssues = issues.Count,
                errors = grouped.GetValueOrDefault("error", 0),
                warnings = grouped.GetValueOrDefault("warning", 0),
                info = grouped.GetValueOrDefault("info", 0),
                issues = issues.Select(i => new
                {
                    i.Id,
                    i.Line,
                    i.Severity,
                    i.Title,
                    i.Description,
                    i.Fix,
                    i.CodeBefore,
                    i.CodeAfter
                })
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
        "Suggests concrete refactoring improvements for a Verse file. " +
        "Detects: duplicated code blocks, hardcoded values that should be @editable, " +
        "missing debug prints, deeply nested if-chains, and array patterns that " +
        "should use concurrent_map. Each suggestion includes before/after code.")]
    public string suggest_verse_refactoring(
        VerseIntelligence intelligence,
        [Description("Full path to the .verse file to analyze")] string filePath)
    {
        try
        {
            var suggestions = intelligence.SuggestRefactoring(filePath);

            return JsonSerializer.Serialize(new
            {
                filePath,
                totalSuggestions = suggestions.Count,
                suggestions = suggestions.Select(s => new
                {
                    s.Category,
                    s.Description,
                    s.LineStart,
                    s.LineEnd,
                    s.CodeBefore,
                    s.CodeAfter,
                    s.Impact
                })
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
        "Scans all devices in a level and generates a complete Verse file with " +
        "@editable declarations for every device, mapped to the correct Verse types. " +
        "Includes OnBegin with event subscriptions and handler stubs. " +
        "This is the 'scan level → instant verse scaffolding' pipeline.")]
    public string generate_device_bindings(
        VerseIntelligence intelligence,
        [Description("Path to the .umap level file to scan for devices")] string levelPath)
    {
        try
        {
            var code = intelligence.GenerateDeviceBindings(levelPath);
            var lineCount = code.Split('\n').Length;

            return JsonSerializer.Serialize(new
            {
                success = true,
                levelPath,
                lineCount,
                sourceCode = code,
                note = "Wire the @editable properties to your devices in UEFN after compiling."
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

    // Helper to map device class → Verse type (mirrors VerseIntelligence logic)
    private static string? MapDeviceClassToVerseType(string deviceClass)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "BP_TimerDevice_C", "timer_device" },
            { "BP_TriggerDevice_C", "trigger_device" },
            { "BP_ButtonDevice_C", "button_device" },
            { "BP_ItemSpawnerDevice_C", "item_spawner_device" },
            { "BP_EliminationManager_C", "elimination_manager_device" },
            { "BP_ScoreManagerDevice_C", "score_manager_device" },
            { "BP_MutatorZone_C", "mutator_zone_device" },
            { "BP_BarrierDevice_C", "barrier_device" },
            { "BP_TeleporterDevice_C", "teleporter_device" },
            { "BP_SpawnPadDevice_C", "player_spawner_device" },
            { "BP_VendingMachineDevice_C", "vending_machine_device" },
            { "BP_CaptureAreaDevice_C", "capture_area_device" },
        };

        map.TryGetValue(deviceClass, out var result);
        return result;
    }
}
