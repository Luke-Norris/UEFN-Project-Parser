using System.Text.Json;
using System.Text.Json.Serialization;

namespace FortniteForge.Core.Config;

/// <summary>
/// Root configuration for FortniteForge — loaded from forge.config.json in the project root.
/// </summary>
public class ForgeConfig
{
    /// <summary>
    /// Path to the UEFN project root (the folder containing the .uefnproject or .uproject file).
    /// </summary>
    public string ProjectPath { get; set; } = "";

    /// <summary>
    /// Path to the UEFN/Fortnite installation directory.
    /// Used for build commands and engine reference.
    /// </summary>
    public string UefnInstallPath { get; set; } = "";

    /// <summary>
    /// Hard read-only mode — blocks ALL write operations regardless of other settings.
    /// Use this when pointing at your active UEFN project for maximum safety.
    /// </summary>
    public bool ReadOnly { get; set; } = false;

    /// <summary>
    /// Folders within Content/ that are owned by the user and can be modified.
    /// Paths are relative to the project's Content/ directory.
    /// If empty, all non-cooked assets in Content/ are considered modifiable.
    /// </summary>
    public List<string> ModifiableFolders { get; set; } = new();

    /// <summary>
    /// Folders that should never be modified, even if they contain uncooked assets.
    /// Paths are relative to the project's Content/ directory.
    /// </summary>
    public List<string> ReadOnlyFolders { get; set; } = new() { "FortniteGame", "Engine" };

    /// <summary>
    /// Directory where backups are stored before modifications.
    /// Relative to the project root. Defaults to ".fortniteforge/backups".
    /// </summary>
    public string BackupDirectory { get; set; } = ".fortniteforge/backups";

    /// <summary>
    /// Directory where staged modifications are written (instead of source files).
    /// Relative to the project root. Defaults to ".fortniteforge/staged".
    /// </summary>
    public string StagingDirectory { get; set; } = ".fortniteforge/staged";

    /// <summary>
    /// Maximum number of backups to retain per file. Oldest are pruned.
    /// </summary>
    public int MaxBackupsPerFile { get; set; } = 10;

    /// <summary>
    /// Whether modifications require explicit dry-run approval before applying.
    /// Strongly recommended to keep enabled.
    /// </summary>
    public bool RequireDryRun { get; set; } = true;

    /// <summary>
    /// Whether to automatically create a backup before every modification.
    /// </summary>
    public bool AutoBackup { get; set; } = true;

    /// <summary>
    /// Optional path to forward Verse compilation errors to (e.g., another Claude Code session).
    /// </summary>
    public string? VerseErrorForwardPath { get; set; }

    /// <summary>
    /// Path to a directory containing .verse reference files for the Verse Reference Library.
    /// Searched recursively for .verse files on first query.
    /// </summary>
    public string? ReferenceLibraryPath { get; set; }

    /// <summary>
    /// Build configuration settings.
    /// </summary>
    public BuildConfig Build { get; set; } = new();

    /// <summary>
    /// Discovers all UEFN project roots within a directory (recursively).
    /// A project root is a directory containing a .uefnproject file.
    /// Useful for scanning map collections.
    /// </summary>
    public static List<string> DiscoverProjects(string searchPath, int maxDepth = 4)
    {
        var results = new List<string>();
        if (!Directory.Exists(searchPath)) return results;

        try
        {
            foreach (var file in Directory.EnumerateFiles(searchPath, "*.uefnproject", SearchOption.AllDirectories))
            {
                var depth = Path.GetRelativePath(searchPath, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length - 1;
                if (depth <= maxDepth)
                    results.Add(Path.GetDirectoryName(file)!);
            }
        }
        catch { }

        // Also check for .uproject files
        try
        {
            foreach (var file in Directory.EnumerateFiles(searchPath, "*.uproject", SearchOption.AllDirectories))
            {
                var dir = Path.GetDirectoryName(file)!;
                if (!results.Contains(dir))
                {
                    var depth = Path.GetRelativePath(searchPath, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length - 1;
                    if (depth <= maxDepth)
                        results.Add(dir);
                }
            }
        }
        catch { }

        return results.OrderBy(p => p).ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ForgeConfig Load(string configPath)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"Config file not found: {configPath}");

        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<ForgeConfig>(json, JsonOptions)
               ?? throw new InvalidOperationException("Failed to deserialize config file.");
    }

    public void Save(string configPath)
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        var dir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(configPath, json);
    }

    /// <summary>
    /// Resolves the full path to the Content directory.
    /// Handles both standard Unreal (.uproject → Content/) and
    /// UEFN (.uefnproject → Plugins/ProjectName/Content/) layouts.
    /// </summary>
    [JsonIgnore]
    public string ContentPath
    {
        get
        {
            if (string.IsNullOrEmpty(ProjectPath) || !Directory.Exists(ProjectPath))
                return Path.Combine(ProjectPath, "Content");

            // Check for UEFN layout: Plugins/<ProjectName>/Content/
            var pluginsDir = Path.Combine(ProjectPath, "Plugins");
            if (Directory.Exists(pluginsDir))
            {
                foreach (var pluginDir in Directory.GetDirectories(pluginsDir))
                {
                    var contentDir = Path.Combine(pluginDir, "Content");
                    if (Directory.Exists(contentDir))
                        return contentDir;
                }
            }

            // Standard Unreal layout
            return Path.Combine(ProjectPath, "Content");
        }
    }

    /// <summary>
    /// Detects the project name from .uefnproject or .uproject file.
    /// </summary>
    [JsonIgnore]
    public string ProjectName
    {
        get
        {
            if (string.IsNullOrEmpty(ProjectPath) || !Directory.Exists(ProjectPath))
                return Path.GetFileName(ProjectPath);

            var uefnProjects = Directory.GetFiles(ProjectPath, "*.uefnproject");
            if (uefnProjects.Length > 0)
                return Path.GetFileNameWithoutExtension(uefnProjects[0]);

            var uprojects = Directory.GetFiles(ProjectPath, "*.uproject");
            if (uprojects.Length > 0)
                return Path.GetFileNameWithoutExtension(uprojects[0]);

            return Path.GetFileName(ProjectPath);
        }
    }

    /// <summary>
    /// Whether this project uses the UEFN project format (.uefnproject).
    /// </summary>
    [JsonIgnore]
    public bool IsUefnProject
    {
        get
        {
            if (string.IsNullOrEmpty(ProjectPath) || !Directory.Exists(ProjectPath))
                return false;
            return Directory.GetFiles(ProjectPath, "*.uefnproject").Length > 0;
        }
    }

    /// <summary>
    /// Whether this project has Unreal Revision Control (.urc/) active.
    /// </summary>
    [JsonIgnore]
    public bool HasUrc => Directory.Exists(Path.Combine(ProjectPath, ".urc"));

    /// <summary>
    /// Validates the configuration and returns any issues found.
    /// </summary>
    public List<string> Validate()
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(ProjectPath))
            issues.Add("ProjectPath is required.");
        else if (!Directory.Exists(ProjectPath))
            issues.Add($"ProjectPath does not exist: {ProjectPath}");

        if (!string.IsNullOrWhiteSpace(ProjectPath) && Directory.Exists(ProjectPath))
        {
            // Check for either .uefnproject or .uproject
            var uefnProjects = Directory.GetFiles(ProjectPath, "*.uefnproject");
            var uprojects = Directory.GetFiles(ProjectPath, "*.uproject");

            if (uefnProjects.Length == 0 && uprojects.Length == 0)
                issues.Add($"No .uefnproject or .uproject file found in ProjectPath: {ProjectPath}");

            // Check Content path resolves
            if (!Directory.Exists(ContentPath))
                issues.Add($"Content directory not found at: {ContentPath}");
        }

        return issues;
    }
}

public class BuildConfig
{
    /// <summary>
    /// Command or path to the UEFN build executable.
    /// </summary>
    public string BuildCommand { get; set; } = "";

    /// <summary>
    /// Additional arguments to pass to the build command.
    /// </summary>
    public string BuildArguments { get; set; } = "";

    /// <summary>
    /// Directory where build logs are written.
    /// </summary>
    public string LogDirectory { get; set; } = "";

    /// <summary>
    /// Timeout in seconds for build operations.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;
}
