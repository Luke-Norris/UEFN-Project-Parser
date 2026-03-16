using System.Text.RegularExpressions;
using FortniteForge.Core.Config;
using FortniteForge.Core.Models;
using Microsoft.Extensions.Logging;

namespace FortniteForge.Core.Services;

/// <summary>
/// Indexes and searches a library of .verse files for reference and examples.
/// Lazy-loaded: builds the in-memory index on first query.
/// </summary>
public class VerseReferenceService
{
    private readonly ForgeConfig _config;
    private readonly ILogger<VerseReferenceService> _logger;
    private Dictionary<string, VerseFileIndex>? _index;
    private bool _loaded;
    private readonly object _loadLock = new();

    // Regex patterns for Verse syntax extraction
    private static readonly Regex UsingRegex = new(
        @"using\s*\{\s*([^}]+)\}", RegexOptions.Compiled);
    private static readonly Regex ClassRegex = new(
        @"^(\w+)\s*(?:<[^>]*>)*\s*:=\s*class\s*(?:<([^>]*)>(?:<[^>]*>)*)?\s*\(([^)]*)\)\s*:",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex EditableRegex = new(
        @"@editable\s+(?:var\s+)?(\w+)\s*:\s*(\[?\]?\s*\w+)",
        RegexOptions.Compiled);
    private static readonly Regex FunctionRegex = new(
        @"^\s{4}(\w+)\s*(?:<[^>]*>)?\s*\([^)]*\)\s*(?:<[^>]*>)*\s*:\s*\w+",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex DeviceTypeRegex = new(
        @":\s*(?:\[\])?(\w+_device)\b", RegexOptions.Compiled);

    public VerseReferenceService(ForgeConfig config, ILogger<VerseReferenceService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public void EnsureLoaded()
    {
        if (!_loaded)
            BuildIndex();
    }

    public void BuildIndex()
    {
        lock (_loadLock)
        {
            if (_loaded) return;

            var libraryPath = _config.ReferenceLibraryPath;
            if (string.IsNullOrEmpty(libraryPath) || !Directory.Exists(libraryPath))
            {
                _logger.LogWarning("ReferenceLibraryPath not configured or doesn't exist: {Path}", libraryPath ?? "(null)");
                _index = new();
                _loaded = true;
                return;
            }

            _index = new Dictionary<string, VerseFileIndex>(StringComparer.OrdinalIgnoreCase);
            var verseFiles = Directory.EnumerateFiles(libraryPath, "*.verse", SearchOption.AllDirectories);

            var count = 0;
            foreach (var filePath in verseFiles)
            {
                try
                {
                    var entry = ParseVerseFile(filePath, libraryPath);
                    _index[filePath] = entry;
                    count++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse verse file: {Path}", filePath);
                }
            }

            _loaded = true;
            _logger.LogInformation("Indexed {Count} Verse files from {Path}", count, libraryPath);
        }
    }

    public void Rebuild()
    {
        lock (_loadLock)
        {
            _loaded = false;
            _index = null;
        }
        BuildIndex();
    }

    private VerseFileIndex ParseVerseFile(string filePath, string libraryPath)
    {
        var content = File.ReadAllText(filePath);
        var lines = content.Split('\n');

        var index = new VerseFileIndex
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            RelativePath = Path.GetRelativePath(libraryPath, filePath),
            FileSizeBytes = new FileInfo(filePath).Length,
            FullText = content,
            LineCount = lines.Length
        };

        index.UsingStatements = ExtractUsingStatements(content);
        index.Classes = ExtractClasses(content);
        index.DeviceTypesUsed = ExtractDeviceTypes(content);
        index.EditableProperties = ExtractEditableProperties(content);
        index.FunctionNames = ExtractFunctions(content);
        index.Categories = CategorizeFile(index);

        return index;
    }

    private static List<string> ExtractUsingStatements(string content)
    {
        return UsingRegex.Matches(content)
            .Select(m => m.Groups[1].Value.Trim())
            .Distinct()
            .ToList();
    }

    private static List<VerseClassInfo> ExtractClasses(string content)
    {
        var classes = new List<VerseClassInfo>();
        foreach (Match m in ClassRegex.Matches(content))
        {
            var modifiers = m.Groups[2].Success
                ? m.Groups[2].Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList()
                : new List<string>();

            classes.Add(new VerseClassInfo
            {
                ClassName = m.Groups[1].Value,
                ParentClass = m.Groups[3].Value.Trim(),
                Modifiers = modifiers
            });
        }
        return classes;
    }

    private static List<string> ExtractDeviceTypes(string content)
    {
        return DeviceTypeRegex.Matches(content)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ExtractEditableProperties(string content)
    {
        return EditableRegex.Matches(content)
            .Select(m => $"{m.Groups[1].Value} : {m.Groups[2].Value.Trim()}")
            .ToList();
    }

    private static List<string> ExtractFunctions(string content)
    {
        return FunctionRegex.Matches(content)
            .Select(m => m.Groups[1].Value)
            .Where(name => name != "if" && name != "for" && name != "loop"
                        && name != "race" && name != "branch" && name != "block"
                        && name != "spawn" && name != "var" && name != "set")
            .Distinct()
            .ToList();
    }

    private static List<VerseCategory> CategorizeFile(VerseFileIndex index)
    {
        var categories = new List<VerseCategory>();
        var text = index.FullText;
        var lowerText = text.ToLowerInvariant();
        var classNames = index.Classes.Select(c => c.ClassName.ToLowerInvariant()).ToList();
        var devices = new HashSet<string>(index.DeviceTypesUsed, StringComparer.OrdinalIgnoreCase);

        // UI System
        if (lowerText.Contains("canvas") || lowerText.Contains("text_block") || lowerText.Contains("overlay")
            || index.UsingStatements.Any(u => u.Contains("/UI")))
            categories.Add(VerseCategory.UISystem);

        // Player Economy
        if (devices.Any(d => d.Contains("tracker")) || lowerText.Contains("currency")
            || lowerText.Contains("money") || lowerText.Contains("persistable")
            || lowerText.Contains("weak_map"))
            categories.Add(VerseCategory.PlayerEconomy);

        // Game Management
        if (classNames.Any(c => c.Contains("game") || c.Contains("manager") || c.Contains("round"))
            || lowerText.Contains("gameloop") || devices.Contains("end_game_device"))
            categories.Add(VerseCategory.GameManagement);

        // Ranked/Progression
        if (lowerText.Contains("rank") || lowerText.Contains("leveling")
            || lowerText.Contains("progression") || lowerText.Contains("experience"))
            categories.Add(VerseCategory.RankedProgression);

        // Tycoon/Building
        if (lowerText.Contains("tycoon") || lowerText.Contains("purchase")
            || lowerText.Contains("unlock") || lowerText.Contains("buyable"))
            categories.Add(VerseCategory.TycoonBuilding);

        // Ability System
        if (lowerText.Contains("ability") || lowerText.Contains("superpower")
            || lowerText.Contains("cooldown") || devices.Contains("item_granter_device"))
            categories.Add(VerseCategory.AbilitySystem);

        // Combat
        if (devices.Any(d => d.Contains("elimination") || d.Contains("damage"))
            || lowerText.Contains("elimination") || lowerText.Contains("weapon"))
            categories.Add(VerseCategory.Combat);

        // Movement
        if (devices.Any(d => d.Contains("mutator") || d.Contains("teleporter"))
            || lowerText.Contains("teleport") || lowerText.Contains("speed"))
            categories.Add(VerseCategory.Movement);

        // Environment
        if (lowerText.Contains("mystery") || lowerText.Contains("prop_manipulator")
            || lowerText.Contains("pet") || lowerText.Contains("zombie"))
            categories.Add(VerseCategory.Environment);

        // Utility fallback
        if (categories.Count == 0)
            categories.Add(VerseCategory.Utility);

        return categories.Distinct().ToList();
    }

    // --- Search Methods ---

    public List<VerseSearchResult> SearchKeyword(string keyword, int maxResults = 20)
    {
        EnsureLoaded();
        var results = new List<VerseSearchResult>();

        foreach (var (_, index) in _index!)
        {
            var matchReasons = new List<string>();
            var relevance = 0;

            // Filename match (highest weight)
            if (index.FileName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                matchReasons.Add($"Filename contains '{keyword}'");
                relevance += 10;
            }

            // Class name match
            var matchedClasses = index.Classes
                .Where(c => c.ClassName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.ClassName).ToList();
            if (matchedClasses.Count > 0)
            {
                matchReasons.Add($"Classes: {string.Join(", ", matchedClasses)}");
                relevance += 8;
            }

            // Device type match
            var matchedDevices = index.DeviceTypesUsed
                .Where(d => d.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matchedDevices.Count > 0)
            {
                matchReasons.Add($"Devices: {string.Join(", ", matchedDevices)}");
                relevance += 6;
            }

            // Function name match
            var matchedFunctions = index.FunctionNames
                .Where(f => f.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Take(5).ToList();
            if (matchedFunctions.Count > 0)
            {
                matchReasons.Add($"Functions: {string.Join(", ", matchedFunctions)}");
                relevance += 5;
            }

            // Editable property match
            var matchedProps = index.EditableProperties
                .Where(p => p.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Take(5).ToList();
            if (matchedProps.Count > 0)
            {
                matchReasons.Add($"Properties: {string.Join(", ", matchedProps)}");
                relevance += 4;
            }

            // Full text match (lowest weight)
            if (relevance == 0 && index.FullText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                var count = CountOccurrences(index.FullText, keyword);
                matchReasons.Add($"{count} occurrence(s) in code");
                relevance += Math.Min(count, 5);
            }

            if (matchReasons.Count > 0)
            {
                results.Add(new VerseSearchResult
                {
                    FilePath = index.FilePath,
                    FileName = index.FileName,
                    RelativePath = index.RelativePath,
                    MatchReasons = matchReasons,
                    Categories = index.Categories,
                    DeviceTypesUsed = index.DeviceTypesUsed,
                    ClassNames = index.Classes.Select(c => c.ClassName).ToList(),
                    Relevance = relevance
                });
            }
        }

        return results.OrderByDescending(r => r.Relevance).Take(maxResults).ToList();
    }

    public List<VerseSearchResult> FindByDevice(string deviceType, int maxResults = 20)
    {
        EnsureLoaded();
        var results = new List<VerseSearchResult>();

        foreach (var (_, index) in _index!)
        {
            var matched = index.DeviceTypesUsed
                .Where(d => d.Contains(deviceType, StringComparison.OrdinalIgnoreCase)).ToList();

            if (matched.Count > 0)
            {
                results.Add(new VerseSearchResult
                {
                    FilePath = index.FilePath,
                    FileName = index.FileName,
                    RelativePath = index.RelativePath,
                    MatchReasons = new List<string> { $"Uses: {string.Join(", ", matched)}" },
                    Categories = index.Categories,
                    DeviceTypesUsed = index.DeviceTypesUsed,
                    ClassNames = index.Classes.Select(c => c.ClassName).ToList(),
                    Relevance = matched.Count
                });
            }
        }

        return results.OrderByDescending(r => r.Relevance).Take(maxResults).ToList();
    }

    public List<VerseSearchResult> FindByCategory(VerseCategory category, int maxResults = 20)
    {
        EnsureLoaded();

        return _index!.Values
            .Where(i => i.Categories.Contains(category))
            .Select(i => new VerseSearchResult
            {
                FilePath = i.FilePath,
                FileName = i.FileName,
                RelativePath = i.RelativePath,
                MatchReasons = new List<string> { $"Categorized as: {category}" },
                Categories = i.Categories,
                DeviceTypesUsed = i.DeviceTypesUsed,
                ClassNames = i.Classes.Select(c => c.ClassName).ToList(),
                Relevance = 5
            })
            .Take(maxResults)
            .ToList();
    }

    public string? GetFileContent(string filePath)
    {
        if (!File.Exists(filePath))
            return null;
        return File.ReadAllText(filePath);
    }

    public VerseLibraryStats GetStats()
    {
        EnsureLoaded();

        var stats = new VerseLibraryStats
        {
            TotalFiles = _index!.Count,
            TotalLines = _index.Values.Sum(i => i.LineCount),
            TotalSizeBytes = _index.Values.Sum(i => i.FileSizeBytes),
            IndexedAt = DateTime.Now
        };

        stats.FilesByCategory = _index.Values
            .SelectMany(i => i.Categories)
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key.ToString(), g => g.Count());

        stats.DeviceUsageCounts = _index.Values
            .SelectMany(i => i.DeviceTypesUsed)
            .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(30)
            .ToDictionary(g => g.Key, g => g.Count());

        stats.TopUsingStatements = _index.Values
            .SelectMany(i => i.UsingStatements)
            .GroupBy(u => u)
            .OrderByDescending(g => g.Count())
            .Take(15)
            .Select(g => $"{g.Key} ({g.Count()} files)")
            .ToList();

        return stats;
    }

    private static int CountOccurrences(string text, string keyword)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(keyword, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx += keyword.Length;
        }
        return count;
    }
}
