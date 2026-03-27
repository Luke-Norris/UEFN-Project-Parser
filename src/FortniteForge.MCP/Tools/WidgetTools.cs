using WellVersed.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace WellVersed.MCP.Tools;

/// <summary>
/// MCP tools for Verse UI widget development.
/// Provides widget API reference, layout patterns, and examples from the library.
/// </summary>
[McpServerToolType]
public class WidgetTools
{
    [McpServerTool, Description(
        "Get the complete Verse UI widget reference — all widget types, their properties, " +
        "usage patterns, and anchoring presets. Use this when building any UI in UEFN Verse. " +
        "Returns the full API reference Claude needs to compose widgets correctly.")]
    public string get_widget_reference()
    {
        var widgets = VerseWidgetReference.GetAllWidgets();
        var presets = VerseWidgetReference.GetAnchoringPresets();

        var output = new List<string> { "=== Verse UI Widget Reference ===\n" };

        foreach (var w in widgets)
        {
            output.Add($"## {w.Name} ({w.Category})");
            output.Add(w.Description);
            if (w.Properties.Count > 0)
            {
                output.Add("Properties:");
                foreach (var p in w.Properties)
                    output.Add($"  {p.Name} : {p.Type} — {p.Description}");
            }
            output.Add($"Usage:\n{w.UsagePattern}");
            if (!string.IsNullOrEmpty(w.Notes))
                output.Add($"Note: {w.Notes}");
            output.Add("");
        }

        output.Add("=== Anchoring Presets ===");
        foreach (var p in presets)
            output.Add($"  {p.Name}: Anchors={p.Anchors} Alignment={p.Alignment}");

        return string.Join("\n", output);
    }

    [McpServerTool, Description(
        "Get a specific Verse UI widget type reference. " +
        "Pass the widget name (e.g. 'canvas', 'text_block', 'overlay', 'stack_box', 'color_block', 'button').")]
    public string get_widget_type(
        [Description("Widget type name (canvas, text_block, overlay, stack_box, color_block, button, texture_block, player_ui)")] string widgetName)
    {
        var widget = VerseWidgetReference.GetWidget(widgetName);
        if (widget == null)
            return $"Widget '{widgetName}' not found. Available: canvas, canvas_slot, overlay, stack_box, text_block, color_block, texture_block, button, player_ui";

        return $"## {widget.Name} ({widget.Category})\n{widget.Description}\n\n" +
               $"Properties:\n{string.Join("\n", widget.Properties.Select(p => $"  {p.Name} : {p.Type} — {p.Description}"))}\n\n" +
               $"Usage:\n{widget.UsagePattern}\n\n" +
               $"Note: {widget.Notes}";
    }

    [McpServerTool, Description(
        "Get common Verse UI code patterns — HUD elements, toggle panels, notifications, " +
        "leaderboards, health bars, and required imports. Optionally filter by keyword. " +
        "Use when you need a working code example for a specific UI feature.")]
    public string get_ui_patterns(
        [Description("Optional keyword to filter patterns (e.g. 'notification', 'leaderboard', 'health', 'toggle')")] string? keyword = null)
    {
        var patterns = keyword != null
            ? VerseWidgetReference.FindPatterns(keyword)
            : VerseWidgetReference.GetCommonPatterns();

        if (patterns.Count == 0)
            return $"No patterns found for '{keyword}'. Try: notification, leaderboard, health, toggle, using";

        var output = new List<string>();
        foreach (var p in patterns)
        {
            output.Add($"=== {p.Name} ===");
            output.Add(p.Description);
            output.Add($"\n{p.Code}\n");
        }
        return string.Join("\n", output);
    }

    [McpServerTool, Description(
        "Search the library for real Verse UI implementations. Finds verse files that use " +
        "specific widget types or UI patterns from the indexed library of 743+ verse files. " +
        "Use to find production examples of UI features like shops, scoreboards, voting systems, etc.")]
    public string find_ui_examples(
        LibraryIndexer indexer,
        [Description("What to search for (e.g. 'shop', 'scoreboard', 'leaderboard', 'voting', 'battlepass', 'rank')")] string query)
    {
        // Search library for verse files that likely contain UI code
        var results = indexer.Search(query);
        var uiFiles = results.VerseFiles
            .Where(h => h.Item.Functions.Any(f =>
                f.Contains("UI", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("Widget", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("Canvas", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("Show", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("Display", StringComparison.OrdinalIgnoreCase)) ||
                h.Item.Summary.Contains("canvas", StringComparison.OrdinalIgnoreCase) ||
                h.Item.Summary.Contains("text_block", StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToList();

        if (uiFiles.Count == 0)
        {
            // Fall back to general search results
            uiFiles = results.VerseFiles.Take(5).ToList();
        }

        if (uiFiles.Count == 0)
            return $"No UI examples found for '{query}'. Try broader keywords or use get_ui_patterns() for built-in patterns.";

        var output = new List<string> { $"UI examples matching '{query}':\n" };
        foreach (var h in uiFiles)
        {
            output.Add($"  [{h.ProjectName}] {h.Item.Name} ({h.Item.LineCount} lines)");
            output.Add($"    {h.Item.Summary}");
            output.Add($"    Path: {h.Item.FilePath}");
            output.Add($"    Use get_verse_source to read the full implementation.\n");
        }

        return string.Join("\n", output);
    }
}
