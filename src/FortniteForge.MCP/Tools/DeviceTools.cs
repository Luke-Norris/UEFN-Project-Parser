using WellVersed.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace WellVersed.MCP.Tools;

/// <summary>
/// MCP tools for working with Creative Devices in UEFN levels.
/// </summary>
[McpServerToolType]
public class DeviceTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Lists all Creative Devices in a level (.umap) file. " +
        "Shows device type, location, label, properties, and wiring connections.")]
    public string list_devices(
        DeviceService deviceService,
        [Description("Path to the .umap level file. Use find_levels to discover available levels.")] string levelPath)
    {
        var devices = deviceService.ListDevicesInLevel(levelPath);

        if (devices.Count == 0)
            return "No Creative Devices found in this level.";

        var summary = devices.Select(d => new
        {
            d.ActorName,
            d.DeviceType,
            d.Label,
            Location = d.Location.ToString(),
            PropertyCount = d.Properties.Count,
            WiringCount = d.Wiring.Count,
            d.IsVerseDevice
        });

        return JsonSerializer.Serialize(new
        {
            level = levelPath,
            deviceCount = devices.Count,
            devices = summary
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Gets detailed information about a specific device including all properties, " +
        "wiring, and location. Use the actorName from list_devices.")]
    public string inspect_device(
        DeviceService deviceService,
        [Description("The actor name of the device (e.g., 'TriggerDevice_3')")] string deviceName,
        [Description("Path to the .umap level file containing the device")] string? levelPath = null)
    {
        if (levelPath != null)
        {
            var device = deviceService.GetDevice(levelPath, deviceName);
            if (device == null)
                return $"Device '{deviceName}' not found in level '{levelPath}'.";

            return JsonSerializer.Serialize(device, JsonOpts);
        }

        // Search across all levels
        var (foundDevice, foundLevel) = deviceService.FindDeviceByName(deviceName);
        if (foundDevice == null)
            return $"Device '{deviceName}' not found in any level.";

        return JsonSerializer.Serialize(new
        {
            foundInLevel = foundLevel,
            device = foundDevice
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Finds all levels (.umap files) in the project. " +
        "Use this to discover available levels before listing devices.")]
    public string find_levels(DeviceService deviceService)
    {
        var levels = deviceService.FindLevels();

        if (levels.Count == 0)
            return "No .umap level files found in the project.";

        return JsonSerializer.Serialize(new
        {
            count = levels.Count,
            levels
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Finds all devices of a specific type across all levels. " +
        "Useful for finding all triggers, spawners, etc.")]
    public string find_devices_by_type(
        DeviceService deviceService,
        [Description("Device type to search for (e.g., 'Trigger', 'Spawner', 'Mutator', 'Barrier')")] string deviceType)
    {
        var devices = deviceService.FindDevicesByType(deviceType);

        if (devices.Count == 0)
            return $"No devices matching type '{deviceType}' found.";

        var summary = devices.Select(d => new
        {
            d.ActorName,
            d.DeviceType,
            d.Label,
            d.LevelPath,
            Location = d.Location.ToString(),
            d.IsVerseDevice
        });

        return JsonSerializer.Serialize(new
        {
            searchType = deviceType,
            count = devices.Count,
            devices = summary
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Gets the schema/definition for a device class from the Fortnite digest files. " +
        "Shows all available properties, events, and functions for a device type.")]
    public string get_device_schema(
        DigestService digestService,
        [Description("Device class name (e.g., 'trigger_device', 'mutator_zone_device')")] string deviceClassName)
    {
        var schema = digestService.GetDeviceSchema(deviceClassName);

        if (schema == null)
        {
            // Try to find similar schemas
            var suggestions = digestService.SearchSchemas(deviceClassName);
            if (suggestions.Count > 0)
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"No schema found for '{deviceClassName}'.",
                    similarSchemas = suggestions.Select(s => s.Name).Take(10)
                }, JsonOpts);
            }
            return $"No schema found for '{deviceClassName}'. Load digest files first or check the class name.";
        }

        return JsonSerializer.Serialize(new
        {
            schema.Name,
            schema.ParentClass,
            schema.SourceFile,
            properties = schema.Properties.Select(p => new
            {
                p.Name,
                p.Type,
                p.DefaultValue,
                p.IsEditable
            }),
            schema.Events,
            schema.Functions
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Lists all known device types from the parsed digest files.")]
    public string list_device_types(DigestService digestService)
    {
        var types = digestService.ListDeviceTypes();

        if (types.Count == 0)
            return "No device types loaded. Digest files may not be parsed yet.";

        return JsonSerializer.Serialize(new
        {
            count = types.Count,
            deviceTypes = types
        }, JsonOpts);
    }
}
