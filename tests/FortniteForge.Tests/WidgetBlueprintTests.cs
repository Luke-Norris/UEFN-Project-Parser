using FortniteForge.Core.Config;
using FortniteForge.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using Xunit;

namespace FortniteForge.Tests;

/// <summary>
/// Tests for Widget Blueprint creation and modification.
/// Uses template files from the sandbox to create modified copies.
/// </summary>
public class WidgetBlueprintTests : IDisposable
{
    private readonly string _testDir;

    public WidgetBlueprintTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "FortniteForge_WidgetTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    private static readonly string SandboxPath = Environment.GetEnvironmentVariable("FORTNITEFORGE_SANDBOX") ?? "";
    private static bool HasSandbox => !string.IsNullOrEmpty(SandboxPath) && Directory.Exists(SandboxPath);

    /// <summary>
    /// Find a Widget Blueprint template in the sandbox.
    /// </summary>
    private string? FindWidgetTemplate()
    {
        if (!HasSandbox) return null;

        var projects = ForgeConfig.DiscoverProjects(SandboxPath);
        foreach (var proj in projects)
        {
            var config = new ForgeConfig { ProjectPath = proj };
            if (!Directory.Exists(config.ContentPath)) continue;

            foreach (var file in Directory.EnumerateFiles(config.ContentPath, "*.uasset", SearchOption.AllDirectories)
                .Where(f => !f.Contains("__External")))
            {
                try
                {
                    var asset = new UAsset(file, EngineVersion.VER_UE5_4);
                    var hasWidgetTree = asset.Exports.Any(e =>
                        e.GetExportClassType()?.ToString() == "WidgetTree");
                    if (hasWidgetTree) return file;
                }
                catch { }
            }
        }
        return null;
    }

    [SkippableFact]
    public void CanParseWidgetBlueprint()
    {
        Skip.IfNot(HasSandbox, "FORTNITEFORGE_SANDBOX not set");
        var template = FindWidgetTemplate();
        Skip.If(template == null, "No Widget Blueprint found in sandbox");

        var asset = new UAsset(template, EngineVersion.VER_UE5_4);

        // Should have WidgetBlueprint and WidgetTree exports
        Assert.Contains(asset.Exports, e => e.GetExportClassType()?.ToString() == "WidgetTree");

        // Should have some widget exports
        var widgetTypes = asset.Exports
            .Select(e => e.GetExportClassType()?.ToString() ?? "")
            .Where(c => c != "" && c != "MetaData" && c != "EdGraph")
            .Distinct()
            .ToList();

        Assert.True(widgetTypes.Count > 0, "Widget Blueprint should have widget exports");
    }

    [SkippableFact]
    public void CanCloneWidgetBlueprint()
    {
        Skip.IfNot(HasSandbox, "FORTNITEFORGE_SANDBOX not set");
        var template = FindWidgetTemplate();
        Skip.If(template == null, "No Widget Blueprint found in sandbox");

        var clonePath = Path.Combine(_testDir, "cloned_widget.uasset");
        File.Copy(template, clonePath);

        // Parse the clone
        var asset = new UAsset(clonePath, EngineVersion.VER_UE5_4);
        Assert.True(asset.Exports.Count > 0);

        // Write it back (round-trip test)
        asset.Write(clonePath);

        // Verify it's still valid
        var reloaded = new UAsset(clonePath, EngineVersion.VER_UE5_4);
        Assert.Equal(asset.Exports.Count, reloaded.Exports.Count);
    }

    [SkippableFact]
    public void CanModifyTextInWidgetBlueprint()
    {
        Skip.IfNot(HasSandbox, "FORTNITEFORGE_SANDBOX not set");
        var template = FindWidgetTemplate();
        Skip.If(template == null, "No Widget Blueprint found in sandbox");

        var modifiedPath = Path.Combine(_testDir, "modified_widget.uasset");
        File.Copy(template, modifiedPath);

        var asset = new UAsset(modifiedPath, EngineVersion.VER_UE5_4);

        // Find text properties and modify them
        int textModified = 0;
        foreach (var export in asset.Exports.OfType<NormalExport>())
        {
            foreach (var prop in export.Data)
            {
                if (prop is TextPropertyData textProp && prop.Name?.ToString() == "Text")
                {
                    // Modify the text
                    textProp.Value = FString.FromString("Modified by FortniteForge!");
                    textModified++;
                }
            }
        }

        // Write modified version
        asset.Write(modifiedPath);

        // Verify modification persisted
        var reloaded = new UAsset(modifiedPath, EngineVersion.VER_UE5_4);
        if (textModified > 0)
        {
            var hasModifiedText = reloaded.Exports.OfType<NormalExport>()
                .SelectMany(e => e.Data)
                .OfType<TextPropertyData>()
                .Any(t => t.Value?.ToString()?.Contains("FortniteForge") == true);
            Assert.True(hasModifiedText, "Text modification should persist after save");
        }
    }

    [SkippableFact]
    public void CanModifyWidgetLayoutProperties()
    {
        Skip.IfNot(HasSandbox, "FORTNITEFORGE_SANDBOX not set");
        var template = FindWidgetTemplate();
        Skip.If(template == null, "No Widget Blueprint found in sandbox");

        var modifiedPath = Path.Combine(_testDir, "layout_modified.uasset");
        File.Copy(template, modifiedPath);

        var asset = new UAsset(modifiedPath, EngineVersion.VER_UE5_4);

        // Find CanvasPanelSlot exports and modify layout
        foreach (var export in asset.Exports.OfType<NormalExport>())
        {
            var cls = export.GetExportClassType()?.ToString();
            if (cls != "CanvasPanelSlot") continue;

            var layoutProp = export.Data.FirstOrDefault(p => p.Name?.ToString() == "LayoutData");
            if (layoutProp is StructPropertyData layoutStruct)
            {
                // Modify the offsets within the LayoutData struct
                foreach (var sub in layoutStruct.Value)
                {
                    if (sub is StructPropertyData offsets && sub.Name?.ToString() == "Offsets")
                    {
                        foreach (var field in offsets.Value)
                        {
                            if (field is FloatPropertyData fp && field.Name?.ToString() == "Left")
                            {
                                fp.Value = 100.0f; // Move 100px from left
                            }
                        }
                    }
                }
            }
        }

        asset.Write(modifiedPath);

        // Verify it's still parseable
        var reloaded = new UAsset(modifiedPath, EngineVersion.VER_UE5_4);
        Assert.True(reloaded.Exports.Count > 0);
    }

    [SkippableFact]
    public void CanEnumerateAllWidgetTypes()
    {
        Skip.IfNot(HasSandbox, "FORTNITEFORGE_SANDBOX not set");
        var template = FindWidgetTemplate();
        Skip.If(template == null, "No Widget Blueprint found in sandbox");

        var asset = new UAsset(template, EngineVersion.VER_UE5_4);

        var types = new Dictionary<string, int>();
        foreach (var export in asset.Exports)
        {
            var cls = export.GetExportClassType()?.ToString() ?? "Unknown";
            types[cls] = types.GetValueOrDefault(cls) + 1;
        }

        // Should have at least the basic structure
        Assert.True(types.Count > 0);

        // Print what we found for debugging
        foreach (var (type, count) in types.OrderByDescending(kv => kv.Value))
        {
            // Just verify we can read them
            Assert.NotNull(type);
        }
    }
}
