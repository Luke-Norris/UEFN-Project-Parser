using FortniteForge.Core.Config;
using FortniteForge.Core.Models;
using FortniteForge.Core.Safety;
using FortniteForge.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using Xunit;
using Xunit.Abstractions;

namespace FortniteForge.Tests;

public class ForestPlacementTest
{
    private const string ProjectPath = @"C:\Users\Luke\Documents\Fortnite Projects\UEFN_AutoMation_Test";
    private const string LevelPath = ProjectPath + @"\Content\UEFN_Automation_Test.umap";
    private const string ExternalActorsDir = ProjectPath + @"\Content\__ExternalActors__\UEFN_Automation_Test";
    private static bool ProjectExists => Directory.Exists(ProjectPath) && File.Exists(LevelPath);

    private readonly ITestOutputHelper _output;
    public ForestPlacementTest(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public void FindTreeActorsInExternalActors()
    {
        Skip.IfNot(ProjectExists, "UEFN project not found");

        var extFiles = Directory.GetFiles(ExternalActorsDir, "*.uasset", SearchOption.AllDirectories);
        _output.WriteLine($"Scanning {extFiles.Length} external actor files...\n");

        var classCounts = new Dictionary<string, List<string>>();

        foreach (var file in extFiles)
        {
            try
            {
                var asset = new UAsset(file, EngineVersion.VER_UE5_4);
                foreach (var export in asset.Exports)
                {
                    var className = export.GetExportClassType()?.ToString() ?? "Unknown";
                    if (className is "Unknown" or "SceneComponent" or "StaticMeshComponent"
                        or "BoxComponent" or "LandscapeTextureHash" or "Texture2D"
                        or "MaterialInstanceEditorOnlyData" or "LandscapeMaterialInstanceConstant"
                        or "AssetImportData") continue;

                    if (!classCounts.ContainsKey(className))
                        classCounts[className] = new();
                    classCounts[className].Add($"{export.ObjectName} ({Path.GetFileName(file)})");
                }
            }
            catch { }
        }

        _output.WriteLine("=== Actor classes in external actors ===");
        foreach (var (cls, instances) in classCounts.OrderByDescending(kv => kv.Value.Count))
        {
            _output.WriteLine($"\n{cls} ({instances.Count} instances):");
            foreach (var inst in instances.Take(3))
                _output.WriteLine($"  {inst}");
            if (instances.Count > 3)
                _output.WriteLine($"  ... +{instances.Count - 3} more");
        }
    }

    [SkippableFact]
    public void InspectTreeAsset()
    {
        Skip.IfNot(ProjectExists, "UEFN project not found");

        // Find a tree file
        var extFiles = Directory.GetFiles(ExternalActorsDir, "*.uasset", SearchOption.AllDirectories);
        string? treeFile = null;

        foreach (var file in extFiles)
        {
            try
            {
                var asset = new UAsset(file, EngineVersion.VER_UE5_4);
                foreach (var export in asset.Exports)
                {
                    var className = export.GetExportClassType()?.ToString() ?? "";
                    if (className.Contains("Tree", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("Alder", StringComparison.OrdinalIgnoreCase))
                    {
                        treeFile = file;
                        break;
                    }
                }
                if (treeFile != null) break;
            }
            catch { }
        }

        if (treeFile == null)
        {
            _output.WriteLine("No tree found. Looking for any foliage-type actor...");
            foreach (var file in extFiles)
            {
                try
                {
                    var asset = new UAsset(file, EngineVersion.VER_UE5_4);
                    foreach (var export in asset.Exports)
                    {
                        var className = export.GetExportClassType()?.ToString() ?? "";
                        if (className.Contains("Foliage", StringComparison.OrdinalIgnoreCase) ||
                            className.Contains("Bush", StringComparison.OrdinalIgnoreCase) ||
                            className.Contains("Plant", StringComparison.OrdinalIgnoreCase) ||
                            className.Contains("Flora", StringComparison.OrdinalIgnoreCase))
                        {
                            treeFile = file;
                            _output.WriteLine($"Found foliage: {className} in {file}");
                            break;
                        }
                    }
                    if (treeFile != null) break;
                }
                catch { }
            }
        }

        Skip.If(treeFile == null, "No tree/foliage actor found in external actors");

        _output.WriteLine($"\nTree file: {treeFile}\n");
        var treeAsset = new UAsset(treeFile!, EngineVersion.VER_UE5_4);

        _output.WriteLine($"Exports: {treeAsset.Exports.Count}");
        _output.WriteLine($"Imports: {treeAsset.Imports.Count}");
        _output.WriteLine("");

        for (int i = 0; i < treeAsset.Exports.Count; i++)
        {
            var exp = treeAsset.Exports[i];
            _output.WriteLine($"[{i}] {exp.ObjectName} ({exp.GetExportClassType()}) outer={exp.OuterIndex}");
            if (exp is NormalExport ne)
            {
                foreach (var prop in ne.Data)
                {
                    string val;
                    try
                    {
                        val = prop switch
                        {
                            StructPropertyData sp => $"{{{string.Join(", ", sp.Value.Select(v => $"{v.Name}={FormatPropValue(v)}"))}}}",
                            ObjectPropertyData op => op.Value.ToString(),
                            BoolPropertyData bp => bp.Value.ToString(),
                            _ => prop.ToString() ?? "?"
                        };
                    }
                    catch { val = $"<error reading {prop.PropertyType}>"; }
                    _output.WriteLine($"     {prop.Name} ({prop.PropertyType}) = {val}");
                }
            }
        }
    }

    [SkippableFact]
    public void PlaceForest()
    {
        Skip.IfNot(ProjectExists, "UEFN project not found");

        // Find a tree external actor to use as template
        var extFiles = Directory.GetFiles(ExternalActorsDir, "*.uasset", SearchOption.AllDirectories);
        string? treeFile = null;
        string? treeClass = null;

        foreach (var file in extFiles)
        {
            try
            {
                var asset = new UAsset(file, EngineVersion.VER_UE5_4);
                foreach (var export in asset.Exports)
                {
                    var className = export.GetExportClassType()?.ToString() ?? "";
                    if (className.Contains("Tree", StringComparison.OrdinalIgnoreCase))
                    {
                        treeFile = file;
                        treeClass = className;
                        break;
                    }
                }
                if (treeFile != null) break;
            }
            catch { }
        }

        Skip.If(treeFile == null, "No tree actor found to clone");
        _output.WriteLine($"Template tree: {treeClass} from {Path.GetFileName(treeFile!)}");

        // Clone strategy for external actors:
        // Each external actor is its own .uasset file.
        // To create a forest, we copy the tree file and modify the location in each copy.
        var backupDir = Path.Combine(ProjectPath, ".fortniteforge", "forest_backups");
        Directory.CreateDirectory(backupDir);

        // Backup original tree
        File.Copy(treeFile!, Path.Combine(backupDir, "original_tree_" + Path.GetFileName(treeFile!)), overwrite: true);

        var random = new Random(42); // fixed seed for reproducibility
        var centerX = 0.0;
        var centerY = 0.0;
        var originalZ = 0.0;
        var areaSize = 5000.0;
        var treeCount = 30;
        var minSpacing = 400.0;

        // Read original tree to get its position (center of forest)
        var templateAsset = new UAsset(treeFile!, EngineVersion.VER_UE5_4);
        foreach (var export in templateAsset.Exports)
        {
            if (export is NormalExport ne)
            {
                var locProp = ne.Data.FirstOrDefault(p => p.Name?.ToString() == "RelativeLocation");
                if (locProp is StructPropertyData sp)
                {
                    var vecProp = sp.Value.OfType<VectorPropertyData>().FirstOrDefault();
                    if (vecProp != null)
                    {
                        centerX = vecProp.Value.X;
                        centerY = vecProp.Value.Y;
                        originalZ = vecProp.Value.Z;
                        break;
                    }
                }
            }
        }
        _output.WriteLine($"Original tree position: ({centerX:F1}, {centerY:F1}, {originalZ:F1})");
        _output.WriteLine($"Using that as forest center, area: {areaSize}x{areaSize}");

        // Generate random positions with minimum spacing
        var positions = new List<(double x, double y, double yaw, double scale)>();
        for (int i = 0; i < treeCount; i++)
        {
            int attempts = 0;
            double x, y;
            do
            {
                x = centerX + (random.NextDouble() * areaSize - areaSize / 2);
                y = centerY + (random.NextDouble() * areaSize - areaSize / 2);
                attempts++;
            }
            while (attempts < 50 && positions.Any(p =>
                Math.Sqrt((p.x - x) * (p.x - x) + (p.y - y) * (p.y - y)) < minSpacing));

            var yaw = random.NextDouble() * 360;
            var scale = 0.7 + random.NextDouble() * 0.6; // 0.7 to 1.3
            positions.Add((x, y, yaw, scale));
        }

        // Place each tree by cloning the file and modifying the transform
        var targetDir = Path.GetDirectoryName(treeFile!)!;
        int placed = 0;

        foreach (var (x, y, yaw, scale) in positions)
        {
            try
            {
                var newFileName = $"ForgeTree_{placed:D3}_{Guid.NewGuid():N}.uasset";
                var newFilePath = Path.Combine(targetDir, newFileName);

                // Copy the template file
                File.Copy(treeFile!, newFilePath);

                // Open and modify the transform
                var asset = new UAsset(newFilePath, EngineVersion.VER_UE5_4);

                foreach (var export in asset.Exports)
                {
                    if (export is NormalExport ne)
                    {
                        // Update location via VectorPropertyData inside StructPropertyData
                        var locProp = ne.Data.FirstOrDefault(p => p.Name?.ToString() == "RelativeLocation");
                        if (locProp is StructPropertyData sp)
                        {
                            var vecProp = sp.Value.OfType<VectorPropertyData>().FirstOrDefault();
                            if (vecProp != null)
                            {
                                vecProp.Value = new FVector(x, y, originalZ);
                            }
                        }

                        // Update rotation via RotatorPropertyData inside StructPropertyData
                        var rotProp = ne.Data.FirstOrDefault(p => p.Name?.ToString() == "RelativeRotation");
                        if (rotProp is StructPropertyData rotStruct)
                        {
                            var rotatorProp = rotStruct.Value.OfType<RotatorPropertyData>().FirstOrDefault();
                            if (rotatorProp != null)
                            {
                                rotatorProp.Value = new FRotator(0, yaw, 0);
                            }
                        }

                        // Update scale via VectorPropertyData inside StructPropertyData
                        var scaleProp = ne.Data.FirstOrDefault(p => p.Name?.ToString() == "RelativeScale3D");
                        if (scaleProp is StructPropertyData scaleStruct)
                        {
                            var scaleVec = scaleStruct.Value.OfType<VectorPropertyData>().FirstOrDefault();
                            if (scaleVec != null)
                            {
                                scaleVec.Value = new FVector(scale, scale, scale);
                            }
                        }
                    }
                }

                asset.Write(newFilePath);
                placed++;
                if (placed <= 3 || placed == treeCount)
                    _output.WriteLine($"  Tree {placed}: ({x:F0}, {y:F0}) yaw={yaw:F0} scale={scale:F2}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  Error placing tree {placed}: {ex.Message}");
            }
        }

        _output.WriteLine($"\nPlaced {placed}/{treeCount} trees as external actors!");
        _output.WriteLine($"Files created in: {targetDir}");

        Assert.True(placed > 0, "Should have placed at least some trees");
    }

    [SkippableFact]
    public void VerifyPlacedTreeTransform()
    {
        Skip.IfNot(ProjectExists, "UEFN project not found");

        var treeFiles = Directory.GetFiles(
            Path.Combine(ExternalActorsDir, "0", "J8"), "ForgeTree_000_*.uasset");
        Skip.If(treeFiles.Length == 0, "No placed trees found — run PlaceForest first");

        var asset = new UAsset(treeFiles[0], EngineVersion.VER_UE5_4);
        foreach (var export in asset.Exports)
        {
            if (export is NormalExport ne)
            {
                var locProp = ne.Data.FirstOrDefault(p => p.Name?.ToString() == "RelativeLocation");
                if (locProp is StructPropertyData sp)
                {
                    var vec = sp.Value.OfType<VectorPropertyData>().FirstOrDefault();
                    if (vec != null)
                    {
                        _output.WriteLine($"Location: ({vec.Value.X:F1}, {vec.Value.Y:F1}, {vec.Value.Z:F1})");
                        // Should be different from original (19850.4, 4217.6) but same Z (2331.4)
                        Assert.InRange(vec.Value.Z, 2331.0, 2332.0);
                    }
                }

                var rotProp = ne.Data.FirstOrDefault(p => p.Name?.ToString() == "RelativeRotation");
                if (rotProp is StructPropertyData rotStruct)
                {
                    var rot = rotStruct.Value.OfType<RotatorPropertyData>().FirstOrDefault();
                    if (rot != null)
                    {
                        _output.WriteLine($"Rotation: pitch={rot.Value.Pitch:F1} yaw={rot.Value.Yaw:F1} roll={rot.Value.Roll:F1}");
                        Assert.Equal(0, rot.Value.Pitch);
                        Assert.Equal(0, rot.Value.Roll);
                    }
                }

                var scaleProp = ne.Data.FirstOrDefault(p => p.Name?.ToString() == "RelativeScale3D");
                if (scaleProp is StructPropertyData scaleStruct)
                {
                    var scaleVec = scaleStruct.Value.OfType<VectorPropertyData>().FirstOrDefault();
                    if (scaleVec != null)
                    {
                        _output.WriteLine($"Scale: ({scaleVec.Value.X:F3}, {scaleVec.Value.Y:F3}, {scaleVec.Value.Z:F3})");
                        Assert.InRange(scaleVec.Value.X, 0.7, 1.3);
                        Assert.Equal(scaleVec.Value.X, scaleVec.Value.Y); // uniform
                        Assert.Equal(scaleVec.Value.X, scaleVec.Value.Z);
                    }
                }
            }
        }
    }

    private static string FormatPropValue(PropertyData prop)
    {
        return prop switch
        {
            DoublePropertyData dp => dp.Value.ToString("F2"),
            FloatPropertyData fp => fp.Value.ToString("F2"),
            IntPropertyData ip => ip.Value.ToString(),
            BoolPropertyData bp => bp.Value.ToString(),
            ObjectPropertyData op => op.Value.ToString() ?? "null",
            _ => prop.ToString() ?? "?"
        };
    }
}
