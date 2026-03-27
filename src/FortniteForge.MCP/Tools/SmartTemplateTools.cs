using WellVersed.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace WellVersed.MCP.Tools;

/// <summary>
/// MCP tools for the Smart Template System — 20+ parameterized game mechanic templates.
///
/// Three tools:
///   list_smart_templates    — browse templates with summaries, filter by category
///   get_smart_template      — full template details including verse code and wiring
///   generate_from_template  — generate a complete GenerationPlan from a template
/// </summary>
[McpServerToolType]
public class SmartTemplateTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Lists all available smart templates — deep, parameterized game mechanic templates. " +
        "Each template generates complete device lists, wiring, production-quality Verse code, and UI specs.\n\n" +
        "Categories: combat, economy, objective, progression, meta, utility.\n\n" +
        "Templates include: ClassSelector, GunGame, LastManStanding, TeamElimination, " +
        "CurrencySystem, UpgradeStation, TradingPost, KingOfTheHill, Domination, " +
        "SearchAndDestroy, CaptureTheFlag, Payload, WaveDefense, Parkour, Deathrun, " +
        "VotingSystem, Matchmaking, AchievementSystem, SpectatorMode, ReplaySystem.")]
    public string list_smart_templates(
        SmartTemplateService templateService,
        [Description("Filter by category: combat, economy, objective, progression, meta, utility. " +
            "Leave empty for all templates.")] string? category = null)
    {
        var summaries = templateService.ListTemplates(category);

        return JsonSerializer.Serialize(new
        {
            count = summaries.Count,
            category = category ?? "all",
            templates = summaries.Select(t => new
            {
                t.Id,
                t.Name,
                t.Category,
                t.Description,
                t.Tags,
                t.EstimatedDeviceCount,
                t.Difficulty,
                t.ParameterCount,
                usage = $"get_smart_template(templateId: '{t.Id}') for full details, " +
                    $"or generate_from_template(templateId: '{t.Id}') for a GenerationPlan"
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Gets full details of a smart template including device list, wiring connections, " +
        "complete Verse source code, and widget spec. Use this to inspect what a template " +
        "will generate before committing.\n\n" +
        "Call list_smart_templates first to see available template IDs.")]
    public string get_smart_template(
        SmartTemplateService templateService,
        [Description("Template ID (e.g., 'gun_game', 'king_of_the_hill', 'currency_system'). " +
            "Call list_smart_templates to see all available IDs.")] string templateId)
    {
        var template = templateService.GetTemplate(templateId);
        if (template == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Template '{templateId}' not found. Call list_smart_templates to see available templates."
            }, JsonOpts);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            template = new
            {
                template.Id,
                template.Name,
                template.Category,
                template.Description,
                template.Difficulty,
                template.Tags,
                template.EstimatedDeviceCount,
                parameters = template.Parameters.Select(p => new
                {
                    p.Name,
                    p.Description,
                    p.Type,
                    p.DefaultValue,
                    p.Required
                }),
                deviceCount = template.Devices.Count,
                devices = template.Devices.Select(d => new
                {
                    d.Role,
                    d.DeviceClass,
                    d.Type,
                    offset = new { d.Offset.X, d.Offset.Y, d.Offset.Z }
                }),
                wiringCount = template.Wiring.Count,
                wiring = template.Wiring.Select(w => new
                {
                    w.SourceRole,
                    w.OutputEvent,
                    w.TargetRole,
                    w.InputAction
                }),
                verseCodeLength = template.VerseCode.Length,
                verseCode = template.VerseCode,
                hasWidgetSpec = !string.IsNullOrEmpty(template.WidgetSpecJson),
                widgetSpec = template.WidgetSpecJson
            },
            nextStep = $"Call generate_from_template(templateId: '{templateId}') to create a GenerationPlan, " +
                "optionally with custom parameter overrides."
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Generates a complete GenerationPlan from a smart template with optional parameter overrides. " +
        "The plan includes device placement, wiring, Verse code, and execution steps.\n\n" +
        "Parameters are template-specific — call get_smart_template to see available parameters " +
        "and their defaults before overriding.\n\n" +
        "Example: generate_from_template('gun_game', '{\"tier_count\":\"5\",\"kills_per_tier\":\"2\"}')")]
    public string generate_from_template(
        SmartTemplateService templateService,
        [Description("Template ID (e.g., 'gun_game', 'currency_system'). " +
            "Call list_smart_templates to see all available IDs.")] string templateId,
        [Description("JSON object of parameter overrides. Keys are parameter names, values are strings. " +
            "Example: '{\"tier_count\":\"5\",\"kills_per_tier\":\"2\"}'. " +
            "Call get_smart_template to see available parameters.")] string? parameters = null)
    {
        Dictionary<string, string>? parsedParams = null;
        if (!string.IsNullOrEmpty(parameters))
        {
            try
            {
                parsedParams = JsonSerializer.Deserialize<Dictionary<string, string>>(parameters);
            }
            catch (JsonException ex)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Invalid parameters JSON: {ex.Message}. Expected format: {{\"key\":\"value\"}}"
                }, JsonOpts);
            }
        }

        var plan = templateService.GenerateFromTemplate(templateId, parsedParams);

        if (plan.SystemName == "Error")
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = plan.Description
            }, JsonOpts);
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            plan = new
            {
                plan.SystemName,
                plan.Category,
                plan.Description,
                deviceCount = plan.Devices.Count,
                devices = plan.Devices.Select(d => new
                {
                    d.Role,
                    d.DeviceClass,
                    d.Type,
                    offset = new { d.Offset.X, d.Offset.Y, d.Offset.Z }
                }),
                wiringCount = plan.Wiring.Count,
                wiring = plan.Wiring.Select(w => new
                {
                    w.SourceRole,
                    w.OutputEvent,
                    w.TargetRole,
                    w.InputAction
                }),
                hasVerseCode = !string.IsNullOrEmpty(plan.VerseCode),
                verseCode = plan.VerseCode,
                stepCount = plan.Steps.Count,
                steps = plan.Steps.Select(s => new
                {
                    s.Order,
                    s.Tool,
                    s.Description
                })
            },
            parametersUsed = parsedParams ?? new Dictionary<string, string>(),
            nextSteps = new[]
            {
                "Review the device list and wiring connections",
                "Use clone_actor to place each device in your level",
                "Use preview_wire_devices + apply_modification to connect signals",
                "Use generate_verse_file to write the Verse code to your project",
                "Compile in UEFN and test the system"
            }
        }, JsonOpts);
    }
}
