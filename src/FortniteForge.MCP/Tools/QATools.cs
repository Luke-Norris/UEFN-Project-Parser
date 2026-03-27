using WellVersed.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace WellVersed.MCP.Tools;

/// <summary>
/// MCP tools for automated QA analysis — static analysis of device wiring graphs
/// to find logic bugs without playtesting.
/// </summary>
[McpServerToolType]
public class QATools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Runs full QA analysis on a level — finds dead devices, orphaned outputs, missing win conditions, " +
        "timing conflicts, unreachable spawns, infinite loops, team config issues, unused verse devices, " +
        "property anomalies, and duplicate wiring. Returns a structured report with severity levels and suggested fixes.")]
    public string run_qa_analysis(
        LogicTracer logicTracer,
        [Description("Path to the .umap level file to analyze. Use find_levels to discover available levels.")] string levelPath)
    {
        try
        {
            var report = logicTracer.AnalyzeLevel(levelPath);

            return JsonSerializer.Serialize(new
            {
                report.LevelPath,
                report.TotalDevices,
                report.TotalConnections,
                report.PassesQA,
                report.Summary,
                counts = new
                {
                    report.CriticalCount,
                    report.WarningCount,
                    report.InfoCount,
                    total = report.Issues.Count
                },
                issues = report.Issues.Select(i => new
                {
                    i.Id,
                    severity = i.Severity.ToString(),
                    i.Category,
                    i.Title,
                    i.Description,
                    i.AffectedDevice,
                    i.SuggestedFix
                })
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"QA analysis failed: {ex.Message}",
                levelPath
            }, JsonOpts);
        }
    }

    [McpServerTool, Description(
        "Traces what happens when a specific device fires an event. Follows the signal through " +
        "the wiring graph and returns the complete chain: A.OnTriggered -> B.Enable -> B.OnComplete -> C.Start -> ...")]
    public string trace_signal_flow(
        LogicTracer logicTracer,
        [Description("Path to the .umap level file.")] string levelPath,
        [Description("Actor name of the device to start tracing from (e.g., 'TriggerDevice_3').")] string startDevice,
        [Description("The output event to trace (e.g., 'OnTriggered'). Leave empty to trace all outputs.")] string? outputEvent = null)
    {
        try
        {
            var graph = logicTracer.BuildDeviceGraph(levelPath);

            // Verify the start device exists
            var startNode = graph.Nodes.FirstOrDefault(n =>
                n.ActorName.Equals(startDevice, StringComparison.OrdinalIgnoreCase));

            if (startNode == null)
            {
                var suggestions = graph.Nodes
                    .Where(n => n.ActorName.Contains(startDevice, StringComparison.OrdinalIgnoreCase))
                    .Select(n => n.ActorName)
                    .Take(5)
                    .ToList();

                return JsonSerializer.Serialize(new
                {
                    error = $"Device '{startDevice}' not found in level.",
                    availableDevices = suggestions.Count > 0 ? suggestions : null,
                    hint = suggestions.Count == 0 ? "Use get_device_graph to see all devices." : null
                }, JsonOpts);
            }

            var steps = logicTracer.TraceSignalFlow(graph, startDevice, outputEvent ?? "");

            if (steps.Count == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    startDevice,
                    outputEvent = outputEvent ?? "(all)",
                    result = "No outgoing signals found from this device/event.",
                    availableOutputs = startNode.OutputEvents
                }, JsonOpts);
            }

            // Build a readable chain
            var chain = steps.Select(s => s.IsCycle
                ? $"[CYCLE] -> {s.DeviceName} (loop detected)"
                : $"{s.DeviceName}.{s.Event} -> {s.TargetDevice}.{s.Action}");

            return JsonSerializer.Serialize(new
            {
                startDevice,
                outputEvent = outputEvent ?? "(all)",
                totalSteps = steps.Count,
                hasCycles = steps.Any(s => s.IsCycle),
                chain,
                steps = steps.Select(s => new
                {
                    s.DeviceName,
                    s.Event,
                    s.TargetDevice,
                    s.Action,
                    s.Depth,
                    s.IsCycle
                })
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Signal trace failed: {ex.Message}",
                startDevice,
                levelPath
            }, JsonOpts);
        }
    }

    [McpServerTool, Description(
        "Returns the raw device wiring graph for a level — all devices (nodes) and their signal connections (edges). " +
        "Useful for understanding the complete device topology before running analysis.")]
    public string get_device_graph(
        LogicTracer logicTracer,
        [Description("Path to the .umap level file.")] string levelPath)
    {
        try
        {
            var graph = logicTracer.BuildDeviceGraph(levelPath);

            return JsonSerializer.Serialize(new
            {
                graph.TotalDevices,
                graph.TotalConnections,
                nodes = graph.Nodes.Select(n => new
                {
                    n.ActorName,
                    n.DeviceClass,
                    n.DeviceType,
                    position = n.Position.ToString(),
                    n.OutputEvents,
                    n.InputActions,
                    n.PropertyCount,
                    n.IsVerseDevice
                }),
                edges = graph.Edges.Select(e => new
                {
                    e.SourceActor,
                    e.OutputEvent,
                    e.TargetActor,
                    e.InputAction
                })
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Failed to build device graph: {ex.Message}",
                levelPath
            }, JsonOpts);
        }
    }
}
