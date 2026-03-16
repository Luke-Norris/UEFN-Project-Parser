using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FortniteForge.Core.Config;
using Microsoft.Extensions.Logging;

namespace FortniteForge.Core.Services;

/// <summary>
/// Indexes all assets, verse files, and device patterns across a library of UEFN projects.
/// Produces a searchable catalog that Claude can query via MCP tools.
/// </summary>
public class LibraryIndexer
{
    private readonly ILogger<LibraryIndexer> _logger;
    private LibraryIndex? _index;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public LibraryIndexer(ILogger<LibraryIndexer> logger)
    {
        _logger = logger;
    }

    public LibraryIndex? Index => _index;

    /// <summary>
    /// Scans all UEFN projects in a directory and builds a searchable index.
    /// </summary>
    public LibraryIndex BuildIndex(string libraryPath)
    {
        var index = new LibraryIndex
        {
            LibraryPath = libraryPath,
            IndexedAt = DateTime.UtcNow
        };

        // Discover projects in the library path and any subdirectories
        var projects = ForgeConfig.DiscoverProjects(libraryPath);
        _logger.LogInformation("Indexing {Count} projects in {Path}", projects.Count, libraryPath);

        foreach (var projectPath in projects)
        {
            try
            {
                var entry = IndexProject(projectPath);
                index.Projects.Add(entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to index {Path}", projectPath);
            }
        }

        // Scan for standalone verse files in code_resources or any directory with .verse files
        var verseDirs = new List<string>();

        // Check for code_resources at the library root level
        var codeResources = Path.Combine(libraryPath, "code_resources");
        if (Directory.Exists(codeResources))
            verseDirs.Add(codeResources);

        // Also check sibling code_resources (if libraryPath is map_resources)
        var siblingCode = Path.Combine(Path.GetDirectoryName(libraryPath) ?? libraryPath, "code_resources");
        if (Directory.Exists(siblingCode) && !verseDirs.Contains(siblingCode))
            verseDirs.Add(siblingCode);

        foreach (var vd in verseDirs)
        {
            _logger.LogInformation("Indexing code resources: {Path}", vd);
            IndexCodeResources(vd, index);
        }

        // Build search keywords
        index.TotalVerseFiles = index.Projects.Sum(p => p.VerseFiles.Count) + index.StandaloneVerseFiles.Count;
        index.TotalAssets = index.Projects.Sum(p => p.Assets.Count);
        index.TotalDeviceTypes = index.Projects.SelectMany(p => p.DeviceTypes).Select(d => d.ClassName).Distinct().Count();

        _index = index;
        return index;
    }

    /// <summary>
    /// Loads a previously saved index from disk.
    /// </summary>
    public LibraryIndex? LoadIndex(string indexPath)
    {
        if (!File.Exists(indexPath)) return null;
        var json = File.ReadAllText(indexPath);
        _index = JsonSerializer.Deserialize<LibraryIndex>(json, JsonOpts);
        return _index;
    }

    /// <summary>
    /// Saves the current index to disk.
    /// </summary>
    public void SaveIndex(string indexPath)
    {
        if (_index == null) return;
        var dir = Path.GetDirectoryName(indexPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(indexPath, JsonSerializer.Serialize(_index, JsonOpts));
    }

    /// <summary>
    /// Searches the library for assets, verse files, and device configs matching a query.
    /// </summary>
    public LibrarySearchResult Search(string query)
    {
        if (_index == null) return new LibrarySearchResult { Query = query };

        var q = query.ToLowerInvariant();
        var keywords = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new LibrarySearchResult { Query = query };

        // Search verse files
        foreach (var project in _index.Projects)
        {
            foreach (var vf in project.VerseFiles)
            {
                var score = ScoreMatch(vf.SearchText, keywords);
                if (score > 0)
                    result.VerseFiles.Add(new SearchHit<VerseFileEntry> { Item = vf, Score = score, ProjectName = project.Name });
            }

            foreach (var asset in project.Assets)
            {
                var score = ScoreMatch(asset.SearchText, keywords);
                if (score > 0)
                    result.Assets.Add(new SearchHit<AssetEntry> { Item = asset, Score = score, ProjectName = project.Name });
            }

            foreach (var dt in project.DeviceTypes)
            {
                var score = ScoreMatch(dt.SearchText, keywords);
                if (score > 0)
                    result.DeviceTypes.Add(new SearchHit<DeviceTypeEntry> { Item = dt, Score = score, ProjectName = project.Name });
            }
        }

        // Standalone verse files
        foreach (var vf in _index.StandaloneVerseFiles)
        {
            var score = ScoreMatch(vf.SearchText, keywords);
            if (score > 0)
                result.VerseFiles.Add(new SearchHit<VerseFileEntry> { Item = vf, Score = score, ProjectName = vf.PackName ?? "Standalone" });
        }

        // Sort by score
        result.VerseFiles = result.VerseFiles.OrderByDescending(h => h.Score).Take(20).ToList();
        result.Assets = result.Assets.OrderByDescending(h => h.Score).Take(20).ToList();
        result.DeviceTypes = result.DeviceTypes.OrderByDescending(h => h.Score).Take(20).ToList();

        return result;
    }

    /// <summary>
    /// Gets all materials across the library.
    /// </summary>
    public List<AssetEntry> GetMaterials()
    {
        if (_index == null) return new();
        return _index.Projects.SelectMany(p => p.Assets)
            .Where(a => a.AssetClass.Contains("Material", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Gets all verse files with optional keyword filter.
    /// </summary>
    public List<VerseFileWithProject> GetVerseFiles(string? filter = null)
    {
        if (_index == null) return new();
        var results = new List<VerseFileWithProject>();

        foreach (var p in _index.Projects)
            foreach (var vf in p.VerseFiles)
                if (filter == null || vf.SearchText.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    results.Add(new VerseFileWithProject { ProjectName = p.Name, File = vf });

        foreach (var vf in _index.StandaloneVerseFiles)
            if (filter == null || vf.SearchText.Contains(filter, StringComparison.OrdinalIgnoreCase))
                results.Add(new VerseFileWithProject { ProjectName = vf.PackName ?? "Standalone", File = vf });

        return results;
    }

    private ProjectIndexEntry IndexProject(string projectPath)
    {
        var config = new ForgeConfig { ProjectPath = projectPath };
        var entry = new ProjectIndexEntry
        {
            Name = config.ProjectName,
            Path = projectPath,
            ContentPath = config.ContentPath
        };

        if (!Directory.Exists(config.ContentPath)) return entry;

        // Index verse files
        foreach (var verseFile in Directory.EnumerateFiles(config.ContentPath, "*.verse", SearchOption.AllDirectories))
        {
            try
            {
                entry.VerseFiles.Add(IndexVerseFile(verseFile, config.ProjectName));
            }
            catch { }
        }

        // Index user-created assets (not external actors)
        foreach (var assetFile in Directory.EnumerateFiles(config.ContentPath, "*.uasset", SearchOption.AllDirectories)
            .Where(f => !f.Contains("__External")))
        {
            try
            {
                var fi = new FileInfo(assetFile);
                var asset = new UAssetAPI.UAsset(assetFile, UAssetAPI.UnrealTypes.EngineVersion.VER_UE5_4);
                var assetClass = asset.Exports.FirstOrDefault()?.GetExportClassType()?.ToString() ?? "Unknown";
                var hasThumbnail = asset.Thumbnails?.Values.Any(t => (t.CompressedImageData?.Length ?? 0) > 0) ?? false;

                entry.Assets.Add(new AssetEntry
                {
                    Name = Path.GetFileNameWithoutExtension(assetFile),
                    FilePath = assetFile,
                    RelativePath = Path.GetRelativePath(config.ContentPath, assetFile),
                    AssetClass = assetClass,
                    FileSize = fi.Length,
                    HasThumbnail = hasThumbnail,
                    SearchText = $"{Path.GetFileNameWithoutExtension(assetFile)} {assetClass}".ToLowerInvariant()
                });
            }
            catch { }
        }

        // Index device types from external actors (sample a subset for speed)
        var extActorDirs = Directory.EnumerateDirectories(config.ContentPath, "__ExternalActors__", SearchOption.AllDirectories);
        var deviceCounts = new Dictionary<string, int>();

        foreach (var extDir in extActorDirs)
        {
            foreach (var file in Directory.EnumerateFiles(extDir, "*.uasset", SearchOption.AllDirectories).Take(500))
            {
                try
                {
                    var asset = new UAssetAPI.UAsset(file, UAssetAPI.UnrealTypes.EngineVersion.VER_UE5_4);
                    var cls = asset.Exports.FirstOrDefault(e =>
                    {
                        var c = e.GetExportClassType()?.ToString() ?? "";
                        return !c.Contains("Component") && !c.Contains("Model") && c != "Level" && c != "MetaData";
                    })?.GetExportClassType()?.ToString();

                    if (cls != null)
                        deviceCounts[cls] = deviceCounts.GetValueOrDefault(cls) + 1;
                }
                catch { }
            }
        }

        foreach (var kv in deviceCounts.OrderByDescending(kv => kv.Value))
        {
            entry.DeviceTypes.Add(new DeviceTypeEntry
            {
                ClassName = kv.Key,
                DisplayName = CleanName(kv.Key),
                Count = kv.Value,
                SearchText = $"{kv.Key} {CleanName(kv.Key)}".ToLowerInvariant()
            });
        }

        return entry;
    }

    private VerseFileEntry IndexVerseFile(string filePath, string projectName)
    {
        var source = File.ReadAllText(filePath);
        var lines = source.Split('\n');

        // Extract key info from verse source
        var classes = Regex.Matches(source, @"(\w+)\s*:=\s*class\s*\(", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToList();

        var functions = Regex.Matches(source, @"(\w+)\s*\(\s*\)\s*:\s*void|(\w+)\s*\(\s*\)\s*<\s*suspends\s*>", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value != "" ? m.Groups[1].Value : m.Groups[2].Value).ToList();

        var deviceRefs = Regex.Matches(source, @"@editable\s+(\w+)\s*:\s*(\w+)", RegexOptions.Multiline)
            .Select(m => new { Name = m.Groups[1].Value, Type = m.Groups[2].Value }).ToList();

        var imports = Regex.Matches(source, @"using\s*\{\s*([^}]+)\s*\}", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value.Trim()).ToList();

        // Build a summary
        var summary = new List<string>();
        if (classes.Count > 0) summary.Add($"Classes: {string.Join(", ", classes)}");
        if (functions.Count > 0) summary.Add($"Functions: {string.Join(", ", functions.Take(5))}");
        if (deviceRefs.Count > 0) summary.Add($"Devices: {string.Join(", ", deviceRefs.Select(d => $"{d.Name}:{d.Type}").Take(5))}");

        return new VerseFileEntry
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            FilePath = filePath,
            LineCount = lines.Length,
            Classes = classes,
            Functions = functions,
            DeviceReferences = deviceRefs.Select(d => $"{d.Name}:{d.Type}").ToList(),
            Imports = imports,
            Summary = string.Join(" | ", summary),
            SearchText = $"{Path.GetFileNameWithoutExtension(filePath)} {string.Join(" ", classes)} {string.Join(" ", functions)} {string.Join(" ", deviceRefs.Select(d => d.Type))} {source.Substring(0, Math.Min(500, source.Length))}".ToLowerInvariant()
        };
    }

    private void IndexCodeResources(string codeResourcesPath, LibraryIndex index)
    {
        foreach (var verseFile in Directory.EnumerateFiles(codeResourcesPath, "*.verse", SearchOption.AllDirectories))
        {
            try
            {
                // Determine pack name from path
                var relativePath = Path.GetRelativePath(codeResourcesPath, verseFile);
                var packName = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).FirstOrDefault() ?? "Unknown";

                var entry = IndexVerseFile(verseFile, packName);
                entry.PackName = packName;
                index.StandaloneVerseFiles.Add(entry);
            }
            catch { }
        }
    }

    private static int ScoreMatch(string text, string[] keywords)
    {
        int score = 0;
        foreach (var kw in keywords)
        {
            if (text.Contains(kw))
                score += kw.Length; // Longer keyword matches score higher
        }
        return score;
    }

    private static string CleanName(string className)
    {
        return className.Replace("Device_", "").Replace("_C", "").Replace("_", " ").Trim();
    }
}

// ========= Index Models =========

public class LibraryIndex
{
    public string LibraryPath { get; set; } = "";
    public DateTime IndexedAt { get; set; }
    public int TotalVerseFiles { get; set; }
    public int TotalAssets { get; set; }
    public int TotalDeviceTypes { get; set; }
    public List<ProjectIndexEntry> Projects { get; set; } = new();
    public List<VerseFileEntry> StandaloneVerseFiles { get; set; } = new();
}

public class ProjectIndexEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string ContentPath { get; set; } = "";
    public List<VerseFileEntry> VerseFiles { get; set; } = new();
    public List<AssetEntry> Assets { get; set; } = new();
    public List<DeviceTypeEntry> DeviceTypes { get; set; } = new();
}

public class VerseFileEntry
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string? PackName { get; set; }
    public int LineCount { get; set; }
    public List<string> Classes { get; set; } = new();
    public List<string> Functions { get; set; } = new();
    public List<string> DeviceReferences { get; set; } = new();
    public List<string> Imports { get; set; } = new();
    public string Summary { get; set; } = "";
    [JsonIgnore] public string SearchText { get; set; } = "";
}

public class AssetEntry
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string AssetClass { get; set; } = "";
    public long FileSize { get; set; }
    public bool HasThumbnail { get; set; }
    [JsonIgnore] public string SearchText { get; set; } = "";
}

public class DeviceTypeEntry
{
    public string ClassName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Count { get; set; }
    [JsonIgnore] public string SearchText { get; set; } = "";
}

public class LibrarySearchResult
{
    public string Query { get; set; } = "";
    public List<SearchHit<VerseFileEntry>> VerseFiles { get; set; } = new();
    public List<SearchHit<AssetEntry>> Assets { get; set; } = new();
    public List<SearchHit<DeviceTypeEntry>> DeviceTypes { get; set; } = new();
}

public class VerseFileWithProject
{
    public string ProjectName { get; set; } = "";
    public VerseFileEntry File { get; set; } = new();
}

public class SearchHit<T>
{
    public T Item { get; set; } = default!;
    public int Score { get; set; }
    public string ProjectName { get; set; } = "";
}
