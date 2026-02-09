using FortniteForge.Core.Config;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace FortniteForge.Core.Services;

/// <summary>
/// Parses the .digest Verse files (fortnite.digest, verse.digest, unreal.digest)
/// to build a schema of available device types, their properties, events, and functions.
/// This schema is used for:
///   - Validating property modifications
///   - Providing autocomplete suggestions
///   - Understanding what properties are user-configurable
/// </summary>
public class DigestService
{
    private readonly ForgeConfig _config;
    private readonly ILogger<DigestService> _logger;
    private Dictionary<string, DeviceSchema>? _deviceSchemas;
    private bool _loaded;

    public DigestService(ForgeConfig config, ILogger<DigestService> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Loads and parses all .digest files found in the project.
    /// </summary>
    public void LoadDigests()
    {
        _deviceSchemas = new Dictionary<string, DeviceSchema>(StringComparer.OrdinalIgnoreCase);

        var digestFiles = FindDigestFiles();
        foreach (var file in digestFiles)
        {
            try
            {
                _logger.LogInformation("Parsing digest: {File}", Path.GetFileName(file));
                ParseDigestFile(file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse digest file: {File}", file);
            }
        }

        _loaded = true;
        _logger.LogInformation("Loaded {Count} device schemas from digest files.", _deviceSchemas.Count);
    }

    /// <summary>
    /// Gets the schema for a specific device type.
    /// </summary>
    public DeviceSchema? GetDeviceSchema(string deviceTypeName)
    {
        EnsureLoaded();
        _deviceSchemas!.TryGetValue(deviceTypeName, out var schema);

        // Also try without common suffixes
        if (schema == null)
        {
            var normalized = deviceTypeName
                .Replace("_C", "")
                .Replace("BP_", "")
                .Replace("_Device", "Device");
            _deviceSchemas.TryGetValue(normalized, out schema);
        }

        return schema;
    }

    /// <summary>
    /// Lists all known device types from the digest files.
    /// </summary>
    public List<string> ListDeviceTypes()
    {
        EnsureLoaded();
        return _deviceSchemas!.Keys.OrderBy(k => k).ToList();
    }

    /// <summary>
    /// Searches device schemas for a query match on name, properties, or events.
    /// </summary>
    public List<DeviceSchema> SearchSchemas(string query)
    {
        EnsureLoaded();
        return _deviceSchemas!.Values
            .Where(s =>
                s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Properties.Any(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                s.Events.Any(e => e.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>
    /// Validates that a property name is valid for a given device type.
    /// </summary>
    public PropertyValidation ValidateProperty(string deviceType, string propertyName)
    {
        var schema = GetDeviceSchema(deviceType);
        if (schema == null)
        {
            return new PropertyValidation
            {
                IsValid = false,
                Reason = $"Unknown device type: {deviceType}. Schema not found in digest files."
            };
        }

        var prop = schema.Properties.FirstOrDefault(p =>
            p.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));

        if (prop == null)
        {
            var similar = schema.Properties
                .Where(p => p.Name.Contains(propertyName, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Name)
                .ToList();

            return new PropertyValidation
            {
                IsValid = false,
                Reason = $"Property '{propertyName}' not found on {deviceType}.",
                Suggestions = similar
            };
        }

        return new PropertyValidation
        {
            IsValid = true,
            PropertyType = prop.Type,
            Reason = $"Property '{propertyName}' is valid ({prop.Type})."
        };
    }

    private List<string> FindDigestFiles()
    {
        var results = new List<string>();
        var searchPaths = new[]
        {
            _config.ContentPath,
            _config.ProjectPath,
            Path.Combine(_config.ProjectPath, "Plugins")
        };

        foreach (var searchPath in searchPaths)
        {
            if (!Directory.Exists(searchPath)) continue;

            results.AddRange(Directory.EnumerateFiles(searchPath, "*.digest.verse", SearchOption.AllDirectories));
            results.AddRange(Directory.EnumerateFiles(searchPath, "fortnite.digest", SearchOption.AllDirectories));
            results.AddRange(Directory.EnumerateFiles(searchPath, "verse.digest", SearchOption.AllDirectories));
            results.AddRange(Directory.EnumerateFiles(searchPath, "unreal.digest", SearchOption.AllDirectories));
        }

        return results.Distinct().ToList();
    }

    /// <summary>
    /// Parses a Verse .digest file to extract class/struct definitions.
    /// Verse syntax is similar to:
    ///   device_class := class(creative_device):
    ///       Property : type = default
    ///       EventName : event() = ...
    /// </summary>
    private void ParseDigestFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var lines = content.Split('\n');

        DeviceSchema? currentSchema = null;
        int indentLevel = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            var currentIndent = line.Length - trimmed.Length;

            // Detect class definitions
            // Pattern: name := class(parent_class):
            // or:      name<public> := class<concrete><final>(parent):
            var classMatch = Regex.Match(trimmed,
                @"^(\w+)(?:<[^>]+>)?\s*:=\s*class(?:<[^>]+>)*\s*\(([^)]*)\)\s*:");

            if (classMatch.Success)
            {
                var className = classMatch.Groups[1].Value;
                var parentClass = classMatch.Groups[2].Value;

                // Only track device-like classes (creative_device descendants or known device types)
                currentSchema = new DeviceSchema
                {
                    Name = className,
                    ParentClass = parentClass,
                    SourceFile = Path.GetFileName(filePath)
                };
                indentLevel = currentIndent;
                _deviceSchemas![className] = currentSchema;
                continue;
            }

            // If we're inside a class, parse members
            if (currentSchema != null && currentIndent > indentLevel)
            {
                // Property pattern: name<specifiers> : type = default
                var propMatch = Regex.Match(trimmed,
                    @"^(\w+)(?:<[^>]+>)?\s*:\s*([^=]+?)(?:\s*=\s*(.+))?$");

                if (propMatch.Success && !trimmed.StartsWith("//") && !trimmed.StartsWith("#"))
                {
                    var propName = propMatch.Groups[1].Value;
                    var propType = propMatch.Groups[2].Value.Trim();
                    var defaultValue = propMatch.Groups[3].Success ? propMatch.Groups[3].Value.Trim() : null;

                    // Check if it's an event
                    if (propType.Contains("event") || propType.Contains("listenable"))
                    {
                        currentSchema.Events.Add(propName);
                    }
                    else if (propType.Contains("(") && propType.Contains(")") && !propType.Contains("event"))
                    {
                        // It's a function/method
                        currentSchema.Functions.Add(propName);
                    }
                    else
                    {
                        currentSchema.Properties.Add(new SchemaProperty
                        {
                            Name = propName,
                            Type = propType,
                            DefaultValue = defaultValue,
                            IsEditable = !trimmed.Contains("<internal>") && !trimmed.Contains("<private>")
                        });
                    }
                }
            }
            else if (currentSchema != null && currentIndent <= indentLevel && !string.IsNullOrWhiteSpace(trimmed))
            {
                // Exited the class block
                currentSchema = null;
            }
        }
    }

    private void EnsureLoaded()
    {
        if (!_loaded)
            LoadDigests();
    }
}

/// <summary>
/// Schema definition extracted from digest files.
/// </summary>
public class DeviceSchema
{
    public string Name { get; set; } = "";
    public string ParentClass { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public List<SchemaProperty> Properties { get; set; } = new();
    public List<string> Events { get; set; } = new();
    public List<string> Functions { get; set; } = new();
}

public class SchemaProperty
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? DefaultValue { get; set; }
    public bool IsEditable { get; set; } = true;
}

public class PropertyValidation
{
    public bool IsValid { get; set; }
    public string Reason { get; set; } = "";
    public string? PropertyType { get; set; }
    public List<string> Suggestions { get; set; } = new();
}
