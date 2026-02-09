using FortniteForge.Core.Config;
using FortniteForge.Core.Models;
using FortniteForge.Core.Safety;
using Microsoft.Extensions.Logging;

namespace FortniteForge.Core.Services;

/// <summary>
/// Audits UEFN project assets and device configurations for issues.
/// Provides intelligent checks that help Claude understand why something might not work.
/// </summary>
public class AuditService
{
    private readonly ForgeConfig _config;
    private readonly DeviceService _deviceService;
    private readonly AssetService _assetService;
    private readonly DigestService _digestService;
    private readonly AssetGuard _guard;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        ForgeConfig config,
        DeviceService deviceService,
        AssetService assetService,
        DigestService digestService,
        AssetGuard guard,
        ILogger<AuditService> logger)
    {
        _config = config;
        _deviceService = deviceService;
        _assetService = assetService;
        _digestService = digestService;
        _guard = guard;
        _logger = logger;
    }

    /// <summary>
    /// Runs a full audit on a level file — checks all devices for common issues.
    /// </summary>
    public AuditResult AuditLevel(string levelPath)
    {
        var result = new AuditResult { Target = levelPath };

        try
        {
            var devices = _deviceService.ListDevicesInLevel(levelPath);

            // Run all audit checks
            CheckUnwiredDevices(devices, result);
            CheckDefaultProperties(devices, result);
            CheckDuplicateDeviceNames(devices, result);
            CheckDeviceReferences(devices, result);
            CheckSpawnerConfigurations(devices, result);
            CheckVerseDeviceBindings(devices, result);
        }
        catch (Exception ex)
        {
            result.Findings.Add(new AuditFinding
            {
                Severity = AuditSeverity.Error,
                Category = "Parse",
                Message = $"Failed to parse level: {ex.Message}",
                Location = levelPath
            });
        }

        result.Status = result.Findings.Any(f => f.Severity == AuditSeverity.Error)
            ? AuditStatus.Fail
            : result.Findings.Any(f => f.Severity == AuditSeverity.Warning)
                ? AuditStatus.Warning
                : AuditStatus.Pass;

        return result;
    }

    /// <summary>
    /// Audits a specific device by name.
    /// </summary>
    public AuditResult AuditDevice(string levelPath, string deviceName)
    {
        var result = new AuditResult { Target = deviceName };
        var device = _deviceService.GetDevice(levelPath, deviceName);

        if (device == null)
        {
            result.Status = AuditStatus.Fail;
            result.Findings.Add(new AuditFinding
            {
                Severity = AuditSeverity.Error,
                Category = "NotFound",
                Message = $"Device '{deviceName}' not found in level.",
                Location = levelPath
            });
            return result;
        }

        // Check this device's properties against schema
        CheckDeviceAgainstSchema(device, result);

        // Check wiring
        if (device.Wiring.Count == 0)
        {
            result.Findings.Add(new AuditFinding
            {
                Severity = AuditSeverity.Info,
                Category = "Wiring",
                Message = "Device has no signal connections.",
                Location = device.ActorName,
                Suggestion = "If this device should respond to events, connect it to other devices."
            });
        }

        result.Status = result.Findings.Any(f => f.Severity == AuditSeverity.Error)
            ? AuditStatus.Fail
            : result.Findings.Any(f => f.Severity == AuditSeverity.Warning)
                ? AuditStatus.Warning
                : AuditStatus.Pass;

        return result;
    }

    /// <summary>
    /// Scans the entire project for common issues.
    /// </summary>
    public AuditResult AuditProject()
    {
        var result = new AuditResult { Target = _config.ProjectPath };

        // Check configuration
        var configIssues = _config.Validate();
        foreach (var issue in configIssues)
        {
            result.Findings.Add(new AuditFinding
            {
                Severity = AuditSeverity.Error,
                Category = "Config",
                Message = issue,
                Location = "forge.config.json"
            });
        }

        // Audit each level
        foreach (var level in _deviceService.FindLevels())
        {
            try
            {
                var levelResult = AuditLevel(level);
                result.Findings.AddRange(levelResult.Findings);
            }
            catch (Exception ex)
            {
                result.Findings.Add(new AuditFinding
                {
                    Severity = AuditSeverity.Warning,
                    Category = "Parse",
                    Message = $"Could not audit level: {ex.Message}",
                    Location = level
                });
            }
        }

        // Check for orphaned assets
        CheckOrphanedAssets(result);

        result.Status = result.Findings.Any(f => f.Severity == AuditSeverity.Error)
            ? AuditStatus.Fail
            : result.Findings.Any(f => f.Severity == AuditSeverity.Warning)
                ? AuditStatus.Warning
                : AuditStatus.Pass;

        return result;
    }

    private void CheckUnwiredDevices(List<DeviceInfo> devices, AuditResult result)
    {
        // Devices that typically need wiring to function
        var wiringExpected = new[] { "Trigger", "Button", "Timer", "Sequence", "Signal" };

        foreach (var device in devices)
        {
            if (device.Wiring.Count == 0 &&
                wiringExpected.Any(w => device.DeviceType.Contains(w, StringComparison.OrdinalIgnoreCase)))
            {
                result.Findings.Add(new AuditFinding
                {
                    Severity = AuditSeverity.Warning,
                    Category = "Wiring",
                    Message = $"Device '{device.ActorName}' ({device.DeviceType}) has no signal connections but typically requires wiring.",
                    Location = device.ActorName,
                    Suggestion = $"Connect this {device.DeviceType} to other devices to make it functional."
                });
            }
        }
    }

    private void CheckDefaultProperties(List<DeviceInfo> devices, AuditResult result)
    {
        foreach (var device in devices)
        {
            // Flag devices that appear to have all default values (no customization)
            var nonDefaultProps = device.Properties.Count(p =>
                !string.IsNullOrEmpty(p.Value) &&
                p.Value != "0" &&
                p.Value != "False" &&
                p.Value != "null" &&
                p.Value != "None");

            if (device.Properties.Count > 5 && nonDefaultProps <= 1)
            {
                result.Findings.Add(new AuditFinding
                {
                    Severity = AuditSeverity.Info,
                    Category = "Configuration",
                    Message = $"Device '{device.ActorName}' ({device.DeviceType}) appears to use mostly default values.",
                    Location = device.ActorName,
                    Suggestion = "Review if this device needs custom configuration."
                });
            }
        }
    }

    private void CheckDuplicateDeviceNames(List<DeviceInfo> devices, AuditResult result)
    {
        var duplicates = devices
            .Where(d => !string.IsNullOrEmpty(d.Label))
            .GroupBy(d => d.Label)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicates)
        {
            result.Findings.Add(new AuditFinding
            {
                Severity = AuditSeverity.Warning,
                Category = "Naming",
                Message = $"Multiple devices share the label '{group.Key}': {string.Join(", ", group.Select(d => d.ActorName))}",
                Location = group.Key,
                Suggestion = "Use unique labels to avoid confusion when debugging."
            });
        }
    }

    private void CheckDeviceReferences(List<DeviceInfo> devices, AuditResult result)
    {
        var deviceNames = new HashSet<string>(devices.Select(d => d.ActorName), StringComparer.OrdinalIgnoreCase);

        foreach (var device in devices)
        {
            foreach (var wire in device.Wiring)
            {
                if (!string.IsNullOrEmpty(wire.TargetDevice) &&
                    !deviceNames.Contains(wire.TargetDevice) &&
                    !wire.TargetDevice.Contains("null", StringComparison.OrdinalIgnoreCase))
                {
                    result.Findings.Add(new AuditFinding
                    {
                        Severity = AuditSeverity.Error,
                        Category = "Reference",
                        Message = $"Device '{device.ActorName}' references unknown target '{wire.TargetDevice}'.",
                        Location = device.ActorName,
                        Suggestion = "The target device may have been deleted or renamed."
                    });
                }
            }
        }
    }

    private void CheckSpawnerConfigurations(List<DeviceInfo> devices, AuditResult result)
    {
        foreach (var device in devices)
        {
            if (!device.DeviceType.Contains("Spawner", StringComparison.OrdinalIgnoreCase))
                continue;

            // Check if spawner has any item/class configured
            var hasItemConfig = device.Properties.Any(p =>
                (p.Name.Contains("Item", StringComparison.OrdinalIgnoreCase) ||
                 p.Name.Contains("Class", StringComparison.OrdinalIgnoreCase) ||
                 p.Name.Contains("Pickup", StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrEmpty(p.Value) &&
                p.Value != "null" &&
                p.Value != "None");

            if (!hasItemConfig)
            {
                result.Findings.Add(new AuditFinding
                {
                    Severity = AuditSeverity.Warning,
                    Category = "Configuration",
                    Message = $"Spawner '{device.ActorName}' does not appear to have an item configured.",
                    Location = device.ActorName,
                    Suggestion = "Set the item/class property to define what this spawner creates."
                });
            }
        }
    }

    private void CheckVerseDeviceBindings(List<DeviceInfo> devices, AuditResult result)
    {
        foreach (var device in devices.Where(d => d.IsVerseDevice))
        {
            if (string.IsNullOrEmpty(device.VerseClassPath))
            {
                result.Findings.Add(new AuditFinding
                {
                    Severity = AuditSeverity.Warning,
                    Category = "Verse",
                    Message = $"Verse device '{device.ActorName}' does not have a clear Verse class binding.",
                    Location = device.ActorName,
                    Suggestion = "Ensure the Verse device is properly linked to its Verse class."
                });
            }
        }
    }

    private void CheckDeviceAgainstSchema(DeviceInfo device, AuditResult result)
    {
        var schema = _deviceService.GetDeviceSchema(device.DeviceClass);
        if (schema == null)
        {
            result.Findings.Add(new AuditFinding
            {
                Severity = AuditSeverity.Info,
                Category = "Schema",
                Message = $"No schema found for device class '{device.DeviceClass}'. Cannot validate properties.",
                Location = device.ActorName
            });
            return;
        }

        // Check for properties not in the schema
        foreach (var prop in device.Properties)
        {
            var schemaProp = schema.Properties.FirstOrDefault(sp =>
                sp.Name.Equals(prop.Name, StringComparison.OrdinalIgnoreCase));

            if (schemaProp != null && !schemaProp.IsEditable)
            {
                result.Findings.Add(new AuditFinding
                {
                    Severity = AuditSeverity.Warning,
                    Category = "Property",
                    Message = $"Property '{prop.Name}' on '{device.ActorName}' is marked as internal/non-editable.",
                    Location = device.ActorName
                });
            }
        }
    }

    private void CheckOrphanedAssets(AuditResult result)
    {
        // Check for assets that might not be referenced by any level
        try
        {
            var assets = _assetService.ListAssets();
            var blueprints = assets.Where(a =>
                a.AssetClass.Contains("Blueprint", StringComparison.OrdinalIgnoreCase) &&
                a.IsModifiable);

            // Just report the count for now — full reference checking is expensive
            var count = blueprints.Count();
            if (count > 0)
            {
                result.Findings.Add(new AuditFinding
                {
                    Severity = AuditSeverity.Info,
                    Category = "Assets",
                    Message = $"Project contains {count} user-created Blueprint assets. Use inspect to verify they are referenced.",
                    Location = _config.ContentPath
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not check for orphaned assets");
        }
    }
}
