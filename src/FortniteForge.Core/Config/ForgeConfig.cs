using System.Text.Json;
using System.Text.Json.Serialization;

namespace FortniteForge.Core.Config;

/// <summary>
/// Root configuration for FortniteForge — loaded from forge.config.json in the project root.
/// </summary>
public class ForgeConfig
{
    /// <summary>
    /// Path to the UEFN project root (contains the .uproject file).
    /// </summary>
    public string ProjectPath { get; set; } = "";

    /// <summary>
    /// Path to the UEFN/Fortnite installation directory.
    /// Used for build commands and engine reference.
    /// </summary>
    public string UefnInstallPath { get; set; } = "";

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
    /// Build configuration settings.
    /// </summary>
    public BuildConfig Build { get; set; } = new();

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
    /// </summary>
    public string ContentPath => Path.Combine(ProjectPath, "Content");

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

        if (!string.IsNullOrWhiteSpace(ProjectPath))
        {
            var uprojectFiles = Directory.Exists(ProjectPath)
                ? Directory.GetFiles(ProjectPath, "*.uproject")
                : Array.Empty<string>();
            if (uprojectFiles.Length == 0)
                issues.Add($"No .uproject file found in ProjectPath: {ProjectPath}");
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
