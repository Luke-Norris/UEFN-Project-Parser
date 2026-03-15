using System.Text.Json;
using System.Text.Json.Serialization;
using FortniteForge.Core.Config;

namespace FortniteForge.Web;

/// <summary>
/// Manages a persistent list of UEFN projects with safety tiers.
/// Stores project list in a JSON file alongside the web server.
/// </summary>
public class ProjectManager
{
    private readonly string _storagePath;
    private ProjectStore _store;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public ProjectManager(string? storagePath = null)
    {
        _storagePath = storagePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".fortniteforge", "projects.json");
        _store = Load();
    }

    public List<ProjectEntry> ListProjects() => _store.Projects;

    public ProjectEntry? GetActiveProject()
        => _store.Projects.FirstOrDefault(p => p.Id == _store.ActiveProjectId);

    public ProjectEntry? GetProject(string id)
        => _store.Projects.FirstOrDefault(p => p.Id == id);

    public ProjectEntry AddProject(string projectPath, ProjectType type, string? customName = null)
    {
        var fullPath = Path.GetFullPath(projectPath);

        // Auto-detect: if the selected folder isn't a project root, look for one inside it
        if (!Directory.GetFiles(fullPath, "*.uefnproject").Any() && !Directory.GetFiles(fullPath, "*.uproject").Any())
        {
            var nested = ForgeConfig.DiscoverProjects(fullPath, maxDepth: 2);
            if (nested.Count == 1)
            {
                // Single project inside — use that as the root
                fullPath = nested[0];
            }
            else if (nested.Count > 1)
            {
                // Multiple projects — add them all
                ProjectEntry? first = null;
                foreach (var np in nested)
                {
                    var added = AddProject(np, type);
                    first ??= added;
                }
                return first!;
            }
        }

        // Check if already added
        var existing = _store.Projects.FirstOrDefault(p =>
            string.Equals(Path.GetFullPath(p.ProjectPath), fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            return existing;

        var config = new ForgeConfig { ProjectPath = fullPath };

        var entry = new ProjectEntry
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            ProjectPath = fullPath,
            Name = customName ?? config.ProjectName,
            Type = type,
            IsUefnProject = config.IsUefnProject,
            HasUrc = config.HasUrc,
            ContentPath = config.ContentPath,
            AddedAt = DateTime.UtcNow
        };

        // Count assets
        if (Directory.Exists(entry.ContentPath))
        {
            try
            {
                entry.AssetCount = Directory.EnumerateFiles(entry.ContentPath, "*.uasset", SearchOption.AllDirectories)
                    .Count(f => !f.Contains("__External"));
                entry.ExternalActorCount = Directory.EnumerateFiles(entry.ContentPath, "*.uasset", SearchOption.AllDirectories)
                    .Count(f => f.Contains("__ExternalActors__"));
                entry.VerseFileCount = Directory.EnumerateFiles(entry.ContentPath, "*.verse", SearchOption.AllDirectories).Count();
            }
            catch { }
        }

        _store.Projects.Add(entry);

        // Auto-activate if first project
        if (_store.Projects.Count == 1)
            _store.ActiveProjectId = entry.Id;

        Save();
        return entry;
    }

    public bool RemoveProject(string id)
    {
        var removed = _store.Projects.RemoveAll(p => p.Id == id) > 0;
        if (removed && _store.ActiveProjectId == id)
            _store.ActiveProjectId = _store.Projects.FirstOrDefault()?.Id;
        if (removed) Save();
        return removed;
    }

    public ProjectEntry? SetActive(string id)
    {
        var project = _store.Projects.FirstOrDefault(p => p.Id == id);
        if (project != null)
        {
            _store.ActiveProjectId = project.Id;
            Save();
        }
        return project;
    }

    /// <summary>
    /// Builds a ForgeConfig for a specific project with appropriate safety settings.
    /// </summary>
    public ForgeConfig BuildConfig(ProjectEntry project)
    {
        return new ForgeConfig
        {
            ProjectPath = project.ProjectPath,
            ReadOnly = project.Type == ProjectType.Library,
            ReadOnlyFolders = new List<string> { "FortniteGame", "Engine" }
        };
    }

    /// <summary>
    /// Scans a directory for UEFN projects and returns them (without adding).
    /// </summary>
    public List<DiscoveredProject> ScanDirectory(string searchPath)
    {
        var projectPaths = ForgeConfig.DiscoverProjects(searchPath);
        return projectPaths.Select(p =>
        {
            var cfg = new ForgeConfig { ProjectPath = p };
            var alreadyAdded = _store.Projects.Any(ex =>
                string.Equals(Path.GetFullPath(ex.ProjectPath), Path.GetFullPath(p), StringComparison.OrdinalIgnoreCase));

            int assets = 0, extActors = 0, verse = 0;
            if (Directory.Exists(cfg.ContentPath))
            {
                try
                {
                    assets = Directory.EnumerateFiles(cfg.ContentPath, "*.uasset", SearchOption.AllDirectories)
                        .Count(f => !f.Contains("__External"));
                    extActors = Directory.EnumerateFiles(cfg.ContentPath, "*.uasset", SearchOption.AllDirectories)
                        .Count(f => f.Contains("__ExternalActors__"));
                    verse = Directory.EnumerateFiles(cfg.ContentPath, "*.verse", SearchOption.AllDirectories).Count();
                }
                catch { }
            }

            return new DiscoveredProject
            {
                ProjectPath = p,
                ProjectName = cfg.ProjectName,
                IsUefnProject = cfg.IsUefnProject,
                HasUrc = cfg.HasUrc,
                AssetCount = assets,
                ExternalActorCount = extActors,
                VerseFileCount = verse,
                AlreadyAdded = alreadyAdded
            };
        }).ToList();
    }

    private ProjectStore Load()
    {
        if (File.Exists(_storagePath))
        {
            try
            {
                var json = File.ReadAllText(_storagePath);
                return JsonSerializer.Deserialize<ProjectStore>(json, JsonOpts) ?? new ProjectStore();
            }
            catch { }
        }
        return new ProjectStore();
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_storagePath, JsonSerializer.Serialize(_store, JsonOpts));
    }
}

// === Models ===

public class ProjectStore
{
    public string? ActiveProjectId { get; set; }
    public List<ProjectEntry> Projects { get; set; } = new();
}

public enum ProjectType
{
    /// <summary>
    /// Your active project. Staged writes when UEFN is open, direct writes with backup when closed.
    /// Full safety: copy-on-read, URC detection, git state checks before risky operations.
    /// </summary>
    MyProject,

    /// <summary>
    /// Reference material. Always read-only. Used for browsing, searching, and copying assets FROM.
    /// No writes ever — safe to point at any map collection.
    /// </summary>
    Library
}

public class ProjectEntry
{
    public string Id { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public string Name { get; set; } = "";
    public ProjectType Type { get; set; }
    public bool IsUefnProject { get; set; }
    public bool HasUrc { get; set; }
    public string ContentPath { get; set; } = "";
    public int AssetCount { get; set; }
    public int ExternalActorCount { get; set; }
    public int VerseFileCount { get; set; }
    public DateTime AddedAt { get; set; }
}

public class DiscoveredProject
{
    public string ProjectPath { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public bool IsUefnProject { get; set; }
    public bool HasUrc { get; set; }
    public int AssetCount { get; set; }
    public int ExternalActorCount { get; set; }
    public int VerseFileCount { get; set; }
    public bool AlreadyAdded { get; set; }
}

public static class ProjectTypeDescriptions
{
    public static string GetDescription(ProjectType type) => type switch
    {
        ProjectType.MyProject =>
            "Your active project. Changes are staged when UEFN is open and written directly (with backup) when closed. " +
            "Full safety system: copy-on-read, URC detection, git state verification before risky operations. " +
            "Use this for maps you are actively developing.",
        ProjectType.Library =>
            "Read-only reference material. No files will ever be modified. " +
            "Use this for map collections, downloaded assets, or projects you want to browse and copy assets FROM. " +
            "Safe to point at any directory — nothing will be changed.",
        _ => ""
    };
}
