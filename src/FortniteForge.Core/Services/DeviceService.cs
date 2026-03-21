using FortniteForge.Core.Config;
using FortniteForge.Core.Models;
using Microsoft.Extensions.Logging;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace FortniteForge.Core.Services;

/// <summary>
/// Specialized service for working with Creative Devices in UEFN levels.
/// Understands device patterns — actors, components, wiring, channels.
/// </summary>
public class DeviceService
{
    private readonly ForgeConfig _config;
    private readonly AssetService _assetService;
    private readonly DigestService _digestService;
    private readonly ILogger<DeviceService> _logger;

    // Known device class patterns in UEFN Creative
    private static readonly HashSet<string> DeviceClassPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BP_", "PBWA_", "B_"
    };

    private static readonly HashSet<string> DeviceClassSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "_C", "_Device_C", "Device_C"
    };

    public DeviceService(
        ForgeConfig config,
        AssetService assetService,
        DigestService digestService,
        ILogger<DeviceService> logger)
    {
        _config = config;
        _assetService = assetService;
        _digestService = digestService;
        _logger = logger;
    }

    /// <summary>
    /// Lists all Creative Devices in a .umap level file.
    /// </summary>
    public List<DeviceInfo> ListDevicesInLevel(string levelPath)
    {
        var asset = _assetService.OpenAsset(levelPath);
        var devices = new List<DeviceInfo>();

        foreach (var export in asset.Exports)
        {
            var className = export.GetExportClassType()?.ToString() ?? "";

            if (!IsDeviceClass(className))
                continue;

            var device = ExtractDeviceInfo(asset, export, levelPath);
            if (device != null)
                devices.Add(device);
        }

        return devices;
    }

    /// <summary>
    /// Gets detailed info about a specific device in a level.
    /// </summary>
    public DeviceInfo? GetDevice(string levelPath, string actorName)
    {
        var asset = _assetService.OpenAsset(levelPath);

        foreach (var export in asset.Exports)
        {
            var exportName = export.ObjectName?.ToString() ?? "";
            if (exportName.Equals(actorName, StringComparison.OrdinalIgnoreCase))
            {
                return ExtractDeviceInfo(asset, export, levelPath);
            }
        }

        return null;
    }

    /// <summary>
    /// Finds all levels (.umap files) in the project.
    /// </summary>
    public List<string> FindLevels()
    {
        var contentPath = _config.ContentPath;
        if (!Directory.Exists(contentPath))
            return new List<string>();

        return Directory.EnumerateFiles(contentPath, "*.umap", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .ToList();
    }

    /// <summary>
    /// Gets the schema for a device's class from the digest files.
    /// </summary>
    public DeviceSchema? GetDeviceSchema(string deviceClassName)
    {
        // Strip common UE prefixes/suffixes to find the Verse class name
        var verseName = deviceClassName
            .Replace("BP_", "")
            .Replace("PBWA_", "")
            .Replace("_C", "")
            .Replace("_Device", "_device")
            .Replace("Device", "_device");

        return _digestService.GetDeviceSchema(verseName)
               ?? _digestService.GetDeviceSchema(deviceClassName);
    }

    /// <summary>
    /// Finds devices of a specific type across all levels.
    /// </summary>
    public List<DeviceInfo> FindDevicesByType(string deviceType)
    {
        var devices = new List<DeviceInfo>();

        foreach (var level in FindLevels())
        {
            try
            {
                var levelDevices = ListDevicesInLevel(level);
                devices.AddRange(levelDevices.Where(d =>
                    d.DeviceClass.Contains(deviceType, StringComparison.OrdinalIgnoreCase) ||
                    d.DeviceType.Contains(deviceType, StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not parse level: {Level}", level);
            }
        }

        return devices;
    }

    /// <summary>
    /// Finds a device by name across all levels.
    /// </summary>
    public (DeviceInfo? Device, string? LevelPath) FindDeviceByName(string deviceName)
    {
        foreach (var level in FindLevels())
        {
            try
            {
                var device = GetDevice(level, deviceName);
                if (device != null)
                    return (device, level);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not parse level: {Level}", level);
            }
        }

        return (null, null);
    }

    private DeviceInfo? ExtractDeviceInfo(UAsset asset, Export export, string levelPath)
    {
        var device = new DeviceInfo
        {
            ActorName = export.ObjectName?.ToString() ?? "",
            DeviceClass = export.GetExportClassType()?.ToString() ?? "",
            LevelPath = levelPath
        };

        device.DeviceType = PrettifyDeviceClassName(device.DeviceClass);
        device.IsVerseDevice = device.DeviceClass.Contains("Verse", StringComparison.OrdinalIgnoreCase) ||
                                device.DeviceClass.Contains("verse", StringComparison.Ordinal);

        if (export is NormalExport normalExport)
        {
            foreach (var prop in normalExport.Data)
            {
                var propName = prop.Name?.ToString() ?? "";

                // Extract transform
                if (propName == "RelativeLocation" && prop is StructPropertyData locStruct)
                {
                    device.Location = ExtractVector(locStruct);
                }
                else if (propName == "RelativeRotation" && prop is StructPropertyData rotStruct)
                {
                    device.Rotation = ExtractVector(rotStruct);
                }
                else if (propName == "RelativeScale3D" && prop is StructPropertyData scaleStruct)
                {
                    device.Scale = ExtractVector(scaleStruct);
                }
                else if (propName == "ActorLabel" && prop is StrPropertyData labelProp)
                {
                    device.Label = labelProp.Value?.ToString() ?? "";
                }
                else
                {
                    // Add as a configurable property
                    device.Properties.Add(new PropertyInfo
                    {
                        Name = propName,
                        Type = prop.PropertyType?.ToString() ?? prop.GetType().Name,
                        Value = FormatPropertyValue(prop),
                        ArrayIndex = prop.ArrayIndex
                    });
                }
            }

            // Extract wiring from properties that look like signal connections
            ExtractWiring(device, normalExport.Data);
        }

        return device;
    }

    private void ExtractWiring(DeviceInfo device, List<PropertyData> properties)
    {
        foreach (var prop in properties)
        {
            var propName = prop.Name?.ToString() ?? "";

            // UEFN wiring is typically stored in array properties referencing other actors
            // and their event/function names. The exact structure depends on the device type.
            if (prop is ArrayPropertyData arrayProp && arrayProp.Value != null)
            {
                if (propName.Contains("Binding", StringComparison.OrdinalIgnoreCase) ||
                    propName.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
                    propName.Contains("Wire", StringComparison.OrdinalIgnoreCase) ||
                    propName.Contains("Link", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var element in arrayProp.Value)
                    {
                        if (element is StructPropertyData structElement)
                        {
                            var wiring = ExtractWiringFromStruct(structElement);
                            if (wiring != null)
                                device.Wiring.Add(wiring);
                        }
                    }
                }
            }
        }
    }

    private DeviceWiring? ExtractWiringFromStruct(StructPropertyData structProp)
    {
        var wiring = new DeviceWiring();
        bool hasData = false;

        foreach (var prop in structProp.Value)
        {
            var name = prop.Name?.ToString() ?? "";

            if (name.Contains("Event", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Output", StringComparison.OrdinalIgnoreCase))
            {
                wiring.OutputEvent = FormatPropertyValue(prop);
                hasData = true;
            }
            else if (name.Contains("Target", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Device", StringComparison.OrdinalIgnoreCase))
            {
                wiring.TargetDevice = FormatPropertyValue(prop);
                hasData = true;
            }
            else if (name.Contains("Action", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Input", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Function", StringComparison.OrdinalIgnoreCase))
            {
                wiring.InputAction = FormatPropertyValue(prop);
                hasData = true;
            }
            else if (name.Contains("Channel", StringComparison.OrdinalIgnoreCase))
            {
                wiring.Channel = FormatPropertyValue(prop);
            }
        }

        return hasData ? wiring : null;
    }

    private Vector3Info ExtractVector(StructPropertyData structProp)
    {
        var vec = new Vector3Info();
        foreach (var prop in structProp.Value)
        {
            // UE5.4 may use VectorPropertyData with X/Y/Z packed directly
            if (prop is UAssetAPI.PropertyTypes.Structs.VectorPropertyData vecProp)
            {
                vec.X = (float)vecProp.Value.X;
                vec.Y = (float)vecProp.Value.Y;
                vec.Z = (float)vecProp.Value.Z;
                return vec;
            }

            var name = prop.Name?.ToString() ?? "";
            if (prop is FloatPropertyData floatProp)
            {
                switch (name)
                {
                    case "X": vec.X = floatProp.Value; break;
                    case "Y": vec.Y = floatProp.Value; break;
                    case "Z": vec.Z = floatProp.Value; break;
                    case "Pitch": vec.X = floatProp.Value; break;
                    case "Yaw": vec.Y = floatProp.Value; break;
                    case "Roll": vec.Z = floatProp.Value; break;
                }
            }
            else if (prop is DoublePropertyData doubleProp)
            {
                switch (name)
                {
                    case "X": vec.X = (float)doubleProp.Value; break;
                    case "Y": vec.Y = (float)doubleProp.Value; break;
                    case "Z": vec.Z = (float)doubleProp.Value; break;
                    case "Pitch": vec.X = (float)doubleProp.Value; break;
                    case "Yaw": vec.Y = (float)doubleProp.Value; break;
                    case "Roll": vec.Z = (float)doubleProp.Value; break;
                }
            }
        }
        return vec;
    }

    private static string FormatPropertyValue(PropertyData prop)
    {
        return prop switch
        {
            BoolPropertyData b => b.Value.ToString(),
            IntPropertyData i => i.Value.ToString(),
            FloatPropertyData f => f.Value.ToString(),
            DoublePropertyData d => d.Value.ToString(),
            StrPropertyData s => s.Value?.ToString() ?? "null",
            NamePropertyData n => n.Value?.ToString() ?? "null",
            ObjectPropertyData o => o.Value?.ToString() ?? "null",
            EnumPropertyData e => e.Value?.ToString() ?? "null",
            _ => prop.ToString() ?? "unknown"
        };
    }

    private static bool IsDeviceClass(string className)
    {
        if (string.IsNullOrEmpty(className))
            return false;

        // Match common UEFN Creative device class patterns
        return className.Contains("Device", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("Spawner", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("Trigger", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("Mutator", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("Granter", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("Barrier", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("Teleporter", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("Zone", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("Volume", StringComparison.OrdinalIgnoreCase) ||
               className.Contains("Prop", StringComparison.OrdinalIgnoreCase) ||
               className.StartsWith("BP_", StringComparison.OrdinalIgnoreCase) ||
               className.StartsWith("PBWA_", StringComparison.OrdinalIgnoreCase);
    }

    private static string PrettifyDeviceClassName(string className)
    {
        return className
            .Replace("BP_", "")
            .Replace("PBWA_", "")
            .Replace("_C", "")
            .Replace("_", " ");
    }
}
