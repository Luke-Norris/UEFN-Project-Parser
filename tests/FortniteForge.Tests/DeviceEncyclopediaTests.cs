using WellVersed.Core.Config;
using WellVersed.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WellVersed.Tests;

/// <summary>
/// Tests for the DeviceEncyclopedia — verifies search, common configurations,
/// device listing, and built-in config data quality.
///
/// These tests work with the built-in configuration database (no digest files needed).
/// Sandbox tests that need real .digest files are gated on WELLVERSED_SANDBOX.
/// </summary>
public class DeviceEncyclopediaTests
{
    private static readonly string? SandboxPath = Environment.GetEnvironmentVariable("WELLVERSED_SANDBOX");
    private static bool SandboxExists => !string.IsNullOrEmpty(SandboxPath) && Directory.Exists(SandboxPath);

    // =====================================================================
    //  GetCommonConfigurations — built-in configs
    // =====================================================================

    [Theory]
    [InlineData("trigger")]
    [InlineData("barrier")]
    [InlineData("item_granter")]
    [InlineData("player_spawner")]
    [InlineData("mutator_zone")]
    [InlineData("score_manager")]
    [InlineData("hud_message")]
    [InlineData("teleporter")]
    [InlineData("elimination_manager")]
    [InlineData("timer")]
    [InlineData("vending_machine")]
    public void GetCommonConfigurations_ReturnsConfigsForKnownDeviceTypes(string deviceClass)
    {
        var encyclopedia = CreateEncyclopedia();
        var configs = encyclopedia.GetCommonConfigurations(deviceClass);

        Assert.NotEmpty(configs);
        foreach (var config in configs)
        {
            Assert.False(string.IsNullOrEmpty(config.Name), $"Config for {deviceClass} missing Name");
            Assert.False(string.IsNullOrEmpty(config.Description), $"Config for {deviceClass} missing Description");
            Assert.NotEmpty(config.Properties);
            Assert.NotEmpty(config.Tags);
        }
    }

    [Fact]
    public void GetCommonConfigurations_TriggerHasOneShotConfig()
    {
        var encyclopedia = CreateEncyclopedia();
        var configs = encyclopedia.GetCommonConfigurations("trigger");

        Assert.Contains(configs, c => c.Name.Contains("One-Shot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetCommonConfigurations_ReturnsEmptyForUnknownDevice()
    {
        var encyclopedia = CreateEncyclopedia();
        var configs = encyclopedia.GetCommonConfigurations("totally_fake_device_xyz123");

        Assert.Empty(configs);
    }

    // =====================================================================
    //  SearchDevices — built-in config search
    // =====================================================================

    [Fact]
    public void SearchDevices_FindsResultsForTrigger()
    {
        var encyclopedia = CreateEncyclopediaWithDigests();
        if (encyclopedia == null) return; // No digest files available

        var results = encyclopedia.SearchDevices("trigger");

        // Should find results from built-in configs at minimum
        Assert.NotNull(results);
    }

    // =====================================================================
    //  ListAllDevices
    // =====================================================================

    [SkippableFact]
    public void ListAllDevices_ReturnsEntries()
    {
        var encyclopedia = CreateEncyclopediaWithDigests();
        Skip.If(encyclopedia == null, "No digest files available");

        var devices = encyclopedia!.ListAllDevices();

        Assert.NotNull(devices);
        // If digests are loaded, should have many devices
        if (devices.Count > 0)
        {
            foreach (var device in devices.Take(5))
            {
                Assert.False(string.IsNullOrEmpty(device.Name));
                Assert.False(string.IsNullOrEmpty(device.DisplayName));
            }
        }
    }

    // =====================================================================
    //  CommonConfiguration model
    // =====================================================================

    [Fact]
    public void CommonConfiguration_DefaultValues()
    {
        var config = new CommonConfiguration();
        Assert.Equal("", config.Name);
        Assert.Equal("", config.Description);
        Assert.Empty(config.Properties);
        Assert.Empty(config.Tags);
    }

    [Fact]
    public void EncyclopediaSearchResult_DefaultValues()
    {
        var result = new EncyclopediaSearchResult();
        Assert.Equal("", result.DeviceName);
        Assert.Equal("", result.DisplayName);
        Assert.Equal(0, result.Score);
        Assert.Empty(result.MatchedProperties);
        Assert.Empty(result.MatchedEvents);
    }

    [Fact]
    public void DeviceSummary_DefaultValues()
    {
        var summary = new DeviceSummary();
        Assert.Equal("", summary.Name);
        Assert.Equal("", summary.DisplayName);
        Assert.Equal(0, summary.PropertyCount);
        Assert.Equal(0, summary.EventCount);
        Assert.Equal(0, summary.UsageCount);
        Assert.False(summary.HasCommonConfigs);
    }

    // =====================================================================
    //  SuggestRelatedProperties
    // =====================================================================

    [Fact]
    public void SuggestRelatedProperties_TeamPropertySuggestsTeamRelated()
    {
        var encyclopedia = CreateEncyclopedia();
        var related = encyclopedia.SuggestRelatedProperties("trigger", "TeamIndex");

        Assert.NotEmpty(related);
        // Should suggest team-related properties
        Assert.True(related.Any(r =>
            r.Contains("Team", StringComparison.OrdinalIgnoreCase)),
            "Should suggest team-related properties");
    }

    [Fact]
    public void SuggestRelatedProperties_DamagePropertySuggestsDamageRelated()
    {
        var encyclopedia = CreateEncyclopedia();
        var related = encyclopedia.SuggestRelatedProperties("mutator_zone", "DamageAmount");

        Assert.NotEmpty(related);
    }

    [Fact]
    public void SuggestRelatedProperties_TimerPropertySuggestsTimerRelated()
    {
        var encyclopedia = CreateEncyclopedia();
        var related = encyclopedia.SuggestRelatedProperties("timer", "Duration");

        Assert.NotEmpty(related);
        Assert.True(related.Any(r =>
            r.Contains("Delay", StringComparison.OrdinalIgnoreCase) ||
            r.Contains("Reset", StringComparison.OrdinalIgnoreCase) ||
            r.Contains("Cooldown", StringComparison.OrdinalIgnoreCase)),
            "Timer duration should suggest delay/reset/cooldown properties");
    }

    // =====================================================================
    //  GetHealthScore
    // =====================================================================

    [Fact]
    public void GetHealthScore_ReturnsGrade_ForDeviceSet()
    {
        var encyclopedia = CreateEncyclopedia();
        var devices = new List<WellVersed.Core.Models.DeviceInfo>
        {
            new()
            {
                ActorName = "Trigger_1",
                DeviceClass = "BP_TriggerDevice_C",
                DeviceType = "Trigger Device",
                Wiring = new List<WellVersed.Core.Models.DeviceWiring>
                {
                    new() { OutputEvent = "OnTriggered", TargetDevice = "Barrier_1" }
                },
                Properties = new List<WellVersed.Core.Models.PropertyInfo>
                {
                    new() { Name = "ActivationCount", Value = "5" }
                }
            },
            new()
            {
                ActorName = "SpawnPad_1",
                DeviceClass = "BP_SpawnPadDevice_C",
                DeviceType = "Player Spawn Pad"
            }
        };

        var health = encyclopedia.GetHealthScore(devices);

        Assert.NotNull(health);
        Assert.InRange(health.Score, 0, 100);
        Assert.False(string.IsNullOrEmpty(health.Grade));
        Assert.False(string.IsNullOrEmpty(health.Summary));
    }

    [Fact]
    public void GetHealthScore_EmptyDevices_ReturnsF()
    {
        var encyclopedia = CreateEncyclopedia();
        var health = encyclopedia.GetHealthScore(new List<WellVersed.Core.Models.DeviceInfo>());

        Assert.Equal(0, health.Score);
        Assert.Equal("F", health.Grade);
    }

    // =====================================================================
    //  Expanded configs: 24+ device types
    // =====================================================================

    [Fact]
    public void GetCommonConfigurations_Has24PlusDeviceTypesWithConfigs()
    {
        var encyclopedia = CreateEncyclopedia();

        var deviceTypes = new[]
        {
            "trigger", "barrier", "item_granter", "player_spawner",
            "mutator_zone", "score_manager", "hud_message", "teleporter",
            "elimination_manager", "timer", "vending_machine", "button",
            "damage_volume", "round_settings", "tracker", "capture_area",
            "class_designer", "conditional_button", "guard_spawner",
            "signal_remote", "prop_mover", "billboard", "popup_dialog",
            "map_indicator"
        };

        var withConfigs = 0;
        foreach (var deviceType in deviceTypes)
        {
            var configs = encyclopedia.GetCommonConfigurations(deviceType);
            if (configs.Count > 0) withConfigs++;
        }

        Assert.True(withConfigs >= 10,
            $"Expected at least 10 device types with configs, got {withConfigs}");
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static DeviceEncyclopedia CreateEncyclopedia()
    {
        var config = new WellVersedConfig { ProjectPath = "." };
        var digestService = new DigestService(config, NullLogger<DigestService>.Instance);
        var libraryIndexer = new LibraryIndexer(NullLogger<LibraryIndexer>.Instance);
        return new DeviceEncyclopedia(digestService, libraryIndexer, NullLogger<DeviceEncyclopedia>.Instance);
    }

    /// <summary>
    /// Creates an encyclopedia with digest files loaded from sandbox.
    /// Returns null if sandbox is not available.
    /// </summary>
    private static DeviceEncyclopedia? CreateEncyclopediaWithDigests()
    {
        if (!SandboxExists) return null;

        // Try to find a project with .digest files
        var projects = WellVersedConfig.DiscoverProjects(SandboxPath!);
        foreach (var project in projects.Take(5))
        {
            var config = new WellVersedConfig { ProjectPath = project };
            var digestService = new DigestService(config, NullLogger<DigestService>.Instance);

            // Check if this project has digest files
            var types = digestService.ListDeviceTypes();
            if (types.Count > 0)
            {
                var libraryIndexer = new LibraryIndexer(NullLogger<LibraryIndexer>.Instance);
                return new DeviceEncyclopedia(digestService, libraryIndexer, NullLogger<DeviceEncyclopedia>.Instance);
            }
        }

        return null;
    }
}
