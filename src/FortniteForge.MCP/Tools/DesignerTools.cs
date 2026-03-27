using WellVersed.Core.Models;
using WellVersed.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace WellVersed.MCP.Tools;

/// <summary>
/// MCP tools for the AI Game Designer + Project Assembler pipeline.
///
/// Three tools:
///   design_game        — NL description → structured GameDesign
///   assemble_project   — Full pipeline: description → complete UEFN project folder
///   get_game_templates — Pre-built game mode templates
///
/// The assemble_project tool is the crown jewel — one MCP call from
/// "I want a capture the flag game" to a complete UEFN project folder.
/// </summary>
[McpServerToolType]
public class DesignerTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Designs a game from a natural language description. Returns a structured GameDesign " +
        "with game mode, player/team counts, round structure, scoring, objectives, economy, " +
        "and UI needs — all extracted from the description using pattern matching.\n\n" +
        "This is Layer 1 of the pipeline. Use assemble_project for the full pipeline.")]
    public string design_game(
        GameDesigner designer,
        [Description("Natural language description of the game. Example: " +
            "'16-player capture the flag with 4 teams, scoring, and a HUD'")] string description,
        [Description("Number of players (auto-detected from description if not provided).")] int? playerCount = null,
        [Description("Number of teams (0 = FFA, auto-detected if not provided).")] int? teamCount = null)
    {
        var design = designer.DesignGame(description, playerCount, teamCount);
        var plan = designer.DesignToDevicePlan(design);

        return JsonSerializer.Serialize(new
        {
            design,
            plan = new
            {
                deviceCount = plan.Devices.Count,
                wiringCount = plan.Wiring.Count,
                devices = plan.Devices.Select(d => new { d.Role, d.Type, d.DeviceClass }),
                wiring = plan.Wiring.Select(w => new { w.SourceRole, w.OutputEvent, w.TargetRole, w.InputAction }),
                hasVerseCode = !string.IsNullOrEmpty(plan.VerseCode)
            },
            nextStep = "Call assemble_project with the same description and an output path to generate the complete project."
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Full pipeline: takes a game description and creates a complete UEFN project folder " +
        "with .uefnproject, config, verse files, widget specs, and a device placement manifest.\n\n" +
        "One MCP call from 'I want a capture the flag game' to a project folder you can open in UEFN.\n\n" +
        "This is the crown jewel — design → plan → assemble in one step.")]
    public string assemble_project(
        GameDesigner designer,
        ProjectAssembler assembler,
        [Description("Natural language description of the game. Example: " +
            "'8v8 team deathmatch with 3 rounds, 5 minute rounds, and a scoreboard'")] string description,
        [Description("Output directory path where the project folder will be created. " +
            "Must be a valid writable directory path.")] string outputPath,
        [Description("Number of players (auto-detected from description if not provided).")] int? playerCount = null,
        [Description("Number of teams (0 = FFA, auto-detected if not provided).")] int? teamCount = null,
        [Description("Template ID to use instead of description parsing. " +
            "Call get_game_templates to see available templates.")] string? templateId = null)
    {
        // Use template or design from description
        GameDesign design;
        if (!string.IsNullOrEmpty(templateId))
        {
            var template = GameDesigner.GetTemplate(templateId);
            if (template == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Template '{templateId}' not found. Call get_game_templates to see available templates."
                }, JsonOpts);
            }
            design = template.Design;

            // Override counts if provided
            if (playerCount.HasValue) design.PlayerCount = playerCount.Value;
            if (teamCount.HasValue) design.TeamCount = teamCount.Value;
        }
        else
        {
            design = designer.DesignGame(description, playerCount, teamCount);
        }

        // Convert design to device plan
        var plan = designer.DesignToDevicePlan(design);

        // Assemble the project
        var result = assembler.AssembleProject(design, plan, outputPath);

        return JsonSerializer.Serialize(new
        {
            result.Success,
            result.ProjectName,
            result.OutputPath,
            result.Error,
            fileCount = result.GeneratedFiles.Count,
            files = result.GeneratedFiles,
            plan = result.Plan,
            design = new
            {
                design.Name,
                design.GameMode,
                design.PlayerCount,
                design.TeamCount,
                objectiveCount = design.Objectives.Count,
                hasEconomy = design.Economy?.HasCurrency ?? false,
                uiCount = design.UINeeds.Count,
                mechanics = design.SpecialMechanics
            },
            result.Warnings
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Lists all pre-built game mode templates with complete GameDesign objects. " +
        "Each template can be passed directly to assemble_project by ID.\n\n" +
        "Templates: Team Deathmatch, Capture the Flag, Free For All, Tycoon, Zone Wars, Gun Game")]
    public string get_game_templates()
    {
        var templates = GameDesigner.GetTemplates();

        return JsonSerializer.Serialize(new
        {
            count = templates.Count,
            templates = templates.Select(t => new
            {
                t.Id,
                t.Name,
                t.Description,
                t.Category,
                t.Tags,
                design = new
                {
                    t.Design.GameMode,
                    t.Design.PlayerCount,
                    t.Design.TeamCount,
                    roundCount = t.Design.Rounds?.RoundCount ?? 0,
                    hasEconomy = t.Design.Economy?.HasCurrency ?? false,
                    objectiveCount = t.Design.Objectives.Count,
                    uiCount = t.Design.UINeeds.Count,
                    mechanics = t.Design.SpecialMechanics
                },
                usage = $"assemble_project(description: '...', outputPath: '...', templateId: '{t.Id}')"
            })
        }, JsonOpts);
    }
}
