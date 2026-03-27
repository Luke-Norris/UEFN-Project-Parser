using WellVersed.Core.Config;
using WellVersed.Core.Models;
using Microsoft.Extensions.Logging;

namespace WellVersed.Core.Services;

/// <summary>
/// Static analysis of device wiring graphs + verse code to find bugs WITHOUT playtesting.
/// Builds a complete graph of all devices and their connections, then runs analysis passes
/// to find dead devices, orphaned outputs, missing win conditions, timing conflicts, loops, etc.
/// </summary>
public class LogicTracer
{
    private readonly WellVersedConfig _config;
    private readonly DeviceService _deviceService;
    private readonly ILogger<LogicTracer> _logger;

    // Patterns for identifying output events on devices
    private static readonly string[] OutputEventPatterns =
    {
        "OnTriggered", "OnSuccess", "OnComplete", "OnActivated", "OnDeactivated",
        "OnEnabled", "OnDisabled", "OnBegin", "OnEnd", "OnFinished",
        "OnPlayerEnter", "OnPlayerExit", "OnDamage", "OnElimination",
        "OnScoreReached", "OnTimerComplete", "OnTimerExpired", "OnRoundEnd",
        "OnRoundStart", "OnGameStart", "OnGameEnd", "OnItemPickup",
        "OnItemDrop", "OnPhaseChange", "OnButtonPressed", "OnInteract",
        "OnSpawned", "OnDespawned", "OnDestroyed", "OnReset",
        "Trigger", "OnFired"
    };

    // Patterns for identifying input actions on devices
    private static readonly string[] InputActionPatterns =
    {
        "Enable", "Disable", "Activate", "Deactivate", "Trigger",
        "Start", "Stop", "Reset", "Show", "Hide",
        "Open", "Close", "Lock", "Unlock", "Toggle",
        "Spawn", "Despawn", "Destroy", "Teleport", "Grant",
        "SetScore", "AddScore", "EndGame", "EndRound",
        "WhenReceived", "OnReceivedFrom"
    };

    // Device classes that indicate a win/end condition
    private static readonly string[] EndGameDevicePatterns =
    {
        "EndGame", "end_game", "Victory", "victory", "GameEnd", "game_end",
        "RoundSettings", "round_settings", "PlayspaceSettings", "playspace_settings",
        "ScoreManager", "score_manager", "EliminationManager", "elimination_manager"
    };

    // Device classes related to team configuration
    private static readonly string[] TeamDevicePatterns =
    {
        "TeamSettings", "team_settings", "ClassSelector", "class_selector",
        "TeamSelect", "team_select", "ClassDesigner", "class_designer"
    };

    // Device classes for spawning
    private static readonly string[] SpawnDevicePatterns =
    {
        "Spawner", "spawner", "SpawnPad", "spawn_pad", "PlayerSpawn", "player_spawn",
        "SpawnPoint", "spawn_point"
    };

    // Device classes for barriers
    private static readonly string[] BarrierDevicePatterns =
    {
        "Barrier", "barrier", "Wall", "wall", "Gate", "gate"
    };

    // Device classes for timers
    private static readonly string[] TimerDevicePatterns =
    {
        "Timer", "timer", "Countdown", "countdown", "Clock", "clock"
    };

    public LogicTracer(
        WellVersedConfig config,
        DeviceService deviceService,
        ILogger<LogicTracer> logger)
    {
        _config = config;
        _deviceService = deviceService;
        _logger = logger;
    }

    /// <summary>
    /// Builds a directed graph of all devices and their wiring connections in a level.
    /// </summary>
    public DeviceGraph BuildDeviceGraph(string levelPath)
    {
        var graph = new DeviceGraph();
        var devices = _deviceService.ListDevicesInLevel(levelPath);

        // Also scan external actors for this level
        var contentDir = Path.GetDirectoryName(levelPath);
        var levelName = Path.GetFileNameWithoutExtension(levelPath);
        if (contentDir != null)
        {
            var externalActorsDir = Path.Combine(contentDir, "__ExternalActors__", levelName);
            if (Directory.Exists(externalActorsDir))
            {
                foreach (var extAsset in Directory.EnumerateFiles(externalActorsDir, "*.uasset", SearchOption.AllDirectories))
                {
                    try
                    {
                        var extDevices = _deviceService.ListDevicesInLevel(extAsset);
                        devices.AddRange(extDevices);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not parse external actor: {Path}", extAsset);
                    }
                }
            }
        }

        // Build nodes
        foreach (var device in devices)
        {
            var node = new DeviceNode
            {
                ActorName = device.ActorName,
                DeviceClass = device.DeviceClass,
                DeviceType = device.DeviceType,
                Position = device.Location,
                PropertyCount = device.Properties.Count,
                IsVerseDevice = device.IsVerseDevice
            };

            // Classify output events from properties
            foreach (var prop in device.Properties)
            {
                var propName = prop.Name ?? "";
                if (IsOutputEvent(propName) && !node.OutputEvents.Contains(propName))
                    node.OutputEvents.Add(propName);
                if (IsInputAction(propName) && !node.InputActions.Contains(propName))
                    node.InputActions.Add(propName);
            }

            // Infer standard events/actions from the device class
            AddClassBasedEvents(node);

            graph.Nodes.Add(node);
        }

        // Build edges from wiring data
        foreach (var device in devices)
        {
            foreach (var wiring in device.Wiring)
            {
                var edge = new DeviceEdge
                {
                    SourceActor = device.ActorName,
                    OutputEvent = wiring.OutputEvent,
                    TargetActor = wiring.TargetDevice,
                    InputAction = wiring.InputAction
                };

                graph.Edges.Add(edge);
            }
        }

        graph.TotalDevices = graph.Nodes.Count;
        graph.TotalConnections = graph.Edges.Count;

        return graph;
    }

    /// <summary>
    /// Runs ALL analysis passes on a level and produces a comprehensive QA report.
    /// </summary>
    public QAReport AnalyzeLevel(string levelPath)
    {
        var graph = BuildDeviceGraph(levelPath);

        // Find verse files in the project for cross-referencing
        var verseFiles = FindVerseFiles();

        var issues = new List<QAIssue>();
        issues.AddRange(FindDeadDevices(graph));
        issues.AddRange(FindOrphanedOutputs(graph));
        issues.AddRange(FindMissingWinCondition(graph));
        issues.AddRange(FindTimingConflicts(graph));
        issues.AddRange(FindUnreachableSpawns(graph));
        issues.AddRange(FindInfiniteLoops(graph));
        issues.AddRange(FindMissingTeamConfig(graph));
        issues.AddRange(FindUnusedVerseDevices(graph, verseFiles));
        issues.AddRange(FindPropertyAnomalies(graph));
        issues.AddRange(FindDuplicateWiring(graph));

        var criticalCount = issues.Count(i => i.Severity == QASeverity.Critical);
        var warningCount = issues.Count(i => i.Severity == QASeverity.Warning);
        var infoCount = issues.Count(i => i.Severity == QASeverity.Info);

        var report = new QAReport
        {
            LevelPath = levelPath,
            TotalDevices = graph.TotalDevices,
            TotalConnections = graph.TotalConnections,
            CriticalCount = criticalCount,
            WarningCount = warningCount,
            InfoCount = infoCount,
            PassesQA = criticalCount == 0,
            Issues = issues.OrderBy(i => i.Severity).ThenBy(i => i.Category).ToList(),
            Summary = BuildSummary(graph, criticalCount, warningCount, infoCount)
        };

        return report;
    }

    /// <summary>
    /// Traces the signal flow from a specific device and output event through the wiring graph.
    /// Returns the chain: A.OutputEvent -> B.InputAction -> B.OutputEvent -> C.InputAction -> ...
    /// </summary>
    public List<SignalStep> TraceSignalFlow(DeviceGraph graph, string startDevice, string outputEvent)
    {
        var steps = new List<SignalStep>();
        var visited = new HashSet<string>(); // track visited device+event to prevent infinite loops
        TraceRecursive(graph, startDevice, outputEvent, steps, visited, 0);
        return steps;
    }

    // ─── Analysis Passes ────────────────────────────────────────────────────

    /// <summary>
    /// Devices with no incoming wiring AND no verse references. They exist but nothing triggers them.
    /// </summary>
    public List<QAIssue> FindDeadDevices(DeviceGraph graph)
    {
        var issues = new List<QAIssue>();
        var targetActors = new HashSet<string>(
            graph.Edges.Select(e => e.TargetActor),
            StringComparer.OrdinalIgnoreCase);
        var sourceActors = new HashSet<string>(
            graph.Edges.Select(e => e.SourceActor),
            StringComparer.OrdinalIgnoreCase);

        int counter = 0;
        foreach (var node in graph.Nodes)
        {
            // Skip if this device receives any incoming wiring
            if (targetActors.Contains(node.ActorName))
                continue;

            // Skip if this device sends any outgoing wiring (it's a source/trigger)
            if (sourceActors.Contains(node.ActorName))
                continue;

            // Skip verse devices (they may be self-triggered via OnBegin)
            if (node.IsVerseDevice)
                continue;

            // Skip spawners and spawn-related devices (they auto-activate)
            if (MatchesAnyPattern(node.DeviceClass, SpawnDevicePatterns))
                continue;

            // Skip end-game/round settings devices (they configure the game mode)
            if (MatchesAnyPattern(node.DeviceClass, EndGameDevicePatterns))
                continue;

            // Skip team config devices (they auto-apply)
            if (MatchesAnyPattern(node.DeviceClass, TeamDevicePatterns))
                continue;

            counter++;
            issues.Add(new QAIssue
            {
                Id = $"DEAD_{counter:D3}",
                Severity = QASeverity.Warning,
                Category = "dead_device",
                Title = $"Dead device: {node.DeviceType}",
                Description = $"'{node.ActorName}' ({node.DeviceType}) has no incoming wiring and sends no outgoing signals. " +
                              "Nothing ever triggers or interacts with this device.",
                AffectedDevice = node.ActorName,
                SuggestedFix = "Wire an event to this device, remove it if unneeded, or add verse code to reference it."
            });
        }

        return issues;
    }

    /// <summary>
    /// Output events that fire but nothing receives them. The signal goes nowhere.
    /// </summary>
    public List<QAIssue> FindOrphanedOutputs(DeviceGraph graph)
    {
        var issues = new List<QAIssue>();
        int counter = 0;

        // For each node that has outgoing edges, check if any output events are NOT wired
        var wiredOutputsByNode = graph.Edges
            .GroupBy(e => e.SourceActor, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new HashSet<string>(g.Select(e => e.OutputEvent), StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph.Nodes)
        {
            if (node.OutputEvents.Count == 0)
                continue;

            wiredOutputsByNode.TryGetValue(node.ActorName, out var wiredOutputs);

            foreach (var output in node.OutputEvents)
            {
                if (wiredOutputs != null && wiredOutputs.Contains(output))
                    continue;

                counter++;
                issues.Add(new QAIssue
                {
                    Id = $"ORPH_{counter:D3}",
                    Severity = QASeverity.Info,
                    Category = "orphaned_output",
                    Title = $"Unwired output: {node.DeviceType}.{output}",
                    Description = $"'{node.ActorName}' has output event '{output}' but nothing is wired to receive it.",
                    AffectedDevice = node.ActorName,
                    SuggestedFix = $"Wire '{output}' to another device's input, or verify this event is intentionally unused."
                });
            }
        }

        return issues;
    }

    /// <summary>
    /// No EndGame device, or EndGame exists but is unreachable from any trigger chain.
    /// Traces the wiring graph to verify EndGame is REACHABLE, not just present.
    /// </summary>
    public List<QAIssue> FindMissingWinCondition(DeviceGraph graph)
    {
        var issues = new List<QAIssue>();

        if (graph.TotalDevices == 0)
            return issues;

        // Find all end-game related nodes
        var endGameNodes = graph.Nodes
            .Where(n => MatchesAnyPattern(n.DeviceClass, EndGameDevicePatterns))
            .ToList();

        // Check for EndGame/EndRound actions in wiring
        bool hasEndGameAction = graph.Edges.Any(e =>
            e.InputAction.Contains("EndGame", StringComparison.OrdinalIgnoreCase) ||
            e.InputAction.Contains("EndRound", StringComparison.OrdinalIgnoreCase) ||
            e.InputAction.Contains("end_game", StringComparison.OrdinalIgnoreCase));

        if (endGameNodes.Count == 0 && !hasEndGameAction)
        {
            issues.Add(new QAIssue
            {
                Id = "WIN_001",
                Severity = QASeverity.Critical,
                Category = "missing_win",
                Title = "No win/end condition detected",
                Description = "This level has no End Game device, no score threshold device, and no wiring that triggers " +
                              "EndGame or EndRound. The game may never end naturally.",
                SuggestedFix = "Add an End Game Device or Round Settings Device and wire a victory condition to it."
            });
            return issues;
        }

        // EndGame device(s) exist — verify they are REACHABLE from some trigger chain
        if (endGameNodes.Count > 0)
        {
            // Build reverse adjacency: for each node, which nodes can reach it?
            var reverseAdj = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var edge in graph.Edges)
            {
                if (!reverseAdj.ContainsKey(edge.TargetActor))
                    reverseAdj[edge.TargetActor] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                reverseAdj[edge.TargetActor].Add(edge.SourceActor);
            }

            // BFS backwards from each end-game node to see if any source device can reach it
            foreach (var endNode in endGameNodes)
            {
                var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var queue = new Queue<string>();
                queue.Enqueue(endNode.ActorName);
                reachable.Add(endNode.ActorName);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (reverseAdj.TryGetValue(current, out var predecessors))
                    {
                        foreach (var pred in predecessors)
                        {
                            if (reachable.Add(pred))
                                queue.Enqueue(pred);
                        }
                    }
                }

                // If only the end-game node itself is reachable (no predecessors), it's isolated
                bool hasIncomingWiring = reachable.Count > 1;
                bool isVerseDevice = endNode.IsVerseDevice;

                if (!hasIncomingWiring && !isVerseDevice)
                {
                    issues.Add(new QAIssue
                    {
                        Id = "WIN_002",
                        Severity = QASeverity.Critical,
                        Category = "missing_win",
                        Title = $"Unreachable end-game device: '{endNode.ActorName}'",
                        Description = $"End-game device '{endNode.ActorName}' ({endNode.DeviceType}) exists but no trigger chain " +
                                      "can reach it through wiring. The game cannot end through this device.",
                        AffectedDevice = endNode.ActorName,
                        SuggestedFix = "Wire a trigger chain (score threshold, timer, or elimination count) to this device's input."
                    });
                }
            }
        }

        return issues;
    }

    /// <summary>
    /// Detects timing conflicts: two timers that both enable/disable the same target device
    /// (durations could overlap), and timers triggered by the same source that target the
    /// same device with conflicting actions.
    /// </summary>
    public List<QAIssue> FindTimingConflicts(DeviceGraph graph)
    {
        var issues = new List<QAIssue>();
        int counter = 0;
        var reportedPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Find all timer devices
        var timerNodes = graph.Nodes
            .Where(n => MatchesAnyPattern(n.DeviceClass, TimerDevicePatterns))
            .ToList();

        var timerActorNames = new HashSet<string>(
            timerNodes.Select(t => t.ActorName),
            StringComparer.OrdinalIgnoreCase);

        // Classify all edges by action type
        var enableEdges = graph.Edges
            .Where(e => e.InputAction.Contains("Enable", StringComparison.OrdinalIgnoreCase) ||
                        e.InputAction.Contains("Activate", StringComparison.OrdinalIgnoreCase) ||
                        e.InputAction.Contains("Start", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var disableEdges = graph.Edges
            .Where(e => e.InputAction.Contains("Disable", StringComparison.OrdinalIgnoreCase) ||
                        e.InputAction.Contains("Deactivate", StringComparison.OrdinalIgnoreCase) ||
                        e.InputAction.Contains("Stop", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Pass A: Two different timers target the same device with enable vs disable
        foreach (var enable in enableEdges)
        {
            if (!timerActorNames.Contains(enable.SourceActor))
                continue;

            foreach (var disable in disableEdges)
            {
                if (!enable.TargetActor.Equals(disable.TargetActor, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!timerActorNames.Contains(disable.SourceActor))
                    continue;

                if (enable.SourceActor.Equals(disable.SourceActor, StringComparison.OrdinalIgnoreCase))
                    continue;

                var pairKey = string.Compare(enable.SourceActor, disable.SourceActor, StringComparison.OrdinalIgnoreCase) < 0
                    ? $"{enable.SourceActor}|{disable.SourceActor}|{enable.TargetActor}"
                    : $"{disable.SourceActor}|{enable.SourceActor}|{enable.TargetActor}";

                if (!reportedPairs.Add(pairKey))
                    continue;

                counter++;
                issues.Add(new QAIssue
                {
                    Id = $"TIME_{counter:D3}",
                    Severity = QASeverity.Warning,
                    Category = "timing",
                    Title = $"Timer conflict: enable/disable race on '{enable.TargetActor}'",
                    Description = $"Timer '{enable.SourceActor}' enables '{enable.TargetActor}' " +
                                  $"while timer '{disable.SourceActor}' disables it. " +
                                  "If both timers fire simultaneously or their durations overlap, " +
                                  "the device state becomes unpredictable.",
                    AffectedDevice = enable.TargetActor,
                    SuggestedFix = "Ensure timer durations don't overlap, or sequence them so one completes before the other starts."
                });
            }
        }

        // Pass B: Two timers triggered by the SAME source event target the same device
        // with the same action type (double-fire)
        if (timerNodes.Count >= 2)
        {
            // Find edges where something triggers a timer
            var timerStartEdges = graph.Edges
                .Where(e => timerActorNames.Contains(e.TargetActor) &&
                            (e.InputAction.Contains("Start", StringComparison.OrdinalIgnoreCase) ||
                             e.InputAction.Contains("Trigger", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // Group by source — if two timers are started by the same source
            var timersBySource = timerStartEdges
                .GroupBy(e => e.SourceActor, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() >= 2);

            foreach (var group in timersBySource)
            {
                var triggeredTimers = group.Select(e => e.TargetActor).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (triggeredTimers.Count < 2) continue;

                // For each pair of timers started by the same source, check if they target the same device
                for (int i = 0; i < triggeredTimers.Count; i++)
                {
                    var timerATargets = graph.Edges
                        .Where(e => e.SourceActor.Equals(triggeredTimers[i], StringComparison.OrdinalIgnoreCase))
                        .Select(e => e.TargetActor)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    for (int j = i + 1; j < triggeredTimers.Count; j++)
                    {
                        var timerBTargets = graph.Edges
                            .Where(e => e.SourceActor.Equals(triggeredTimers[j], StringComparison.OrdinalIgnoreCase))
                            .Select(e => e.TargetActor);

                        foreach (var sharedTarget in timerBTargets.Where(t => timerATargets.Contains(t)))
                        {
                            var pairKey = $"{triggeredTimers[i]}|{triggeredTimers[j]}|{sharedTarget}|concurrent";
                            if (!reportedPairs.Add(pairKey)) continue;

                            counter++;
                            issues.Add(new QAIssue
                            {
                                Id = $"TIME_{counter:D3}",
                                Severity = QASeverity.Warning,
                                Category = "timing",
                                Title = $"Concurrent timers targeting '{sharedTarget}'",
                                Description = $"Timers '{triggeredTimers[i]}' and '{triggeredTimers[j]}' are both started by " +
                                              $"'{group.Key}' and both target '{sharedTarget}'. " +
                                              "Their completion events will overlap, causing unpredictable behavior on the target.",
                                AffectedDevice = sharedTarget,
                                SuggestedFix = "Chain the timers sequentially (Timer A complete -> start Timer B) instead of starting both from the same source."
                            });
                        }
                    }
                }
            }
        }

        return issues;
    }

    /// <summary>
    /// Spawn points with barriers that never disable, or spawns outside the playable area.
    /// </summary>
    public List<QAIssue> FindUnreachableSpawns(DeviceGraph graph)
    {
        var issues = new List<QAIssue>();
        int counter = 0;

        var spawnNodes = graph.Nodes
            .Where(n => MatchesAnyPattern(n.DeviceClass, SpawnDevicePatterns))
            .ToList();

        var barrierNodes = graph.Nodes
            .Where(n => MatchesAnyPattern(n.DeviceClass, BarrierDevicePatterns))
            .ToList();

        if (spawnNodes.Count == 0)
        {
            if (graph.TotalDevices > 0)
            {
                issues.Add(new QAIssue
                {
                    Id = "SPAWN_001",
                    Severity = QASeverity.Critical,
                    Category = "spawn",
                    Title = "No spawn points found",
                    Description = "This level has devices but no spawn points. Players cannot enter the game.",
                    SuggestedFix = "Add at least one Player Spawn Pad device."
                });
            }
            return issues;
        }

        // Check for barriers near spawn points that never get disabled
        var barriersWithDisable = new HashSet<string>(
            graph.Edges
                .Where(e => e.InputAction.Contains("Disable", StringComparison.OrdinalIgnoreCase) ||
                            e.InputAction.Contains("Hide", StringComparison.OrdinalIgnoreCase) ||
                            e.InputAction.Contains("Destroy", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.TargetActor),
            StringComparer.OrdinalIgnoreCase);

        foreach (var barrier in barrierNodes)
        {
            if (barriersWithDisable.Contains(barrier.ActorName))
                continue;

            // Check proximity to spawn points (within 2000 units ≈ 20 meters)
            foreach (var spawn in spawnNodes)
            {
                var dist = Distance(spawn.Position, barrier.Position);
                if (dist < 2000f)
                {
                    counter++;
                    issues.Add(new QAIssue
                    {
                        Id = $"SPAWN_{counter + 1:D3}",
                        Severity = QASeverity.Warning,
                        Category = "spawn",
                        Title = $"Permanent barrier near spawn '{spawn.ActorName}'",
                        Description = $"Barrier '{barrier.ActorName}' is {dist:N0} units from spawn '{spawn.ActorName}' " +
                                      "and has no wiring to disable it. Players may spawn blocked.",
                        AffectedDevice = spawn.ActorName,
                        SuggestedFix = "Wire a disable action to the barrier, or move the spawn point."
                    });
                }
            }
        }

        // Check for spawns that are completely isolated — no wiring at all and no verse device nearby
        var allWiredActors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in graph.Edges)
        {
            allWiredActors.Add(edge.SourceActor);
            allWiredActors.Add(edge.TargetActor);
        }

        var verseNodes = graph.Nodes.Where(n => n.IsVerseDevice).ToList();

        foreach (var spawn in spawnNodes)
        {
            bool hasAnyWiring = allWiredActors.Contains(spawn.ActorName);
            bool hasVerseDeviceNearby = verseNodes.Any(v => Distance(spawn.Position, v.Position) < 5000f);

            if (!hasAnyWiring && !hasVerseDeviceNearby)
            {
                counter++;
                issues.Add(new QAIssue
                {
                    Id = $"SPAWN_{counter + 1:D3}",
                    Severity = QASeverity.Warning,
                    Category = "spawn",
                    Title = $"Unwired spawn with no verse device nearby: '{spawn.ActorName}'",
                    Description = $"Spawn '{spawn.ActorName}' has no wiring connections and no verse device within 5000 units. " +
                                  "It will use default behavior with no game logic controlling it.",
                    AffectedDevice = spawn.ActorName,
                    SuggestedFix = "Wire this spawn to a round settings or game manager device, or place a verse device to manage it."
                });
            }
        }

        // Check for spawns that are extremely far from all other devices (possible outlier)
        if (spawnNodes.Count > 0 && graph.Nodes.Count > 2)
        {
            var nonSpawnNodes = graph.Nodes.Where(n => !MatchesAnyPattern(n.DeviceClass, SpawnDevicePatterns)).ToList();
            if (nonSpawnNodes.Count > 0)
            {
                foreach (var spawn in spawnNodes)
                {
                    var minDist = nonSpawnNodes.Min(n => Distance(spawn.Position, n.Position));
                    if (minDist > 50000f) // 500 meters — very far from all other devices
                    {
                        counter++;
                        issues.Add(new QAIssue
                        {
                            Id = $"SPAWN_{counter + 1:D3}",
                            Severity = QASeverity.Warning,
                            Category = "spawn",
                            Title = $"Isolated spawn point: '{spawn.ActorName}'",
                            Description = $"Spawn '{spawn.ActorName}' is {minDist:N0} units from the nearest device. " +
                                          "It may be outside the intended play area.",
                            AffectedDevice = spawn.ActorName,
                            SuggestedFix = "Verify the spawn position or move it closer to the gameplay area."
                        });
                    }
                }
            }
        }

        return issues;
    }

    /// <summary>
    /// Detect cycles in the wiring graph. Device A triggers B, B triggers C, C triggers A.
    /// </summary>
    public List<QAIssue> FindInfiniteLoops(DeviceGraph graph)
    {
        var issues = new List<QAIssue>();

        // Build adjacency list
        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in graph.Edges)
        {
            if (!adjacency.ContainsKey(edge.SourceActor))
                adjacency[edge.SourceActor] = new List<string>();
            adjacency[edge.SourceActor].Add(edge.TargetActor);
        }

        // DFS cycle detection
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cycles = new List<List<string>>();
        var path = new List<string>();

        foreach (var node in graph.Nodes)
        {
            if (!visited.Contains(node.ActorName))
            {
                FindCyclesDFS(node.ActorName, adjacency, visited, onStack, path, cycles);
            }
        }

        int counter = 0;
        foreach (var cycle in cycles)
        {
            counter++;
            var chain = string.Join(" -> ", cycle);
            issues.Add(new QAIssue
            {
                Id = $"LOOP_{counter:D3}",
                Severity = QASeverity.Critical,
                Category = "loop",
                Title = $"Infinite loop detected ({cycle.Count} devices)",
                Description = $"Signal cycle: {chain}. This chain will fire endlessly, " +
                              "potentially crashing the game or causing unintended behavior.",
                AffectedDevice = cycle.First(),
                SuggestedFix = "Break the cycle by removing one connection, or add a conditional gate/counter to limit iterations."
            });
        }

        return issues;
    }

    /// <summary>
    /// Multiple team spawners but no team assignment mechanism. Checks for team settings device,
    /// team-indexed spawners (naming convention), or verse devices that handle team logic.
    /// </summary>
    public List<QAIssue> FindMissingTeamConfig(DeviceGraph graph)
    {
        var issues = new List<QAIssue>();

        var spawnNodes = graph.Nodes
            .Where(n => MatchesAnyPattern(n.DeviceClass, SpawnDevicePatterns))
            .ToList();

        if (spawnNodes.Count < 2)
            return issues;

        var hasTeamDevice = graph.Nodes.Any(n => MatchesAnyPattern(n.DeviceClass, TeamDevicePatterns));

        // Check for team-indexed spawners by naming convention (e.g., "Spawner_TeamA", "Spawn_Team1")
        var teamNamedSpawners = spawnNodes
            .Where(s => s.ActorName.Contains("Team", StringComparison.OrdinalIgnoreCase) ||
                        s.ActorName.Contains("team_", StringComparison.OrdinalIgnoreCase) ||
                        s.ActorName.Contains("_a_", StringComparison.OrdinalIgnoreCase) ||
                        s.ActorName.Contains("_b_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        bool hasTeamIndexedSpawners = teamNamedSpawners.Count >= 2;

        // Check for verse devices that might handle team assignment
        var hasVerseTeamHandler = graph.Nodes.Any(n =>
            n.IsVerseDevice &&
            (n.ActorName.Contains("Team", StringComparison.OrdinalIgnoreCase) ||
             n.DeviceClass.Contains("Team", StringComparison.OrdinalIgnoreCase)));

        // Check for team-related wiring or properties on any device
        var hasTeamProperties = graph.Nodes.Any(n =>
            n.DeviceClass.Contains("Team", StringComparison.OrdinalIgnoreCase));
        var hasTeamWiring = graph.Edges.Any(e =>
            e.InputAction.Contains("Team", StringComparison.OrdinalIgnoreCase) ||
            e.OutputEvent.Contains("Team", StringComparison.OrdinalIgnoreCase));

        // Signals that the level is intended to be team-based
        bool looksTeamBased = hasTeamIndexedSpawners || hasTeamProperties || hasTeamWiring;

        if (looksTeamBased && !hasTeamDevice && !hasVerseTeamHandler)
        {
            issues.Add(new QAIssue
            {
                Id = "TEAM_001",
                Severity = QASeverity.Warning,
                Category = "team",
                Title = "Team-based setup but no team configuration device",
                Description = $"Found {spawnNodes.Count} spawn devices " +
                              (hasTeamIndexedSpawners ? $"({teamNamedSpawners.Count} with team naming) " : "") +
                              "and team-related elements, but no Team Settings, Class Selector device, or verse device " +
                              "handling team assignment. Teams may not be properly configured.",
                SuggestedFix = "Add a Team Settings Device or Class Selector Device to configure team assignments, " +
                               "or create a verse device that assigns teams on player spawn."
            });
        }

        // Check for score devices without team separation in a team-based level
        var scoreNodes = graph.Nodes
            .Where(n => n.DeviceClass.Contains("Score", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (scoreNodes.Count > 0 && looksTeamBased && !hasTeamDevice && !hasVerseTeamHandler)
        {
            issues.Add(new QAIssue
            {
                Id = "TEAM_002",
                Severity = QASeverity.Info,
                Category = "team",
                Title = "Score tracking without team configuration",
                Description = $"Found {scoreNodes.Count} score device(s) in a level that appears team-based, " +
                              "but no explicit team configuration. Scores may not be tracked per-team correctly.",
                SuggestedFix = "Add a Team Settings Device to ensure score tracking is team-aware."
            });
        }

        // Check for multiple spawners that are spatially grouped (suggesting teams) but without team setup
        if (!looksTeamBased && spawnNodes.Count >= 4 && !hasTeamDevice)
        {
            // Cluster spawns by proximity — if they form 2+ distinct groups, teams may be intended
            var clusters = ClusterSpawns(spawnNodes, 3000f);
            if (clusters >= 2)
            {
                issues.Add(new QAIssue
                {
                    Id = "TEAM_003",
                    Severity = QASeverity.Info,
                    Category = "team",
                    Title = $"Spawn points form {clusters} spatial clusters — teams intended?",
                    Description = $"The {spawnNodes.Count} spawn points form {clusters} distinct spatial clusters, " +
                                  "which often indicates team-based gameplay, but no team configuration was found.",
                    SuggestedFix = "If this is team-based, add a Team Settings Device. If FFA, this is fine to ignore."
                });
            }
        }

        return issues;
    }

    /// <summary>
    /// Simple single-linkage clustering: counts how many groups of spawns are separated
    /// by more than the threshold distance.
    /// </summary>
    private static int ClusterSpawns(List<DeviceNode> spawns, float threshold)
    {
        if (spawns.Count == 0) return 0;
        var assigned = new bool[spawns.Count];
        int clusterCount = 0;

        for (int i = 0; i < spawns.Count; i++)
        {
            if (assigned[i]) continue;
            clusterCount++;
            assigned[i] = true;
            var queue = new Queue<int>();
            queue.Enqueue(i);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                for (int j = 0; j < spawns.Count; j++)
                {
                    if (assigned[j]) continue;
                    if (Distance(spawns[current].Position, spawns[j].Position) <= threshold)
                    {
                        assigned[j] = true;
                        queue.Enqueue(j);
                    }
                }
            }
        }

        return clusterCount;
    }

    /// <summary>
    /// Verse device classes that are defined in .verse files but never placed in the level.
    /// Scans Content/ and Plugins/ directories for creative_device class definitions and
    /// compares against placed actors.
    /// </summary>
    public List<QAIssue> FindUnusedVerseDevices(DeviceGraph graph, List<string> verseFiles)
    {
        var issues = new List<QAIssue>();
        int counter = 0;

        // Extract class names from verse files — track which file defines each class
        var verseClassSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in verseFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                // Match Verse class definitions: "my_device := class(creative_device):"
                // Also match subclasses of creative_device subclasses
                var matches = System.Text.RegularExpressions.Regex.Matches(
                    content,
                    @"(\w+)\s*:=\s*class\s*\(\s*creative_device\s*\)");

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    verseClassSources[match.Groups[1].Value] = file;
                }

                // Also check for classes extending other known verse device classes
                // e.g., my_game := class(some_base_device):
                foreach (var knownClass in verseClassSources.Keys.ToList())
                {
                    var subMatches = System.Text.RegularExpressions.Regex.Matches(
                        content,
                        $@"(\w+)\s*:=\s*class\s*\(\s*{System.Text.RegularExpressions.Regex.Escape(knownClass)}\s*\)");

                    foreach (System.Text.RegularExpressions.Match sub in subMatches)
                    {
                        verseClassSources.TryAdd(sub.Groups[1].Value, file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read verse file: {Path}", file);
            }
        }

        if (verseClassSources.Count == 0)
            return issues;

        // Check which verse device classes are placed in the level
        var placedVerseClasses = new HashSet<string>(
            graph.Nodes
                .Where(n => n.IsVerseDevice)
                .Select(n => n.DeviceClass),
            StringComparer.OrdinalIgnoreCase);

        // Also collect actor names — sometimes the actor name contains the verse class
        var placedActorNames = new HashSet<string>(
            graph.Nodes.Select(n => n.ActorName),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (className, sourceFile) in verseClassSources)
        {
            // Check if any placed device class contains this verse class name
            bool isPlaced = placedVerseClasses.Any(p =>
                p.Contains(className, StringComparison.OrdinalIgnoreCase));

            // Also check actor names for a match
            if (!isPlaced)
            {
                isPlaced = placedActorNames.Any(a =>
                    a.Contains(className, StringComparison.OrdinalIgnoreCase));
            }

            if (!isPlaced)
            {
                var relativeSource = Path.GetFileName(sourceFile);
                counter++;
                issues.Add(new QAIssue
                {
                    Id = $"VERSE_{counter:D3}",
                    Severity = QASeverity.Info,
                    Category = "verse",
                    Title = $"Unplaced verse device: {className}",
                    Description = $"Verse class '{className}' (defined in {relativeSource}) extends creative_device " +
                                  "but no instance is placed in this level.",
                    SuggestedFix = "Place an instance of this device in the level, or remove the class if it's unused."
                });
            }
        }

        return issues;
    }

    /// <summary>
    /// Devices with suspicious property values: 0-radius triggers, 0-second timers, negative scores.
    /// </summary>
    public List<QAIssue> FindPropertyAnomalies(DeviceGraph graph)
    {
        var issues = new List<QAIssue>();
        int counter = 0;

        foreach (var node in graph.Nodes)
        {
            // Check for timer devices with 0-second duration
            if (MatchesAnyPattern(node.DeviceClass, TimerDevicePatterns))
            {
                // Look for nodes that have been placed but might have zero-duration defaults
                // This is a heuristic — we flag timers with very few properties set
                if (node.PropertyCount == 0)
                {
                    counter++;
                    issues.Add(new QAIssue
                    {
                        Id = $"PROP_{counter:D3}",
                        Severity = QASeverity.Info,
                        Category = "property",
                        Title = $"Timer with default config: '{node.ActorName}'",
                        Description = $"Timer device '{node.ActorName}' has no custom properties set. " +
                                      "It may be using default duration which might not be the intended behavior.",
                        AffectedDevice = node.ActorName,
                        SuggestedFix = "Review the timer duration and other settings."
                    });
                }
            }

            // Check trigger devices with no custom properties (may have 0 radius)
            if (node.DeviceClass.Contains("Trigger", StringComparison.OrdinalIgnoreCase) &&
                !node.DeviceClass.Contains("Button", StringComparison.OrdinalIgnoreCase))
            {
                if (node.PropertyCount == 0)
                {
                    counter++;
                    issues.Add(new QAIssue
                    {
                        Id = $"PROP_{counter:D3}",
                        Severity = QASeverity.Info,
                        Category = "property",
                        Title = $"Trigger with default config: '{node.ActorName}'",
                        Description = $"Trigger device '{node.ActorName}' has no custom properties set. " +
                                      "Default trigger radius and conditions may not work as expected.",
                        AffectedDevice = node.ActorName,
                        SuggestedFix = "Review the trigger radius, affected team, and trigger conditions."
                    });
                }
            }

            // Flag any device at the exact origin (0,0,0) — likely not intentionally placed
            if (node.Position.X == 0f && node.Position.Y == 0f && node.Position.Z == 0f)
            {
                counter++;
                issues.Add(new QAIssue
                {
                    Id = $"PROP_{counter:D3}",
                    Severity = QASeverity.Warning,
                    Category = "property",
                    Title = $"Device at world origin: '{node.ActorName}'",
                    Description = $"'{node.ActorName}' ({node.DeviceType}) is positioned at (0, 0, 0). " +
                                  "This is likely an unintentionally placed or unmoved device.",
                    AffectedDevice = node.ActorName,
                    SuggestedFix = "Move the device to its intended position in the level."
                });
            }
        }

        return issues;
    }

    /// <summary>
    /// Same output->input connection wired multiple times.
    /// </summary>
    public List<QAIssue> FindDuplicateWiring(DeviceGraph graph)
    {
        var issues = new List<QAIssue>();
        int counter = 0;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in graph.Edges)
        {
            var key = $"{edge.SourceActor}|{edge.OutputEvent}|{edge.TargetActor}|{edge.InputAction}";
            if (!seen.Add(key))
            {
                counter++;
                issues.Add(new QAIssue
                {
                    Id = $"DUP_{counter:D3}",
                    Severity = QASeverity.Warning,
                    Category = "duplicate",
                    Title = $"Duplicate wiring: {edge.SourceActor} -> {edge.TargetActor}",
                    Description = $"The connection '{edge.SourceActor}.{edge.OutputEvent}' -> " +
                                  $"'{edge.TargetActor}.{edge.InputAction}' is wired more than once. " +
                                  "Duplicate wiring causes the action to fire multiple times per trigger.",
                    AffectedDevice = edge.SourceActor,
                    SuggestedFix = "Remove the duplicate connection in the UEFN device wiring editor."
                });
            }
        }

        return issues;
    }

    // ─── Signal Tracing ─────────────────────────────────────────────────────

    private void TraceRecursive(
        DeviceGraph graph,
        string deviceName,
        string outputEvent,
        List<SignalStep> steps,
        HashSet<string> visited,
        int depth)
    {
        if (depth > 50) return; // prevent runaway recursion

        var key = $"{deviceName}|{outputEvent}";
        if (!visited.Add(key))
        {
            steps.Add(new SignalStep
            {
                DeviceName = deviceName,
                Event = outputEvent,
                Action = "(cycle detected — stopping trace)",
                Depth = depth,
                IsCycle = true
            });
            return;
        }

        // Find all edges originating from this device+event
        var outgoing = graph.Edges
            .Where(e => e.SourceActor.Equals(deviceName, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrEmpty(outputEvent) ||
                         e.OutputEvent.Equals(outputEvent, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var edge in outgoing)
        {
            steps.Add(new SignalStep
            {
                DeviceName = edge.SourceActor,
                Event = edge.OutputEvent,
                TargetDevice = edge.TargetActor,
                Action = edge.InputAction,
                Depth = depth
            });

            // Find the target node and trace its outputs
            var targetNode = graph.Nodes.FirstOrDefault(n =>
                n.ActorName.Equals(edge.TargetActor, StringComparison.OrdinalIgnoreCase));

            if (targetNode != null)
            {
                foreach (var nextOutput in targetNode.OutputEvents)
                {
                    TraceRecursive(graph, edge.TargetActor, nextOutput, steps, visited, depth + 1);
                }
            }
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private List<string> FindVerseFiles()
    {
        var results = new List<string>();

        // Scan Content/ directory
        var contentPath = _config.ContentPath;
        if (!string.IsNullOrEmpty(contentPath) && Directory.Exists(contentPath))
        {
            try
            {
                results.AddRange(Directory.EnumerateFiles(contentPath, "*.verse", SearchOption.AllDirectories));
            }
            catch
            {
                // Ignore access errors
            }
        }

        // Also scan Plugins/ directory (verse files can live there too)
        var projectPath = _config.ProjectPath;
        if (!string.IsNullOrEmpty(projectPath))
        {
            var pluginsPath = Path.Combine(projectPath, "Plugins");
            if (Directory.Exists(pluginsPath))
            {
                try
                {
                    results.AddRange(Directory.EnumerateFiles(pluginsPath, "*.verse", SearchOption.AllDirectories));
                }
                catch
                {
                    // Ignore access errors
                }
            }
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsOutputEvent(string propName)
    {
        return OutputEventPatterns.Any(p =>
            propName.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsInputAction(string propName)
    {
        return InputActionPatterns.Any(p =>
            propName.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesAnyPattern(string value, string[] patterns)
    {
        return patterns.Any(p => value.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private void AddClassBasedEvents(DeviceNode node)
    {
        var cls = node.DeviceClass.ToLowerInvariant();

        // Triggers always have OnTriggered
        if (cls.Contains("trigger"))
        {
            if (!node.OutputEvents.Contains("OnTriggered"))
                node.OutputEvents.Add("OnTriggered");
            if (!node.InputActions.Contains("Trigger"))
                node.InputActions.Add("Trigger");
            if (!node.InputActions.Contains("Enable"))
                node.InputActions.Add("Enable");
            if (!node.InputActions.Contains("Disable"))
                node.InputActions.Add("Disable");
        }

        // Timers have OnComplete
        if (cls.Contains("timer") || cls.Contains("countdown"))
        {
            if (!node.OutputEvents.Contains("OnTimerComplete"))
                node.OutputEvents.Add("OnTimerComplete");
            if (!node.InputActions.Contains("Start"))
                node.InputActions.Add("Start");
            if (!node.InputActions.Contains("Stop"))
                node.InputActions.Add("Stop");
            if (!node.InputActions.Contains("Reset"))
                node.InputActions.Add("Reset");
        }

        // Spawners
        if (cls.Contains("spawner") || cls.Contains("spawn"))
        {
            if (!node.OutputEvents.Contains("OnSpawned"))
                node.OutputEvents.Add("OnSpawned");
            if (!node.InputActions.Contains("Spawn"))
                node.InputActions.Add("Spawn");
            if (!node.InputActions.Contains("Despawn"))
                node.InputActions.Add("Despawn");
        }

        // Barriers
        if (cls.Contains("barrier"))
        {
            if (!node.InputActions.Contains("Enable"))
                node.InputActions.Add("Enable");
            if (!node.InputActions.Contains("Disable"))
                node.InputActions.Add("Disable");
        }

        // All devices generally support Enable/Disable
        if (!node.InputActions.Contains("Enable"))
            node.InputActions.Add("Enable");
        if (!node.InputActions.Contains("Disable"))
            node.InputActions.Add("Disable");
    }

    private static float Distance(Vector3Info a, Vector3Info b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private void FindCyclesDFS(
        string node,
        Dictionary<string, List<string>> adjacency,
        HashSet<string> visited,
        HashSet<string> onStack,
        List<string> path,
        List<List<string>> cycles)
    {
        visited.Add(node);
        onStack.Add(node);
        path.Add(node);

        if (adjacency.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (onStack.Contains(neighbor))
                {
                    // Found a cycle — extract it from the path
                    var cycleStart = path.IndexOf(neighbor);
                    if (cycleStart >= 0)
                    {
                        var cycle = path.GetRange(cycleStart, path.Count - cycleStart);
                        cycle.Add(neighbor); // close the cycle
                        cycles.Add(cycle);
                    }
                }
                else if (!visited.Contains(neighbor))
                {
                    FindCyclesDFS(neighbor, adjacency, visited, onStack, path, cycles);
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        onStack.Remove(node);
    }

    private static string BuildSummary(DeviceGraph graph, int critical, int warning, int info)
    {
        var total = critical + warning + info;
        if (total == 0)
            return $"QA PASSED — {graph.TotalDevices} devices, {graph.TotalConnections} connections, 0 issues found.";

        var status = critical > 0 ? "FAILED" : "PASSED (with warnings)";
        return $"QA {status} — {graph.TotalDevices} devices, {graph.TotalConnections} connections, " +
               $"{total} issues ({critical} critical, {warning} warnings, {info} info).";
    }
}

/// <summary>
/// A single step in a signal trace: which device fired what event to which target.
/// </summary>
public class SignalStep
{
    /// <summary>
    /// The device that fires or receives the signal at this step.
    /// </summary>
    public string DeviceName { get; set; } = "";

    /// <summary>
    /// The output event being fired.
    /// </summary>
    public string Event { get; set; } = "";

    /// <summary>
    /// The target device receiving the signal.
    /// </summary>
    public string? TargetDevice { get; set; }

    /// <summary>
    /// The input action triggered on the target.
    /// </summary>
    public string Action { get; set; } = "";

    /// <summary>
    /// Depth in the trace chain (0 = direct from start device).
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// True if this step detected a cycle back to an already-visited device.
    /// </summary>
    public bool IsCycle { get; set; }
}
