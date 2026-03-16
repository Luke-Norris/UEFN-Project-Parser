using FortniteForge.Core.Models;
using FortniteForge.Core.Services.VerseGeneration;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace FortniteForge.MCP.Tools;

/// <summary>
/// MCP tools for generating Verse source code — UI widgets and game logic devices.
/// Claude can use these to scaffold complete, compilable Verse files for UEFN.
/// </summary>
[McpServerToolType]
public class VerseGenTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Lists all available Verse code templates with their parameters. " +
        "Includes UI widgets (HUD, scoreboard, menus, notifications, progress bars) " +
        "and game logic devices (game manager, timer, teams, eliminations, collectibles, zones).")]
    public string list_verse_templates()
    {
        var uiTemplates = VerseUIGenerator.GetUITemplates();
        var deviceTemplates = VerseDeviceGenerator.GetDeviceTemplates();

        return JsonSerializer.Serialize(new
        {
            ui = uiTemplates.Select(t => new
            {
                t.Name,
                t.Description,
                type = t.Type.ToString(),
                t.Category,
                parameters = t.Parameters.Select(p => new
                {
                    p.Name,
                    p.Description,
                    p.DefaultValue,
                    p.Required
                })
            }),
            devices = deviceTemplates.Select(t => new
            {
                t.Name,
                t.Description,
                type = t.Type.ToString(),
                t.Category,
                parameters = t.Parameters.Select(p => new
                {
                    p.Name,
                    p.Description,
                    p.DefaultValue,
                    p.Required
                })
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Previews the Verse code that would be generated for a template. " +
        "Returns the source code without writing any files. " +
        "Use this to review code before generating.")]
    public string preview_verse_code(
        VerseUIGenerator uiGenerator,
        VerseDeviceGenerator deviceGenerator,
        [Description("Template type (e.g., 'HudOverlay', 'GameManager', 'Scoreboard', 'TimerController', etc.)")] string templateType,
        [Description("Verse class name (e.g., 'my_game_hud')")] string className,
        [Description("Template parameters as JSON object (e.g., '{\"show_health\":\"true\",\"timer_seconds\":\"120\"}')")] string? parameters = null)
    {
        if (!Enum.TryParse<VerseTemplateType>(templateType, true, out var type))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Unknown template type: {templateType}",
                availableTypes = Enum.GetNames<VerseTemplateType>()
            }, JsonOpts);
        }

        var request = new VerseFileRequest
        {
            TemplateType = type,
            ClassName = className,
            OutputDirectory = null, // Don't write file for preview
            Parameters = ParseParameters(parameters)
        };

        var result = IsUITemplate(type)
            ? GeneratePreview(uiGenerator, request)
            : GeneratePreview(deviceGenerator, request);

        return JsonSerializer.Serialize(new
        {
            success = true,
            templateType = type.ToString(),
            className,
            sourceCode = result
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Generates a Verse source file from a template and writes it to disk. " +
        "Creates a complete, compilable .verse file in the specified directory. " +
        "After generating, compile in UEFN and place the device in your level.")]
    public string generate_verse_file(
        VerseUIGenerator uiGenerator,
        VerseDeviceGenerator deviceGenerator,
        [Description("Template type (e.g., 'HudOverlay', 'GameManager', 'Scoreboard')")] string templateType,
        [Description("Verse class name (e.g., 'my_game_hud')")] string className,
        [Description("Output file name without extension")] string? fileName = null,
        [Description("Output directory path (defaults to project Content folder)")] string? outputDirectory = null,
        [Description("Template parameters as JSON (e.g., '{\"show_health\":\"true\"}')")] string? parameters = null)
    {
        if (!Enum.TryParse<VerseTemplateType>(templateType, true, out var type))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Unknown template type: {templateType}",
                availableTypes = Enum.GetNames<VerseTemplateType>()
            }, JsonOpts);
        }

        var request = new VerseFileRequest
        {
            TemplateType = type,
            ClassName = className,
            FileName = fileName ?? className,
            OutputDirectory = outputDirectory,
            Parameters = ParseParameters(parameters)
        };

        VerseFileResult result;
        if (IsUITemplate(type))
            result = uiGenerator.Generate(request);
        else
            result = deviceGenerator.Generate(request);

        return JsonSerializer.Serialize(new
        {
            result.Success,
            result.FilePath,
            result.ClassName,
            result.Error,
            result.Notes,
            lineCount = result.SourceCode.Split('\n').Length,
            sourcePreview = result.SourceCode.Length > 500
                ? result.SourceCode[..500] + "\n... (truncated)"
                : result.SourceCode
        }, JsonOpts);
    }

    private static bool IsUITemplate(VerseTemplateType type)
    {
        return type is VerseTemplateType.HudOverlay
            or VerseTemplateType.Scoreboard
            or VerseTemplateType.InteractionMenu
            or VerseTemplateType.NotificationPopup
            or VerseTemplateType.ItemTracker
            or VerseTemplateType.ProgressBar
            or VerseTemplateType.CustomWidget;
    }

    private static string GeneratePreview(VerseUIGenerator gen, VerseFileRequest request)
    {
        // Temporarily null out directory so no file is written
        request.OutputDirectory = null;
        var result = gen.Generate(request);
        return result.SourceCode;
    }

    private static string GeneratePreview(VerseDeviceGenerator gen, VerseFileRequest request)
    {
        request.OutputDirectory = null;
        var result = gen.Generate(request);
        return result.SourceCode;
    }

    private static Dictionary<string, string> ParseParameters(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
            return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(parametersJson)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}
