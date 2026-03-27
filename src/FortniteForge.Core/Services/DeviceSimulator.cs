using WellVersed.Core.Config;
using WellVersed.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace WellVersed.Core.Services;

/// <summary>
/// Discrete event simulator that models device state machines and event flow.
/// Traces "what happens when X fires" through the entire wiring graph + verse code,
/// showing every state change. NOT a game engine — it's a simulation tracer.
/// </summary>
public class DeviceSimulator
{
    private readonly WellVersedConfig _config;
    private readonly DeviceService _deviceService;
    private readonly DigestService _digestService;
    private readonly ILogger<DeviceSimulator> _logger;

    private const int MaxSimulationDepth = 100;
    private const float DefaultTimerDuration = 10f;

    // Known device type patterns for phase detection
    private static readonly HashSet<string> EndGameKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "endgame", "end_game", "victory", "gameover", "game_over", "winner", "finalround"
    };

    private static readonly HashSet<string> ScoringKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "score", "point", "elimination", "kill", "objective", "capture", "collect"
    };

    public DeviceSimulator(
        WellVersedConfig config,
        DeviceService deviceService,
        DigestService digestService,
        ILogger<DeviceSimulator> logger)
    {
        _config = config;
        _deviceService = deviceService;
        _digestService = digestService;
        _logger = logger;
    }

    /// <summary>
    /// Builds a simulation world from a level — loads devices, schemas, verse handlers, wiring.
    /// </summary>
    public SimulationWorld BuildSimulation(string levelPath)
    {
        var world = new SimulationWorld { LevelPath = levelPath };

        // Load all devices from the level
        List<DeviceInfo> devices;
        try
        {
            devices = _deviceService.ListDevicesInLevel(levelPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load devices from {Level}, trying external actors", levelPath);
            devices = new List<DeviceInfo>();
        }

        // Also scan external actors directory for UEFN's per-actor file storage
        var levelName = Path.GetFileNameWithoutExtension(levelPath);
        var contentDir = Path.GetDirectoryName(levelPath) ?? "";
        var externalActorsDir = Path.Combine(contentDir, "__ExternalActors__", levelName);
        if (Directory.Exists(externalActorsDir))
        {
            foreach (var actorFile in Directory.EnumerateFiles(externalActorsDir, "*.uasset", SearchOption.AllDirectories))
            {
                try
                {
                    var actorDevices = _deviceService.ListDevicesInLevel(actorFile);
                    devices.AddRange(actorDevices);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not parse external actor: {File}", actorFile);
                }
            }
        }

        // Build simulated devices
        foreach (var device in devices)
        {
            var simDevice = new SimulatedDevice
            {
                ActorName = device.ActorName,
                DeviceClass = device.DeviceClass,
                DeviceType = device.DeviceType,
                Phase = DevicePhase.Idle,
                IsVerseDevice = device.DeviceClass.Contains("Verse", StringComparison.OrdinalIgnoreCase) ||
                                device.DeviceClass.Contains("creative_device", StringComparison.OrdinalIgnoreCase)
            };

            // Copy properties
            foreach (var prop in device.Properties)
            {
                simDevice.Properties[prop.Name] = prop.Value ?? "";
            }

            // Load schema for events/actions
            var schema = _deviceService.GetDeviceSchema(device.DeviceClass);
            if (schema != null)
            {
                simDevice.AvailableEvents.AddRange(schema.Events);
                simDevice.AvailableActions.AddRange(schema.Functions);
            }

            // Add well-known events/actions based on device type patterns
            AddWellKnownEventsAndActions(simDevice);

            // Copy wiring
            foreach (var wire in device.Wiring)
            {
                world.Wiring.Add(new SimulationWire
                {
                    SourceDevice = device.ActorName,
                    OutputEvent = wire.OutputEvent,
                    TargetDevice = wire.TargetDevice,
                    InputAction = wire.InputAction,
                    Channel = wire.Channel
                });
            }

            world.Devices[device.ActorName] = simDevice;
        }

        // Parse verse files for event subscriptions
        ParseVerseHandlers(world);

        return world;
    }

    /// <summary>
    /// Simulates what happens when a specific event fires on a device.
    /// Follows the wiring graph, tracking state changes at each step.
    /// </summary>
    public SimulationResult SimulateEvent(SimulationWorld world, string deviceName, string eventName)
    {
        var result = new SimulationResult
        {
            InitialTrigger = $"{deviceName}.{eventName}",
            Steps = new List<SimulationStep>(),
            FinalStates = new Dictionary<string, DevicePhase>(),
            Warnings = new List<string>()
        };

        // Reset all device phases
        foreach (var dev in world.Devices.Values)
            dev.Phase = DevicePhase.Idle;

        var visited = new HashSet<string>();
        float currentTime = 0f;

        // Execute the simulation
        SimulateEventRecursive(world, deviceName, eventName, result, visited, ref currentTime, 0);

        // Capture final states
        foreach (var kvp in world.Devices)
            result.FinalStates[kvp.Key] = kvp.Value.Phase;

        result.TotalSimulatedTime = currentTime;

        // Check if simulation reaches EndGame
        result.ReachesEndGame = result.Steps.Any(s =>
            s.TargetDevice != null && ContainsAnyKeyword(s.TargetDevice, EndGameKeywords) ||
            s.Action != null && ContainsAnyKeyword(s.Action, EndGameKeywords));

        // Build DFA model from the simulation
        result.DFA = BuildDFA(world.LevelPath);

        return result;
    }

    /// <summary>
    /// Simulates the full game loop starting from GameStart/OnBegin.
    /// Traces warmup -> active play -> scoring -> win condition -> end.
    /// </summary>
    public SimulationResult SimulateGameLoop(string levelPath)
    {
        var world = BuildSimulation(levelPath);
        var result = new SimulationResult
        {
            InitialTrigger = "GameStart",
            Steps = new List<SimulationStep>(),
            FinalStates = new Dictionary<string, DevicePhase>(),
            Warnings = new List<string>()
        };

        // Reset all device phases
        foreach (var dev in world.Devices.Values)
            dev.Phase = DevicePhase.Idle;

        var visited = new HashSet<string>();
        float currentTime = 0f;

        // Find game start triggers — devices that fire on game begin
        var startDevices = FindGameStartDevices(world);

        if (startDevices.Count == 0)
        {
            result.Warnings.Add("No game start triggers found. Looked for: OnBegin, GameStart, timer devices with auto-start.");

            // Fall back to simulating from all devices that have wiring out
            var devicesWithWiring = world.Devices.Values
                .Where(d => world.Wiring.Any(w => w.SourceDevice == d.ActorName))
                .OrderBy(d => d.ActorName)
                .Take(5)
                .ToList();

            foreach (var dev in devicesWithWiring)
            {
                var firstEvent = dev.AvailableEvents.FirstOrDefault() ?? "OnTriggered";
                SimulateEventRecursive(world, dev.ActorName, firstEvent, result, visited, ref currentTime, 0);
            }
        }
        else
        {
            foreach (var (deviceName, eventName) in startDevices)
            {
                SimulateEventRecursive(world, deviceName, eventName, result, visited, ref currentTime, 0);
            }
        }

        // Capture final states
        foreach (var kvp in world.Devices)
            result.FinalStates[kvp.Key] = kvp.Value.Phase;

        result.TotalSimulatedTime = currentTime;
        result.ReachesEndGame = result.Steps.Any(s =>
            s.TargetDevice != null && ContainsAnyKeyword(s.TargetDevice, EndGameKeywords) ||
            s.Action != null && ContainsAnyKeyword(s.Action, EndGameKeywords));

        // Build DFA model from the simulation
        result.DFA = BuildDFA(levelPath);

        // Add dead end warnings
        var deadEnds = FindDeadEndStates(world);
        foreach (var de in deadEnds)
        {
            result.Warnings.Add($"Dead end: {de.DeviceName} ({de.DeviceType}) stuck in {de.Phase} — {de.Reason}");
        }

        return result;
    }

    /// <summary>
    /// Traces backwards from win/end conditions to game start.
    /// Shows the critical path required for the game to complete.
    /// </summary>
    public SimulationResult TraceWinCondition(string levelPath)
    {
        var world = BuildSimulation(levelPath);
        var result = new SimulationResult
        {
            InitialTrigger = "WinCondition (reverse trace)",
            Steps = new List<SimulationStep>(),
            FinalStates = new Dictionary<string, DevicePhase>(),
            Warnings = new List<string>()
        };

        // Find EndGame/Score devices
        var endDevices = world.Devices.Values
            .Where(d =>
                ContainsAnyKeyword(d.ActorName, EndGameKeywords) ||
                ContainsAnyKeyword(d.DeviceClass, EndGameKeywords) ||
                ContainsAnyKeyword(d.DeviceType, EndGameKeywords) ||
                ContainsAnyKeyword(d.ActorName, ScoringKeywords) ||
                ContainsAnyKeyword(d.DeviceClass, ScoringKeywords))
            .ToList();

        if (endDevices.Count == 0)
        {
            result.Warnings.Add("No EndGame or Score devices found. Cannot trace win condition.");
            return result;
        }

        // Reverse trace — for each end device, find what wires lead TO it
        var visited = new HashSet<string>();
        int stepNumber = 0;

        foreach (var endDev in endDevices)
        {
            TraceBackward(world, endDev.ActorName, result, visited, ref stepNumber, 0);
        }

        // Reverse the steps so they read start-to-end
        result.Steps.Reverse();
        for (int i = 0; i < result.Steps.Count; i++)
            result.Steps[i].StepNumber = i + 1;

        result.ReachesEndGame = endDevices.Count > 0;
        result.DFA = BuildDFA(world.LevelPath);

        return result;
    }

    /// <summary>
    /// Finds devices in states where no further events can fire — game stuck.
    /// </summary>
    public List<DeadEndState> FindDeadEndStates(SimulationWorld world)
    {
        var deadEnds = new List<DeadEndState>();

        foreach (var kvp in world.Devices)
        {
            var device = kvp.Value;
            var deviceName = kvp.Key;

            // Check if this device has any outgoing wires
            var outgoingWires = world.Wiring.Where(w => w.SourceDevice == deviceName).ToList();

            // Check if any other device wires TO this one
            var incomingWires = world.Wiring.Where(w => w.TargetDevice == deviceName).ToList();

            // Device is a dead end if:
            // 1. It has incoming wires but no outgoing wires (terminal node)
            // 2. AND it's not an EndGame/Score device (those are supposed to be terminal)
            if (incomingWires.Count > 0 && outgoingWires.Count == 0 &&
                !ContainsAnyKeyword(deviceName, EndGameKeywords) &&
                !ContainsAnyKeyword(device.DeviceType, EndGameKeywords) &&
                !ContainsAnyKeyword(device.DeviceType, ScoringKeywords))
            {
                deadEnds.Add(new DeadEndState
                {
                    DeviceName = deviceName,
                    DeviceType = device.DeviceType,
                    Phase = device.Phase,
                    Reason = $"Receives events from {incomingWires.Count} source(s) but has no outgoing connections. " +
                             $"If this device activates, no downstream events will fire."
                });
            }

            // Also flag devices that wire to nonexistent targets
            foreach (var wire in outgoingWires)
            {
                if (!string.IsNullOrEmpty(wire.TargetDevice) && !world.Devices.ContainsKey(wire.TargetDevice))
                {
                    deadEnds.Add(new DeadEndState
                    {
                        DeviceName = deviceName,
                        DeviceType = device.DeviceType,
                        Phase = device.Phase,
                        Reason = $"Wires to '{wire.TargetDevice}' which does not exist in this level. Broken connection."
                    });
                }
            }
        }

        return deadEnds;
    }

    /// <summary>
    /// Builds a DFA model from a level — pure state machine, no game design opinions.
    /// Nodes = devices with their current state. Edges = event→action wiring.
    /// Includes simulation history for step-by-step playback.
    /// </summary>
    public DFAModel BuildDFA(string levelPath)
    {
        var world = BuildSimulation(levelPath);
        var dfa = new DFAModel();

        // Build nodes with force-directed layout positions
        int i = 0;
        int count = world.Devices.Count;
        foreach (var (name, device) in world.Devices)
        {
            // Circular layout as starting positions
            var angle = 2.0 * Math.PI * i / Math.Max(count, 1);
            var radius = 250.0 + count * 10;

            dfa.Nodes.Add(new DFANode
            {
                DeviceName = name,
                DeviceClass = device.DeviceClass,
                DeviceType = device.DeviceType,
                CurrentPhase = device.Phase,
                AvailableEvents = device.AvailableEvents,
                AvailableActions = device.AvailableActions,
                X = (float)(400 + radius * Math.Cos(angle)),
                Y = (float)(300 + radius * Math.Sin(angle))
            });
            i++;
        }

        // Build edges from wiring
        foreach (var wire in world.Wiring)
        {
            var resultPhase = InferResultingPhase(wire.InputAction);
            dfa.Edges.Add(new DFAEdge
            {
                SourceDevice = wire.SourceDevice,
                Event = wire.OutputEvent,
                TargetDevice = wire.TargetDevice,
                Action = wire.InputAction,
                ResultingPhase = resultPhase,
                IsConditional = false
            });
        }

        // Add edges from verse handlers
        foreach (var handler in world.VerseHandlers)
        {
            foreach (var call in handler.DeviceCalls)
            {
                dfa.Edges.Add(new DFAEdge
                {
                    SourceDevice = handler.DeviceName,
                    Event = handler.EventName,
                    TargetDevice = call.TargetDevice,
                    Action = call.MethodName,
                    ResultingPhase = InferResultingPhase(call.MethodName),
                    IsConditional = !string.IsNullOrEmpty(handler.Condition),
                    Condition = handler.Condition
                });
            }
        }

        // Compute initial state hash
        dfa.StateHash = ComputeStateHash(world.Devices);

        // Run full simulation for history
        var startDevices = FindStartDevices(world);
        float time = 0f;
        int step = 0;

        // Initial snapshot
        dfa.History.Add(new DFASnapshot
        {
            StepNumber = 0,
            SimulatedTime = 0,
            FiredEdgeSource = "",
            FiredEdgeEvent = "Initial State",
            DevicePhases = world.Devices.ToDictionary(d => d.Key, d => d.Value.Phase),
            StateHash = dfa.StateHash
        });

        // Simulate from each start device
        foreach (var start in startDevices)
        {
            var visited = new HashSet<string>();
            SimulateDFARecursive(world, start.device, start.evt, dfa, visited, ref time, ref step);
        }

        return dfa;
    }

    private void SimulateDFARecursive(
        SimulationWorld world, string deviceName, string eventName,
        DFAModel dfa, HashSet<string> visited, ref float currentTime, ref int stepNum)
    {
        var visitKey = $"{deviceName}.{eventName}";
        if (visited.Contains(visitKey) || stepNum > MaxSimulationDepth)
            return;
        visited.Add(visitKey);

        // Find all wires from this device/event
        var matchingWires = world.Wiring
            .Where(w => w.SourceDevice == deviceName &&
                        (w.OutputEvent == eventName || eventName == "*"))
            .ToList();

        foreach (var wire in matchingWires)
        {
            if (!world.Devices.TryGetValue(wire.TargetDevice, out var target))
                continue;

            var oldPhase = target.Phase;
            var newPhase = InferResultingPhase(wire.InputAction);

            // Check if timer — add delay
            if (target.DeviceClass.Contains("Timer", StringComparison.OrdinalIgnoreCase) &&
                wire.InputAction.Contains("Start", StringComparison.OrdinalIgnoreCase))
            {
                var duration = GetTimerDuration(target);
                currentTime += duration;
            }

            target.Phase = newPhase;
            stepNum++;

            dfa.History.Add(new DFASnapshot
            {
                StepNumber = stepNum,
                SimulatedTime = currentTime,
                FiredEdgeSource = $"{deviceName}.{wire.OutputEvent}",
                FiredEdgeEvent = $"{wire.TargetDevice}.{wire.InputAction}",
                DevicePhases = world.Devices.ToDictionary(d => d.Key, d => d.Value.Phase),
                StateHash = ComputeStateHash(world.Devices)
            });

            // Continue downstream — what events does this action trigger?
            var downstreamEvents = GetDownstreamEvents(wire.InputAction, target);
            foreach (var evt in downstreamEvents)
            {
                SimulateDFARecursive(world, wire.TargetDevice, evt, dfa, visited, ref currentTime, ref stepNum);
            }
        }
    }

    private static DevicePhase InferResultingPhase(string action)
    {
        var lower = action.ToLower();
        if (lower.Contains("enable") || lower.Contains("activate") || lower.Contains("start") || lower.Contains("show"))
            return DevicePhase.Active;
        if (lower.Contains("disable") || lower.Contains("deactivate") || lower.Contains("hide"))
            return DevicePhase.Disabled;
        if (lower.Contains("trigger") || lower.Contains("fire"))
            return DevicePhase.Triggered;
        if (lower.Contains("complete") || lower.Contains("finish") || lower.Contains("end"))
            return DevicePhase.Completed;
        if (lower.Contains("reset") || lower.Contains("restart"))
            return DevicePhase.Idle;
        return DevicePhase.Active;
    }

    private static List<string> GetDownstreamEvents(string action, SimulatedDevice device)
    {
        var lower = action.ToLower();
        var events = new List<string>();

        if (lower.Contains("start")) events.Add("OnCompleted");
        if (lower.Contains("trigger")) events.Add("OnTriggered");
        if (lower.Contains("activate")) events.Add("OnActivated");
        if (lower.Contains("enable")) events.Add("OnEnabled");
        if (lower.Contains("complete")) events.Add("OnCompleted");
        if (lower.Contains("grant")) events.Add("OnItemGranted");

        // If no specific downstream, check device's available events
        if (events.Count == 0 && device.AvailableEvents.Count > 0)
        {
            events.Add(device.AvailableEvents[0]);
        }

        return events;
    }

    private static float GetTimerDuration(SimulatedDevice device)
    {
        if (device.Properties.TryGetValue("Duration", out var dur) && float.TryParse(dur, out var val))
            return val;
        if (device.Properties.TryGetValue("TimerDuration", out var td) && float.TryParse(td, out var tdVal))
            return tdVal;
        return DefaultTimerDuration;
    }

    private static string ComputeStateHash(Dictionary<string, SimulatedDevice> devices)
    {
        var parts = devices.OrderBy(d => d.Key).Select(d => $"{d.Key}:{d.Value.Phase}");
        return string.Join("|", parts).GetHashCode().ToString("X8");
    }

    private List<(string device, string evt)> FindStartDevices(SimulationWorld world)
    {
        var starts = new List<(string, string)>();

        foreach (var (name, device) in world.Devices)
        {
            // Verse devices start via OnBegin
            if (device.IsVerseDevice)
                starts.Add((name, "OnBegin"));

            // Devices with no incoming wiring are potential start points
            var hasIncoming = world.Wiring.Any(w => w.TargetDevice == name);
            if (!hasIncoming && device.AvailableEvents.Count > 0)
                starts.Add((name, device.AvailableEvents[0]));
        }

        if (starts.Count == 0 && world.Devices.Count > 0)
            starts.Add((world.Devices.Keys.First(), "*"));

        return starts.Distinct().ToList();
    }

    // ─── Private Simulation Logic ──────────────────────────────────────────────

    private void SimulateEventRecursive(
        SimulationWorld world,
        string deviceName,
        string eventName,
        SimulationResult result,
        HashSet<string> visited,
        ref float currentTime,
        int depth)
    {
        if (depth >= MaxSimulationDepth)
        {
            result.Warnings.Add($"Cycle detected or max depth reached at {deviceName}.{eventName} (depth {depth})");
            return;
        }

        var visitKey = $"{deviceName}.{eventName}";
        if (visited.Contains(visitKey))
        {
            result.Warnings.Add($"Cycle detected: {deviceName}.{eventName} already visited");
            return;
        }
        visited.Add(visitKey);

        // Find all wires from this device/event
        var outgoingWires = world.Wiring
            .Where(w => w.SourceDevice == deviceName &&
                        (string.IsNullOrEmpty(w.OutputEvent) || w.OutputEvent.Contains(eventName, StringComparison.OrdinalIgnoreCase) || eventName == "*"))
            .ToList();

        // Also check verse handlers
        var verseHandlers = world.VerseHandlers
            .Where(h => h.DeviceName == deviceName && h.EventName.Contains(eventName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Process each outgoing wire
        foreach (var wire in outgoingWires)
        {
            if (string.IsNullOrEmpty(wire.TargetDevice))
                continue;

            var targetDevice = world.Devices.GetValueOrDefault(wire.TargetDevice);
            var oldPhase = targetDevice?.Phase ?? DevicePhase.Idle;

            // Determine new phase based on the action
            var newPhase = DetermineNewPhase(wire.InputAction, oldPhase);
            if (targetDevice != null)
                targetDevice.Phase = newPhase;

            // Check for timer delays
            float timeDelay = 0f;
            if (targetDevice != null && IsTimerDevice(targetDevice))
            {
                timeDelay = GetTimerDuration(targetDevice);
                currentTime += timeDelay;
            }

            var step = new SimulationStep
            {
                StepNumber = result.Steps.Count + 1,
                SimulatedTime = currentTime,
                TriggerDevice = deviceName,
                Event = eventName,
                TargetDevice = wire.TargetDevice,
                Action = wire.InputAction,
                OldPhase = oldPhase,
                NewPhase = newPhase,
                Description = BuildStepDescription(deviceName, eventName, wire.TargetDevice, wire.InputAction, timeDelay),
                IsConditional = false
            };

            result.Steps.Add(step);

            // Recurse — the target device fires its own events
            var nextEvent = DetermineNextEvent(wire.InputAction, targetDevice);
            if (!string.IsNullOrEmpty(nextEvent))
            {
                SimulateEventRecursive(world, wire.TargetDevice, nextEvent, result, visited, ref currentTime, depth + 1);
            }
        }

        // Process verse handlers
        foreach (var handler in verseHandlers)
        {
            var step = new SimulationStep
            {
                StepNumber = result.Steps.Count + 1,
                SimulatedTime = currentTime,
                TriggerDevice = deviceName,
                Event = eventName,
                TargetDevice = handler.DeviceName,
                Action = $"Verse: {handler.HandlerFunction}",
                OldPhase = DevicePhase.Idle,
                NewPhase = DevicePhase.Running,
                Description = $"Verse handler '{handler.HandlerFunction}' called on {handler.DeviceName}",
                VerseHandlersCalled = new List<string> { handler.HandlerFunction },
                IsConditional = handler.HasCondition,
                Condition = handler.Condition
            };

            result.Steps.Add(step);

            // Check if the verse handler calls methods on other devices
            foreach (var call in handler.DeviceCalls)
            {
                if (world.Devices.ContainsKey(call.TargetDevice))
                {
                    SimulateEventRecursive(world, call.TargetDevice, call.MethodName, result, visited, ref currentTime, depth + 1);
                }
            }
        }
    }

    private void TraceBackward(
        SimulationWorld world,
        string targetDevice,
        SimulationResult result,
        HashSet<string> visited,
        ref int stepNumber,
        int depth)
    {
        if (depth >= MaxSimulationDepth || visited.Contains(targetDevice))
            return;

        visited.Add(targetDevice);

        // Find all wires that lead TO this device
        var incomingWires = world.Wiring.Where(w => w.TargetDevice == targetDevice).ToList();

        foreach (var wire in incomingWires)
        {
            stepNumber++;
            var sourceDevice = world.Devices.GetValueOrDefault(wire.SourceDevice);

            result.Steps.Add(new SimulationStep
            {
                StepNumber = stepNumber,
                SimulatedTime = 0,
                TriggerDevice = wire.SourceDevice,
                Event = wire.OutputEvent,
                TargetDevice = targetDevice,
                Action = wire.InputAction,
                OldPhase = DevicePhase.Idle,
                NewPhase = DevicePhase.Active,
                Description = $"{wire.SourceDevice}.{wire.OutputEvent} -> {targetDevice}.{wire.InputAction}"
            });

            // Continue tracing backward
            TraceBackward(world, wire.SourceDevice, result, visited, ref stepNumber, depth + 1);
        }
    }

    private List<(string DeviceName, string EventName)> FindGameStartDevices(SimulationWorld world)
    {
        var startDevices = new List<(string, string)>();

        foreach (var kvp in world.Devices)
        {
            var device = kvp.Value;
            var name = kvp.Key;

            // Devices with OnBegin or auto-start patterns
            if (device.AvailableEvents.Any(e => e.Contains("OnBegin", StringComparison.OrdinalIgnoreCase)))
            {
                startDevices.Add((name, "OnBegin"));
            }
            else if (device.AvailableEvents.Any(e => e.Contains("GameStart", StringComparison.OrdinalIgnoreCase)))
            {
                startDevices.Add((name, "GameStart"));
            }
            // Timer devices that auto-start
            else if (IsTimerDevice(device) &&
                     device.Properties.TryGetValue("AutoStart", out var autoStart) &&
                     autoStart.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                startDevices.Add((name, "OnCompleted"));
            }
            // Verse devices typically run OnBegin
            else if (device.DeviceClass.Contains("Verse", StringComparison.OrdinalIgnoreCase) ||
                     device.DeviceClass.Contains("creative_device", StringComparison.OrdinalIgnoreCase))
            {
                startDevices.Add((name, "OnBegin"));
            }
        }

        // If still nothing, use devices that have outgoing wires but no incoming wires (roots)
        if (startDevices.Count == 0)
        {
            var incomingTargets = world.Wiring.Select(w => w.TargetDevice).ToHashSet();
            foreach (var kvp in world.Devices)
            {
                if (!incomingTargets.Contains(kvp.Key) &&
                    world.Wiring.Any(w => w.SourceDevice == kvp.Key))
                {
                    var firstEvent = kvp.Value.AvailableEvents.FirstOrDefault() ?? "OnActivated";
                    startDevices.Add((kvp.Key, firstEvent));
                }
            }
        }

        return startDevices;
    }

    // ─── Helper Methods ────────────────────────────────────────────────────────

    private void AddWellKnownEventsAndActions(SimulatedDevice device)
    {
        var type = device.DeviceType.ToLowerInvariant();
        var cls = device.DeviceClass.ToLowerInvariant();

        // Timer devices
        if (type.Contains("timer") || cls.Contains("timer"))
        {
            AddIfMissing(device.AvailableEvents, "OnCompleted");
            AddIfMissing(device.AvailableEvents, "OnReset");
            AddIfMissing(device.AvailableActions, "Start");
            AddIfMissing(device.AvailableActions, "Pause");
            AddIfMissing(device.AvailableActions, "Reset");
        }

        // Trigger devices
        if (type.Contains("trigger") || cls.Contains("trigger"))
        {
            AddIfMissing(device.AvailableEvents, "OnTriggered");
            AddIfMissing(device.AvailableEvents, "OnUntriggered");
            AddIfMissing(device.AvailableActions, "Enable");
            AddIfMissing(device.AvailableActions, "Disable");
        }

        // Button devices
        if (type.Contains("button") || cls.Contains("button"))
        {
            AddIfMissing(device.AvailableEvents, "OnPressed");
            AddIfMissing(device.AvailableActions, "Enable");
            AddIfMissing(device.AvailableActions, "Disable");
        }

        // Spawner devices
        if (type.Contains("spawner") || cls.Contains("spawner") || type.Contains("spawn"))
        {
            AddIfMissing(device.AvailableEvents, "OnSpawned");
            AddIfMissing(device.AvailableActions, "Enable");
            AddIfMissing(device.AvailableActions, "Disable");
            AddIfMissing(device.AvailableActions, "Spawn");
        }

        // Barrier devices
        if (type.Contains("barrier") || cls.Contains("barrier"))
        {
            AddIfMissing(device.AvailableEvents, "OnEnabled");
            AddIfMissing(device.AvailableEvents, "OnDisabled");
            AddIfMissing(device.AvailableActions, "Enable");
            AddIfMissing(device.AvailableActions, "Disable");
        }

        // Score/elimination devices
        if (type.Contains("score") || cls.Contains("score") || type.Contains("elimination"))
        {
            AddIfMissing(device.AvailableEvents, "OnScoreReached");
            AddIfMissing(device.AvailableEvents, "OnEliminated");
            AddIfMissing(device.AvailableActions, "Activate");
            AddIfMissing(device.AvailableActions, "SetScore");
        }

        // EndGame devices
        if (type.Contains("endgame") || cls.Contains("endgame") || type.Contains("end game"))
        {
            AddIfMissing(device.AvailableActions, "Activate");
            AddIfMissing(device.AvailableActions, "EndGame");
        }

        // Verse/creative devices always have OnBegin
        if (cls.Contains("verse") || cls.Contains("creative_device"))
        {
            AddIfMissing(device.AvailableEvents, "OnBegin");
        }

        // Generic actions for all devices
        AddIfMissing(device.AvailableActions, "Enable");
        AddIfMissing(device.AvailableActions, "Disable");
    }

    private static void AddIfMissing(List<string> list, string item)
    {
        if (!list.Contains(item, StringComparer.OrdinalIgnoreCase))
            list.Add(item);
    }

    private void ParseVerseHandlers(SimulationWorld world)
    {
        // Find verse files in the project
        var pluginsDir = Path.Combine(_config.ProjectPath, "Plugins");
        if (!Directory.Exists(pluginsDir)) return;

        var verseFiles = Directory.EnumerateFiles(pluginsDir, "*.verse", SearchOption.AllDirectories).ToList();

        foreach (var verseFile in verseFiles)
        {
            try
            {
                var content = File.ReadAllText(verseFile);
                ParseVerseFileForHandlers(world, content, Path.GetFileName(verseFile));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read verse file: {File}", verseFile);
            }
        }
    }

    private void ParseVerseFileForHandlers(SimulationWorld world, string content, string fileName)
    {
        // Look for event subscriptions: DeviceName.EventName.Subscribe(HandlerFunction)
        var subscribePattern = new Regex(
            @"(\w+)\s*\.\s*(\w+)\s*\.\s*Subscribe\s*\(\s*(\w+)\s*\)",
            RegexOptions.Multiline);

        foreach (Match match in subscribePattern.Matches(content))
        {
            var deviceRef = match.Groups[1].Value;
            var eventName = match.Groups[2].Value;
            var handlerName = match.Groups[3].Value;

            var handler = new VerseHandler
            {
                DeviceName = deviceRef,
                EventName = eventName,
                HandlerFunction = handlerName,
                SourceFile = fileName
            };

            // Look for the handler function body to find device calls
            var funcPattern = new Regex(
                $@"{Regex.Escape(handlerName)}\s*\([^)]*\)\s*:\s*void\s*=\s*\{{([^}}]*)\}}",
                RegexOptions.Singleline);

            var funcMatch = funcPattern.Match(content);
            if (funcMatch.Success)
            {
                var body = funcMatch.Groups[1].Value;

                // Find device method calls: SomeDevice.SomeMethod()
                var callPattern = new Regex(@"(\w+)\s*\.\s*(\w+)\s*\(");
                foreach (Match callMatch in callPattern.Matches(body))
                {
                    handler.DeviceCalls.Add(new VerseDeviceCall
                    {
                        TargetDevice = callMatch.Groups[1].Value,
                        MethodName = callMatch.Groups[2].Value
                    });
                }

                // Check for conditionals
                if (body.Contains("if ") || body.Contains("if("))
                {
                    handler.HasCondition = true;
                    var condPattern = new Regex(@"if\s*[\(]?\s*(.+?)\s*[\)]?\s*:");
                    var condMatch = condPattern.Match(body);
                    if (condMatch.Success)
                        handler.Condition = condMatch.Groups[1].Value.Trim();
                }
            }

            world.VerseHandlers.Add(handler);
        }

        // Also look for Awaits: DeviceName.EventName.Await()
        var awaitPattern = new Regex(
            @"(\w+)\s*\.\s*(\w+)\s*\.\s*Await\s*\(\s*\)",
            RegexOptions.Multiline);

        foreach (Match match in awaitPattern.Matches(content))
        {
            world.VerseHandlers.Add(new VerseHandler
            {
                DeviceName = match.Groups[1].Value,
                EventName = match.Groups[2].Value,
                HandlerFunction = "Await",
                SourceFile = fileName
            });
        }
    }

    private static bool IsTimerDevice(SimulatedDevice device)
    {
        return device.DeviceType.Contains("Timer", StringComparison.OrdinalIgnoreCase) ||
               device.DeviceClass.Contains("Timer", StringComparison.OrdinalIgnoreCase);
    }

    private static DevicePhase DetermineNewPhase(string? action, DevicePhase currentPhase)
    {
        if (string.IsNullOrEmpty(action)) return DevicePhase.Active;

        var lower = action.ToLowerInvariant();
        if (lower.Contains("enable") || lower.Contains("activate") || lower.Contains("start"))
            return DevicePhase.Active;
        if (lower.Contains("disable") || lower.Contains("deactivate"))
            return DevicePhase.Disabled;
        if (lower.Contains("trigger") || lower.Contains("fire"))
            return DevicePhase.Triggered;
        if (lower.Contains("complete") || lower.Contains("finish") || lower.Contains("end"))
            return DevicePhase.Completed;
        if (lower.Contains("reset") || lower.Contains("restart"))
            return DevicePhase.Idle;
        if (lower.Contains("cooldown") || lower.Contains("pause"))
            return DevicePhase.Cooldown;

        return DevicePhase.Running;
    }

    private static string? DetermineNextEvent(string? action, SimulatedDevice? device)
    {
        if (string.IsNullOrEmpty(action)) return null;

        var lower = action.ToLowerInvariant();

        // When a device is activated, it fires its primary event
        if (lower.Contains("enable") || lower.Contains("activate") || lower.Contains("start"))
        {
            if (device != null)
            {
                // Use the first available event
                return device.AvailableEvents.FirstOrDefault();
            }
            return "OnActivated";
        }

        if (lower.Contains("trigger"))
            return "OnTriggered";
        if (lower.Contains("complete"))
            return "OnCompleted";
        if (lower.Contains("spawn"))
            return "OnSpawned";

        return null;
    }

    private static string BuildStepDescription(string source, string eventName, string target, string? action, float timeDelay)
    {
        var desc = $"{source}.{eventName} -> {target}";
        if (!string.IsNullOrEmpty(action))
            desc += $".{action}";
        if (timeDelay > 0)
            desc += $" (after {timeDelay:F1}s delay)";
        return desc;
    }

    private static bool ContainsAnyKeyword(string text, HashSet<string> keywords)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}

// ─── Simulation Models ─────────────────────────────────────────────────────

public enum DevicePhase { Idle, Active, Running, Completed, Disabled, Triggered, Cooldown }

public class SimulatedDevice
{
    public string ActorName { get; set; } = "";
    public string DeviceClass { get; set; } = "";
    public string DeviceType { get; set; } = "";
    public DevicePhase Phase { get; set; } = DevicePhase.Idle;
    public bool IsVerseDevice { get; set; }
    public Dictionary<string, string> Properties { get; set; } = new();
    public List<string> AvailableEvents { get; set; } = new();
    public List<string> AvailableActions { get; set; } = new();
}

public class SimulationStep
{
    public int StepNumber { get; set; }
    public float SimulatedTime { get; set; }
    public string TriggerDevice { get; set; } = "";
    public string Event { get; set; } = "";
    public string TargetDevice { get; set; } = "";
    public string Action { get; set; } = "";
    public DevicePhase OldPhase { get; set; }
    public DevicePhase NewPhase { get; set; }
    public string Description { get; set; } = "";
    public List<string> VerseHandlersCalled { get; set; } = new();
    public bool IsConditional { get; set; }
    public string? Condition { get; set; }
}

public class SimulationResult
{
    public string InitialTrigger { get; set; } = "";
    public List<SimulationStep> Steps { get; set; } = new();
    public Dictionary<string, DevicePhase> FinalStates { get; set; } = new();
    public float TotalSimulatedTime { get; set; }
    public List<string> Warnings { get; set; } = new();
    public bool ReachesEndGame { get; set; }
    public DFAModel? DFA { get; set; }
}

public class SimulationWorld
{
    public string LevelPath { get; set; } = "";
    public Dictionary<string, SimulatedDevice> Devices { get; set; } = new();
    public List<SimulationWire> Wiring { get; set; } = new();
    public List<VerseHandler> VerseHandlers { get; set; } = new();
}

public class SimulationWire
{
    public string SourceDevice { get; set; } = "";
    public string OutputEvent { get; set; } = "";
    public string TargetDevice { get; set; } = "";
    public string InputAction { get; set; } = "";
    public string? Channel { get; set; }
}

public class VerseHandler
{
    public string DeviceName { get; set; } = "";
    public string EventName { get; set; } = "";
    public string HandlerFunction { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public bool HasCondition { get; set; }
    public string? Condition { get; set; }
    public List<VerseDeviceCall> DeviceCalls { get; set; } = new();
}

public class VerseDeviceCall
{
    public string TargetDevice { get; set; } = "";
    public string MethodName { get; set; } = "";
}

/// <summary>
/// DFA (Deterministic Finite Automaton) model of the device network.
/// States = device phases. Transitions = event→action wiring.
/// No game design opinions — pure graph theory.
/// </summary>
public class DFAModel
{
    /// <summary>All device states (one per device).</summary>
    public List<DFANode> Nodes { get; set; } = new();

    /// <summary>All transitions (event→action edges).</summary>
    public List<DFAEdge> Edges { get; set; } = new();

    /// <summary>Hash of all device phases — identifies the global state.</summary>
    public string StateHash { get; set; } = "";

    /// <summary>Snapshot of the global state at each simulation step.</summary>
    public List<DFASnapshot> History { get; set; } = new();
}

public class DFANode
{
    public string DeviceName { get; set; } = "";
    public string DeviceClass { get; set; } = "";
    public string DeviceType { get; set; } = "";
    public DevicePhase CurrentPhase { get; set; } = DevicePhase.Idle;
    public List<string> AvailableEvents { get; set; } = new();
    public List<string> AvailableActions { get; set; } = new();
    public float X { get; set; } // layout position
    public float Y { get; set; } // layout position
}

public class DFAEdge
{
    public string SourceDevice { get; set; } = "";
    public string Event { get; set; } = "";
    public string TargetDevice { get; set; } = "";
    public string Action { get; set; } = "";
    public DevicePhase ResultingPhase { get; set; } = DevicePhase.Active;
    public bool IsConditional { get; set; }
    public string? Condition { get; set; }
}

/// <summary>
/// A snapshot of the entire DFA at one point in the simulation.
/// Allows stepping forward/back through the state history.
/// </summary>
public class DFASnapshot
{
    public int StepNumber { get; set; }
    public float SimulatedTime { get; set; }
    public string FiredEdgeSource { get; set; } = "";
    public string FiredEdgeEvent { get; set; } = "";
    public Dictionary<string, DevicePhase> DevicePhases { get; set; } = new();
    public string StateHash { get; set; } = "";
}

public class DeadEndState
{
    public string DeviceName { get; set; } = "";
    public string DeviceType { get; set; } = "";
    public DevicePhase Phase { get; set; }
    public string Reason { get; set; } = "";
}
