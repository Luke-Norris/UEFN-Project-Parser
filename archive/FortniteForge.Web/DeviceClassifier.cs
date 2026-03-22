using FortniteForge.Core.Config;
using FortniteForge.Core.Models;
using FortniteForge.Core.Services;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace FortniteForge.Web;

/// <summary>
/// Classifies external actors as devices vs static meshes/terrain.
/// Extracts full property data for devices including configurable values.
/// </summary>
public static class DeviceClassifier
{
    // Prefixes that definitively mark an asset as a device
    private static readonly string[] DevicePrefixes =
    {
        "Device_",
        "BP_Device_",
        "BP_Creative_",
    };

    // Class name patterns that indicate a device ONLY when not prefixed with Prop_/Athena_Prop_/CP_Prop_
    private static readonly string[] DeviceKeywords =
    {
        "Spawner", "Timer", "Trigger", "Button", "Barrier",
        "Checkpoint", "VendingMachine", "ClassSelector", "ClassDesigner",
        "ItemSpawner", "ScoreManager", "Mutator", "Teleporter",
        "Sequencer", "Tracker", "Elimination", "StormController",
        "MapIndicator", "Objective", "Scoreboard", "Matchmaking",
        "Sentry", "Minigame", "Powerup", "DamageVolume",
        "HealVolume", "SafeZone", "Keylock", "PinballBumper",
        "creative_device", "Transmitter", "Receiver",
    };

    // Patterns that are NEVER devices (static/decorative)
    private static readonly string[] NotDevicePrefixes =
    {
        "Prop_", "Athena_Prop_", "CP_Prop_", "CP_Apollo_", "CP_Asteria_",
        "MilitaryBase_", "CP_Glass_", "CP_Tree_", "CP_Rock_", "CP_Cliff_",
        "AssetImportData", "StaticMesh", "Material", "Texture",
        "Landscape", "Foliage", "HLOD", "LayerInfo",
    };

    // Internal/engine properties — never shown
    private static readonly HashSet<string> InternalProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "UCSSerializationIndex", "bNetAddressable", "CreationMethod",
        "AttachParent", "bReplicates", "bAutoActivate",
        "GenerateOverlapEvents", "bHiddenInGame", "bIsEditorOnly",
        "CanCharacterStepUpOn", "bCanEverAffectNavigation",
        "bVisualizeComponent", "bEditableWhenInherited",
        "bIsMainWorldOnly", "bNetUseOwnerRelevancy",
    };

    // Rendering/engine noise — shown but deprioritized (collapsed by default)
    private static readonly HashSet<string> NoiseProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "CachedMaxDrawDistance", "LDMaxDrawDistance", "CullDistance",
        "MaxDrawDistance", "MaxDistanceFadeRange", "MinDrawDistance",
        "DataVersion", "ActorTemplateID", "PlaysetPackagePathName",
        "BodyInstance", "LightmassSettings", "bOverrideLightMapRes",
        "OverriddenLightMapRes", "bCastStaticShadow", "bCastDynamicShadow",
        "CastShadow", "bAffectDynamicIndirectLighting",
        "IndirectLightingCacheQuality", "bUseAttachParentBound",
        "BoundsScale", "bReceivesDecals", "bAllowCullDistanceVolume",
        "bRenderInMainPass", "bRenderInDepthPass",
        "VisibilityId", "RuntimeGrid", "bIsSpatiallyLoaded",
        "bIsHLODRelevant", "HLODLayer", "bEnableAutoLODGeneration",
        "LODParentPrimitive", "bUseAsOccluder",
        "OnComponentPhysicsStateChanged", "VolumetricScatteringIntensity",
        "SerializationControl",
    };

    public static bool IsDevice(string className)
    {
        // Definitely NOT a device
        if (NotDevicePrefixes.Any(p => className.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return false;

        // Definitely a device (Device_ prefix, BP_Device_, BP_Creative_)
        if (DevicePrefixes.Any(p => className.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Check keywords in the name
        return DeviceKeywords.Any(kw => className.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Scans all external actors in a level and returns classified results.
    /// </summary>
    public static LevelContents ClassifyLevel(string levelPath, ForgeConfig config)
    {
        var result = new LevelContents { LevelPath = levelPath, LevelName = Path.GetFileNameWithoutExtension(levelPath) };
        var contentDir = Path.GetDirectoryName(levelPath) ?? "";
        var externalActorsDir = Path.Combine(contentDir, "__ExternalActors__", result.LevelName);

        if (!Directory.Exists(externalActorsDir))
            return result;

        var actorFiles = Directory.EnumerateFiles(externalActorsDir, "*.uasset", SearchOption.AllDirectories).ToList();
        result.TotalActorFiles = actorFiles.Count;

        foreach (var file in actorFiles)
        {
            try
            {
                var actor = ParseExternalActor(file);
                if (actor == null) continue;

                if (IsDevice(actor.ClassName))
                    result.Devices.Add(actor);
                else
                    result.StaticActors.Add(actor);
            }
            catch
            {
                result.ParseErrors++;
            }
        }

        return result;
    }

    /// <summary>
    /// Parses a single external actor and returns a detailed device/actor view.
    /// </summary>
    public static ActorDetail? ParseExternalActor(string filePath)
    {
        var asset = new UAsset(filePath, EngineVersion.VER_UE5_4);

        string? actorClass = null;
        string? actorName = null;
        var allProperties = new List<ActorProperty>();
        var components = new List<ComponentInfo>();
        double x = 0, y = 0, z = 0;
        double rotPitch = 0, rotYaw = 0, rotRoll = 0;
        bool hasPosition = false;

        foreach (var export in asset.Exports)
        {
            if (export is not NormalExport ne) continue;

            var className = ne.GetExportClassType()?.ToString() ?? "";
            var objName = ne.ObjectName?.ToString() ?? "";

            // Identify the primary actor (not a component)
            if (!className.Contains("Component") && !className.Contains("Model")
                && !className.Contains("HLODLayer") && !className.Contains("MetaData")
                && !className.Contains("Level") && !className.Contains("Brush")
                && actorClass == null)
            {
                actorClass = className;
                actorName = CleanActorName(objName, className);
            }

            // Extract all properties from this export
            var compProps = new List<ActorProperty>();
            foreach (var prop in ne.Data)
            {
                var ap = ConvertProperty(prop, asset);
                if (ap != null)
                    compProps.Add(ap);
            }

            components.Add(new ComponentInfo
            {
                ClassName = className,
                ObjectName = objName,
                Properties = compProps,
                PropertyCount = compProps.Count
            });

            // Look for transform
            var locProp = ne.Data.FirstOrDefault(p => p.Name?.ToString() == "RelativeLocation");
            if (locProp is StructPropertyData locStruct)
            {
                var vec = ExtractVector(locStruct);
                if (vec != null) { x = vec.Value.x; y = vec.Value.y; z = vec.Value.z; hasPosition = true; }
            }

            var rotProp = ne.Data.FirstOrDefault(p => p.Name?.ToString() == "RelativeRotation");
            if (rotProp is StructPropertyData rotStruct)
            {
                var vec = ExtractVector(rotStruct);
                if (vec != null) { rotPitch = vec.Value.x; rotYaw = vec.Value.y; rotRoll = vec.Value.z; }
            }
        }

        if (actorClass == null) return null;

        // Collect all non-internal properties, tagged with component info and importance
        foreach (var comp in components)
        {
            foreach (var prop in comp.Properties)
            {
                if (InternalProperties.Contains(prop.Name)) continue;

                prop.ComponentName = comp.ObjectName;
                prop.ComponentClass = comp.ClassName;
                prop.Importance = NoiseProperties.Contains(prop.Name) ? "low" : "high";
                allProperties.Add(prop);
            }
        }

        return new ActorDetail
        {
            ClassName = actorClass,
            DisplayName = actorName ?? CleanActorName(Path.GetFileNameWithoutExtension(filePath), actorClass),
            IsDevice = IsDevice(actorClass),
            FilePath = filePath,
            X = x, Y = y, Z = z,
            RotationYaw = rotYaw,
            HasPosition = hasPosition,
            Properties = allProperties,
            Components = components,
            TotalPropertyCount = allProperties.Count
        };
    }

    /// <summary>
    /// Strips UAID hashes and suffixes from actor names.
    /// "Device_VendingMachine_V2_C_UAID_347DF6B386882BDC01_1808791282" → "Vending Machine V2"
    /// </summary>
    public static string CleanActorName(string rawName, string className)
    {
        // Use class name as base, clean it up
        var name = className;

        // Remove common prefixes
        name = name.Replace("Device_", "").Replace("_C", "");

        // Remove UAID suffix from raw name if present
        var uaidIdx = rawName.IndexOf("_UAID_", StringComparison.OrdinalIgnoreCase);
        if (uaidIdx > 0)
            name = rawName[..uaidIdx].Replace("Device_", "").TrimEnd('_');

        // Convert underscores to spaces, handle V2/V3 etc
        name = name.Replace("_V2", " V2").Replace("_V3", " V3")
                    .Replace("_", " ")
                    .Replace("  ", " ")
                    .Trim();

        // Remove trailing "_C" class suffix
        if (name.EndsWith(" C")) name = name[..^2].Trim();

        return name;
    }

    private static ActorProperty? ConvertProperty(PropertyData prop, UAsset asset)
    {
        var name = prop.Name?.ToString();
        if (string.IsNullOrEmpty(name) || name == "None") return null;

        var ap = new ActorProperty
        {
            Name = name,
            Type = prop.PropertyType?.ToString() ?? prop.GetType().Name
        };

        ap.Value = prop switch
        {
            BoolPropertyData b => b.Value.ToString(),
            IntPropertyData i => i.Value.ToString(),
            FloatPropertyData f => f.Value.ToString("G"),
            DoublePropertyData d => d.Value.ToString("G"),
            StrPropertyData s => s.Value?.ToString() ?? "",
            NamePropertyData n => n.Value?.ToString() ?? "",
            TextPropertyData t => t.Value?.ToString() ?? "",
            EnumPropertyData e => CleanEnumValue(e.Value?.ToString()),
            BytePropertyData bp => bp.EnumValue?.ToString() ?? bp.Value.ToString(),
            ObjectPropertyData o => o.Value?.ToString() ?? "",
            SoftObjectPropertyData so => so.Value.AssetPath.PackageName?.ToString() ?? so.Value.ToString() ?? "",
            StructPropertyData sp => FormatStruct(sp, asset),
            SetPropertyData set => $"[{set.Value?.Length ?? 0} items]",
            ArrayPropertyData arr => $"[{arr.Value?.Length ?? 0} items]",
            MapPropertyData map => $"[{map.Value?.Count ?? 0} entries]",
            _ => SafeToString(prop)
        };

        ap.IsEditable = IsEditableProperty(prop);

        return ap;
    }

    private static string CleanEnumValue(string? val)
    {
        if (val == null) return "";
        // "EComponentCreationMethod::SimpleConstructionScript" → "SimpleConstructionScript"
        var colonIdx = val.LastIndexOf("::");
        return colonIdx >= 0 ? val[(colonIdx + 2)..] : val;
    }

    private static string FormatStruct(StructPropertyData sp, UAsset asset)
    {
        if (sp.Value == null || sp.Value.Count == 0) return "{}";

        var structType = sp.StructType?.ToString() ?? "";

        if (structType is "Vector" or "Rotator")
        {
            var vals = new List<string>();
            foreach (var v in sp.Value)
            {
                if (v is DoublePropertyData d) vals.Add(d.Value.ToString("F1"));
                else if (v is FloatPropertyData f) vals.Add(f.Value.ToString("F1"));
                else if (v is VectorPropertyData vp) return $"({vp.Value.X:F1}, {vp.Value.Y:F1}, {vp.Value.Z:F1})";
            }
            if (vals.Count >= 3) return $"({string.Join(", ", vals)})";
        }

        if (structType == "LinearColor" || structType == "Color")
        {
            return $"Color({string.Join(", ", sp.Value.Select(v => ConvertProperty(v, asset)?.Value ?? "?"))})";
        }

        // Generic struct: show first few fields
        var fields = sp.Value.Take(4).Select(v =>
        {
            var cp = ConvertProperty(v, asset);
            return cp != null ? $"{cp.Name}={cp.Value}" : null;
        }).Where(s => s != null);

        var result = string.Join(", ", fields);
        if (sp.Value.Count > 4) result += $", ...+{sp.Value.Count - 4}";
        return $"{{{result}}}";
    }

    private static bool IsEditableProperty(PropertyData prop)
    {
        // Properties that can be modified via the SetProperty flow
        return prop is BoolPropertyData or IntPropertyData or FloatPropertyData
            or DoublePropertyData or StrPropertyData or NamePropertyData
            or EnumPropertyData or BytePropertyData;
    }

    private static (double x, double y, double z)? ExtractVector(StructPropertyData sp)
    {
        double x = 0, y = 0, z = 0;
        bool found = false;
        foreach (var c in sp.Value)
        {
            var n = c.Name?.ToString() ?? "";
            if (c is DoublePropertyData d) { found = true; switch (n) { case "X": x = d.Value; break; case "Y": y = d.Value; break; case "Z": z = d.Value; break; } }
            else if (c is FloatPropertyData f) { found = true; switch (n) { case "X": x = f.Value; break; case "Y": y = f.Value; break; case "Z": z = f.Value; break; } }
            else if (c is VectorPropertyData v) return (v.Value.X, v.Value.Y, v.Value.Z);
        }
        return found ? (x, y, z) : null;
    }

    private static string SafeToString(PropertyData prop)
    {
        try { return prop.ToString() ?? prop.GetType().Name; }
        catch { return $"[{prop.GetType().Name}]"; }
    }
}

// === Models ===

public class LevelContents
{
    public string LevelPath { get; set; } = "";
    public string LevelName { get; set; } = "";
    public int TotalActorFiles { get; set; }
    public int ParseErrors { get; set; }
    public List<ActorDetail> Devices { get; set; } = new();
    public List<ActorDetail> StaticActors { get; set; } = new();
}

public class ActorDetail
{
    public string ClassName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsDevice { get; set; }
    public string? FilePath { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double RotationYaw { get; set; }
    public bool HasPosition { get; set; }
    public int TotalPropertyCount { get; set; }
    public List<ActorProperty> Properties { get; set; } = new();
    public List<ComponentInfo> Components { get; set; } = new();
}

public class ActorProperty
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsEditable { get; set; }
    /// <summary>
    /// True for all properties in external actor files — they only store overrides,
    /// so every property present IS a non-default value.
    /// </summary>
    public bool IsOverride { get; set; } = true;
    /// <summary>Which component this property belongs to.</summary>
    public string ComponentName { get; set; } = "";
    public string ComponentClass { get; set; } = "";
    /// <summary>high = meaningful config, low = rendering/engine noise</summary>
    public string Importance { get; set; } = "high";
}

public class ComponentInfo
{
    public string ClassName { get; set; } = "";
    public string ObjectName { get; set; } = "";
    public int PropertyCount { get; set; }
    public List<ActorProperty> Properties { get; set; } = new();
}
