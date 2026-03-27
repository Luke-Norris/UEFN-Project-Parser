using System.Text.RegularExpressions;
using WellVersed.Core.Config;
using WellVersed.Core.Models;
using Microsoft.Extensions.Logging;

namespace WellVersed.Core.Services;

/// <summary>
/// Verse Intelligence Engine — semantic analysis, anti-pattern detection,
/// refactoring suggestions, and device-binding scaffolding for .verse files.
/// Goes beyond generation: it understands existing Verse code.
/// </summary>
public class VerseIntelligence
{
    private readonly WellVersedConfig _config;
    private readonly DeviceService _deviceService;
    private readonly ILogger<VerseIntelligence> _logger;

    // ─── Regex patterns for Verse syntax extraction ───

    private static readonly Regex ClassRegex = new(
        @"^(\w+)\s*(?:<[^>]*>)*\s*:=\s*class\s*(?:<([^>]*)>(?:<[^>]*>)*)?\s*\(([^)]*)\)\s*:",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex FunctionRegex = new(
        @"^\s{4}(\w+)\s*(?:<[^>]*>)?\s*\(([^)]*)\)\s*(?:<[^>]*>)*\s*:\s*(\w+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex EditableRegex = new(
        @"@editable\s+(?:var\s+)?(\w+)\s*:\s*(\[?\]?\s*[\w.]+)",
        RegexOptions.Compiled);

    private static readonly Regex VarDeclarationRegex = new(
        @"^\s{4}(?:var\s+)?(\w+)\s*:\s*([\w.\[\]?]+)\s*=",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex SubscriptionRegex = new(
        @"(\w+)\s*\.\s*((?:Subscribes?|(?:On\w+))\w*)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex SleepCallRegex = new(
        @"\bSleep\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex SuspendsRegex = new(
        @"<suspends>",
        RegexOptions.Compiled);

    private static readonly Regex DecidesRegex = new(
        @"<decides>",
        RegexOptions.Compiled);

    private static readonly Regex SetRegex = new(
        @"\bset\s+(\w+)\s*=",
        RegexOptions.Compiled);

    private static readonly Regex FailableCallRegex = new(
        @"(\w+)\s*\[",
        RegexOptions.Compiled);

    private static readonly Regex LoopRegex = new(
        @"\bloop\s*:",
        RegexOptions.Compiled);

    private static readonly Regex PrintRegex = new(
        @"\bPrint\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex SpawnRegex = new(
        @"\bspawn\s*\{",
        RegexOptions.Compiled);

    private static readonly Regex DeviceTypeRegex = new(
        @":\s*(?:\[\])?(\w+_device)\b",
        RegexOptions.Compiled);

    private static readonly Regex HardcodedNumberRegex = new(
        @"(?<!\w)(?:(?:\d+\.\d+)|(?:\d{2,}))(?!\w)",
        RegexOptions.Compiled);

    // ─── Known device class → Verse type mappings ───

    private static readonly Dictionary<string, string> DeviceClassToVerseType = new(StringComparer.OrdinalIgnoreCase)
    {
        { "BP_TimerDevice_C", "timer_device" },
        { "BP_TriggerDevice_C", "trigger_device" },
        { "BP_ButtonDevice_C", "button_device" },
        { "BP_ItemSpawnerDevice_C", "item_spawner_device" },
        { "BP_ItemGranterDevice_C", "item_granter_device" },
        { "BP_EliminationManager_C", "elimination_manager_device" },
        { "BP_ScoreManagerDevice_C", "score_manager_device" },
        { "BP_TeamSettingsDevice_C", "team_settings_and_inventory_device" },
        { "BP_MapIndicatorDevice_C", "map_indicator_device" },
        { "BP_HUDMessageDevice_C", "hud_message_device" },
        { "BP_MutatorZone_C", "mutator_zone_device" },
        { "BP_BarrierDevice_C", "barrier_device" },
        { "BP_TeleporterDevice_C", "teleporter_device" },
        { "BP_SpawnPadDevice_C", "player_spawner_device" },
        { "BP_VendingMachineDevice_C", "vending_machine_device" },
        { "BP_DamageVolumeDevice_C", "damage_volume_device" },
        { "BP_PlayerReferenceDevice_C", "player_reference_device" },
        { "BP_ConditionalButtonDevice_C", "conditional_button_device" },
        { "BP_ClassSelectorDevice_C", "class_designer_device" },
        { "BP_HUDControllerDevice_C", "hud_controller_device" },
        { "BP_RoundSettingsDevice_C", "round_settings_device" },
        { "BP_TrackerDevice_C", "tracker_device" },
        { "BP_PropMoverDevice_C", "prop_mover_device" },
        { "BP_CaptureAreaDevice_C", "capture_area_device" },
        { "BP_BillboardDevice_C", "billboard_device" },
        { "BP_PopUpDialogDevice_C", "popup_dialog_device" },
        { "BP_ChannelDevice_C", "signal_remote_manager_device" },
        { "BP_MovementModulatorDevice_C", "movement_modulator_device" },
        { "BP_SkydiveVolumeDevice_C", "skydive_volume_device" },
        { "BP_ExplosiveDevice_C", "explosive_device" },
        { "BP_AudioPlayerDevice_C", "audio_player_device" },
        { "BP_VFXSpawnerDevice_C", "vfx_spawner_device" },
        { "BP_GuardSpawnerDevice_C", "guard_spawner_device" },
        { "BP_WildlifeSpawnerDevice_C", "wildlife_spawner_device" },
        { "BP_InventoryBarrierDevice_C", "inventory_barrier_device" },
    };

    // ─── Common Verse events per device type ───

    private static readonly Dictionary<string, List<string>> DeviceEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        { "timer_device", new() { "SuccessEvent", "FailEvent" } },
        { "trigger_device", new() { "TriggeredEvent" } },
        { "button_device", new() { "InteractedWithEvent" } },
        { "elimination_manager_device", new() { "EliminationEvent" } },
        { "item_spawner_device", new() { "ItemPickedUpEvent", "ItemSpawnedEvent" } },
        { "capture_area_device", new() { "AreaEnteredEvent", "AreaExitedEvent", "AreaCompletedEvent" } },
        { "mutator_zone_device", new() { "AgentEntersEvent", "AgentExitsEvent" } },
        { "conditional_button_device", new() { "ActivatedEvent", "CompletedEvent" } },
        { "player_spawner_device", new() { "SpawnedEvent" } },
        { "guard_spawner_device", new() { "SpawnedEvent", "EliminatedEvent" } },
        { "damage_volume_device", new() { "AgentEntersEvent", "AgentExitsEvent", "DamageDealtEvent" } },
        { "score_manager_device", new() { "ScoreReachedEvent" } },
        { "tracker_device", new() { "TrackedEvent" } },
        { "popup_dialog_device", new() { "RespondingButtonEvent" } },
        { "prop_mover_device", new() { "MovedToEndEvent", "MovedToStartEvent" } },
    };

    public VerseIntelligence(
        WellVersedConfig config,
        DeviceService deviceService,
        ILogger<VerseIntelligence> logger)
    {
        _config = config;
        _deviceService = deviceService;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════
    // AnalyzeVerseFile — full semantic analysis
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Parses a .verse file and extracts classes, functions, editable properties,
    /// event subscriptions, detected patterns, anti-patterns, and complexity score.
    /// </summary>
    public VerseAnalysis AnalyzeVerseFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Verse file not found: {filePath}");

        var content = File.ReadAllText(filePath);
        var lines = content.Split('\n');

        var analysis = new VerseAnalysis
        {
            FilePath = filePath,
            LineCount = lines.Length
        };

        // Extract classes
        foreach (Match m in ClassRegex.Matches(content))
            analysis.Classes.Add(m.Groups[1].Value);

        // Extract functions
        foreach (Match m in FunctionRegex.Matches(content))
            analysis.Functions.Add(m.Groups[1].Value);

        // Extract @editable properties
        foreach (Match m in EditableRegex.Matches(content))
            analysis.EditableProperties.Add($"{m.Groups[1].Value}: {m.Groups[2].Value.Trim()}");

        // Extract variable declarations
        foreach (Match m in VarDeclarationRegex.Matches(content))
            analysis.Variables.Add($"{m.Groups[1].Value}: {m.Groups[2].Value}");

        // Extract event subscriptions
        foreach (Match m in SubscriptionRegex.Matches(content))
            analysis.EventSubscriptions.Add($"{m.Groups[1].Value}.{m.Groups[2].Value}");

        // Detect high-level patterns
        analysis.DetectedPatterns = DetectPatterns(content, lines);

        // Detect anti-patterns
        analysis.AntiPatterns = DetectAntiPatterns(filePath);

        // Compute complexity score
        analysis.ComplexityScore = ComputeComplexity(analysis, content, lines);

        return analysis;
    }

    private List<string> DetectPatterns(string content, string[] lines)
    {
        var patterns = new List<string>();

        if (LoopRegex.IsMatch(content) && SleepCallRegex.IsMatch(content))
            patterns.Add("timer_loop");

        if (Regex.IsMatch(content, @"EliminationEvent|Eliminated|elimination", RegexOptions.IgnoreCase))
            patterns.Add("elimination_handler");

        if (Regex.IsMatch(content, @"Score|Points|score_manager", RegexOptions.IgnoreCase))
            patterns.Add("score_tracking");

        if (Regex.IsMatch(content, @"widget|canvas|ui_|hud|SetText|SetVisibility", RegexOptions.IgnoreCase))
            patterns.Add("ui_update");

        if (Regex.IsMatch(content, @"Spawn|spawner|SpawnedEvent", RegexOptions.IgnoreCase))
            patterns.Add("spawn_system");

        if (Regex.IsMatch(content, @"Round|Phase|round_settings", RegexOptions.IgnoreCase))
            patterns.Add("round_management");

        if (Regex.IsMatch(content, @"Team|team_|TeamIndex", RegexOptions.IgnoreCase))
            patterns.Add("team_logic");

        if (Regex.IsMatch(content, @"Persistent|persistent_|SaveData", RegexOptions.IgnoreCase))
            patterns.Add("persistent_data");

        if (Regex.IsMatch(content, @"Map\[|map\[|concurrent_map", RegexOptions.IgnoreCase))
            patterns.Add("player_state_map");

        if (Regex.IsMatch(content, @"Teleport|teleporter_device", RegexOptions.IgnoreCase))
            patterns.Add("teleport_system");

        if (Regex.IsMatch(content, @"mutator_zone|movement_modulator|ApplyMovement", RegexOptions.IgnoreCase))
            patterns.Add("movement_modifier");

        if (SpawnRegex.IsMatch(content))
            patterns.Add("async_spawn");

        if (Regex.IsMatch(content, @"ItemGranter|item_granter|GrantItem", RegexOptions.IgnoreCase))
            patterns.Add("item_granting");

        if (Regex.IsMatch(content, @"creative_prop|prop_mover", RegexOptions.IgnoreCase))
            patterns.Add("prop_manipulation");

        return patterns;
    }

    private int ComputeComplexity(VerseAnalysis analysis, string content, string[] lines)
    {
        var score = 0;

        // Class count
        score += analysis.Classes.Count switch
        {
            0 => 0,
            1 => 1,
            2 => 2,
            _ => 3
        };

        // Function count
        score += analysis.Functions.Count switch
        {
            <= 3 => 0,
            <= 7 => 1,
            <= 15 => 2,
            _ => 3
        };

        // Device references
        var deviceRefCount = analysis.EditableProperties.Count(p => p.Contains("_device"));
        score += deviceRefCount switch
        {
            <= 2 => 0,
            <= 5 => 1,
            _ => 2
        };

        // Nesting depth — count max indentation level
        var maxIndent = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var indent = line.Length - line.TrimStart().Length;
            var level = indent / 4; // Verse uses 4-space indent
            if (level > maxIndent) maxIndent = level;
        }
        score += maxIndent switch
        {
            <= 3 => 0,
            <= 5 => 1,
            _ => 2
        };

        return Math.Clamp(score, 1, 10);
    }

    // ═══════════════════════════════════════════════════════
    // FindDeviceReferences — cross-reference Verse ↔ level
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Extracts all @editable device references from a Verse file and
    /// cross-references them against placed devices in the level.
    /// </summary>
    public List<VerseDeviceReference> FindDeviceReferences(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Verse file not found: {filePath}");

        var content = File.ReadAllText(filePath);
        var lines = content.Split('\n');
        var references = new List<VerseDeviceReference>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var match = EditableRegex.Match(line);
            if (!match.Success) continue;

            var varName = match.Groups[1].Value;
            var verseType = match.Groups[2].Value.Trim();

            // Only track device references
            if (!verseType.Contains("device", StringComparison.OrdinalIgnoreCase))
                continue;

            references.Add(new VerseDeviceReference
            {
                VariableName = varName,
                VerseType = verseType,
                Line = i + 1
            });
        }

        // Try to match against placed devices
        TryMatchDevicesInProject(references);

        return references;
    }

    private void TryMatchDevicesInProject(List<VerseDeviceReference> references)
    {
        try
        {
            var levels = _deviceService.FindLevels();
            if (levels.Count == 0) return;

            // Collect all placed devices across levels
            var allDevices = new List<DeviceInfo>();
            foreach (var level in levels)
            {
                try
                {
                    allDevices.AddRange(_deviceService.ListDevicesInLevel(level));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not parse level for device matching: {Level}", level);
                }
            }

            // For each Verse reference, try to find a matching placed device
            foreach (var reference in references)
            {
                var verseType = reference.VerseType.ToLowerInvariant();

                foreach (var device in allDevices)
                {
                    // Check if the device class maps to this Verse type
                    if (DeviceClassToVerseType.TryGetValue(device.DeviceClass, out var mappedType))
                    {
                        if (mappedType.Equals(verseType, StringComparison.OrdinalIgnoreCase))
                        {
                            reference.FoundInLevel = true;
                            reference.MatchedActorName = device.ActorName;
                            reference.MatchedDeviceClass = device.DeviceClass;
                            break;
                        }
                    }

                    // Fallback: fuzzy match on device class name
                    var normalizedClass = device.DeviceClass
                        .Replace("BP_", "").Replace("PBWA_", "").Replace("_C", "")
                        .Replace("Device", "_device").Replace("_device_device", "_device")
                        .ToLowerInvariant();
                    if (normalizedClass.Contains(verseType.Replace("_device", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        reference.FoundInLevel = true;
                        reference.MatchedActorName = device.ActorName;
                        reference.MatchedDeviceClass = device.DeviceClass;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not match devices against project levels");
        }
    }

    // ═══════════════════════════════════════════════════════
    // DetectAntiPatterns — code quality issues
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Scans a Verse file for common mistakes and anti-patterns.
    /// </summary>
    public List<VerseAntiPattern> DetectAntiPatterns(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Verse file not found: {filePath}");

        var content = File.ReadAllText(filePath);
        var lines = content.Split('\n');
        var issues = new List<VerseAntiPattern>();

        DetectSleepWithoutSuspends(lines, issues);
        DetectSetOnImmutable(lines, content, issues);
        DetectMissingFailableCheck(lines, issues);
        DetectInfiniteLoopWithoutSleep(lines, issues);
        DetectLargePersistentArrays(lines, issues);
        DetectMissingOptionalCheck(lines, issues);
        DetectOrphanedSubscription(content, lines, issues);
        DetectRedundantDecides(lines, issues);
        DetectUnusedVariables(content, lines, issues);

        return issues;
    }

    private void DetectSleepWithoutSuspends(string[] lines, List<VerseAntiPattern> issues)
    {
        // Track which functions have <suspends>
        var currentFunction = "";
        var currentFunctionHasSuspends = false;
        var functionStartLine = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Detect function declaration
            var funcMatch = FunctionRegex.Match(line);
            if (funcMatch.Success)
            {
                currentFunction = funcMatch.Groups[1].Value;
                currentFunctionHasSuspends = line.Contains("<suspends>");
                functionStartLine = i + 1;
            }

            // Detect OnBegin override (always suspends)
            if (trimmed.StartsWith("OnBegin") && line.Contains("<override>"))
            {
                currentFunction = "OnBegin";
                currentFunctionHasSuspends = true;
                functionStartLine = i + 1;
            }

            // Detect Sleep call outside suspends context
            if (SleepCallRegex.IsMatch(trimmed) && !currentFunctionHasSuspends && !string.IsNullOrEmpty(currentFunction))
            {
                issues.Add(new VerseAntiPattern
                {
                    Id = "sleep_without_suspends",
                    Line = i + 1,
                    Severity = "error",
                    Title = "Sleep() called outside <suspends> context",
                    Description = $"Sleep() is called in '{currentFunction}' which does not have the <suspends> effect. " +
                                  "Sleep requires a suspending execution context.",
                    Fix = $"Add <suspends> to the function signature: {currentFunction}<suspends>():void =",
                    CodeBefore = trimmed,
                    CodeAfter = trimmed.Replace("Sleep(", "# Add <suspends> to function signature, then:\n        Sleep(")
                });
            }
        }
    }

    private void DetectSetOnImmutable(string[] lines, string content, List<VerseAntiPattern> issues)
    {
        // Find all 'var' declarations to know what's mutable
        var mutableVars = new HashSet<string>();
        foreach (Match m in Regex.Matches(content, @"\bvar\s+(\w+)\s*:"))
            mutableVars.Add(m.Groups[1].Value);

        // Also @editable vars are mutable
        foreach (Match m in EditableRegex.Matches(content))
            mutableVars.Add(m.Groups[1].Value);

        for (int i = 0; i < lines.Length; i++)
        {
            var match = SetRegex.Match(lines[i]);
            if (!match.Success) continue;

            var varName = match.Groups[1].Value;
            // If we know the variable was declared without 'var', it's immutable
            if (!mutableVars.Contains(varName))
            {
                // Check if it's a known constant (declared with := without var)
                var declPattern = new Regex($@"\b{Regex.Escape(varName)}\s*:\s*\w+\s*=", RegexOptions.Multiline);
                if (declPattern.IsMatch(content) && !Regex.IsMatch(content, $@"\bvar\s+{Regex.Escape(varName)}\b"))
                {
                    issues.Add(new VerseAntiPattern
                    {
                        Id = "set_on_immutable",
                        Line = i + 1,
                        Severity = "error",
                        Title = $"'set' used on potentially immutable variable '{varName}'",
                        Description = $"The variable '{varName}' does not appear to be declared with 'var', making it immutable. " +
                                      "Using 'set' on an immutable binding is a compile error.",
                        Fix = $"Declare the variable with 'var': var {varName} : <type> = <value>",
                        CodeBefore = lines[i].Trim(),
                        CodeAfter = $"# Ensure declaration uses 'var': var {varName} : <type> = <value>"
                    });
                }
            }
        }
    }

    private void DetectMissingFailableCheck(string[] lines, List<VerseAntiPattern> issues)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();

            // Detect failable indexing or query without if/for guard
            if (FailableCallRegex.IsMatch(trimmed) && !trimmed.StartsWith("if") &&
                !trimmed.StartsWith("for") && !trimmed.StartsWith("#"))
            {
                // Check if the line has array indexing with []
                if (Regex.IsMatch(trimmed, @"\w+\[\s*\d+\s*\]") && !trimmed.Contains("if") && !trimmed.Contains("for"))
                {
                    // Check this is not a type declaration (e.g. "Items: []item = ...")
                    if (!Regex.IsMatch(trimmed, @":\s*\["))
                    {
                        issues.Add(new VerseAntiPattern
                        {
                            Id = "unguarded_failable",
                            Line = i + 1,
                            Severity = "warning",
                            Title = "Array index access without failable guard",
                            Description = "Array indexing in Verse is failable. If not wrapped in an 'if' or used in " +
                                          "a <decides> context, the failure will propagate and may crash the device.",
                            Fix = "Wrap in an if expression: if (Value := Array[Index]):",
                            CodeBefore = trimmed,
                            CodeAfter = $"if (Value := {trimmed.Trim()}):\n            # use Value"
                        });
                    }
                }
            }
        }
    }

    private void DetectInfiniteLoopWithoutSleep(string[] lines, List<VerseAntiPattern> issues)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!LoopRegex.IsMatch(trimmed)) continue;

            // Scan the body of the loop (indented lines after this one)
            var baseIndent = lines[i].Length - trimmed.Length;
            var hasSleep = false;
            var hasBreak = false;

            for (int j = i + 1; j < lines.Length; j++)
            {
                var bodyLine = lines[j];
                if (string.IsNullOrWhiteSpace(bodyLine)) continue;

                var bodyIndent = bodyLine.Length - bodyLine.TrimStart().Length;
                if (bodyIndent <= baseIndent) break; // Left the loop body

                if (SleepCallRegex.IsMatch(bodyLine)) hasSleep = true;
                if (bodyLine.TrimStart().StartsWith("break")) hasBreak = true;
                if (Regex.IsMatch(bodyLine, @"\bAwait\b|\brace\b|\bsync\b")) hasSleep = true;
            }

            if (!hasSleep && !hasBreak)
            {
                issues.Add(new VerseAntiPattern
                {
                    Id = "infinite_loop_no_sleep",
                    Line = i + 1,
                    Severity = "error",
                    Title = "Infinite loop without Sleep() or break",
                    Description = "This loop has no Sleep(), Await, or break statement. It will freeze the " +
                                  "game because Verse runs on the game thread. Every tick-based loop MUST " +
                                  "have a Sleep(0.0) or similar yield point.",
                    Fix = "Add Sleep(0.0) inside the loop to yield every frame.",
                    CodeBefore = trimmed,
                    CodeAfter = "loop:\n            Sleep(0.0)\n            # ... loop body"
                });
            }
        }
    }

    private void DetectLargePersistentArrays(string[] lines, List<VerseAntiPattern> issues)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();

            // Detect large array literals or persistent array declarations
            if (Regex.IsMatch(trimmed, @"\bpersistent\b.*\[\]", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(trimmed, @"@editable.*:\s*\[\]\s*\w+.*=\s*array", RegexOptions.IgnoreCase))
            {
                issues.Add(new VerseAntiPattern
                {
                    Id = "large_persistent_array",
                    Line = i + 1,
                    Severity = "warning",
                    Title = "Persistent or large array declaration",
                    Description = "Large arrays in persistent data can cause performance issues. UEFN has " +
                                  "limits on persistent data size. Consider using a map or limiting array size.",
                    Fix = "Use a bounded collection or implement pagination for large datasets.",
                    CodeBefore = trimmed
                });
            }
        }
    }

    private void DetectMissingOptionalCheck(string[] lines, List<VerseAntiPattern> issues)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();

            // Detect direct method calls on optional types (accessing ?type without if check)
            if (Regex.IsMatch(trimmed, @"\?\w+\.\w+\(") && !trimmed.Contains("if") && !trimmed.StartsWith("#"))
            {
                issues.Add(new VerseAntiPattern
                {
                    Id = "missing_optional_check",
                    Line = i + 1,
                    Severity = "warning",
                    Title = "Method call on optional type without null check",
                    Description = "Calling methods on an optional (?) type without first checking if it has a " +
                                  "value can fail. Use an 'if' guard to safely unwrap the optional.",
                    Fix = "Wrap in an if expression to safely unwrap: if (Value := OptionalVar?):",
                    CodeBefore = trimmed
                });
            }
        }
    }

    private void DetectOrphanedSubscription(string content, string[] lines, List<VerseAntiPattern> issues)
    {
        // Find event subscriptions and check if handlers exist
        var subscriptions = SubscriptionRegex.Matches(content);
        foreach (Match sub in subscriptions)
        {
            var deviceVar = sub.Groups[1].Value;
            var eventName = sub.Groups[2].Value;

            // Look for a handler function — common pattern: HandleEventName or OnEventName
            var handlerPatterns = new[]
            {
                $"Handle{eventName}", $"On{eventName}",
                $"handle_{eventName}", $"on_{eventName}",
                eventName.Replace("Event", "Handler")
            };

            var hasHandler = handlerPatterns.Any(hp =>
                content.Contains(hp, StringComparison.OrdinalIgnoreCase));

            // Also check if a lambda is used inline
            var lineIdx = content.IndexOf(sub.Value, StringComparison.Ordinal);
            var lineNum = content[..lineIdx].Count(c => c == '\n') + 1;
            var subscribeLine = lines.ElementAtOrDefault(lineNum - 1) ?? "";
            var hasInlineLambda = subscribeLine.Contains("=>") || subscribeLine.Contains("lambda");

            if (!hasHandler && !hasInlineLambda)
            {
                // This isn't necessarily wrong — could be Subscribe(callback) with a variable reference
                // Only flag if the subscription line doesn't pass any argument
                if (Regex.IsMatch(subscribeLine, @"Subscribe\s*\(\s*\)"))
                {
                    issues.Add(new VerseAntiPattern
                    {
                        Id = "orphaned_subscription",
                        Line = lineNum,
                        Severity = "info",
                        Title = $"Event subscription without visible handler: {deviceVar}.{eventName}",
                        Description = $"'{deviceVar}.{eventName}' is subscribed but no handler function " +
                                      "was found nearby. Ensure a callback is passed to Subscribe().",
                        Fix = "Pass a handler function: Device.Event.Subscribe(HandleEvent)"
                    });
                }
            }
        }
    }

    private void DetectRedundantDecides(string[] lines, List<VerseAntiPattern> issues)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!DecidesRegex.IsMatch(line)) continue;

            // Check if the function body actually has any failable operations
            var funcMatch = FunctionRegex.Match(line);
            if (!funcMatch.Success) continue;

            var baseIndent = line.Length - line.TrimStart().Length;
            var hasFailableOp = false;

            for (int j = i + 1; j < lines.Length; j++)
            {
                var bodyLine = lines[j];
                if (string.IsNullOrWhiteSpace(bodyLine)) continue;
                var bodyIndent = bodyLine.Length - bodyLine.TrimStart().Length;
                if (bodyIndent <= baseIndent) break;

                // Check for failable operations: [], ?, set on failable, etc.
                if (Regex.IsMatch(bodyLine, @"\w+\[|\.Query|\.Find|\?|<decides>"))
                {
                    hasFailableOp = true;
                    break;
                }
            }

            if (!hasFailableOp)
            {
                issues.Add(new VerseAntiPattern
                {
                    Id = "redundant_decides",
                    Line = i + 1,
                    Severity = "info",
                    Title = "Function marked <decides> but no failable operations found",
                    Description = "This function has the <decides> effect but does not appear to contain " +
                                  "any failable operations. The <decides> effect may be unnecessary.",
                    Fix = "Remove <decides> if the function never fails.",
                    CodeBefore = line.Trim(),
                    CodeAfter = line.Trim().Replace("<decides>", "").Replace("  ", " ")
                });
            }
        }
    }

    private void DetectUnusedVariables(string content, string[] lines, List<VerseAntiPattern> issues)
    {
        // Find local variable declarations
        foreach (Match m in VarDeclarationRegex.Matches(content))
        {
            var varName = m.Groups[1].Value;

            // Skip common false positives
            if (varName is "Self" or "self" or "_") continue;

            // Count occurrences of this variable name (excluding the declaration itself)
            var occurrences = Regex.Matches(content, $@"\b{Regex.Escape(varName)}\b").Count;

            // If it only appears once (the declaration), it's unused
            if (occurrences <= 1)
            {
                var lineIdx = content.IndexOf(m.Value, StringComparison.Ordinal);
                var lineNum = content[..lineIdx].Count(c => c == '\n') + 1;

                issues.Add(new VerseAntiPattern
                {
                    Id = "unused_variable",
                    Line = lineNum,
                    Severity = "info",
                    Title = $"Variable '{varName}' appears to be unused",
                    Description = $"'{varName}' is declared but never referenced elsewhere in the file.",
                    Fix = "Remove the variable or use it in your logic.",
                    CodeBefore = lines[lineNum - 1].Trim()
                });
            }
        }
    }

    // ═══════════════════════════════════════════════════════
    // SuggestRefactoring — improvement suggestions
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Analyzes a Verse file and suggests concrete refactoring improvements.
    /// </summary>
    public List<RefactoringSuggestion> SuggestRefactoring(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Verse file not found: {filePath}");

        var content = File.ReadAllText(filePath);
        var lines = content.Split('\n');
        var suggestions = new List<RefactoringSuggestion>();

        SuggestExtractFunction(lines, suggestions);
        SuggestReplaceHardcodedValues(lines, suggestions);
        SuggestAddDebugging(content, lines, suggestions);
        SuggestSimplifyNestedIf(lines, suggestions);
        SuggestUseConcurrentMap(content, lines, suggestions);

        return suggestions;
    }

    private void SuggestExtractFunction(string[] lines, List<RefactoringSuggestion> suggestions)
    {
        // Look for duplicated code blocks (same 3+ lines appearing twice)
        var blockSize = 3;
        var seenBlocks = new Dictionary<string, int>(); // normalized block -> first line number

        for (int i = 0; i < lines.Length - blockSize; i++)
        {
            // Skip blank/comment lines
            if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].TrimStart().StartsWith("#"))
                continue;

            var block = string.Join("\n", lines.Skip(i).Take(blockSize).Select(l => l.Trim()));
            if (string.IsNullOrWhiteSpace(block)) continue;

            if (seenBlocks.TryGetValue(block, out var firstLine))
            {
                suggestions.Add(new RefactoringSuggestion
                {
                    Category = "extract_function",
                    Description = $"Duplicated code block found at lines {firstLine + 1} and {i + 1}. " +
                                  "Extract into a shared function to reduce duplication.",
                    LineStart = i + 1,
                    LineEnd = i + blockSize,
                    CodeBefore = block,
                    CodeAfter = "# Extract into a function:\n    SharedLogic():void =\n        " +
                                string.Join("\n        ", lines.Skip(i).Take(blockSize).Select(l => l.Trim())),
                    Impact = "medium"
                });
            }
            else
            {
                seenBlocks[block] = i;
            }
        }
    }

    private void SuggestReplaceHardcodedValues(string[] lines, List<RefactoringSuggestion> suggestions)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("#") || trimmed.StartsWith("@editable")) continue;

            // Look for hardcoded numeric literals that could be @editable
            var matches = HardcodedNumberRegex.Matches(trimmed);
            foreach (Match m in matches)
            {
                // Skip line numbers in comments, array indices, common constants
                if (m.Value is "0" or "1" or "0.0" or "1.0") continue;

                // Skip if this is inside a type declaration or import
                if (trimmed.Contains("using") || trimmed.Contains(":=")) continue;

                suggestions.Add(new RefactoringSuggestion
                {
                    Category = "add_editable",
                    Description = $"Hardcoded value {m.Value} could be an @editable property for " +
                                  "easier tuning without code changes.",
                    LineStart = i + 1,
                    LineEnd = i + 1,
                    CodeBefore = trimmed,
                    CodeAfter = $"@editable ConfigValue : float = {m.Value}\n    # Then use ConfigValue instead of {m.Value}",
                    Impact = "low"
                });
                break; // One suggestion per line
            }
        }
    }

    private void SuggestAddDebugging(string content, string[] lines, List<RefactoringSuggestion> suggestions)
    {
        // If there are zero Print() calls, suggest adding them
        if (!PrintRegex.IsMatch(content))
        {
            // Find OnBegin or first function
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("OnBegin") || FunctionRegex.IsMatch(lines[i]))
                {
                    suggestions.Add(new RefactoringSuggestion
                    {
                        Category = "add_debugging",
                        Description = "No Print() calls found in this file. Adding debug prints helps " +
                                      "trace execution flow when testing in UEFN.",
                        LineStart = i + 1,
                        LineEnd = i + 1,
                        CodeBefore = lines[i].Trim(),
                        CodeAfter = lines[i].Trim() + "\n        Print(\"[DEBUG] Function started\")",
                        Impact = "low"
                    });
                    break;
                }
            }
        }
    }

    private void SuggestSimplifyNestedIf(string[] lines, List<RefactoringSuggestion> suggestions)
    {
        // Detect deeply nested if chains (3+ levels)
        var ifDepth = 0;
        var ifStartLine = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("if") && trimmed.EndsWith(":"))
            {
                if (ifDepth == 0) ifStartLine = i;
                ifDepth++;

                if (ifDepth >= 3)
                {
                    suggestions.Add(new RefactoringSuggestion
                    {
                        Category = "simplify_nesting",
                        Description = "Deeply nested if-chain detected (3+ levels). Consider using " +
                                      "early returns, guard clauses, or extracting inner logic into " +
                                      "separate functions.",
                        LineStart = ifStartLine + 1,
                        LineEnd = i + 1,
                        CodeBefore = "if (CondA):\n            if (CondB):\n                if (CondC):\n                    # deep logic",
                        CodeAfter = "# Use guard clauses or extract:\n    HandleCase()<decides>:void =\n        CondA?\n        CondB?\n        CondC?\n        # flat logic",
                        Impact = "medium"
                    });
                    ifDepth = 0; // Reset to avoid duplicate suggestions
                }
            }
            else if (!string.IsNullOrWhiteSpace(trimmed) &&
                     (lines[i].Length - trimmed.Length) <= (ifStartLine >= 0 ? lines[ifStartLine].Length - lines[ifStartLine].TrimStart().Length : 0))
            {
                ifDepth = 0;
            }
        }
    }

    private void SuggestUseConcurrentMap(string content, string[] lines, List<RefactoringSuggestion> suggestions)
    {
        // If tracking per-player state with arrays, suggest using a map
        if (Regex.IsMatch(content, @"\[\]\s*(?:agent|player)", RegexOptions.IgnoreCase) &&
            !content.Contains("concurrent_map", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (Regex.IsMatch(lines[i], @"\[\]\s*(?:agent|player)", RegexOptions.IgnoreCase))
                {
                    suggestions.Add(new RefactoringSuggestion
                    {
                        Category = "use_concurrent_map",
                        Description = "Player/agent arrays are harder to maintain than a concurrent_map. " +
                                      "Consider using weak_map or concurrent_map for per-player state.",
                        LineStart = i + 1,
                        LineEnd = i + 1,
                        CodeBefore = lines[i].Trim(),
                        CodeAfter = "var PlayerState : weak_map(player, player_data) = map{}",
                        Impact = "high"
                    });
                    break;
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════
    // GenerateDeviceBindings — scan level → instant scaffold
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Scans all devices in a level and generates a complete Verse file with
    /// @editable declarations, type mappings, and OnBegin event subscriptions.
    /// </summary>
    public string GenerateDeviceBindings(string levelPath)
    {
        if (!File.Exists(levelPath))
            throw new FileNotFoundException($"Level file not found: {levelPath}");

        var devices = new List<DeviceInfo>();

        // Get devices from the .umap itself
        try
        {
            devices.AddRange(_deviceService.ListDevicesInLevel(levelPath));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse .umap for devices: {Path}", levelPath);
        }

        // Also scan __ExternalActors__ for this level
        var contentDir = Path.GetDirectoryName(levelPath);
        if (contentDir != null)
        {
            var levelName = Path.GetFileNameWithoutExtension(levelPath);
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
                    catch
                    {
                        // Skip unparseable external actors
                    }
                }
            }
        }

        // Filter to only devices (not props/meshes)
        devices = devices.Where(d => !string.IsNullOrEmpty(d.DeviceClass)).ToList();

        // Group by device type to avoid duplicate @editable declarations
        var devicesByType = devices
            .GroupBy(d => d.DeviceClass, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key)
            .ToList();

        var levelName2 = Path.GetFileNameWithoutExtension(levelPath);
        var className = SanitizeVerseName(levelName2) + "_bindings";

        return BuildVerseScaffold(className, devicesByType);
    }

    private string BuildVerseScaffold(string className, List<IGrouping<string, DeviceInfo>> devicesByType)
    {
        var sb = new System.Text.StringBuilder();

        // Using statements
        sb.AppendLine("using { /Fortnite.com/Devices }");
        sb.AppendLine("using { /Verse.org/Simulation }");
        sb.AppendLine("using { /UnrealEngine.com/Temporary/Diagnostics }");
        sb.AppendLine();

        // Class declaration
        sb.AppendLine($"{className} := class(creative_device):");
        sb.AppendLine();

        // @editable declarations grouped by device type
        sb.AppendLine("    # ─── Device References ───");
        sb.AppendLine("    # Wire these to the corresponding devices in your level.");
        sb.AppendLine();

        var editableNames = new List<(string Name, string VerseType, string DeviceClass, int Count)>();

        foreach (var group in devicesByType)
        {
            var deviceClass = group.Key;
            var verseType = MapToVerseType(deviceClass);
            if (verseType == null) continue; // Skip unknown device types

            var count = group.Count();
            var baseName = VerseVariableName(verseType);

            if (count == 1)
            {
                var device = group.First();
                var name = !string.IsNullOrEmpty(device.Label)
                    ? SanitizeVerseName(device.Label)
                    : baseName;

                sb.AppendLine($"    @editable {name} : {verseType} = {verseType}{{}}");
                editableNames.Add((name, verseType, deviceClass, 1));
            }
            else
            {
                // Multiple devices of same type — use array
                sb.AppendLine($"    # {count}x {PrettifyClassName(deviceClass)} in level");
                sb.AppendLine($"    @editable {baseName}s : []{verseType} = array{{}}");
                editableNames.Add(($"{baseName}s", verseType, deviceClass, count));
            }
        }

        if (editableNames.Count == 0)
        {
            sb.AppendLine("    # No recognized device types found in this level.");
            sb.AppendLine("    # Add @editable declarations manually for custom devices.");
        }

        sb.AppendLine();

        // OnBegin with event subscriptions
        sb.AppendLine("    OnBegin<override>()<suspends>:void =");
        sb.AppendLine("        Print(\"Device bindings initialized\")");
        sb.AppendLine();

        foreach (var (name, verseType, deviceClass, count) in editableNames)
        {
            if (!DeviceEvents.TryGetValue(verseType, out var events)) continue;

            sb.AppendLine($"        # ─── {PrettifyClassName(deviceClass)} ───");

            if (count > 1)
            {
                // Array — subscribe in loop
                sb.AppendLine($"        for (Device : {name}):");
                foreach (var evt in events)
                {
                    sb.AppendLine($"            Device.{evt}.Subscribe(Handle{verseType.Replace("_device", "").Replace("_", "")}_{evt})");
                }
                sb.AppendLine();
            }
            else
            {
                foreach (var evt in events)
                {
                    sb.AppendLine($"        {name}.{evt}.Subscribe(Handle{SanitizeVerseName(name)}_{evt})");
                }
                sb.AppendLine();
            }
        }

        // Generate handler stubs
        sb.AppendLine("    # ─── Event Handlers ───");
        sb.AppendLine();

        foreach (var (name, verseType, deviceClass, count) in editableNames)
        {
            if (!DeviceEvents.TryGetValue(verseType, out var events)) continue;

            var handlerPrefix = count > 1
                ? $"{verseType.Replace("_device", "").Replace("_", "")}"
                : SanitizeVerseName(name);

            foreach (var evt in events)
            {
                var paramType = GetEventParameterType(evt);
                sb.AppendLine($"    Handle{handlerPrefix}_{evt}({paramType}):void =");
                sb.AppendLine($"        Print(\"{name}.{evt} fired\")");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string? MapToVerseType(string deviceClass)
    {
        if (DeviceClassToVerseType.TryGetValue(deviceClass, out var verseType))
            return verseType;

        // Heuristic fallback: BP_XxxDevice_C → xxx_device
        if (deviceClass.StartsWith("BP_") && deviceClass.EndsWith("_C"))
        {
            var inner = deviceClass[3..^2]; // Strip BP_ and _C
            var normalized = Regex.Replace(inner, @"([a-z])([A-Z])", "$1_$2").ToLowerInvariant();
            if (!normalized.EndsWith("_device"))
                normalized += "_device";
            return normalized;
        }

        return null;
    }

    private static string VerseVariableName(string verseType)
    {
        // timer_device → Timer, button_device → Button
        var name = verseType.Replace("_device", "");
        var parts = name.Split('_');
        return string.Join("", parts.Select(p =>
            string.IsNullOrEmpty(p) ? "" : char.ToUpper(p[0]) + p[1..]));
    }

    private static string SanitizeVerseName(string name)
    {
        // Replace non-alphanumeric with underscore, ensure starts with letter
        var sanitized = Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");
        if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
            sanitized = "_" + sanitized;
        return sanitized;
    }

    private static string PrettifyClassName(string className)
    {
        return className.Replace("BP_", "").Replace("PBWA_", "").Replace("_C", "").Replace("_", " ");
    }

    private static string GetEventParameterType(string eventName)
    {
        // Most device events pass an agent parameter
        if (eventName.Contains("Eliminated") || eventName.Contains("Elimination"))
            return "Result : elimination_result";
        if (eventName.Contains("Agent") || eventName.Contains("Triggered") ||
            eventName.Contains("Entered") || eventName.Contains("Exited") ||
            eventName.Contains("Spawned") || eventName.Contains("Interacted"))
            return "Agent : agent";
        if (eventName.Contains("Score"))
            return "Agent : agent";
        if (eventName.Contains("Picked"))
            return "Agent : agent";
        if (eventName.Contains("Moved") || eventName.Contains("Completed"))
            return "";
        if (eventName.Contains("Button") || eventName.Contains("Responding"))
            return "Agent : agent";
        return "Agent : agent";
    }
}
