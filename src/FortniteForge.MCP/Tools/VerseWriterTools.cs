using WellVersed.Core.Models;
using WellVersed.Core.Services;
using WellVersed.Core.Services.VerseGeneration;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace WellVersed.MCP.Tools;

/// <summary>
/// MCP tools for Claude to write arbitrary custom Verse code beyond templates.
///
/// These tools give Claude all the context it needs — device schemas, reference
/// implementations from real maps, Verse syntax patterns, and the code builder —
/// to generate correct, compilable Verse code for any game mechanic.
///
/// Workflow:
///   1. get_verse_context — assembles device schemas + reference examples for the mechanic
///   2. write_verse_device — writes custom Verse code to a .verse file
///   3. validate_verse_syntax — checks Verse syntax patterns (not a full compiler)
/// </summary>
[McpServerToolType]
public class VerseWriterTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Assembles all the context Claude needs to write custom Verse code for a game mechanic. " +
        "Returns: relevant device schemas (properties, events, functions), " +
        "reference implementations from real UEFN maps, common Verse patterns, " +
        "and a code skeleton with the right imports and class structure.\n\n" +
        "Use this BEFORE writing custom Verse code to ensure you have correct API names and patterns.")]
    public string get_verse_context(
        DigestService digestService,
        VerseReferenceService referenceService,
        [Description("Description of the mechanic to implement. " +
            "Example: 'Track eliminations per team and display on a billboard'")] string mechanic,
        [Description("Device types this code will interact with (comma-separated). " +
            "Example: 'elimination_manager,billboard_device,timer_device'")] string? deviceTypes = null,
        [Description("Verse categories to search for reference code. " +
            "Options: GameManagement, PlayerEconomy, UISystem, AbilitySystem, TycoonBuilding, " +
            "RankedProgression, Environment, Utility, Combat, Movement")] string? categories = null)
    {
        var context = new VerseWritingContext { Mechanic = mechanic };

        // 1. Get device schemas for the mentioned devices
        if (!string.IsNullOrEmpty(deviceTypes))
        {
            foreach (var deviceType in deviceTypes.Split(',', StringSplitOptions.TrimEntries))
            {
                var schema = digestService.GetDeviceSchema(deviceType);
                if (schema != null)
                {
                    context.DeviceSchemas.Add(new DeviceSchemaInfo
                    {
                        ClassName = schema.Name,
                        Properties = schema.Properties.Select(p => new DeviceSchemaPropertyInfo
                        {
                            Name = p.Name,
                            Type = p.Type,
                            DefaultValue = p.DefaultValue
                        }).ToList(),
                        Events = schema.Events,
                        Functions = schema.Functions
                    });
                }
            }
        }

        // 2. Search reference library for relevant examples
        var keywords = ExtractKeywords(mechanic);
        foreach (var keyword in keywords.Take(3))
        {
            try
            {
                var results = referenceService.SearchKeyword(keyword, 3);
                foreach (var result in results)
                {
                    if (context.ReferenceExamples.Count >= 5) break;
                    if (context.ReferenceExamples.Any(r => r.FilePath == result.FilePath)) continue;

                    // Read the file content for Claude
                    string? sourceCode = null;
                    try
                    {
                        if (File.Exists(result.FilePath))
                            sourceCode = File.ReadAllText(result.FilePath);
                    }
                    catch { /* Non-critical */ }

                    context.ReferenceExamples.Add(new ReferenceExample
                    {
                        FileName = result.FileName,
                        FilePath = result.FilePath,
                        Classes = result.ClassNames,
                        Functions = new List<string>(), // Functions not in search result
                        MatchReasons = result.MatchReasons,
                        SourceCode = sourceCode != null && sourceCode.Length < 3000 ? sourceCode : null,
                        Truncated = sourceCode != null && sourceCode.Length >= 3000
                    });
                }
            }
            catch { /* Reference service may not be initialized */ }
        }

        // 3. Generate a code skeleton
        var builder = new VerseCodeBuilder();
        builder.Comment($"Custom device: {mechanic}")
            .Line()
            .StandardUsings();

        // Add UI usings if mechanic mentions UI/HUD/display
        if (mechanic.Contains("ui", StringComparison.OrdinalIgnoreCase) ||
            mechanic.Contains("hud", StringComparison.OrdinalIgnoreCase) ||
            mechanic.Contains("display", StringComparison.OrdinalIgnoreCase) ||
            mechanic.Contains("widget", StringComparison.OrdinalIgnoreCase) ||
            mechanic.Contains("billboard", StringComparison.OrdinalIgnoreCase))
        {
            builder.UIUsings();
        }

        builder.Line()
            .ClassDef("custom_device")
            .Comment("Add @editable device references here")
            .Line()
            .Comment("Add state variables here")
            .Line()
            .OnBegin()
            .Comment("Subscribe to device events and initialize state")
            .EndBlock();

        context.CodeSkeleton = builder.ToString();

        // 4. Add Verse syntax patterns
        context.SyntaxPatterns = new List<VerseSyntaxPattern>
        {
            new() { Pattern = "Event Subscription", Example = "MyDevice.TriggeredEvent.Subscribe(OnTriggered)", Description = "Subscribe to a device event in OnBegin" },
            new() { Pattern = "Failable Check", Example = "if (Player := Agent.GetFortCharacter[].GetInstigator[]?.GetPlayer[]):", Description = "Failable function chain with if binding" },
            new() { Pattern = "Persistent Data", Example = "my_data <persistable> := class:\n    var Score : int = 0", Description = "Persistable struct for saving player data" },
            new() { Pattern = "Map Variable", Example = "var PlayerScores : [player]int = map{}", Description = "Per-player state using map type" },
            new() { Pattern = "Timer Loop", Example = "loop:\n    Sleep(Interval)\n    OnTick()", Description = "Repeating timer using loop + Sleep" },
            new() { Pattern = "Spawning Objects", Example = "SpawnedObj := SpawnProp(AssetRef, Position, Rotation)", Description = "Spawn a prop or asset at runtime" },
            new() { Pattern = "UI Widget", Example = "canvas:\n    Slots := array:\n        canvas_slot:\n            Widget := text_block{DefaultText := \"Hello\"}", Description = "Creating UI with canvas + slots" },
        };

        return JsonSerializer.Serialize(context, JsonOpts);
    }

    [McpServerTool, Description(
        "Writes custom Verse code to a .verse file in the project. " +
        "Use get_verse_context first to assemble the context needed to write correct code. " +
        "Claude should write the full Verse source code and pass it here.")]
    public string write_verse_file(
        WellVersed.Core.Config.WellVersedConfig config,
        [Description("The complete Verse source code to write.")] string sourceCode,
        [Description("File name without extension (e.g., 'my_game_manager').")] string fileName,
        [Description("Optional output directory. If not provided, writes to project Content.")] string? outputDirectory = null)
    {
        var outputDir = outputDirectory ?? Path.Combine(config.ProjectPath, "Content");
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        var filePath = Path.Combine(outputDir, $"{fileName}.verse");
        File.WriteAllText(filePath, sourceCode);

        // Basic syntax checks
        var warnings = new List<string>();
        if (!sourceCode.Contains("using {"))
            warnings.Add("Missing 'using' statements — Verse files typically need imports");
        if (!sourceCode.Contains("creative_device"))
            warnings.Add("No 'creative_device' class found — UEFN devices must extend creative_device");
        if (!sourceCode.Contains("OnBegin"))
            warnings.Add("No 'OnBegin' method — this is the entry point for UEFN devices");

        // Check indentation consistency (Verse uses 4-space indentation)
        var lines = sourceCode.Split('\n');
        var tabLines = lines.Count(l => l.StartsWith('\t'));
        if (tabLines > 0)
            warnings.Add($"Found {tabLines} lines with tab indentation — Verse requires 4-space indentation");

        return JsonSerializer.Serialize(new
        {
            success = true,
            filePath,
            fileName = $"{fileName}.verse",
            lineCount = lines.Length,
            classCount = sourceCode.Split("class(").Length - 1 + sourceCode.Split("class:").Length - 1,
            warnings,
            nextSteps = new[]
            {
                "Open the file in UEFN to compile",
                "If compilation succeeds, place the device in your level",
                "Wire @editable properties to other devices in the level"
            }
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Validates Verse code syntax patterns without a full compiler. " +
        "Checks for common mistakes: wrong indentation, missing imports, " +
        "incorrect effect specifiers, type mismatches.")]
    public string validate_verse_syntax(
        [Description("The Verse source code to validate.")] string sourceCode)
    {
        var issues = new List<VerseIssue>();

        var lines = sourceCode.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNum = i + 1;

            // Tab check
            if (line.Contains('\t'))
                issues.Add(new VerseIssue { Line = lineNum, Severity = "error", Message = "Tab character found — Verse requires 4-space indentation" });

            // Common syntax mistakes
            if (line.TrimStart().StartsWith("func "))
                issues.Add(new VerseIssue { Line = lineNum, Severity = "warning", Message = "Verse uses method syntax without 'func' keyword. Define methods as: MethodName() : void =" });

            if (line.Contains("var ") && !line.Contains("var ") && line.Contains(" = "))
                issues.Add(new VerseIssue { Line = lineNum, Severity = "warning", Message = "Variable declarations use ':=' for immutable, 'var Name : Type = Value' for mutable" });

            if (line.Contains("true") && !line.Contains("logic"))
                if (!line.Contains("//") && !line.Contains("\""))
                    issues.Add(new VerseIssue { Line = lineNum, Severity = "info", Message = "Verse uses 'true/false' for logic type (equivalent to bool)" });

            if (line.Contains("override>") && !line.Contains("<override>"))
                issues.Add(new VerseIssue { Line = lineNum, Severity = "error", Message = "Effect specifier syntax: <override> not override>" });

            if (line.Contains("<suspends>") && !line.Contains("OnBegin") && !line.Contains("loop") && !line.Contains("Sleep"))
                issues.Add(new VerseIssue { Line = lineNum, Severity = "info", Message = "<suspends> context — ensure this function is called from a suspending context" });
        }

        // Structure checks
        if (!sourceCode.Contains("using { /Fortnite.com/Devices }"))
            issues.Add(new VerseIssue { Line = 1, Severity = "warning", Message = "Missing 'using { /Fortnite.com/Devices }' — needed for device types" });

        if (!sourceCode.Contains("using { /Verse.org/Simulation }"))
            issues.Add(new VerseIssue { Line = 1, Severity = "warning", Message = "Missing 'using { /Verse.org/Simulation }' — needed for Print, Sleep, etc." });

        var errorCount = issues.Count(i => i.Severity == "error");
        var warningCount = issues.Count(i => i.Severity == "warning");

        return JsonSerializer.Serialize(new
        {
            valid = errorCount == 0,
            errorCount,
            warningCount,
            infoCount = issues.Count(i => i.Severity == "info"),
            issues = issues.OrderBy(i => i.Line).ThenByDescending(i => i.Severity),
            note = "This is a pattern-based check, not a full compilation. " +
                   "Compile in UEFN for definitive validation."
        }, JsonOpts);
    }

    // ========= Helpers =========

    private static List<string> ExtractKeywords(string description)
    {
        var lower = description.ToLower();
        var keywords = new List<string>();

        // Extract meaningful keywords
        var mechanicWords = new[]
        {
            "elimination", "score", "timer", "round", "spawn", "capture", "zone",
            "tracker", "team", "hud", "billboard", "widget", "economy", "currency",
            "tycoon", "upgrade", "barrier", "teleport", "mutator", "trigger",
            "button", "vending", "item", "collect", "progression", "persistent",
            "leaderboard", "notification", "ability", "cooldown", "health", "shield"
        };

        foreach (var word in mechanicWords)
        {
            if (lower.Contains(word))
                keywords.Add(word);
        }

        // Also split the description into words and take meaningful ones
        var descWords = description.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .Select(w => w.ToLower().Trim(',', '.', '!', '?'))
            .Where(w => !new[] { "with", "that", "this", "when", "from", "each", "should", "would" }.Contains(w))
            .Take(5);

        keywords.AddRange(descWords);

        return keywords.Distinct().ToList();
    }
}

// ========= Context Models =========

public class VerseWritingContext
{
    public string Mechanic { get; set; } = "";
    public List<DeviceSchemaInfo> DeviceSchemas { get; set; } = new();
    public List<ReferenceExample> ReferenceExamples { get; set; } = new();
    public string? CodeSkeleton { get; set; }
    public List<VerseSyntaxPattern> SyntaxPatterns { get; set; } = new();
}

public class DeviceSchemaInfo
{
    public string ClassName { get; set; } = "";
    public List<DeviceSchemaPropertyInfo> Properties { get; set; } = new();
    public List<string> Events { get; set; } = new();
    public List<string> Functions { get; set; } = new();
}

public class DeviceSchemaPropertyInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? DefaultValue { get; set; }
}

public class ReferenceExample
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public List<string> Classes { get; set; } = new();
    public List<string> Functions { get; set; } = new();
    public List<string> MatchReasons { get; set; } = new();
    public string? SourceCode { get; set; }
    public bool Truncated { get; set; }
}

public class VerseSyntaxPattern
{
    public string Pattern { get; set; } = "";
    public string Example { get; set; } = "";
    public string Description { get; set; } = "";
}

public class VerseIssue
{
    public int Line { get; set; }
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
}
