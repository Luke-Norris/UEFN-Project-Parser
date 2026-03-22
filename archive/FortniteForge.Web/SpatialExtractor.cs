using FortniteForge.Core.Config;
using FortniteForge.Core.Services;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace FortniteForge.Web;

/// <summary>
/// Extracts actor info from UEFN external actor files.
/// Provides class breakdown and positions where available.
/// </summary>
public static class SpatialExtractor
{
    public static SpatialData ExtractActorPositions(string levelPath, ForgeConfig config, AssetService assetService)
    {
        var result = new SpatialData();
        var levelName = Path.GetFileNameWithoutExtension(levelPath);
        var contentDir = Path.GetDirectoryName(levelPath) ?? "";
        var externalActorsDir = Path.Combine(contentDir, "__ExternalActors__", levelName);

        if (!Directory.Exists(externalActorsDir))
            return result;

        var actorFiles = Directory.EnumerateFiles(externalActorsDir, "*.uasset", SearchOption.AllDirectories).ToList();
        result.TotalFiles = actorFiles.Count;

        foreach (var file in actorFiles)
        {
            try
            {
                var asset = new UAsset(file, EngineVersion.VER_UE5_4);

                string? actorClass = null;
                string? actorName = null;
                (double x, double y, double z)? location = null;

                foreach (var export in asset.Exports)
                {
                    var className = export.GetExportClassType()?.ToString() ?? "";
                    var objName = export.ObjectName?.ToString() ?? "";

                    // Find the primary actor export (not a component)
                    if (!className.Contains("Component") && !className.Contains("Model")
                        && !className.Contains("HLODLayer") && !className.Contains("MetaData")
                        && !className.Contains("Level") && !className.Contains("Brush")
                        && actorClass == null)
                    {
                        actorClass = className;
                        actorName = objName;
                    }

                    // Try to extract position from property data
                    if (export is NormalExport ne && ne.Data.Count > 0)
                    {
                        var loc = ExtractVector(ne, "RelativeLocation");
                        if (loc != null) location = loc;
                    }
                }

                // Fall back to first export class if no actor found
                if (actorClass == null && asset.Exports.Count > 0)
                {
                    actorClass = asset.Exports[0].GetExportClassType()?.ToString() ?? "Unknown";
                    actorName = asset.Exports[0].ObjectName?.ToString();
                }

                if (actorClass != null)
                {
                    result.Actors.Add(new ActorPosition
                    {
                        Name = actorName ?? Path.GetFileNameWithoutExtension(file),
                        ClassName = actorClass,
                        X = location?.x ?? 0,
                        Y = location?.y ?? 0,
                        Z = location?.z ?? 0,
                        HasPosition = location != null,
                        Source = "ExternalActor",
                        FilePath = file
                    });
                }
            }
            catch
            {
                result.ParseErrors++;
            }
        }

        // Build class breakdown
        var groups = result.Actors.GroupBy(a => a.ClassName)
            .Select(g => new ClassBreakdown { ClassName = g.Key, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .ToList();
        result.ClassBreakdown = groups;
        result.ActorsWithPosition = result.Actors.Count(a => a.HasPosition);

        return result;
    }

    private static (double x, double y, double z)? ExtractVector(NormalExport export, string propertyName)
    {
        var prop = export.Data.FirstOrDefault(p => p.Name?.ToString() == propertyName);

        if (prop is StructPropertyData structProp)
        {
            double x = 0, y = 0, z = 0;
            bool found = false;

            foreach (var component in structProp.Value)
            {
                var name = component.Name?.ToString() ?? "";
                if (component is DoublePropertyData dp)
                {
                    found = true;
                    switch (name) { case "X": x = dp.Value; break; case "Y": y = dp.Value; break; case "Z": z = dp.Value; break; }
                }
                else if (component is FloatPropertyData fp)
                {
                    found = true;
                    switch (name) { case "X": x = fp.Value; break; case "Y": y = fp.Value; break; case "Z": z = fp.Value; break; }
                }
                else if (component is VectorPropertyData vp)
                {
                    return (vp.Value.X, vp.Value.Y, vp.Value.Z);
                }
            }

            if (found) return (x, y, z);
        }

        return null;
    }
}

public class SpatialData
{
    public int TotalFiles { get; set; }
    public int ParseErrors { get; set; }
    public int ActorsWithPosition { get; set; }
    public List<ActorPosition> Actors { get; set; } = new();
    public List<ClassBreakdown> ClassBreakdown { get; set; } = new();
}

public class ActorPosition
{
    public string Name { get; set; } = "";
    public string ClassName { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double RotationYaw { get; set; }
    public bool HasPosition { get; set; }
    public string Source { get; set; } = "";
    public string? FilePath { get; set; }
}

public class ClassBreakdown
{
    public string ClassName { get; set; } = "";
    public int Count { get; set; }
}
