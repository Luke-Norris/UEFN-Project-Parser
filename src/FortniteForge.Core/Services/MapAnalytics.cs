using WellVersed.Core.Config;
using WellVersed.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;

namespace WellVersed.Core.Services;

/// <summary>
/// Analyzes maps across the reference library to find patterns, benchmark maps,
/// and generate insights. Works with the 92-project reference library to produce
/// data nobody else has.
/// </summary>
public class MapAnalytics
{
    private readonly WellVersedConfig _config;
    private readonly LibraryIndexer _libraryIndexer;
    private readonly ILogger<MapAnalytics> _logger;

    // Cache of library profiles once computed
    private List<MapProfile>? _libraryProfiles;

    // Device classes that signal specific gameplay features
    private static readonly Dictionary<string, string> FeatureDevices = new(StringComparer.OrdinalIgnoreCase)
    {
        // Scoring / Economy
        { "ScoreManager", "Score System" },
        { "score_manager", "Score System" },
        { "CurrencyManager", "Economy System" },
        { "currency_manager", "Economy System" },
        { "GoldManager", "Economy System" },

        // Combat
        { "EliminationManager", "Elimination Tracking" },
        { "elimination_manager", "Elimination Tracking" },
        { "DamageVolume", "Damage Zones" },
        { "damage_volume", "Damage Zones" },
        { "Sentry", "AI Enemies" },

        // Spawning / Teams
        { "SpawnPad", "Spawn System" },
        { "TeamSettings", "Team Configuration" },
        { "team_settings", "Team Configuration" },
        { "RoundSettings", "Round Management" },
        { "round_settings", "Round Management" },

        // UI Feedback
        { "HUDMessage", "HUD Feedback" },
        { "hud_message", "HUD Feedback" },
        { "Billboard", "HUD Feedback" },
        { "PopUp", "HUD Feedback" },
        { "MapIndicator", "Map Indicators" },
        { "map_indicator", "Map Indicators" },

        // Interaction
        { "Trigger", "Trigger Zones" },
        { "Button", "Interactive Buttons" },
        { "ItemGranter", "Item Granting" },
        { "item_granter", "Item Granting" },
        { "VendingMachine", "Vending Machines" },
        { "vending_machine", "Vending Machines" },
        { "Chest", "Loot Chests" },

        // Movement / Traversal
        { "Teleporter", "Teleporters" },
        { "LaunchPad", "Launch Pads" },
        { "Checkpoint", "Checkpoints" },
        { "checkpoint", "Checkpoints" },
        { "SpeedBoost", "Speed Boosts" },

        // Timing
        { "Timer", "Timers" },
        { "Countdown", "Countdown System" },
        { "Sequencer", "Sequencing" },
        { "Signal", "Signal System" },

        // Environment
        { "Barrier", "Barriers" },
        { "Mutator", "Mutator Zones" },
        { "StormController", "Storm/Zone" },
        { "storm_controller", "Storm/Zone" },

        // Audio / Visual
        { "Audio", "Audio System" },
        { "SFX", "Audio System" },
        { "VFX", "Visual Effects" },
        { "Particle", "Visual Effects" },
        { "Light", "Dynamic Lighting" },

        // Vehicles
        { "Vehicle", "Vehicles" },
        { "vehicle_spawner", "Vehicles" },

        // Building
        { "Prop", "Prop Manipulation" },
        { "PropMover", "Prop Manipulation" },
        { "PropManipulator", "Prop Manipulation" },

        // Advanced
        { "Conditional", "Conditional Logic" },
        { "conditional_button", "Conditional Logic" },
        { "Matchmaking", "Matchmaking" },
        { "Tracker", "Stat Tracking" },
        { "Leaderboard", "Leaderboards" },
    };

    // Genre classification rules: device pattern -> genre evidence
    private static readonly Dictionary<string, string[]> GenreSignals = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Battle Royale", new[] { "storm", "loot", "chest", "zone", "shrink", "circle", "safezone" } },
        { "Deathmatch", new[] { "elimination", "respawn", "ffa", "team_deathmatch", "kill", "arena" } },
        { "Tycoon", new[] { "tycoon", "currency", "shop", "buy", "upgrade", "earn", "gold", "collect" } },
        { "Parkour", new[] { "checkpoint", "timer", "obstacle", "jump", "parkour", "race", "speedrun" } },
        { "Horror", new[] { "horror", "scare", "dark", "jumpscare", "escape", "survival", "creature" } },
        { "Racing", new[] { "race", "vehicle", "lap", "finish_line", "speed", "boost", "track" } },
        { "Puzzle", new[] { "puzzle", "switch", "sequence", "unlock", "combination", "riddle" } },
        { "Creative Build", new[] { "prop_manipulator", "gallery", "prefab", "building" } },
        { "PvE", new[] { "sentry", "wave", "enemy", "defend", "npc", "ai_spawner", "horde" } },
        { "Prop Hunt", new[] { "prop_hunt", "disguise", "hide", "seek", "prop_o_matic" } },
        { "Box Fight", new[] { "box_fight", "box", "1v1", "arena" } },
        { "Zone Wars", new[] { "zone_war", "moving_zone", "storm", "endgame" } },
    };

    public MapAnalytics(
        WellVersedConfig config,
        LibraryIndexer libraryIndexer,
        ILogger<MapAnalytics> logger)
    {
        _config = config;
        _libraryIndexer = libraryIndexer;
        _logger = logger;
    }

    /// <summary>
    /// Generate a comprehensive profile of a single map project.
    /// </summary>
    public MapProfile ProfileMap(string projectPath)
    {
        var tempConfig = new WellVersedConfig { ProjectPath = projectPath };
        var profile = new MapProfile
        {
            ProjectName = tempConfig.ProjectName,
            ProjectPath = projectPath
        };

        var contentPath = tempConfig.ContentPath;
        if (!Directory.Exists(contentPath))
        {
            _logger.LogWarning("Content path not found for {Path}", projectPath);
            return profile;
        }

        // Count levels
        var levels = Directory.EnumerateFiles(contentPath, "*.umap", SearchOption.AllDirectories).ToList();
        profile.LevelCount = levels.Count;

        // Count verse files and extract classes
        var verseFiles = FindVerseFiles(projectPath);
        profile.VerseFileCount = verseFiles.Count;
        foreach (var vf in verseFiles)
        {
            try
            {
                var source = File.ReadAllText(vf);
                profile.VerseTotalLines += source.Split('\n').Length;
                var classes = Regex.Matches(source, @"(\w+)\s*:=\s*class\s*\(", RegexOptions.Multiline);
                foreach (Match m in classes)
                    profile.VerseClasses.Add(m.Groups[1].Value);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read verse file: {Path}", vf);
            }
        }

        // Count widgets
        profile.WidgetCount = CountWidgetBlueprints(contentPath);

        // Count user-created assets (not external actors)
        try
        {
            profile.UserAssetCount = Directory.EnumerateFiles(contentPath, "*.uasset", SearchOption.AllDirectories)
                .Count(f => !f.Contains("__External"));
        }
        catch { }

        // Analyze devices from external actors
        AnalyzeDevices(contentPath, levels, profile);

        // Classify genre
        profile.MapClassification = ClassifyGenre(profile);

        // Score dimensions
        profile.ComplexityScore = ScoreComplexity(profile);
        profile.PolishScore = ScorePolish(profile);
        profile.GameplayVarietyScore = ScoreGameplayVariety(profile);
        profile.OverallRating = ComputeOverallRating(profile);

        return profile;
    }

    /// <summary>
    /// Compare a map against all projects in the reference library.
    /// Requires the library index to be built first.
    /// </summary>
    public LibraryComparison CompareToLibrary(string projectPath)
    {
        var profile = ProfileMap(projectPath);
        var libraryProfiles = GetOrBuildLibraryProfiles();

        var comparison = new LibraryComparison
        {
            ProjectName = profile.ProjectName
        };

        if (libraryProfiles.Count == 0)
        {
            comparison.MissingFeatures.Add("Library index not available. Run build_library_index first.");
            return comparison;
        }

        // Percentile calculations
        comparison.DeviceCountPercentile = ComputePercentile(
            libraryProfiles.Select(p => p.TotalDevices).ToList(), profile.TotalDevices);
        comparison.WiringComplexityPercentile = ComputePercentile(
            libraryProfiles.Select(p => p.TotalWirings).ToList(), profile.TotalWirings);
        comparison.VerseUsagePercentile = ComputePercentile(
            libraryProfiles.Select(p => p.VerseFileCount).ToList(), profile.VerseFileCount);
        comparison.ActorCountPercentile = ComputePercentile(
            libraryProfiles.Select(p => p.TotalActors).ToList(), profile.TotalActors);
        comparison.WidgetUsagePercentile = ComputePercentile(
            libraryProfiles.Select(p => p.WidgetCount).ToList(), profile.WidgetCount);

        // Find most similar map by device composition
        var similar = FindSimilarMaps(profile, libraryProfiles, 1);
        if (similar.Count > 0)
        {
            comparison.MostSimilarMap = similar[0].ProjectName;
            comparison.SimilarityScore = similar[0].SimilarityScore;
        }

        // Identify missing features
        comparison.MissingFeatures = FindMissingFeatures(profile, libraryProfiles);

        // Identify strengths
        comparison.Strengths = FindStrengths(profile, comparison);

        return comparison;
    }

    /// <summary>
    /// Aggregate statistics across all projects in the reference library.
    /// </summary>
    public LibraryInsights GetLibraryInsights()
    {
        var profiles = GetOrBuildLibraryProfiles();
        var insights = new LibraryInsights
        {
            TotalProjects = profiles.Count,
            AnalyzedAt = DateTime.UtcNow
        };

        if (profiles.Count == 0)
            return insights;

        // Averages
        insights.AverageDeviceCount = Math.Round(profiles.Average(p => p.TotalDevices), 1);
        insights.AverageActorCount = Math.Round(profiles.Average(p => p.TotalActors), 1);
        insights.AverageVerseFileCount = Math.Round(profiles.Average(p => p.VerseFileCount), 1);
        insights.AverageVerseLines = Math.Round(profiles.Average(p => p.VerseTotalLines), 1);
        insights.AverageWiringCount = Math.Round(profiles.Average(p => p.TotalWirings), 1);

        // Median / Max
        var sortedDeviceCounts = profiles.Select(p => p.TotalDevices).OrderBy(x => x).ToList();
        insights.MedianDeviceCount = sortedDeviceCounts[sortedDeviceCounts.Count / 2];
        insights.MaxDeviceCount = sortedDeviceCounts.Last();
        insights.MaxDeviceProject = profiles.OrderByDescending(p => p.TotalDevices).First().ProjectName;

        // Top device types across all projects
        var globalDeviceCounts = new Dictionary<string, (int TotalInstances, HashSet<string> Projects)>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            foreach (var (deviceType, count) in profile.DevicesByType)
            {
                if (!globalDeviceCounts.ContainsKey(deviceType))
                    globalDeviceCounts[deviceType] = (0, new HashSet<string>());
                var (total, projects) = globalDeviceCounts[deviceType];
                globalDeviceCounts[deviceType] = (total + count, projects);
                projects.Add(profile.ProjectName);
            }
        }

        insights.TopDeviceTypes = globalDeviceCounts
            .OrderByDescending(kv => kv.Value.Projects.Count)
            .ThenByDescending(kv => kv.Value.TotalInstances)
            .Take(20)
            .Select(kv => new DeviceUsageStat
            {
                DeviceClass = kv.Key,
                DisplayName = CleanDeviceName(kv.Key),
                TotalInstances = kv.Value.TotalInstances,
                ProjectCount = kv.Value.Projects.Count
            })
            .ToList();

        // Feature adoption: what percentage of maps use each feature
        var featureCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            var features = DetectFeatures(profile);
            foreach (var feature in features)
            {
                featureCounts.TryGetValue(feature, out var c);
                featureCounts[feature] = c + 1;
            }
        }
        insights.FeatureAdoption = featureCounts
            .OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => (int)Math.Round(100.0 * kv.Value / profiles.Count));

        // Genre distribution
        insights.GenreDistribution = profiles
            .GroupBy(p => p.MapClassification)
            .ToDictionary(g => g.Key, g => g.Count());

        // Verse / Widget adoption
        insights.PercentWithVerse = (int)Math.Round(100.0 * profiles.Count(p => p.VerseFileCount > 0) / profiles.Count);
        insights.PercentWithWidgets = (int)Math.Round(100.0 * profiles.Count(p => p.WidgetCount > 0) / profiles.Count);

        return insights;
    }

    /// <summary>
    /// Generate a comprehensive report for a map project.
    /// </summary>
    public MapReport GenerateMapReport(string projectPath)
    {
        var profile = ProfileMap(projectPath);
        var libraryProfiles = GetOrBuildLibraryProfiles();
        LibraryComparison? comparison = null;

        if (libraryProfiles.Count > 0)
            comparison = CompareToLibrary(projectPath);

        var report = new MapReport
        {
            Profile = profile,
            Comparison = comparison
        };

        // Section 1: Overview
        var overview = new MapReportSection { Title = "Overview" };
        overview.Lines.Add($"Project: {profile.ProjectName}");
        overview.Lines.Add($"Classification: {profile.MapClassification}");
        overview.Lines.Add($"Overall Rating: {profile.OverallRating}");
        overview.Lines.Add($"Complexity: {profile.ComplexityScore}/100 | Polish: {profile.PolishScore}/100 | Variety: {profile.GameplayVarietyScore}/100");
        overview.Lines.Add($"Levels: {profile.LevelCount} | Actors: {profile.TotalActors:N0} | Devices: {profile.TotalDevices:N0}");
        overview.Lines.Add($"Verse Files: {profile.VerseFileCount} ({profile.VerseTotalLines:N0} lines) | Widgets: {profile.WidgetCount} | User Assets: {profile.UserAssetCount}");
        report.Sections.Add(overview);

        // Section 2: Device Breakdown
        var devices = new MapReportSection { Title = "Device Breakdown" };
        devices.Lines.Add($"Total Devices: {profile.TotalDevices} across {profile.DevicesByType.Count} types");
        foreach (var (type, count) in profile.DevicesByType.OrderByDescending(kv => kv.Value).Take(15))
            devices.Lines.Add($"  {CleanDeviceName(type)}: {count}");
        if (profile.DevicesByType.Count > 15)
            devices.Lines.Add($"  ... and {profile.DevicesByType.Count - 15} more types");
        report.Sections.Add(devices);

        // Section 3: Wiring Analysis
        var wiring = new MapReportSection { Title = "Wiring Analysis" };
        wiring.Lines.Add($"Total Wiring Connections: {profile.TotalWirings}");
        if (profile.TotalDevices > 0)
        {
            var ratio = (float)profile.TotalWirings / profile.TotalDevices;
            wiring.Lines.Add($"Wiring Density: {ratio:F2} connections per device");
            if (ratio < 0.1f && profile.TotalDevices > 10)
                wiring.Lines.Add("Low wiring density suggests many devices may be unconfigured or purely decorative.");
            else if (ratio > 1.0f)
                wiring.Lines.Add("High wiring density indicates a well-connected, logic-heavy map.");
        }
        report.Sections.Add(wiring);

        // Section 4: Verse Quality
        var verse = new MapReportSection { Title = "Verse Code" };
        if (profile.VerseFileCount == 0)
        {
            verse.Lines.Add("No Verse code found. This map relies entirely on device wiring for logic.");
            verse.Lines.Add("Consider adding Verse for custom game mechanics, UI, and advanced logic.");
        }
        else
        {
            verse.Lines.Add($"{profile.VerseFileCount} Verse files, {profile.VerseTotalLines:N0} total lines");
            if (profile.VerseClasses.Count > 0)
                verse.Lines.Add($"Classes: {string.Join(", ", profile.VerseClasses.Take(10))}");
            var avgLines = profile.VerseTotalLines / profile.VerseFileCount;
            if (avgLines < 20)
                verse.Lines.Add("Files are very short — may be stubs or minimal implementations.");
            else if (avgLines > 200)
                verse.Lines.Add("Files are substantial — indicates serious custom logic.");
        }
        report.Sections.Add(verse);

        // Section 5: Library Comparison
        if (comparison != null && libraryProfiles.Count > 0)
        {
            var comp = new MapReportSection { Title = $"Comparison to Library ({libraryProfiles.Count} projects)" };
            comp.Lines.Add($"Device Count: Top {100 - comparison.DeviceCountPercentile}% (more devices than {comparison.DeviceCountPercentile}% of maps)");
            comp.Lines.Add($"Wiring Complexity: Top {100 - comparison.WiringComplexityPercentile}% (more wiring than {comparison.WiringComplexityPercentile}% of maps)");
            comp.Lines.Add($"Verse Usage: Top {100 - comparison.VerseUsagePercentile}% (more verse than {comparison.VerseUsagePercentile}% of maps)");
            comp.Lines.Add($"Actor Count: Top {100 - comparison.ActorCountPercentile}% (more actors than {comparison.ActorCountPercentile}% of maps)");

            if (!string.IsNullOrEmpty(comparison.MostSimilarMap))
                comp.Lines.Add($"Most Similar Map: {comparison.MostSimilarMap} ({comparison.SimilarityScore:P0} similarity)");

            report.Sections.Add(comp);

            // Strengths
            if (comparison.Strengths.Count > 0)
            {
                var strengths = new MapReportSection { Title = "Strengths" };
                foreach (var s in comparison.Strengths)
                    strengths.Lines.Add($"  + {s}");
                report.Sections.Add(strengths);
            }

            // Suggestions
            if (comparison.MissingFeatures.Count > 0)
            {
                var suggestions = new MapReportSection { Title = "Suggestions" };
                suggestions.Lines.Add("Common features in the library that this map is missing:");
                foreach (var f in comparison.MissingFeatures)
                    suggestions.Lines.Add($"  - {f}");
                report.Sections.Add(suggestions);
            }
        }

        return report;
    }

    /// <summary>
    /// Find the N most similar maps to a target project by device composition.
    /// </summary>
    public List<SimilarMap> FindSimilarMaps(string projectPath, int topN = 5)
    {
        var profile = ProfileMap(projectPath);
        var libraryProfiles = GetOrBuildLibraryProfiles();
        return FindSimilarMaps(profile, libraryProfiles, topN);
    }

    // ===== Internal similarity search =====

    private List<SimilarMap> FindSimilarMaps(MapProfile target, List<MapProfile> library, int topN)
    {
        if (library.Count == 0)
            return new List<SimilarMap>();

        // Build device fingerprint vector for target
        var allDeviceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        allDeviceTypes.UnionWith(target.DevicesByType.Keys);
        foreach (var p in library)
            allDeviceTypes.UnionWith(p.DevicesByType.Keys);

        var orderedTypes = allDeviceTypes.OrderBy(t => t).ToList();
        var targetVector = BuildFingerprint(target, orderedTypes);

        var results = new List<SimilarMap>();
        foreach (var candidate in library)
        {
            // Skip comparing a project to itself
            if (candidate.ProjectPath.Equals(target.ProjectPath, StringComparison.OrdinalIgnoreCase))
                continue;

            var candidateVector = BuildFingerprint(candidate, orderedTypes);
            var similarity = CosineSimilarity(targetVector, candidateVector);

            var targetTypes = new HashSet<string>(target.DevicesByType.Keys, StringComparer.OrdinalIgnoreCase);
            var candidateTypes = new HashSet<string>(candidate.DevicesByType.Keys, StringComparer.OrdinalIgnoreCase);

            results.Add(new SimilarMap
            {
                ProjectName = candidate.ProjectName,
                ProjectPath = candidate.ProjectPath,
                SimilarityScore = similarity,
                SharedDeviceTypes = targetTypes.Intersect(candidateTypes, StringComparer.OrdinalIgnoreCase).ToList(),
                UniqueDeviceTypes = candidateTypes.Except(targetTypes, StringComparer.OrdinalIgnoreCase).ToList(),
                DeviceCount = candidate.TotalDevices,
                VerseFileCount = candidate.VerseFileCount,
                Classification = candidate.MapClassification
            });
        }

        return results
            .OrderByDescending(r => r.SimilarityScore)
            .Take(topN)
            .ToList();
    }

    // ===== Profiling helpers =====

    private void AnalyzeDevices(string contentPath, List<string> levels, MapProfile profile)
    {
        // Scan external actors for device info
        var externalActorDirs = new List<string>();
        try
        {
            externalActorDirs = Directory.EnumerateDirectories(contentPath, "__ExternalActors__", SearchOption.AllDirectories).ToList();
        }
        catch { }

        foreach (var extDir in externalActorDirs)
        {
            foreach (var file in Directory.EnumerateFiles(extDir, "*.uasset", SearchOption.AllDirectories))
            {
                try
                {
                    var asset = new UAsset(file, EngineVersion.VER_UE5_4);
                    profile.TotalActors++;

                    foreach (var export in asset.Exports)
                    {
                        var className = export.GetExportClassType()?.ToString() ?? "";
                        if (string.IsNullOrEmpty(className) || className == "Level" || className == "MetaData") continue;

                        // Skip components
                        if (className.Contains("Component") || className.Contains("Model")) continue;

                        if (IsDevice(className))
                        {
                            profile.TotalDevices++;
                            profile.DevicesByType.TryGetValue(className, out var cnt);
                            profile.DevicesByType[className] = cnt + 1;
                        }

                        // Categorize
                        var category = CategorizeActor(className);
                        profile.ActorsByCategory.TryGetValue(category, out var catCnt);
                        profile.ActorsByCategory[category] = catCnt + 1;

                        // Count wiring
                        if (export is NormalExport normalExport)
                            profile.TotalWirings += CountWirings(normalExport);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not parse external actor: {Path}", file);
                }
            }
        }

        // Also scan level files for actors
        foreach (var levelPath in levels)
        {
            try
            {
                var asset = new UAsset(levelPath, EngineVersion.VER_UE5_4);
                var levelExport = asset.Exports.OfType<LevelExport>().FirstOrDefault();
                if (levelExport == null) continue;

                foreach (var actorRef in levelExport.Actors)
                {
                    if (!actorRef.IsExport()) continue;
                    var exportIdx = actorRef.Index - 1;
                    if (exportIdx < 0 || exportIdx >= asset.Exports.Count) continue;

                    var export = asset.Exports[exportIdx];
                    var className = export.GetExportClassType()?.ToString() ?? "";
                    if (string.IsNullOrEmpty(className)) continue;

                    profile.TotalActors++;

                    if (IsDevice(className))
                    {
                        profile.TotalDevices++;
                        profile.DevicesByType.TryGetValue(className, out var cnt);
                        profile.DevicesByType[className] = cnt + 1;
                    }

                    var category = CategorizeActor(className);
                    profile.ActorsByCategory.TryGetValue(category, out var catCnt);
                    profile.ActorsByCategory[category] = catCnt + 1;

                    if (export is NormalExport ne)
                        profile.TotalWirings += CountWirings(ne);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not parse level: {Path}", levelPath);
            }
        }
    }

    private static int CountWirings(NormalExport export)
    {
        int count = 0;
        foreach (var prop in export.Data)
        {
            var propName = prop.Name?.ToString() ?? "";
            if (prop is UAssetAPI.PropertyTypes.Objects.ArrayPropertyData arrayProp && arrayProp.Value != null)
            {
                if (propName.Contains("Binding", StringComparison.OrdinalIgnoreCase) ||
                    propName.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
                    propName.Contains("Wire", StringComparison.OrdinalIgnoreCase) ||
                    propName.Contains("Link", StringComparison.OrdinalIgnoreCase))
                {
                    count += arrayProp.Value.Length;
                }
            }
        }
        return count;
    }

    private static List<string> FindVerseFiles(string projectPath)
    {
        var results = new List<string>();

        // Search in Plugins/*/Content/ (UEFN layout)
        var pluginsDir = Path.Combine(projectPath, "Plugins");
        if (Directory.Exists(pluginsDir))
        {
            try
            {
                results.AddRange(Directory.EnumerateFiles(pluginsDir, "*.verse", SearchOption.AllDirectories));
            }
            catch { }
        }

        // Also search Content/ directly
        var contentDir = Path.Combine(projectPath, "Content");
        if (Directory.Exists(contentDir))
        {
            try
            {
                results.AddRange(Directory.EnumerateFiles(contentDir, "*.verse", SearchOption.AllDirectories));
            }
            catch { }
        }

        return results.Distinct().ToList();
    }

    private static int CountWidgetBlueprints(string contentPath)
    {
        int count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(contentPath, "*.uasset", SearchOption.AllDirectories)
                .Where(f => !f.Contains("__External")))
            {
                try
                {
                    var asset = new UAsset(file, EngineVersion.VER_UE5_4);
                    var primaryClass = asset.Exports.FirstOrDefault()?.GetExportClassType()?.ToString() ?? "";
                    if (primaryClass.Contains("WidgetBlueprint", StringComparison.OrdinalIgnoreCase))
                        count++;
                }
                catch { }
            }
        }
        catch { }
        return count;
    }

    // ===== Classification =====

    private string ClassifyGenre(MapProfile profile)
    {
        // Score each genre by matching device types and verse class names
        var genreScores = new Dictionary<string, int>();

        // Build a searchable text from all device types and verse classes
        var allSignals = string.Join(" ",
            profile.DevicesByType.Keys.Concat(profile.VerseClasses)
        ).ToLowerInvariant();

        foreach (var (genre, signals) in GenreSignals)
        {
            int score = 0;
            foreach (var signal in signals)
            {
                if (allSignals.Contains(signal, StringComparison.OrdinalIgnoreCase))
                    score += 2;
            }

            // Also check project name
            if (profile.ProjectName.Contains(genre.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
                genre.Split(' ').Any(w => profile.ProjectName.Contains(w, StringComparison.OrdinalIgnoreCase)))
                score += 3;

            if (score > 0)
                genreScores[genre] = score;
        }

        // Additional heuristic rules
        if (profile.DevicesByType.Any(kv => kv.Key.Contains("Storm", StringComparison.OrdinalIgnoreCase)))
            genreScores["Battle Royale"] = genreScores.GetValueOrDefault("Battle Royale") + 5;

        if (profile.DevicesByType.Any(kv => kv.Key.Contains("Checkpoint", StringComparison.OrdinalIgnoreCase))
            && !profile.DevicesByType.Any(kv => kv.Key.Contains("Elimination", StringComparison.OrdinalIgnoreCase)))
            genreScores["Parkour"] = genreScores.GetValueOrDefault("Parkour") + 3;

        if (genreScores.Count == 0)
            return "General Creative";

        return genreScores.OrderByDescending(kv => kv.Value).First().Key;
    }

    // ===== Scoring =====

    private static int ScoreComplexity(MapProfile profile)
    {
        double score = 0;

        // Device count contribution (0-30)
        score += Math.Min(30, profile.TotalDevices / 5.0);

        // Wiring contribution (0-25)
        score += Math.Min(25, profile.TotalWirings / 3.0);

        // Verse contribution (0-25)
        score += Math.Min(15, profile.VerseFileCount * 3.0);
        score += Math.Min(10, profile.VerseTotalLines / 50.0);

        // Actor count contribution (0-10)
        score += Math.Min(10, profile.TotalActors / 100.0);

        // Widget contribution (0-10)
        score += Math.Min(10, profile.WidgetCount * 5.0);

        return (int)Math.Clamp(score, 0, 100);
    }

    private static int ScorePolish(MapProfile profile)
    {
        double score = 50; // Start at baseline

        // Verse code is a strong polish signal
        if (profile.VerseFileCount > 0) score += 10;
        if (profile.VerseTotalLines > 100) score += 5;
        if (profile.VerseTotalLines > 500) score += 5;

        // Widgets indicate UI work
        if (profile.WidgetCount > 0) score += 10;
        if (profile.WidgetCount > 3) score += 5;

        // Variety of device types suggests intentional design
        if (profile.DevicesByType.Count > 5) score += 5;
        if (profile.DevicesByType.Count > 15) score += 5;

        // Multiple levels suggests scope
        if (profile.LevelCount > 1) score += 5;

        // Wiring suggests configuration
        if (profile.TotalWirings > 0) score += 5;
        if (profile.TotalDevices > 0 && (float)profile.TotalWirings / profile.TotalDevices > 0.5f)
            score += 5;

        // Too few devices suggests incomplete
        if (profile.TotalDevices < 3) score -= 20;

        return (int)Math.Clamp(score, 0, 100);
    }

    private static int ScoreGameplayVariety(MapProfile profile)
    {
        double score = 0;

        // Unique device type count is the primary driver
        score += Math.Min(40, profile.DevicesByType.Count * 3.0);

        // Feature categories detected
        var features = DetectFeatures(profile);
        score += Math.Min(40, features.Count * 6.0);

        // Verse classes add variety
        score += Math.Min(10, profile.VerseClasses.Count * 3.0);

        // Widgets add variety
        score += Math.Min(10, profile.WidgetCount * 5.0);

        return (int)Math.Clamp(score, 0, 100);
    }

    private static string ComputeOverallRating(MapProfile profile)
    {
        var avg = (profile.ComplexityScore + profile.PolishScore + profile.GameplayVarietyScore) / 3.0;
        return avg switch
        {
            >= 80 => "S",
            >= 65 => "A",
            >= 45 => "B",
            >= 25 => "C",
            _ => "D"
        };
    }

    // ===== Feature detection =====

    private static HashSet<string> DetectFeatures(MapProfile profile)
    {
        var features = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (deviceType, _) in profile.DevicesByType)
        {
            foreach (var (pattern, feature) in FeatureDevices)
            {
                if (deviceType.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    features.Add(feature);
            }
        }

        if (profile.VerseFileCount > 0)
            features.Add("Custom Verse Logic");

        if (profile.WidgetCount > 0)
            features.Add("Custom UI Widgets");

        return features;
    }

    // ===== Library comparison helpers =====

    private List<string> FindMissingFeatures(MapProfile profile, List<MapProfile> library)
    {
        var targetFeatures = DetectFeatures(profile);
        var missing = new List<string>();

        // Count how many library maps have each feature
        var featureCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var libProfile in library)
        {
            foreach (var feature in DetectFeatures(libProfile))
            {
                featureCounts.TryGetValue(feature, out var c);
                featureCounts[feature] = c + 1;
            }
        }

        // Features present in 30%+ of library maps but absent in the target
        var threshold = library.Count * 0.30;
        foreach (var (feature, count) in featureCounts.OrderByDescending(kv => kv.Value))
        {
            if (count >= threshold && !targetFeatures.Contains(feature))
                missing.Add($"{feature} (used by {(int)Math.Round(100.0 * count / library.Count)}% of library maps)");
        }

        return missing.Take(8).ToList();
    }

    private static List<string> FindStrengths(MapProfile profile, LibraryComparison comparison)
    {
        var strengths = new List<string>();

        if (comparison.DeviceCountPercentile >= 75)
            strengths.Add($"Device-rich map (more devices than {comparison.DeviceCountPercentile}% of library maps)");
        if (comparison.WiringComplexityPercentile >= 75)
            strengths.Add($"Well-wired (more connections than {comparison.WiringComplexityPercentile}% of library maps)");
        if (comparison.VerseUsagePercentile >= 75)
            strengths.Add($"Strong Verse usage (more code than {comparison.VerseUsagePercentile}% of library maps)");
        if (comparison.WidgetUsagePercentile >= 75)
            strengths.Add($"Custom UI (more widgets than {comparison.WidgetUsagePercentile}% of library maps)");

        if (profile.GameplayVarietyScore >= 70)
            strengths.Add("High gameplay variety — uses many different device types");
        if (profile.PolishScore >= 70)
            strengths.Add("Well-polished — verse code, UI, and wiring all present");
        if (profile.VerseClasses.Count >= 3)
            strengths.Add($"Custom Verse classes: {string.Join(", ", profile.VerseClasses.Take(5))}");

        return strengths;
    }

    // ===== Library profile management =====

    private List<MapProfile> GetOrBuildLibraryProfiles()
    {
        if (_libraryProfiles != null)
            return _libraryProfiles;

        var index = _libraryIndexer.Index;
        if (index == null || index.Projects.Count == 0)
        {
            _logger.LogWarning("Library index not available. Call build_library_index first.");
            return new List<MapProfile>();
        }

        _logger.LogInformation("Building profiles for {Count} library projects...", index.Projects.Count);
        _libraryProfiles = new List<MapProfile>();

        foreach (var project in index.Projects)
        {
            try
            {
                var profile = BuildProfileFromIndex(project);
                _libraryProfiles.Add(profile);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not profile library project: {Name}", project.Name);
            }
        }

        _logger.LogInformation("Built {Count} library profiles", _libraryProfiles.Count);
        return _libraryProfiles;
    }

    /// <summary>
    /// Build a lightweight MapProfile from the already-indexed project data,
    /// avoiding a full re-scan of assets.
    /// </summary>
    private MapProfile BuildProfileFromIndex(ProjectIndexEntry project)
    {
        var profile = new MapProfile
        {
            ProjectName = project.Name,
            ProjectPath = project.Path
        };

        // Verse data from index
        profile.VerseFileCount = project.VerseFiles.Count;
        profile.VerseTotalLines = project.VerseFiles.Sum(vf => vf.LineCount);
        profile.VerseClasses = project.VerseFiles.SelectMany(vf => vf.Classes).Distinct().ToList();

        // Device data from index
        foreach (var dt in project.DeviceTypes)
        {
            profile.DevicesByType[dt.ClassName] = dt.Count;
            profile.TotalDevices += dt.Count;
        }

        // Widget count from assets
        profile.WidgetCount = project.Assets.Count(a =>
            a.AssetClass.Contains("WidgetBlueprint", StringComparison.OrdinalIgnoreCase));

        // User asset count
        profile.UserAssetCount = project.Assets.Count;

        // Level count: count .umap files if available
        if (Directory.Exists(project.ContentPath))
        {
            try
            {
                profile.LevelCount = Directory.EnumerateFiles(project.ContentPath, "*.umap", SearchOption.AllDirectories).Count();
            }
            catch { }
        }

        // Estimate total actors from device counts (devices are a subset of actors)
        // We use a rough multiplier since the index only samples external actors
        profile.TotalActors = profile.TotalDevices * 3; // rough estimate

        // We cannot easily get wiring from the index, so set to 0
        // A full ProfileMap scan would be needed for accurate wiring counts

        // Classify and score
        profile.MapClassification = ClassifyGenre(profile);
        profile.ComplexityScore = ScoreComplexity(profile);
        profile.PolishScore = ScorePolish(profile);
        profile.GameplayVarietyScore = ScoreGameplayVariety(profile);
        profile.OverallRating = ComputeOverallRating(profile);

        return profile;
    }

    // ===== Math utilities =====

    private static float[] BuildFingerprint(MapProfile profile, List<string> orderedTypes)
    {
        var vector = new float[orderedTypes.Count];
        for (int i = 0; i < orderedTypes.Count; i++)
        {
            if (profile.DevicesByType.TryGetValue(orderedTypes[i], out var count))
                vector[i] = count;
        }
        return vector;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;

        double dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        if (magA == 0 || magB == 0) return 0;
        return (float)(dot / (Math.Sqrt(magA) * Math.Sqrt(magB)));
    }

    private static int ComputePercentile(List<int> values, int target)
    {
        if (values.Count == 0) return 50;
        var sorted = values.OrderBy(v => v).ToList();
        var belowCount = sorted.Count(v => v < target);
        return (int)Math.Round(100.0 * belowCount / sorted.Count);
    }

    // ===== Actor classification (mirrors LevelAnalyticsService) =====

    private static readonly string[] DevicePatterns = {
        "device", "spawner", "trigger", "mutator", "granter", "barrier",
        "teleporter", "zone", "volume", "prop_mover", "BP_", "PBWA_"
    };

    private static bool IsDevice(string className)
    {
        var lower = className.ToLowerInvariant();
        return DevicePatterns.Any(p => lower.Contains(p.ToLowerInvariant()));
    }

    private static string CategorizeActor(string className)
    {
        var lower = className.ToLowerInvariant();
        if (IsDevice(className)) return "Device";
        if (lower.Contains("light")) return "Light";
        if (lower.Contains("staticmesh") || lower.Contains("mesh")) return "StaticMesh";
        if (lower.Contains("landscape") || lower.Contains("terrain")) return "Terrain";
        if (lower.Contains("foliage") || lower.Contains("tree") || lower.Contains("grass")) return "Foliage";
        if (lower.Contains("volume")) return "Volume";
        if (lower.Contains("camera")) return "Camera";
        if (lower.Contains("player") || lower.Contains("spawn")) return "PlayerStart";
        return "Other";
    }

    private static string CleanDeviceName(string className)
    {
        return className
            .Replace("Device_", "")
            .Replace("_C", "")
            .Replace("BP_", "")
            .Replace("PBWA_", "")
            .Replace("_", " ")
            .Trim();
    }
}
