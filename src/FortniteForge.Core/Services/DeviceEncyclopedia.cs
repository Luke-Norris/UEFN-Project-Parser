using WellVersed.Core.Config;
using Microsoft.Extensions.Logging;

namespace WellVersed.Core.Services;

/// <summary>
/// A searchable encyclopedia of UEFN Creative Devices, combining digest schemas
/// with real-world property usage statistics from reference projects.
/// Provides fuzzy search, common configurations ("recipes"), and property
/// co-occurrence suggestions.
/// </summary>
public class DeviceEncyclopedia
{
    private readonly DigestService _digestService;
    private readonly LibraryIndexer _libraryIndexer;
    private readonly ILogger<DeviceEncyclopedia> _logger;

    // Cached analysis results — built lazily from the library index
    private Dictionary<string, DeviceUsageProfile>? _usageProfiles;
    private List<DeviceReferenceEntry>? _referenceEntries;
    private bool _analysisBuilt;

    // Pre-built common configurations for popular device types
    private static readonly Dictionary<string, List<CommonConfiguration>> BuiltInConfigs = BuildCommonConfigs();

    // Static co-occurrence groups: properties that are commonly set together
    private static readonly Dictionary<string, List<string>> PropertyCoOccurrenceGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TeamProperties"] = new() { "TeamIndex", "AffectsAllTeams", "ExcludeTeam", "TriggeringTeam", "BlockingTeam" },
        ["TimerProperties"] = new() { "Duration", "Delay", "CooldownTime", "AutoReset", "CountDirection" },
        ["DamageProperties"] = new() { "DamageAmount", "DamagePerTick", "TickRate", "DamageType" },
        ["SpawnProperties"] = new() { "SpawnCount", "RespawnTime", "SpawnOrder", "bRandomizeSpawn" },
        ["VisibilityProperties"] = new() { "bStartEnabled", "bVisibleInGame", "bHiddenInGame", "Opacity" },
        ["ScoreProperties"] = new() { "ScoreToAward", "ScoreToWin", "ScorePerElimination" }
    };

    public DeviceEncyclopedia(
        DigestService digestService,
        LibraryIndexer libraryIndexer,
        ILogger<DeviceEncyclopedia> logger)
    {
        _digestService = digestService;
        _libraryIndexer = libraryIndexer;
        _logger = logger;
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Fuzzy search across device names, property names, event names, and descriptions.
    /// Returns ranked results with match context.
    /// </summary>
    public List<EncyclopediaSearchResult> SearchDevices(string query)
    {
        var results = new List<EncyclopediaSearchResult>();
        var queryLower = query.ToLowerInvariant();
        var keywords = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Search digest schemas
        var schemas = _digestService.SearchSchemas(query);
        foreach (var schema in schemas)
        {
            var score = ScoreDeviceMatch(schema, keywords);
            var matchedProperties = schema.Properties
                .Where(p => keywords.Any(k =>
                    p.Name.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                    p.Type.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .Select(p => p.Name)
                .ToList();

            var matchedEvents = schema.Events
                .Where(e => keywords.Any(k => e.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            results.Add(new EncyclopediaSearchResult
            {
                DeviceName = schema.Name,
                DisplayName = PrettifyName(schema.Name),
                ParentClass = schema.ParentClass,
                Score = score,
                PropertyCount = schema.Properties.Count,
                EventCount = schema.Events.Count,
                FunctionCount = schema.Functions.Count,
                MatchedProperties = matchedProperties,
                MatchedEvents = matchedEvents,
                HasCommonConfigs = BuiltInConfigs.ContainsKey(NormalizeDeviceName(schema.Name)),
                UsageCount = GetUsageCount(schema.Name)
            });
        }

        // Also search built-in config names/descriptions
        foreach (var (deviceKey, configs) in BuiltInConfigs)
        {
            if (results.Any(r => NormalizeDeviceName(r.DeviceName) == deviceKey))
                continue;

            var configScore = 0;
            var matchedConfigNames = new List<string>();
            foreach (var config in configs)
            {
                foreach (var kw in keywords)
                {
                    if (config.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                        config.Description.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    {
                        configScore += kw.Length;
                        matchedConfigNames.Add(config.Name);
                    }
                }
            }

            if (configScore > 0)
            {
                var schema = _digestService.GetDeviceSchema(deviceKey);
                results.Add(new EncyclopediaSearchResult
                {
                    DeviceName = deviceKey,
                    DisplayName = PrettifyName(deviceKey),
                    ParentClass = schema?.ParentClass ?? "",
                    Score = configScore,
                    PropertyCount = schema?.Properties.Count ?? 0,
                    EventCount = schema?.Events.Count ?? 0,
                    FunctionCount = schema?.Functions.Count ?? 0,
                    MatchedProperties = new List<string>(),
                    MatchedEvents = new List<string>(),
                    HasCommonConfigs = true,
                    MatchContext = $"Matched configs: {string.Join(", ", matchedConfigNames.Distinct().Take(3))}",
                    UsageCount = GetUsageCount(deviceKey)
                });
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.UsageCount)
            .Take(30)
            .ToList();
    }

    /// <summary>
    /// Gets the full reference for a device type: schema properties with types/defaults,
    /// usage statistics from real maps, and common configurations.
    /// </summary>
    public DeviceReference? GetDeviceReference(string deviceClass)
    {
        var normalized = NormalizeDeviceName(deviceClass);
        var schema = _digestService.GetDeviceSchema(deviceClass)
                     ?? _digestService.GetDeviceSchema(normalized);

        if (schema == null)
        {
            // Try searching for partial match
            var candidates = _digestService.SearchSchemas(deviceClass);
            schema = candidates.FirstOrDefault();
        }

        if (schema == null)
            return null;

        var profile = GetUsageProfile(schema.Name);
        var configs = GetCommonConfigurations(schema.Name);

        var propertyRefs = schema.Properties.Select(p =>
        {
            var usage = profile?.PropertyUsage.GetValueOrDefault(p.Name);
            return new PropertyReference
            {
                Name = p.Name,
                Type = p.Type,
                DefaultValue = p.DefaultValue,
                IsEditable = p.IsEditable,
                UsagePercent = usage?.UsagePercent ?? 0,
                CommonValues = usage?.CommonValues ?? new List<ValueFrequency>(),
                Description = InferPropertyDescription(p.Name, p.Type),
                RelatedProperties = GetRelatedProperties(schema.Name, p.Name)
            };
        }).ToList();

        // Sort: most commonly used properties first, then alphabetical
        propertyRefs = propertyRefs
            .OrderByDescending(p => p.UsagePercent)
            .ThenBy(p => p.Name)
            .ToList();

        return new DeviceReference
        {
            DeviceName = schema.Name,
            DisplayName = PrettifyName(schema.Name),
            ParentClass = schema.ParentClass,
            SourceFile = schema.SourceFile,
            Properties = propertyRefs,
            Events = schema.Events,
            Functions = schema.Functions,
            CommonConfigurations = configs,
            TotalUsageCount = profile?.TotalInstances ?? 0,
            ProjectsUsedIn = profile?.ProjectCount ?? 0,
            Description = InferDeviceDescription(schema.Name, schema.ParentClass)
        };
    }

    /// <summary>
    /// Gets pre-built common configurations for a device type.
    /// These are curated "recipes" like "one-shot trigger" or "team-only spawner".
    /// </summary>
    public List<CommonConfiguration> GetCommonConfigurations(string deviceClass)
    {
        var normalized = NormalizeDeviceName(deviceClass);
        var results = new List<CommonConfiguration>();

        // Check built-in configs
        if (BuiltInConfigs.TryGetValue(normalized, out var builtIn))
            results.AddRange(builtIn);

        // Also check with common prefix/suffix variations
        foreach (var variant in GetNameVariants(deviceClass))
        {
            if (BuiltInConfigs.TryGetValue(variant, out var variantConfigs))
            {
                foreach (var cfg in variantConfigs)
                {
                    if (!results.Any(r => r.Name == cfg.Name))
                        results.Add(cfg);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Gets usage statistics for a specific property across the reference library.
    /// Shows how often it's set to non-default values and what values are common.
    /// </summary>
    public PropertyUsageStats? GetPropertyUsageStats(string deviceClass, string propertyName)
    {
        var profile = GetUsageProfile(deviceClass);
        if (profile == null) return null;

        if (!profile.PropertyUsage.TryGetValue(propertyName, out var usage))
        {
            // Try case-insensitive
            var key = profile.PropertyUsage.Keys
                .FirstOrDefault(k => k.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
            if (key != null)
                usage = profile.PropertyUsage[key];
        }

        return usage;
    }

    /// <summary>
    /// Suggests related properties based on co-occurrence patterns.
    /// "If you're setting property X, you probably also want to set Y."
    /// </summary>
    public List<string> SuggestRelatedProperties(string deviceClass, string propertyName)
    {
        return GetRelatedProperties(deviceClass, propertyName);
    }

    /// <summary>
    /// Computes a letter-grade health score for a set of devices.
    /// Evaluates wiring coverage, property customization, presence of game management
    /// and UI feedback devices.
    /// </summary>
    public DeviceHealthScore GetHealthScore(List<Models.DeviceInfo> devices)
    {
        if (devices.Count == 0)
            return new DeviceHealthScore { Score = 0, Grade = "F", Summary = "No devices to evaluate." };

        var totalDevices = devices.Count;

        // % of devices that have at least one wiring connection
        var wiredDevices = devices.Count(d => d.Wiring.Count > 0);
        var wiredPercent = (double)wiredDevices / totalDevices;

        // % of devices that have at least one non-default property set
        var customizedDevices = devices.Count(d => d.Properties.Any(p =>
            !string.IsNullOrEmpty(p.Value) &&
            p.Value != "0" && p.Value != "False" && p.Value != "null" && p.Value != "None"));
        var customizedPercent = totalDevices > 0 ? (double)customizedDevices / totalDevices : 0;

        // Presence of game management devices
        var hasSpawner = devices.Any(d =>
            d.DeviceType.Contains("Spawn", StringComparison.OrdinalIgnoreCase));
        var hasScoreManager = devices.Any(d =>
            d.DeviceType.Contains("Score", StringComparison.OrdinalIgnoreCase) &&
            d.DeviceType.Contains("Manager", StringComparison.OrdinalIgnoreCase));

        // Presence of UI feedback devices
        var hasHud = devices.Any(d =>
            d.DeviceType.Contains("HUD", StringComparison.OrdinalIgnoreCase) ||
            d.DeviceType.Contains("Billboard", StringComparison.OrdinalIgnoreCase) ||
            d.DeviceClass.Contains("HUDMessage", StringComparison.OrdinalIgnoreCase));

        // Compute weighted score (0-100)
        var score = 0.0;
        score += wiredPercent * 40;          // 40 points for wiring coverage
        score += customizedPercent * 30;     // 30 points for property customization
        if (hasSpawner) score += 10;         // 10 points for spawner presence
        if (hasScoreManager) score += 10;    // 10 points for score management
        if (hasHud) score += 10;             // 10 points for UI feedback

        var intScore = Math.Clamp((int)Math.Round(score), 0, 100);
        var grade = intScore switch
        {
            >= 95 => "A+",
            >= 90 => "A",
            >= 85 => "B+",
            >= 80 => "B",
            >= 70 => "C",
            >= 60 => "D",
            _ => "F"
        };

        var orphanedCount = totalDevices - wiredDevices;
        var summary = $"{wiredDevices}/{totalDevices} devices wired ({wiredPercent:P0}), " +
                       $"{customizedDevices} customized. " +
                       (orphanedCount > 0 ? $"{orphanedCount} orphaned devices. " : "") +
                       (!hasSpawner ? "Missing spawner. " : "") +
                       (!hasHud ? "Missing HUD feedback. " : "");

        return new DeviceHealthScore
        {
            Score = intScore,
            Grade = grade,
            Summary = summary.TrimEnd(),
            WiredPercent = wiredPercent,
            CustomizedPercent = customizedPercent,
            OrphanedDeviceCount = orphanedCount,
            HasGameManagement = hasSpawner || hasScoreManager,
            HasUIFeedback = hasHud
        };
    }

    /// <summary>
    /// Lists all device types known from digest files, enriched with usage counts.
    /// </summary>
    public List<DeviceSummary> ListAllDevices()
    {
        var types = _digestService.ListDeviceTypes();
        return types.Select(t =>
        {
            var schema = _digestService.GetDeviceSchema(t);
            return new DeviceSummary
            {
                Name = t,
                DisplayName = PrettifyName(t),
                ParentClass = schema?.ParentClass ?? "",
                PropertyCount = schema?.Properties.Count ?? 0,
                EventCount = schema?.Events.Count ?? 0,
                FunctionCount = schema?.Functions.Count ?? 0,
                UsageCount = GetUsageCount(t),
                HasCommonConfigs = BuiltInConfigs.ContainsKey(NormalizeDeviceName(t))
            };
        })
        .OrderByDescending(d => d.UsageCount)
        .ThenBy(d => d.DisplayName)
        .ToList();
    }

    // ─── Analysis Builders ───────────────────────────────────────────────────

    /// <summary>
    /// Builds usage analysis from the library index data.
    /// Call this after the library has been indexed for enriched results.
    /// </summary>
    public void BuildAnalysis()
    {
        _usageProfiles = new Dictionary<string, DeviceUsageProfile>(StringComparer.OrdinalIgnoreCase);
        _referenceEntries = new List<DeviceReferenceEntry>();

        var index = _libraryIndexer.Index;
        if (index == null)
        {
            _analysisBuilt = true;
            _logger.LogInformation("No library index available — encyclopedia will use schema data only.");
            return;
        }

        // Aggregate device type usage across all projects
        foreach (var project in index.Projects)
        {
            foreach (var dt in project.DeviceTypes)
            {
                var normalized = NormalizeDeviceName(dt.ClassName);
                if (!_usageProfiles.TryGetValue(normalized, out var profile))
                {
                    profile = new DeviceUsageProfile { DeviceClass = normalized };
                    _usageProfiles[normalized] = profile;
                }

                profile.TotalInstances += dt.Count;
                profile.ProjectCount++;

                _referenceEntries.Add(new DeviceReferenceEntry
                {
                    DeviceClass = dt.ClassName,
                    ProjectName = project.Name,
                    InstanceCount = dt.Count
                });
            }
        }

        _analysisBuilt = true;
        _logger.LogInformation(
            "Built encyclopedia analysis: {DeviceTypes} device types across {Projects} projects.",
            _usageProfiles.Count,
            index.Projects.Count);
    }

    // ─── Private Helpers ─────────────────────────────────────────────────────

    private DeviceUsageProfile? GetUsageProfile(string deviceClass)
    {
        if (!_analysisBuilt) BuildAnalysis();

        var normalized = NormalizeDeviceName(deviceClass);
        DeviceUsageProfile? profile = null;
        _usageProfiles?.TryGetValue(normalized, out profile);

        // Try variants
        if (profile == null)
        {
            foreach (var variant in GetNameVariants(deviceClass))
            {
                if (_usageProfiles?.TryGetValue(variant, out profile) == true)
                    break;
            }
        }

        return profile;
    }

    private int GetUsageCount(string deviceClass)
    {
        var profile = GetUsageProfile(deviceClass);
        return profile?.TotalInstances ?? 0;
    }

    private List<string> GetRelatedProperties(string deviceClass, string propertyName)
    {
        var related = new List<string>();

        // Check co-occurrence groups: if the property belongs to a known group,
        // suggest the other properties in that group
        foreach (var (_, groupProps) in PropertyCoOccurrenceGroups)
        {
            if (groupProps.Any(p => p.Equals(propertyName, StringComparison.OrdinalIgnoreCase)))
            {
                related.AddRange(groupProps);
            }
        }

        // If no co-occurrence group matched, fall back to the device schema
        // and suggest properties with similar naming patterns
        if (related.Count == 0)
        {
            var schema = _digestService.GetDeviceSchema(deviceClass);
            if (schema != null)
            {
                var propLower = propertyName.ToLowerInvariant();
                // Extract the semantic root of the property name (e.g., "Damage" from "DamageAmount")
                var roots = ExtractNameRoots(propLower);

                foreach (var schemaProp in schema.Properties)
                {
                    var spLower = schemaProp.Name.ToLowerInvariant();
                    if (spLower == propLower) continue;
                    if (roots.Any(root => spLower.Contains(root)))
                        related.Add(schemaProp.Name);
                }
            }
        }

        // Remove the queried property itself and duplicates
        return related
            .Where(r => !r.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
    }

    /// <summary>
    /// Extracts meaningful word roots from a camelCase or PascalCase property name.
    /// E.g., "DamageAmount" -> ["damage", "amount"], "bStartEnabled" -> ["start", "enabled"]
    /// </summary>
    private static List<string> ExtractNameRoots(string name)
    {
        // Strip common prefixes
        var clean = name.TrimStart('b').TrimStart('_');
        if (clean.Length == 0) clean = name;

        // Split on camelCase boundaries
        var parts = System.Text.RegularExpressions.Regex.Split(clean, "(?<=[a-z])(?=[A-Z])|_")
            .Where(p => p.Length >= 3) // Only meaningful roots
            .Select(p => p.ToLowerInvariant())
            .ToList();

        return parts;
    }

    private static int ScoreDeviceMatch(DeviceSchema schema, string[] keywords)
    {
        int score = 0;
        var nameLower = schema.Name.ToLowerInvariant();

        foreach (var kw in keywords)
        {
            // Name match — highest weight
            if (nameLower.Contains(kw))
                score += kw.Length * 3;

            // Property name match
            foreach (var prop in schema.Properties)
            {
                if (prop.Name.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    score += kw.Length;
            }

            // Event match
            foreach (var evt in schema.Events)
            {
                if (evt.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    score += kw.Length;
            }
        }

        return score;
    }

    private static string NormalizeDeviceName(string name)
    {
        return name
            .Replace("BP_", "")
            .Replace("PBWA_", "")
            .Replace("_C", "")
            .Replace("Device_", "")
            .Replace(" ", "_")
            .ToLowerInvariant()
            .TrimEnd('_');
    }

    private static List<string> GetNameVariants(string name)
    {
        var normalized = NormalizeDeviceName(name);
        var variants = new List<string> { normalized };

        // Add common variations
        if (!normalized.EndsWith("_device"))
            variants.Add(normalized + "_device");
        if (normalized.EndsWith("_device"))
            variants.Add(normalized.Replace("_device", ""));
        if (!normalized.Contains("_"))
            variants.Add(normalized.Replace("device", "_device"));

        // Also add the pretty name lowered
        variants.Add(PrettifyName(name).ToLowerInvariant().Replace(" ", "_"));

        return variants.Distinct().ToList();
    }

    private static string PrettifyName(string name)
    {
        var clean = name
            .Replace("BP_", "")
            .Replace("PBWA_", "")
            .Replace("_C", "")
            .Replace("_", " ");

        // Insert spaces before capitals (camelCase → Camel Case)
        var result = System.Text.RegularExpressions.Regex.Replace(clean, "(?<=[a-z])(?=[A-Z])", " ");
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(result.ToLower()).Trim();
    }

    private static string InferDeviceDescription(string name, string parentClass)
    {
        var lower = name.ToLowerInvariant();

        if (lower.Contains("trigger")) return "Detects player overlap or interaction to fire events.";
        if (lower.Contains("spawner") || lower.Contains("spawn_pad")) return "Manages player or item spawn points.";
        if (lower.Contains("mutator_zone")) return "Applies gameplay modifications within a zone volume.";
        if (lower.Contains("barrier")) return "Togglable barrier/wall that blocks player movement.";
        if (lower.Contains("teleporter")) return "Teleports players between linked locations.";
        if (lower.Contains("granter") || lower.Contains("item_granter")) return "Grants items or weapons to players.";
        if (lower.Contains("elimination_manager")) return "Manages elimination scoring and game-over conditions.";
        if (lower.Contains("score_manager")) return "Tracks and manages team or player scores.";
        if (lower.Contains("timer")) return "Manages countdown or elapsed time tracking.";
        if (lower.Contains("hud_message")) return "Displays messages on the player HUD.";
        if (lower.Contains("channel")) return "Broadcasts events across the signal channel system.";
        if (lower.Contains("conditional_button")) return "Button that checks conditions before triggering.";
        if (lower.Contains("class_selector") || lower.Contains("class_designer")) return "Manages player class selection and loadouts.";
        if (lower.Contains("tracker")) return "Tracks player statistics and progression.";
        if (lower.Contains("vending_machine")) return "Dispenses items in exchange for resources.";
        if (lower.Contains("vehicle_spawner")) return "Spawns and manages vehicles.";
        if (lower.Contains("storm")) return "Controls the storm/safe zone boundaries.";

        if (!string.IsNullOrEmpty(parentClass))
            return $"Creative device (extends {parentClass}).";
        return "Creative device.";
    }

    private static string InferPropertyDescription(string propName, string propType)
    {
        var lower = propName.ToLowerInvariant();

        if (lower == "benabled" || lower == "enabled") return "Whether this device is active at game start.";
        if (lower.Contains("team")) return "Team affiliation or filter for this property.";
        if (lower.Contains("damage")) return "Amount of damage dealt.";
        if (lower.Contains("health")) return "Health value or threshold.";
        if (lower.Contains("delay")) return "Time delay in seconds before activation.";
        if (lower.Contains("duration")) return "How long the effect lasts in seconds.";
        if (lower.Contains("cooldown")) return "Minimum time between activations.";
        if (lower.Contains("radius") || lower.Contains("range")) return "Effective radius or range.";
        if (lower.Contains("count") || lower.Contains("max")) return "Maximum count or limit.";
        if (lower.Contains("visibility") || lower.Contains("visible")) return "Controls visual visibility.";
        if (lower.Contains("collision")) return "Controls collision behavior.";
        if (lower.Contains("item") && lower.Contains("def")) return "Reference to the item definition.";
        if (lower.Contains("color")) return "Color value.";
        if (lower.Contains("text") || lower.Contains("message")) return "Display text or message content.";
        if (lower.Contains("sound") || lower.Contains("audio")) return "Sound effect reference.";
        if (lower.Contains("phase")) return "Game phase this applies to.";

        return $"{propType} property.";
    }

    // ─── Built-in Common Configurations ──────────────────────────────────────

    private static Dictionary<string, List<CommonConfiguration>> BuildCommonConfigs()
    {
        var configs = new Dictionary<string, List<CommonConfiguration>>(StringComparer.OrdinalIgnoreCase);

        // Trigger Device configurations
        configs["trigger"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "One-Shot Trigger",
                Description = "Fires once when any player enters, then disables itself.",
                Properties = new Dictionary<string, string>
                {
                    ["NumberOfTimesCanTrigger"] = "1",
                    ["ActivatedDuringPhase"] = "All",
                    ["TriggeringTeam"] = "Any"
                },
                Tags = new List<string> { "basic", "one-shot", "beginner" }
            },
            new()
            {
                Name = "Team-Only Trigger",
                Description = "Only fires for players on a specific team.",
                Properties = new Dictionary<string, string>
                {
                    ["TriggeringTeam"] = "1",
                    ["NumberOfTimesCanTrigger"] = "0"
                },
                Tags = new List<string> { "team", "filter" }
            },
            new()
            {
                Name = "Delayed Trigger",
                Description = "Fires after a configurable delay when players enter.",
                Properties = new Dictionary<string, string>
                {
                    ["TriggerDelay"] = "3.0",
                    ["NumberOfTimesCanTrigger"] = "0",
                    ["bRetriggerableDelay"] = "False"
                },
                Tags = new List<string> { "delay", "timer" }
            }
        };

        // Barrier Device configurations
        configs["barrier"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Timed Barrier (10s)",
                Description = "Barrier that automatically opens after 10 seconds, then closes again.",
                Properties = new Dictionary<string, string>
                {
                    ["bStartEnabled"] = "True",
                    ["AutoOpenDelay"] = "10.0",
                    ["AutoCloseDelay"] = "3.0"
                },
                Tags = new List<string> { "timer", "auto", "cycle" }
            },
            new()
            {
                Name = "Phase-Locked Barrier",
                Description = "Barrier active only during specific game phases.",
                Properties = new Dictionary<string, string>
                {
                    ["bStartEnabled"] = "False",
                    ["ActiveDuringPhase"] = "Setup"
                },
                Tags = new List<string> { "phase", "setup" }
            },
            new()
            {
                Name = "Team Barrier",
                Description = "Barrier that blocks one team but allows the other through.",
                Properties = new Dictionary<string, string>
                {
                    ["bStartEnabled"] = "True",
                    ["BlockingTeam"] = "1"
                },
                Tags = new List<string> { "team", "one-way" }
            }
        };

        // Item Granter configurations
        configs["item_granter"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Spawn Loadout Granter",
                Description = "Grants a standard loadout when players spawn.",
                Properties = new Dictionary<string, string>
                {
                    ["GrantOnSpawn"] = "True",
                    ["ClearInventoryOnGrant"] = "False",
                    ["GrantToTeam"] = "Any"
                },
                Tags = new List<string> { "spawn", "loadout", "beginner" }
            },
            new()
            {
                Name = "Pickup-Style Granter",
                Description = "Grants items on overlap like a floor pickup, then disappears temporarily.",
                Properties = new Dictionary<string, string>
                {
                    ["GrantOnOverlap"] = "True",
                    ["RespawnTime"] = "30.0",
                    ["ItemCount"] = "1"
                },
                Tags = new List<string> { "pickup", "overlap", "respawn" }
            }
        };

        // Spawner configurations
        configs["player_spawner"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Team-Only Spawner",
                Description = "Spawn pad for a specific team only.",
                Properties = new Dictionary<string, string>
                {
                    ["TeamIndex"] = "1",
                    ["SpawnOrder"] = "Sequential"
                },
                Tags = new List<string> { "team", "spawn" }
            },
            new()
            {
                Name = "Random Spawn Pad",
                Description = "Players spawn at random from a pool of pads.",
                Properties = new Dictionary<string, string>
                {
                    ["SpawnOrder"] = "Random",
                    ["bEnabled"] = "True"
                },
                Tags = new List<string> { "random", "spawn" }
            }
        };

        // Mutator Zone configurations
        configs["mutator_zone"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Damage Zone",
                Description = "Zone that deals periodic damage to players inside.",
                Properties = new Dictionary<string, string>
                {
                    ["DamagePerTick"] = "10",
                    ["TickRate"] = "1.0",
                    ["AffectsAllTeams"] = "True"
                },
                Tags = new List<string> { "damage", "hazard" }
            },
            new()
            {
                Name = "Speed Boost Zone",
                Description = "Zone that increases player movement speed.",
                Properties = new Dictionary<string, string>
                {
                    ["SpeedMultiplier"] = "2.0",
                    ["AffectsAllTeams"] = "True"
                },
                Tags = new List<string> { "speed", "buff" }
            },
            new()
            {
                Name = "No-Build Zone",
                Description = "Zone where building and editing are disabled.",
                Properties = new Dictionary<string, string>
                {
                    ["bDisableBuilding"] = "True",
                    ["bDisableEditing"] = "True",
                    ["AffectsAllTeams"] = "True"
                },
                Tags = new List<string> { "no-build", "restriction" }
            }
        };

        // Score Manager configurations
        configs["score_manager"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Elimination Scoring",
                Description = "Awards points for eliminations with a target score to win.",
                Properties = new Dictionary<string, string>
                {
                    ["ScorePerElimination"] = "1",
                    ["ScoreToWin"] = "25",
                    ["bShowScoreboard"] = "True"
                },
                Tags = new List<string> { "elimination", "scoring", "competitive" }
            },
            new()
            {
                Name = "Objective Scoring",
                Description = "Points awarded by objective completion, not eliminations.",
                Properties = new Dictionary<string, string>
                {
                    ["ScorePerElimination"] = "0",
                    ["ScoreToWin"] = "100",
                    ["bShowScoreboard"] = "True"
                },
                Tags = new List<string> { "objective", "scoring" }
            }
        };

        // HUD Message Device configurations
        configs["hud_message"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Welcome Message",
                Description = "Shows a message to all players at game start.",
                Properties = new Dictionary<string, string>
                {
                    ["MessageText"] = "Welcome to the game!",
                    ["ShowOnGameStart"] = "True",
                    ["Duration"] = "5.0",
                    ["ShowToAllPlayers"] = "True"
                },
                Tags = new List<string> { "welcome", "intro", "beginner" }
            },
            new()
            {
                Name = "Countdown Timer Display",
                Description = "Persistent timer display on screen.",
                Properties = new Dictionary<string, string>
                {
                    ["MessageType"] = "Timer",
                    ["bPersistent"] = "True"
                },
                Tags = new List<string> { "timer", "countdown", "hud" }
            }
        };

        // Teleporter configurations
        configs["teleporter"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Two-Way Portal",
                Description = "Bidirectional teleporter linked to a partner pad.",
                Properties = new Dictionary<string, string>
                {
                    ["TeleporterGroup"] = "1",
                    ["bBidirectional"] = "True",
                    ["TeleportDelay"] = "0.0"
                },
                Tags = new List<string> { "portal", "bidirectional" }
            },
            new()
            {
                Name = "One-Way Launch",
                Description = "Teleports players in one direction only, like a launcher.",
                Properties = new Dictionary<string, string>
                {
                    ["TeleporterGroup"] = "1",
                    ["bBidirectional"] = "False",
                    ["bMaintainVelocity"] = "True"
                },
                Tags = new List<string> { "launcher", "one-way" }
            }
        };

        // Elimination Manager
        configs["elimination_manager"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Last One Standing",
                Description = "Game ends when only one player/team remains.",
                Properties = new Dictionary<string, string>
                {
                    ["bLastOneStanding"] = "True",
                    ["bAllowRespawn"] = "False"
                },
                Tags = new List<string> { "battle-royale", "solo", "no-respawn" }
            },
            new()
            {
                Name = "Respawn Deathmatch",
                Description = "Players respawn on elimination, game runs on timer.",
                Properties = new Dictionary<string, string>
                {
                    ["bAllowRespawn"] = "True",
                    ["RespawnTime"] = "5.0",
                    ["bLastOneStanding"] = "False"
                },
                Tags = new List<string> { "deathmatch", "respawn", "team" }
            }
        };

        // Timer Device
        configs["timer"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Game Clock (5 min)",
                Description = "5-minute countdown timer that ends the game when it reaches zero.",
                Properties = new Dictionary<string, string>
                {
                    ["Duration"] = "300",
                    ["bAutoStart"] = "True",
                    ["bEndGameOnComplete"] = "True"
                },
                Tags = new List<string> { "game-clock", "countdown", "5-minutes" }
            },
            new()
            {
                Name = "Repeating Event Timer",
                Description = "Timer that fires an event at regular intervals.",
                Properties = new Dictionary<string, string>
                {
                    ["Duration"] = "30",
                    ["bAutoStart"] = "True",
                    ["bLoop"] = "True"
                },
                Tags = new List<string> { "repeating", "loop", "interval" }
            }
        };

        // Vending Machine
        configs["vending_machine"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Free Vending Machine",
                Description = "Dispenses items at no cost to the player.",
                Properties = new Dictionary<string, string>
                {
                    ["Cost"] = "0",
                    ["bInfiniteUses"] = "True"
                },
                Tags = new List<string> { "free", "unlimited" }
            },
            new()
            {
                Name = "One-Time Purchase",
                Description = "Single-use vending machine with a resource cost.",
                Properties = new Dictionary<string, string>
                {
                    ["Cost"] = "100",
                    ["CostResource"] = "Gold",
                    ["bInfiniteUses"] = "False",
                    ["UseCount"] = "1"
                },
                Tags = new List<string> { "shop", "one-time", "gold" }
            }
        };

        // Conditional Button
        configs["conditional_button"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Item-Check Button",
                Description = "Only activates if the player has a required item.",
                Properties = new Dictionary<string, string>
                {
                    ["bRequireItem"] = "True",
                    ["InteractText"] = "Activate"
                },
                Tags = new List<string> { "conditional", "item-check" }
            },
            new()
            {
                Name = "Team-Only Button",
                Description = "Button that only one team can interact with.",
                Properties = new Dictionary<string, string>
                {
                    ["TriggeringTeam"] = "1",
                    ["InteractText"] = "Press"
                },
                Tags = new List<string> { "team", "button" }
            }
        };

        // Vehicle Spawner
        configs["vehicle_spawner"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Respawning Vehicle Pad",
                Description = "Vehicle respawns after being destroyed or driven away.",
                Properties = new Dictionary<string, string>
                {
                    ["RespawnTime"] = "30.0",
                    ["bAutoSpawn"] = "True"
                },
                Tags = new List<string> { "vehicle", "respawn" }
            },
            new()
            {
                Name = "One-Time Vehicle",
                Description = "Vehicle spawns once and does not respawn.",
                Properties = new Dictionary<string, string>
                {
                    ["RespawnTime"] = "0",
                    ["bAutoSpawn"] = "True",
                    ["SpawnLimit"] = "1"
                },
                Tags = new List<string> { "vehicle", "one-time" }
            }
        };

        // Class Selector / Class Designer
        configs["class_selector"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Pre-Game Class Picker",
                Description = "Allows players to choose a class during the setup phase.",
                Properties = new Dictionary<string, string>
                {
                    ["ActiveDuringPhase"] = "Setup",
                    ["bShowUI"] = "True"
                },
                Tags = new List<string> { "class", "setup", "loadout" }
            }
        };

        // Tracker Device
        configs["tracker"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Elimination Tracker",
                Description = "Tracks player eliminations and fires events at thresholds.",
                Properties = new Dictionary<string, string>
                {
                    ["TrackedStat"] = "Eliminations",
                    ["GoalValue"] = "10"
                },
                Tags = new List<string> { "stats", "elimination", "tracking" }
            },
            new()
            {
                Name = "Score Milestone Tracker",
                Description = "Fires events when a player reaches score milestones.",
                Properties = new Dictionary<string, string>
                {
                    ["TrackedStat"] = "Score",
                    ["GoalValue"] = "50"
                },
                Tags = new List<string> { "stats", "score", "milestone" }
            }
        };

        // Storm Controller
        configs["storm_controller"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Closing Storm (BR-Style)",
                Description = "Storm that closes in phases like a battle royale.",
                Properties = new Dictionary<string, string>
                {
                    ["bEnabled"] = "True",
                    ["NumberOfPhases"] = "5",
                    ["InitialWaitTime"] = "60"
                },
                Tags = new List<string> { "storm", "battle-royale", "closing" }
            }
        };

        // Damage Volume / Zone
        configs["damage_volume"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Kill Zone",
                Description = "Instantly eliminates any player who enters.",
                Properties = new Dictionary<string, string>
                {
                    ["DamageAmount"] = "9999",
                    ["AffectsAllTeams"] = "True"
                },
                Tags = new List<string> { "kill", "hazard", "instant" }
            }
        };

        // Checkpoint Device
        configs["checkpoint"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Race Checkpoint",
                Description = "Checkpoint that must be reached in order during a race.",
                Properties = new Dictionary<string, string>
                {
                    ["bMustReachInOrder"] = "True",
                    ["bShowMarker"] = "True"
                },
                Tags = new List<string> { "race", "checkpoint", "ordered" }
            }
        };

        // Signal Remote / Channel Device
        configs["signal_remote"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Broadcast Relay",
                Description = "Receives a signal on one channel and broadcasts on another.",
                Properties = new Dictionary<string, string>
                {
                    ["ListenChannel"] = "1",
                    ["BroadcastChannel"] = "2"
                },
                Tags = new List<string> { "signal", "relay", "channel" }
            }
        };

        // Sequencer Device
        configs["sequencer"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Round Sequencer",
                Description = "Cycles through steps for multi-round game modes.",
                Properties = new Dictionary<string, string>
                {
                    ["bAutoAdvance"] = "True",
                    ["StepDelay"] = "5.0",
                    ["bLoop"] = "True"
                },
                Tags = new List<string> { "sequence", "rounds", "loop" }
            }
        };

        // Capture Area
        configs["capture_area"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "King-of-the-Hill Zone",
                Description = "Zone that awards points while a team controls it.",
                Properties = new Dictionary<string, string>
                {
                    ["CaptureTime"] = "10.0",
                    ["ScorePerSecond"] = "1",
                    ["bNeutralOnEmpty"] = "True"
                },
                Tags = new List<string> { "capture", "koth", "zone" }
            }
        };

        // Player Reference Device
        configs["player_reference"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Team Filter",
                Description = "Filters events to only affect a specific team.",
                Properties = new Dictionary<string, string>
                {
                    ["TeamIndex"] = "1",
                    ["FilterMode"] = "IncludeOnly"
                },
                Tags = new List<string> { "filter", "team", "reference" }
            }
        };

        // Map Indicator
        configs["map_indicator"] = new List<CommonConfiguration>
        {
            new()
            {
                Name = "Objective Marker",
                Description = "Shows a persistent marker on the map for an objective location.",
                Properties = new Dictionary<string, string>
                {
                    ["bShowOnMap"] = "True",
                    ["bShowInWorld"] = "True",
                    ["MarkerColor"] = "Blue"
                },
                Tags = new List<string> { "marker", "objective", "map" }
            }
        };

        return configs;
    }
}

// ─── Encyclopedia Models ─────────────────────────────────────────────────────

/// <summary>
/// Search result from the device encyclopedia.
/// </summary>
public class EncyclopediaSearchResult
{
    public string DeviceName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ParentClass { get; set; } = "";
    public int Score { get; set; }
    public int PropertyCount { get; set; }
    public int EventCount { get; set; }
    public int FunctionCount { get; set; }
    public List<string> MatchedProperties { get; set; } = new();
    public List<string> MatchedEvents { get; set; } = new();
    public bool HasCommonConfigs { get; set; }
    public string? MatchContext { get; set; }
    public int UsageCount { get; set; }
}

/// <summary>
/// Full reference documentation for a device type.
/// </summary>
public class DeviceReference
{
    public string DeviceName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ParentClass { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public string Description { get; set; } = "";
    public List<PropertyReference> Properties { get; set; } = new();
    public List<string> Events { get; set; } = new();
    public List<string> Functions { get; set; } = new();
    public List<CommonConfiguration> CommonConfigurations { get; set; } = new();
    public int TotalUsageCount { get; set; }
    public int ProjectsUsedIn { get; set; }
}

/// <summary>
/// Property reference with usage statistics.
/// </summary>
public class PropertyReference
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? DefaultValue { get; set; }
    public bool IsEditable { get; set; }
    public string Description { get; set; } = "";
    public double UsagePercent { get; set; }
    public List<ValueFrequency> CommonValues { get; set; } = new();
    public List<string> RelatedProperties { get; set; } = new();
}

/// <summary>
/// A value and how often it appears in real projects.
/// </summary>
public class ValueFrequency
{
    public string Value { get; set; } = "";
    public int Count { get; set; }
    public double Percent { get; set; }
}

/// <summary>
/// A pre-built device configuration with property presets.
/// </summary>
public class CommonConfiguration
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Dictionary<string, string> Properties { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Brief summary of a device type for listing.
/// </summary>
public class DeviceSummary
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ParentClass { get; set; } = "";
    public int PropertyCount { get; set; }
    public int EventCount { get; set; }
    public int FunctionCount { get; set; }
    public int UsageCount { get; set; }
    public bool HasCommonConfigs { get; set; }
}

/// <summary>
/// Internal: usage statistics for a property.
/// </summary>
public class PropertyUsageStats
{
    public string PropertyName { get; set; } = "";
    public double UsagePercent { get; set; }
    public int TimesSet { get; set; }
    public int TotalInstances { get; set; }
    public List<ValueFrequency> CommonValues { get; set; } = new();
}

/// <summary>
/// Internal: aggregated usage profile for a device type across the library.
/// </summary>
public class DeviceUsageProfile
{
    public string DeviceClass { get; set; } = "";
    public int TotalInstances { get; set; }
    public int ProjectCount { get; set; }
    public Dictionary<string, PropertyUsageStats> PropertyUsage { get; set; } = new();
}

/// <summary>
/// Internal: reference entry tracking a device type in a specific project.
/// </summary>
public class DeviceReferenceEntry
{
    public string DeviceClass { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public int InstanceCount { get; set; }
}

/// <summary>
/// Health score for a set of devices in a level, evaluating completeness and quality.
/// </summary>
public class DeviceHealthScore
{
    public int Score { get; set; }
    public string Grade { get; set; } = "";
    public string Summary { get; set; } = "";
    public double WiredPercent { get; set; }
    public double CustomizedPercent { get; set; }
    public int OrphanedDeviceCount { get; set; }
    public bool HasGameManagement { get; set; }
    public bool HasUIFeedback { get; set; }
}
