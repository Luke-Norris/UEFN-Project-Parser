using System.Text.Json;
using WellVersed.Core.Models;
using WellVersed.Core.Services;
using Xunit;

namespace WellVersed.Tests;

/// <summary>
/// Tests for the SystemImporter — verifies JSON roundtrip serialization,
/// recipe format, and import preview logic.
///
/// These tests exercise the serialization/deserialization path without needing
/// real .uasset files (those operations require a full project context).
/// </summary>
public class SystemImporterTests
{
    // =====================================================================
    //  ExportSystemToJson — serialization
    // =====================================================================

    [Fact]
    public void ExportSystemToJson_ProducesValidJson()
    {
        var system = CreateTestSystem();

        var importer = CreateImporterForSerialization();
        var json = importer.ExportSystemToJson(system);

        Assert.False(string.IsNullOrEmpty(json));
        var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
    }

    [Fact]
    public void ExportSystemToJson_ContainsSystemName()
    {
        var system = CreateTestSystem();
        system.Name = "Test Capture System";

        var importer = CreateImporterForSerialization();
        var json = importer.ExportSystemToJson(system);

        Assert.Contains("Test Capture System", json);
    }

    [Fact]
    public void ExportSystemToJson_ContainsAllDevices()
    {
        var system = CreateTestSystem();

        var importer = CreateImporterForSerialization();
        var json = importer.ExportSystemToJson(system);

        Assert.Contains("TriggerDevice", json);
        Assert.Contains("TimerDevice", json);
    }

    [Fact]
    public void ExportSystemToJson_ContainsWiring()
    {
        var system = CreateTestSystem();

        var importer = CreateImporterForSerialization();
        var json = importer.ExportSystemToJson(system);

        Assert.Contains("OnTriggered", json);
        Assert.Contains("Start", json);
    }

    [Fact]
    public void ExportSystemToJson_ContainsFormatVersion()
    {
        var system = CreateTestSystem();

        var importer = CreateImporterForSerialization();
        var json = importer.ExportSystemToJson(system);

        Assert.Contains("formatVersion", json);
    }

    // =====================================================================
    //  JSON Roundtrip — ExportSystemToJson -> parse recipe -> verify
    // =====================================================================

    [Fact]
    public void JsonRoundtrip_PreservesDeviceCount()
    {
        var system = CreateTestSystem();

        var importer = CreateImporterForSerialization();
        var json = importer.ExportSystemToJson(system);

        var recipe = JsonSerializer.Deserialize<SystemRecipe>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.NotNull(recipe);
        Assert.Equal(system.Devices.Count, recipe!.Devices.Count);
        Assert.Equal(system.Wiring.Count, recipe.Wiring.Count);
    }

    [Fact]
    public void JsonRoundtrip_PreservesCategory()
    {
        var system = CreateTestSystem();
        system.Category = "capture";

        var importer = CreateImporterForSerialization();
        var json = importer.ExportSystemToJson(system);

        var recipe = JsonSerializer.Deserialize<SystemRecipe>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.Equal("capture", recipe!.Category);
    }

    [Fact]
    public void JsonRoundtrip_PreservesDeviceProperties()
    {
        var system = CreateTestSystem();
        system.Devices[0].Properties.Add(new ExtractedProperty
        {
            Name = "TriggerDelay",
            Type = "float",
            Value = "3.0"
        });

        var importer = CreateImporterForSerialization();
        var json = importer.ExportSystemToJson(system);

        var recipe = JsonSerializer.Deserialize<SystemRecipe>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.Contains(recipe!.Devices[0].Properties, p => p.Name == "TriggerDelay" && p.Value == "3.0");
    }

    [Fact]
    public void JsonRoundtrip_PreservesPositionOffsets()
    {
        var system = CreateTestSystem();
        system.Devices[0].Offset = new Vector3Info(100, 200, 300);

        var importer = CreateImporterForSerialization();
        var json = importer.ExportSystemToJson(system);

        var recipe = JsonSerializer.Deserialize<SystemRecipe>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.Equal(100, recipe!.Devices[0].Offset.X);
        Assert.Equal(200, recipe.Devices[0].Offset.Y);
        Assert.Equal(300, recipe.Devices[0].Offset.Z);
    }

    // =====================================================================
    //  ImportPreview models
    // =====================================================================

    [Fact]
    public void ImportPreview_DefaultValues()
    {
        var preview = new ImportPreview();
        Assert.Equal("", preview.SystemName);
        Assert.Equal("", preview.SourceLevel);
        Assert.Equal("", preview.TargetLevel);
        Assert.Equal(0, preview.DeviceCount);
        Assert.Equal(0, preview.WiringCount);
        Assert.Empty(preview.MissingTemplates);
        Assert.Empty(preview.Warnings);
    }

    [Fact]
    public void ImportResult_DefaultValues()
    {
        var result = new ImportResult();
        Assert.False(result.Success);
        Assert.Equal("", result.Message);
        Assert.Equal(0, result.DevicesPlaced);
        Assert.Equal(0, result.WiresCreated);
        Assert.Empty(result.Errors);
    }

    // =====================================================================
    //  SystemRecipe model
    // =====================================================================

    [Fact]
    public void SystemRecipe_DefaultFormatVersion()
    {
        var recipe = new SystemRecipe();
        Assert.Equal(1, recipe.FormatVersion);
    }

    [Fact]
    public void RecipeDevice_DefaultScale()
    {
        var device = new RecipeDevice();
        Assert.Equal(1f, device.Scale.X);
        Assert.Equal(1f, device.Scale.Y);
        Assert.Equal(1f, device.Scale.Z);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static ExtractedSystem CreateTestSystem()
    {
        return new ExtractedSystem
        {
            Name = "Trigger + Timer",
            Category = "gameplay",
            DetectionMethod = "wiring",
            Confidence = 0.9f,
            DeviceCount = 2,
            SourceLevel = "TestLevel.umap",
            Devices = new List<ExtractedDevice>
            {
                new()
                {
                    Role = "trigger",
                    DeviceClass = "BP_TriggerDevice_C",
                    DeviceType = "Trigger Device",
                    ActorName = "TriggerDevice_1",
                    Offset = new Vector3Info(0, 0, 0),
                    Rotation = new Vector3Info(0, 0, 0),
                    Scale = new Vector3Info(1, 1, 1)
                },
                new()
                {
                    Role = "timer",
                    DeviceClass = "BP_TimerDevice_C",
                    DeviceType = "Timer Device",
                    ActorName = "TimerDevice_1",
                    Offset = new Vector3Info(500, 0, 0),
                    Rotation = new Vector3Info(0, 0, 0),
                    Scale = new Vector3Info(1, 1, 1)
                }
            },
            Wiring = new List<ExtractedWiring>
            {
                new()
                {
                    SourceRole = "trigger",
                    SourceActor = "TriggerDevice_1",
                    OutputEvent = "OnTriggered",
                    TargetRole = "timer",
                    TargetActor = "TimerDevice_1",
                    InputAction = "Start"
                }
            }
        };
    }

    /// <summary>
    /// Creates a SystemImporter that can only do serialization (no actual imports).
    /// The DI dependencies are mocked minimally since we only test ExportSystemToJson.
    /// </summary>
    private static SystemImporter CreateImporterForSerialization()
    {
        var config = new WellVersed.Core.Config.WellVersedConfig { ProjectPath = "." };
        var detector = new UefnDetector(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<UefnDetector>.Instance);
        var fileAccess = new SafeFileAccess(config, detector, Microsoft.Extensions.Logging.Abstractions.NullLogger<SafeFileAccess>.Instance);
        var guard = new WellVersed.Core.Safety.AssetGuard(config, detector, Microsoft.Extensions.Logging.Abstractions.NullLogger<WellVersed.Core.Safety.AssetGuard>.Instance);
        var assetService = new AssetService(config, guard, fileAccess, Microsoft.Extensions.Logging.Abstractions.NullLogger<AssetService>.Instance);
        var digestService = new DigestService(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<DigestService>.Instance);
        var deviceService = new DeviceService(config, assetService, digestService, Microsoft.Extensions.Logging.Abstractions.NullLogger<DeviceService>.Instance);
        var backupService = new BackupService(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<BackupService>.Instance);
        var validator = new AssetValidator(Microsoft.Extensions.Logging.Abstractions.NullLogger<AssetValidator>.Instance);
        var placementService = new ActorPlacementService(config, guard, fileAccess, backupService, validator, Microsoft.Extensions.Logging.Abstractions.NullLogger<ActorPlacementService>.Instance);
        var modService = new ModificationService(config, assetService, backupService, guard, fileAccess, digestService, placementService, validator, Microsoft.Extensions.Logging.Abstractions.NullLogger<ModificationService>.Instance);
        var extractor = new SystemExtractor(config, deviceService, Microsoft.Extensions.Logging.Abstractions.NullLogger<SystemExtractor>.Instance);

        return new SystemImporter(
            config, placementService, modService, extractor, validator,
            deviceService, guard, fileAccess, backupService,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SystemImporter>.Instance);
    }
}
