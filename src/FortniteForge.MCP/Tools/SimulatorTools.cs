using WellVersed.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace WellVersed.MCP.Tools;

/// <summary>
/// MCP tools for the Device Behavior Simulator — discrete event simulation
/// that traces event flow through device wiring graphs and verse code.
/// </summary>
[McpServerToolType]
public class SimulatorTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Simulates what happens when a specific event fires on a device. " +
        "Traces the entire event chain through wiring and verse handlers, " +
        "showing every state change, time delay, and conditional branch.")]
    public string simulate_event(
        DeviceSimulator simulator,
        [Description("Path to the level (.umap) file to simulate")] string levelPath,
        [Description("Name of the device that fires the event")] string deviceName,
        [Description("Name of the event to fire (e.g., 'OnTriggered', 'OnCompleted')")] string eventName)
    {
        var world = simulator.BuildSimulation(levelPath);
        var result = simulator.SimulateEvent(world, deviceName, eventName);

        return JsonSerializer.Serialize(new
        {
            result.InitialTrigger,
            stepCount = result.Steps.Count,
            result.TotalSimulatedTime,
            result.ReachesEndGame,
            warnings = result.Warnings,
            steps = result.Steps.Select(s => new
            {
                s.StepNumber,
                s.SimulatedTime,
                s.TriggerDevice,
                s.Event,
                s.TargetDevice,
                s.Action,
                oldPhase = s.OldPhase.ToString(),
                newPhase = s.NewPhase.ToString(),
                s.Description,
                s.VerseHandlersCalled,
                s.IsConditional,
                s.Condition
            }),
            finalStates = result.FinalStates.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToString())
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Simulates the full game loop from start to end. " +
        "Detects game phases (Warmup, Active, Scoring, EndGame, RoundReset) " +
        "and builds an interactive game loop model showing how the game flows.")]
    public string simulate_game_loop(
        DeviceSimulator simulator,
        [Description("Path to the level (.umap) file to simulate")] string levelPath)
    {
        var result = simulator.SimulateGameLoop(levelPath);

        return JsonSerializer.Serialize(new
        {
            result.InitialTrigger,
            stepCount = result.Steps.Count,
            result.TotalSimulatedTime,
            result.ReachesEndGame,
            warnings = result.Warnings,
            dfa = result.DFA == null ? null : new
            {
                nodes = result.DFA.Nodes.Select(n => new
                {
                    n.DeviceName, n.DeviceClass, n.DeviceType,
                    currentPhase = n.CurrentPhase.ToString(),
                    n.AvailableEvents, n.AvailableActions, n.X, n.Y
                }),
                edges = result.DFA.Edges.Select(e => new
                {
                    e.SourceDevice, e.Event, e.TargetDevice, e.Action,
                    resultingPhase = e.ResultingPhase.ToString(),
                    e.IsConditional, e.Condition
                }),
                result.DFA.StateHash,
                historyCount = result.DFA.History.Count
            },
            steps = result.Steps.Select(s => new
            {
                s.StepNumber, s.SimulatedTime, s.TriggerDevice, s.Event,
                s.TargetDevice, s.Action,
                oldPhase = s.OldPhase.ToString(),
                newPhase = s.NewPhase.ToString(),
                s.Description, s.VerseHandlersCalled, s.IsConditional, s.Condition
            }),
            finalStates = result.FinalStates.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToString())
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Traces backwards from the win/end condition to game start. " +
        "Shows the critical path of events required for the game to complete.")]
    public string trace_win_condition(
        DeviceSimulator simulator,
        [Description("Path to the level (.umap) file to analyze")] string levelPath)
    {
        var result = simulator.TraceWinCondition(levelPath);

        return JsonSerializer.Serialize(new
        {
            result.InitialTrigger,
            stepCount = result.Steps.Count,
            result.ReachesEndGame,
            warnings = result.Warnings,
            criticalPath = result.Steps.Select(s => new
            {
                s.StepNumber, s.TriggerDevice, s.Event,
                s.TargetDevice, s.Action, s.Description
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Finds devices in dead-end states where no further events can fire. " +
        "Identifies stuck game states, broken wiring, and unreachable devices.")]
    public string find_dead_end_states(
        DeviceSimulator simulator,
        [Description("Path to the level (.umap) file to analyze")] string levelPath)
    {
        var world = simulator.BuildSimulation(levelPath);
        var deadEnds = simulator.FindDeadEndStates(world);

        return JsonSerializer.Serialize(new
        {
            levelPath,
            totalDevices = world.Devices.Count,
            totalWires = world.Wiring.Count,
            deadEndCount = deadEnds.Count,
            deadEnds = deadEnds.Select(d => new
            {
                d.DeviceName,
                d.DeviceType,
                phase = d.Phase.ToString(),
                d.Reason
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Builds a DFA (Deterministic Finite Automaton) model of the device network. " +
        "Returns nodes (devices with states), edges (event→action wiring), and simulation history. " +
        "The history allows step-by-step playback of the entire event cascade. " +
        "This is the data model for the interactive simulator diagram.")]
    public string build_dfa(
        DeviceSimulator simulator,
        [Description("Path to the level (.umap) file to analyze")] string levelPath)
    {
        var dfa = simulator.BuildDFA(levelPath);

        return JsonSerializer.Serialize(new
        {
            nodeCount = dfa.Nodes.Count,
            edgeCount = dfa.Edges.Count,
            historySteps = dfa.History.Count,
            initialStateHash = dfa.StateHash,
            nodes = dfa.Nodes.Select(n => new
            {
                n.DeviceName,
                n.DeviceClass,
                n.DeviceType,
                phase = n.CurrentPhase.ToString(),
                events = n.AvailableEvents,
                actions = n.AvailableActions,
                x = n.X,
                y = n.Y
            }),
            edges = dfa.Edges.Select(e => new
            {
                e.SourceDevice,
                e.Event,
                e.TargetDevice,
                e.Action,
                resultingPhase = e.ResultingPhase.ToString(),
                e.IsConditional,
                e.Condition
            }),
            history = dfa.History.Select(h => new
            {
                h.StepNumber,
                h.SimulatedTime,
                h.FiredEdgeSource,
                h.FiredEdgeEvent,
                h.StateHash,
                devicePhases = h.DevicePhases.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToString())
            })
        }, JsonOpts);
    }
}
