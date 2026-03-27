using WellVersed.Core.Models;
using WellVersed.Core.Services;
using Xunit;

namespace WellVersed.Tests;

/// <summary>
/// Tests for the LogicTracer — verifies device graph analysis, dead device detection,
/// infinite loop detection, missing win conditions, signal tracing, and QA report generation.
///
/// These tests construct synthetic DeviceGraphs directly (no real .uasset files needed)
/// to validate the analysis logic.
/// </summary>
public class LogicTracerTests
{
    // =====================================================================
    //  FindDeadDevices
    // =====================================================================

    [Fact]
    public void FindDeadDevices_DetectsUnconnectedDevice()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "TriggerDevice_1",
            DeviceClass = "BP_TriggerDevice_C",
            DeviceType = "Trigger Device"
        });
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "DeadBarrier_1",
            DeviceClass = "BP_BarrierDevice_C",
            DeviceType = "Barrier Device"
        });
        // TriggerDevice_1 is a source — not dead
        graph.Edges.Add(new DeviceEdge
        {
            SourceActor = "TriggerDevice_1",
            OutputEvent = "OnTriggered",
            TargetActor = "SomeOther",
            InputAction = "Enable"
        });
        graph.TotalDevices = graph.Nodes.Count;
        graph.TotalConnections = graph.Edges.Count;

        // Use the static method approach — create a minimal LogicTracer
        // Since FindDeadDevices is public, we construct a LogicTracer and test via the method
        var tracer = CreateTracer();
        var issues = tracer.FindDeadDevices(graph);

        // DeadBarrier_1 should be flagged — no incoming, no outgoing
        Assert.True(issues.Count >= 1, $"Expected at least 1 dead device, got {issues.Count}");
        Assert.Contains(issues, i => i.AffectedDevice == "DeadBarrier_1");
    }

    [Fact]
    public void FindDeadDevices_SkipsSpawnerDevices()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "SpawnPad_1",
            DeviceClass = "BP_PlayerSpawner_C",
            DeviceType = "Player Spawner"
        });
        graph.TotalDevices = 1;

        var tracer = CreateTracer();
        var issues = tracer.FindDeadDevices(graph);

        // Spawners should NOT be flagged as dead — they auto-activate
        Assert.Empty(issues);
    }

    [Fact]
    public void FindDeadDevices_SkipsVerseDevices()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "VerseManager_1",
            DeviceClass = "my_custom_device",
            DeviceType = "Custom Device",
            IsVerseDevice = true
        });
        graph.TotalDevices = 1;

        var tracer = CreateTracer();
        var issues = tracer.FindDeadDevices(graph);

        // Verse devices may self-trigger via OnBegin — not dead
        Assert.Empty(issues);
    }

    [Fact]
    public void FindDeadDevices_SkipsDevicesWithIncomingWiring()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "Barrier_1",
            DeviceClass = "BP_BarrierDevice_C",
            DeviceType = "Barrier Device"
        });
        graph.Edges.Add(new DeviceEdge
        {
            SourceActor = "SomeTrigger",
            OutputEvent = "OnTriggered",
            TargetActor = "Barrier_1",
            InputAction = "Disable"
        });
        graph.TotalDevices = 1;
        graph.TotalConnections = 1;

        var tracer = CreateTracer();
        var issues = tracer.FindDeadDevices(graph);

        // Barrier_1 has incoming wiring — not dead
        Assert.Empty(issues);
    }

    // =====================================================================
    //  FindInfiniteLoops
    // =====================================================================

    [Fact]
    public void FindInfiniteLoops_DetectsCycle()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode { ActorName = "A", DeviceClass = "DevA" });
        graph.Nodes.Add(new DeviceNode { ActorName = "B", DeviceClass = "DevB" });
        graph.Nodes.Add(new DeviceNode { ActorName = "C", DeviceClass = "DevC" });
        graph.Edges.Add(new DeviceEdge { SourceActor = "A", TargetActor = "B", OutputEvent = "Out", InputAction = "In" });
        graph.Edges.Add(new DeviceEdge { SourceActor = "B", TargetActor = "C", OutputEvent = "Out", InputAction = "In" });
        graph.Edges.Add(new DeviceEdge { SourceActor = "C", TargetActor = "A", OutputEvent = "Out", InputAction = "In" });
        graph.TotalDevices = 3;
        graph.TotalConnections = 3;

        var tracer = CreateTracer();
        var issues = tracer.FindInfiniteLoops(graph);

        Assert.True(issues.Count >= 1, "Should detect at least one cycle");
        Assert.Contains(issues, i => i.Category == "loop");
        Assert.Contains(issues, i => i.Severity == QASeverity.Critical);
    }

    [Fact]
    public void FindInfiniteLoops_NoCycleInLinearChain()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode { ActorName = "A", DeviceClass = "DevA" });
        graph.Nodes.Add(new DeviceNode { ActorName = "B", DeviceClass = "DevB" });
        graph.Nodes.Add(new DeviceNode { ActorName = "C", DeviceClass = "DevC" });
        graph.Edges.Add(new DeviceEdge { SourceActor = "A", TargetActor = "B", OutputEvent = "Out", InputAction = "In" });
        graph.Edges.Add(new DeviceEdge { SourceActor = "B", TargetActor = "C", OutputEvent = "Out", InputAction = "In" });
        graph.TotalDevices = 3;
        graph.TotalConnections = 2;

        var tracer = CreateTracer();
        var issues = tracer.FindInfiniteLoops(graph);

        Assert.Empty(issues);
    }

    // =====================================================================
    //  FindMissingWinCondition
    // =====================================================================

    [Fact]
    public void FindMissingWinCondition_DetectsMissingEndGame()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "Trigger_1",
            DeviceClass = "BP_TriggerDevice_C",
            DeviceType = "Trigger Device"
        });
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "Barrier_1",
            DeviceClass = "BP_BarrierDevice_C",
            DeviceType = "Barrier Device"
        });
        graph.TotalDevices = 2;

        var tracer = CreateTracer();
        var issues = tracer.FindMissingWinCondition(graph);

        Assert.True(issues.Count >= 1, "Should detect missing win condition");
        Assert.Contains(issues, i => i.Category == "missing_win");
        Assert.Contains(issues, i => i.Severity == QASeverity.Critical);
    }

    [Fact]
    public void FindMissingWinCondition_NoIssueWhenEndGameDeviceExistsAndIsReachable()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "EndGameDevice_1",
            DeviceClass = "BP_EndGame_C",
            DeviceType = "End Game"
        });
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "Trigger_1",
            DeviceClass = "BP_TriggerDevice_C",
            DeviceType = "Trigger Device"
        });
        // End game device needs a trigger path (reachability check)
        graph.Edges.Add(new DeviceEdge
        {
            SourceActor = "Trigger_1",
            OutputEvent = "OnTriggered",
            TargetActor = "EndGameDevice_1",
            InputAction = "Activate"
        });
        graph.TotalDevices = 2;
        graph.TotalConnections = 1;

        var tracer = CreateTracer();
        var issues = tracer.FindMissingWinCondition(graph);

        Assert.Empty(issues);
    }

    [Fact]
    public void FindMissingWinCondition_NoIssueWhenEndGameActionWired()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "ScoreTracker_1",
            DeviceClass = "BP_ScoreTracker_C",
            DeviceType = "Score Tracker"
        });
        graph.Edges.Add(new DeviceEdge
        {
            SourceActor = "ScoreTracker_1",
            OutputEvent = "OnScoreReached",
            TargetActor = "GameController",
            InputAction = "EndGame"
        });
        graph.TotalDevices = 1;
        graph.TotalConnections = 1;

        var tracer = CreateTracer();
        var issues = tracer.FindMissingWinCondition(graph);

        Assert.Empty(issues);
    }

    // =====================================================================
    //  FindDuplicateWiring
    // =====================================================================

    [Fact]
    public void FindDuplicateWiring_DetectsDuplicate()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode { ActorName = "Trigger_1" });
        graph.Nodes.Add(new DeviceNode { ActorName = "Barrier_1" });
        graph.Edges.Add(new DeviceEdge
        {
            SourceActor = "Trigger_1", OutputEvent = "OnTriggered",
            TargetActor = "Barrier_1", InputAction = "Disable"
        });
        graph.Edges.Add(new DeviceEdge
        {
            SourceActor = "Trigger_1", OutputEvent = "OnTriggered",
            TargetActor = "Barrier_1", InputAction = "Disable"
        });
        graph.TotalDevices = 2;
        graph.TotalConnections = 2;

        var tracer = CreateTracer();
        var issues = tracer.FindDuplicateWiring(graph);

        Assert.True(issues.Count >= 1, "Should detect duplicate wiring");
        Assert.Contains(issues, i => i.Category == "duplicate");
    }

    // =====================================================================
    //  TraceSignalFlow
    // =====================================================================

    [Fact]
    public void TraceSignalFlow_FollowsLinearChain()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode { ActorName = "A", OutputEvents = new List<string> { "Out" } });
        graph.Nodes.Add(new DeviceNode { ActorName = "B", OutputEvents = new List<string> { "Out2" } });
        graph.Nodes.Add(new DeviceNode { ActorName = "C" });
        graph.Edges.Add(new DeviceEdge { SourceActor = "A", OutputEvent = "Out", TargetActor = "B", InputAction = "In" });
        graph.Edges.Add(new DeviceEdge { SourceActor = "B", OutputEvent = "Out2", TargetActor = "C", InputAction = "In2" });
        graph.TotalDevices = 3;
        graph.TotalConnections = 2;

        var tracer = CreateTracer();
        var steps = tracer.TraceSignalFlow(graph, "A", "Out");

        Assert.True(steps.Count >= 1, "Should trace at least one step");
        Assert.Equal("A", steps[0].DeviceName);
        Assert.Equal("Out", steps[0].Event);
        Assert.Equal("B", steps[0].TargetDevice);
    }

    [Fact]
    public void TraceSignalFlow_StopsOnCycle()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode { ActorName = "A", OutputEvents = new List<string> { "Out" } });
        graph.Nodes.Add(new DeviceNode { ActorName = "B", OutputEvents = new List<string> { "Out" } });
        graph.Edges.Add(new DeviceEdge { SourceActor = "A", OutputEvent = "Out", TargetActor = "B", InputAction = "In" });
        graph.Edges.Add(new DeviceEdge { SourceActor = "B", OutputEvent = "Out", TargetActor = "A", InputAction = "In" });
        graph.TotalDevices = 2;
        graph.TotalConnections = 2;

        var tracer = CreateTracer();
        var steps = tracer.TraceSignalFlow(graph, "A", "Out");

        // Should trace through but not loop infinitely
        var hasCycle = steps.Any(s => s.IsCycle);
        Assert.True(steps.Count <= 100, "Should not loop infinitely");
        Assert.True(hasCycle, "Should mark cycle detection");
    }

    // =====================================================================
    //  FindPropertyAnomalies
    // =====================================================================

    [Fact]
    public void FindPropertyAnomalies_DetectsDeviceAtOrigin()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "MisplacedTrigger_1",
            DeviceClass = "BP_TriggerDevice_C",
            DeviceType = "Trigger Device",
            Position = new Vector3Info(0, 0, 0),
            PropertyCount = 5
        });
        graph.TotalDevices = 1;

        var tracer = CreateTracer();
        var issues = tracer.FindPropertyAnomalies(graph);

        Assert.True(issues.Count >= 1, "Should detect device at origin");
        Assert.Contains(issues, i => i.Description!.Contains("(0, 0, 0)"));
    }

    // =====================================================================
    //  FindTimingConflicts
    // =====================================================================

    [Fact]
    public void FindTimingConflicts_DetectsTwoTimersTargetingSameDevice()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode { ActorName = "TimerA", DeviceClass = "BP_TimerDevice_C", DeviceType = "Timer" });
        graph.Nodes.Add(new DeviceNode { ActorName = "TimerB", DeviceClass = "BP_TimerDevice_C", DeviceType = "Timer" });
        graph.Nodes.Add(new DeviceNode { ActorName = "Barrier_1", DeviceClass = "BP_BarrierDevice_C", DeviceType = "Barrier" });

        // TimerA enables the barrier, TimerB disables it
        graph.Edges.Add(new DeviceEdge
        {
            SourceActor = "TimerA", OutputEvent = "OnComplete",
            TargetActor = "Barrier_1", InputAction = "Enable"
        });
        graph.Edges.Add(new DeviceEdge
        {
            SourceActor = "TimerB", OutputEvent = "OnComplete",
            TargetActor = "Barrier_1", InputAction = "Disable"
        });
        graph.TotalDevices = 3;
        graph.TotalConnections = 2;

        var tracer = CreateTracer();
        var issues = tracer.FindTimingConflicts(graph);

        Assert.True(issues.Count >= 1, $"Should detect timer conflict, got {issues.Count} issues");
        Assert.Contains(issues, i => i.Category == "timing");
    }

    [Fact]
    public void FindTimingConflicts_NoIssue_WhenSingleTimer()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode { ActorName = "Timer_1", DeviceClass = "BP_TimerDevice_C" });
        graph.Nodes.Add(new DeviceNode { ActorName = "Barrier_1", DeviceClass = "BP_BarrierDevice_C" });
        graph.Edges.Add(new DeviceEdge
        {
            SourceActor = "Timer_1", OutputEvent = "OnComplete",
            TargetActor = "Barrier_1", InputAction = "Enable"
        });
        graph.TotalDevices = 2;
        graph.TotalConnections = 1;

        var tracer = CreateTracer();
        var issues = tracer.FindTimingConflicts(graph);

        Assert.Empty(issues);
    }

    // =====================================================================
    //  FindUnreachableSpawns
    // =====================================================================

    [Fact]
    public void FindUnreachableSpawns_DetectsBarrierBlockingSpawn()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "Spawn_1",
            DeviceClass = "BP_PlayerSpawner_C",
            DeviceType = "Player Spawner",
            Position = new Vector3Info(100, 100, 0)
        });
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "Barrier_1",
            DeviceClass = "BP_BarrierDevice_C",
            DeviceType = "Barrier",
            Position = new Vector3Info(100, 200, 0) // Within 2000 units
        });
        // No wiring to disable the barrier
        graph.TotalDevices = 2;
        graph.TotalConnections = 0;

        var tracer = CreateTracer();
        var issues = tracer.FindUnreachableSpawns(graph);

        Assert.True(issues.Count >= 1, "Should detect spawn blocked by permanent barrier");
        Assert.Contains(issues, i => i.Category == "spawn");
    }

    [Fact]
    public void FindUnreachableSpawns_NoIssue_WhenBarrierIsDisabled()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "Spawn_1",
            DeviceClass = "BP_PlayerSpawner_C",
            DeviceType = "Player Spawner",
            Position = new Vector3Info(100, 100, 0)
        });
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "Barrier_1",
            DeviceClass = "BP_BarrierDevice_C",
            DeviceType = "Barrier",
            Position = new Vector3Info(100, 200, 0)
        });
        // Barrier is wired to be disabled
        graph.Edges.Add(new DeviceEdge
        {
            SourceActor = "SomeTrigger", OutputEvent = "OnTriggered",
            TargetActor = "Barrier_1", InputAction = "Disable"
        });
        graph.TotalDevices = 2;
        graph.TotalConnections = 1;

        var tracer = CreateTracer();
        var issues = tracer.FindUnreachableSpawns(graph);

        // Barrier_1 near spawn but it has a Disable wiring — should not flag
        Assert.DoesNotContain(issues, i =>
            i.Description != null && i.Description.Contains("Barrier_1") && i.Description.Contains("no wiring to disable"));
    }

    [Fact]
    public void FindUnreachableSpawns_NoSpawns_FlagsNoSpawnPoints()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "Trigger_1",
            DeviceClass = "BP_TriggerDevice_C",
            DeviceType = "Trigger"
        });
        graph.TotalDevices = 1;

        var tracer = CreateTracer();
        var issues = tracer.FindUnreachableSpawns(graph);

        Assert.Contains(issues, i =>
            i.Category == "spawn" && i.Title.Contains("No spawn points"));
    }

    // =====================================================================
    //  FindMissingWinCondition — verifies EndGame reachability
    // =====================================================================

    [Fact]
    public void FindMissingWinCondition_DetectsUnreachableEndGame()
    {
        var graph = new DeviceGraph();
        // Has trigger and barrier but no end-game / score / elim manager device and no EndGame wiring
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "Trigger_1",
            DeviceClass = "BP_TriggerDevice_C",
            DeviceType = "Trigger Device"
        });
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "Barrier_1",
            DeviceClass = "BP_BarrierDevice_C",
            DeviceType = "Barrier Device"
        });
        graph.Edges.Add(new DeviceEdge
        {
            SourceActor = "Trigger_1", OutputEvent = "OnTriggered",
            TargetActor = "Barrier_1", InputAction = "Disable"
        });
        graph.TotalDevices = 2;
        graph.TotalConnections = 1;

        var tracer = CreateTracer();
        var issues = tracer.FindMissingWinCondition(graph);

        Assert.True(issues.Count >= 1, "Should detect missing win condition");
    }

    // =====================================================================
    //  FindMissingTeamConfig
    // =====================================================================

    [Fact]
    public void FindMissingTeamConfig_DetectsTeamSpawnersWithoutTeamSettings()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "Spawn_TeamA_1",
            DeviceClass = "BP_PlayerSpawner_C",
            DeviceType = "Player Spawner",
            Position = new Vector3Info(0, 0, 0)
        });
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "Spawn_TeamB_1",
            DeviceClass = "BP_PlayerSpawner_C",
            DeviceType = "Player Spawner",
            Position = new Vector3Info(5000, 0, 0)
        });
        // No team settings device
        graph.TotalDevices = 2;
        graph.TotalConnections = 0;

        var tracer = CreateTracer();
        var issues = tracer.FindMissingTeamConfig(graph);

        Assert.True(issues.Count >= 1, "Should detect team spawners without team config");
        Assert.Contains(issues, i => i.Category == "team");
    }

    [Fact]
    public void FindMissingTeamConfig_NoIssue_WhenTeamSettingsPresent()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "Spawn_TeamA",
            DeviceClass = "BP_PlayerSpawner_C",
            Position = new Vector3Info(0, 0, 0)
        });
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "Spawn_TeamB",
            DeviceClass = "BP_PlayerSpawner_C",
            Position = new Vector3Info(5000, 0, 0)
        });
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "TeamSettings_1",
            DeviceClass = "BP_TeamSettingsDevice_C",
            DeviceType = "Team Settings"
        });
        graph.TotalDevices = 3;

        var tracer = CreateTracer();
        var issues = tracer.FindMissingTeamConfig(graph);

        Assert.Empty(issues);
    }

    [Fact]
    public void FindMissingTeamConfig_NoIssue_WhenOnlyOneSpawner()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode
        {
            ActorName = "Spawn_1",
            DeviceClass = "BP_PlayerSpawner_C",
            Position = new Vector3Info(0, 0, 0)
        });
        graph.TotalDevices = 1;

        var tracer = CreateTracer();
        var issues = tracer.FindMissingTeamConfig(graph);

        // Only 1 spawner — not team-based, should not flag
        Assert.Empty(issues);
    }

    // =====================================================================
    //  FindUnusedVerseDevices
    // =====================================================================

    [Fact]
    public void FindUnusedVerseDevices_DetectsUnplacedVerseDevice()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode { ActorName = "Trigger_1", DeviceClass = "BP_TriggerDevice_C" });
        graph.TotalDevices = 1;

        // Write temp verse file defining a device that is NOT placed
        var tempDir = Path.Combine(Path.GetTempPath(), $"wv_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var verseFile = Path.Combine(tempDir, "my_custom_device.verse");
        File.WriteAllText(verseFile, @"
my_custom_device := class(creative_device):
    OnBegin<override>()<suspends>:void =
        Print(""hello"")
");

        try
        {
            var tracer = CreateTracer();
            var issues = tracer.FindUnusedVerseDevices(graph, new List<string> { verseFile });

            Assert.True(issues.Count >= 1, "Should detect unplaced verse device");
            Assert.Contains(issues, i =>
                i.Description != null && i.Description.Contains("my_custom_device"));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void FindUnusedVerseDevices_NoIssue_WhenNoVerseFiles()
    {
        var graph = new DeviceGraph();
        graph.Nodes.Add(new DeviceNode { ActorName = "Trigger_1", DeviceClass = "BP_TriggerDevice_C" });
        graph.TotalDevices = 1;

        var tracer = CreateTracer();
        var issues = tracer.FindUnusedVerseDevices(graph, new List<string>());

        Assert.Empty(issues);
    }

    // =====================================================================
    //  Helper
    // =====================================================================

    private static LogicTracer CreateTracer()
    {
        var config = new WellVersed.Core.Config.WellVersedConfig { ProjectPath = "." };
        var detector = new UefnDetector(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<UefnDetector>.Instance);
        var fileAccess = new SafeFileAccess(config, detector, Microsoft.Extensions.Logging.Abstractions.NullLogger<SafeFileAccess>.Instance);
        var guard = new WellVersed.Core.Safety.AssetGuard(config, detector, Microsoft.Extensions.Logging.Abstractions.NullLogger<WellVersed.Core.Safety.AssetGuard>.Instance);
        var assetService = new AssetService(config, guard, fileAccess, Microsoft.Extensions.Logging.Abstractions.NullLogger<AssetService>.Instance);
        var digestService = new DigestService(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<DigestService>.Instance);
        var deviceService = new DeviceService(config, assetService, digestService, Microsoft.Extensions.Logging.Abstractions.NullLogger<DeviceService>.Instance);
        return new LogicTracer(config, deviceService, Microsoft.Extensions.Logging.Abstractions.NullLogger<LogicTracer>.Instance);
    }
}
