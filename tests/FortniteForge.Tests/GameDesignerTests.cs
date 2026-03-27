using WellVersed.Core.Models;
using WellVersed.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WellVersed.Tests;

/// <summary>
/// Tests for the GameDesigner — verifies natural language design parsing,
/// template loading, game mode classification, and generation plan creation.
/// </summary>
public class GameDesignerTests
{
    private readonly GameDesigner _designer = new(NullLogger<GameDesigner>.Instance);

    // =====================================================================
    //  DesignGame — natural language parsing
    // =====================================================================

    [Fact]
    public void DesignGame_TeamDeathmatch_ProducesTeamBasedMode()
    {
        var design = _designer.DesignGame("team deathmatch for 8 players");

        Assert.Equal(GameModeType.TeamBased, design.GameMode);
        Assert.Equal(8, design.PlayerCount);
        Assert.True(design.TeamCount >= 2, "Team deathmatch should have at least 2 teams");
        Assert.False(string.IsNullOrEmpty(design.Name));
    }

    [Fact]
    public void DesignGame_CaptureTheFlag_ProducesObjectives()
    {
        var design = _designer.DesignGame("capture the flag with 2 teams");

        Assert.Equal(GameModeType.TeamBased, design.GameMode);
        Assert.NotNull(design.Objectives);
        Assert.True(design.Objectives.Count > 0, "CTF should have objectives");
        Assert.True(design.TeamCount >= 2);
    }

    [Fact]
    public void DesignGame_TycoonWithUpgrades_ProducesEconomy()
    {
        var design = _designer.DesignGame("tycoon with upgrades and passive income");

        Assert.Equal(GameModeType.Tycoon, design.GameMode);
        Assert.NotNull(design.Economy);
        Assert.True(design.Economy!.HasCurrency, "Tycoon should have currency");
    }

    [Fact]
    public void DesignGame_FreeForAll_ProducesFFA()
    {
        var design = _designer.DesignGame("free for all elimination with 16 players");

        Assert.Equal(GameModeType.FreeForAll, design.GameMode);
        Assert.Equal(16, design.PlayerCount);
    }

    [Fact]
    public void DesignGame_ExtractsPlayerCount()
    {
        var design = _designer.DesignGame("deathmatch for 32 players");
        Assert.Equal(32, design.PlayerCount);
    }

    [Fact]
    public void DesignGame_OverridesPlayerCountParameter()
    {
        var design = _designer.DesignGame("deathmatch game", playerCount: 24);
        Assert.Equal(24, design.PlayerCount);
    }

    [Fact]
    public void DesignGame_ZoneWars_ProducesRoundsMode()
    {
        var design = _designer.DesignGame("zone wars with shrinking storm");

        // Zone wars is typically round-based or FFA
        Assert.True(
            design.GameMode == GameModeType.Rounds || design.GameMode == GameModeType.FreeForAll,
            $"Zone wars should be Rounds or FFA, got {design.GameMode}");
    }

    [Fact]
    public void DesignGame_AlwaysPopulatesName()
    {
        var design = _designer.DesignGame("some random game mode nobody ever heard of");
        Assert.False(string.IsNullOrEmpty(design.Name));
    }

    [Fact]
    public void DesignGame_AlwaysHasSpawnAreas()
    {
        var design = _designer.DesignGame("team deathmatch");
        Assert.NotNull(design.SpawnAreas);
        Assert.True(design.SpawnAreas.Count >= 1, "Should have at least one spawn area");
    }

    [Fact]
    public void DesignGame_TeamGame_HasMultipleSpawnAreas()
    {
        var design = _designer.DesignGame("team deathmatch for 8 players with 2 teams");

        Assert.True(
            design.SpawnAreas.Count >= 2,
            $"Team game should have spawn areas per team, got {design.SpawnAreas.Count}");
    }

    // =====================================================================
    //  Templates
    // =====================================================================

    [Fact]
    public void GetTemplates_ReturnsAllSixTemplates()
    {
        var templates = GameDesigner.GetTemplates();
        Assert.Equal(6, templates.Count);
    }

    [Fact]
    public void GetTemplates_AllHaveRequiredFields()
    {
        var templates = GameDesigner.GetTemplates();
        foreach (var template in templates)
        {
            Assert.False(string.IsNullOrEmpty(template.Id), $"Template missing Id");
            Assert.False(string.IsNullOrEmpty(template.Name), $"Template {template.Id} missing Name");
            Assert.False(string.IsNullOrEmpty(template.Description), $"Template {template.Id} missing Description");
            Assert.False(string.IsNullOrEmpty(template.Category), $"Template {template.Id} missing Category");
            Assert.NotEmpty(template.Tags);
            Assert.NotNull(template.Design);
        }
    }

    [Fact]
    public void GetTemplates_AllHaveUniqueIds()
    {
        var templates = GameDesigner.GetTemplates();
        var ids = templates.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void GetTemplates_ContainsExpectedTemplates()
    {
        var templates = GameDesigner.GetTemplates();
        var ids = templates.Select(t => t.Id).ToHashSet();

        Assert.Contains("team_deathmatch", ids);
        Assert.Contains("capture_the_flag", ids);
        Assert.Contains("free_for_all", ids);
        Assert.Contains("tycoon", ids);
        Assert.Contains("zone_wars", ids);
        Assert.Contains("gun_game", ids);
    }

    [Fact]
    public void GetTemplates_AllDesignsHaveValidGameMode()
    {
        var templates = GameDesigner.GetTemplates();
        var validModes = Enum.GetValues<GameModeType>().ToHashSet();

        foreach (var template in templates)
        {
            Assert.Contains(template.Design.GameMode, validModes);
        }
    }

    // =====================================================================
    //  DesignToDevicePlan
    // =====================================================================

    [Fact]
    public void DesignToDevicePlan_ProducesDevicesAndWiring()
    {
        var design = _designer.DesignGame("team deathmatch for 8 players");
        var plan = _designer.DesignToDevicePlan(design);

        Assert.NotNull(plan);
        Assert.True(plan.Devices.Count > 0, "Plan should have devices");
        Assert.True(plan.Wiring.Count > 0, "Plan should have wiring connections");
    }

    [Fact]
    public void DesignToDevicePlan_IncludesSpawnDevices()
    {
        var design = _designer.DesignGame("team deathmatch for 8 players");
        var plan = _designer.DesignToDevicePlan(design);

        var hasSpawnDevice = plan.Devices.Any(d =>
            d.DeviceClass.Contains("Spawn", StringComparison.OrdinalIgnoreCase) ||
            d.DeviceClass.Contains("spawner", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasSpawnDevice, "Plan should include spawn devices");
    }

    [Fact]
    public void DesignToDevicePlan_TycoonIncludesEconomyDevices()
    {
        var design = _designer.DesignGame("tycoon with upgrades");
        var plan = _designer.DesignToDevicePlan(design);

        Assert.True(plan.Devices.Count > 0, "Tycoon plan should have devices");
        // Tycoon should have some kind of granter or vending device
        var hasEconomyDevice = plan.Devices.Any(d =>
            d.DeviceClass.Contains("Granter", StringComparison.OrdinalIgnoreCase) ||
            d.DeviceClass.Contains("Vending", StringComparison.OrdinalIgnoreCase) ||
            d.DeviceClass.Contains("Timer", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasEconomyDevice, "Tycoon plan should include economy-related devices");
    }

    [Fact]
    public void DesignToDevicePlan_PlanHasSystemName()
    {
        var design = _designer.DesignGame("capture the flag");
        var plan = _designer.DesignToDevicePlan(design);

        Assert.False(string.IsNullOrEmpty(plan.SystemName));
    }

    // =====================================================================
    //  DesignToDevicePlan — spatial offsets (not all at origin)
    // =====================================================================

    [Fact]
    public void DesignToDevicePlan_DevicesHaveMeaningfulSpatialOffsets()
    {
        var design = _designer.DesignGame("team deathmatch for 8 players");
        var plan = _designer.DesignToDevicePlan(design);

        // At least some devices should have non-zero offsets
        var hasNonZero = plan.Devices.Any(d =>
            d.Offset.X != 0 || d.Offset.Y != 0 || d.Offset.Z != 0);

        Assert.True(hasNonZero,
            "Devices should have spatial offsets — not all at origin");
    }

    [Fact]
    public void DesignToDevicePlan_NotAllDevicesAtSameLocation()
    {
        var design = _designer.DesignGame("team deathmatch for 8 players with 2 teams");
        var plan = _designer.DesignToDevicePlan(design);

        if (plan.Devices.Count < 2) return; // Need at least 2 to compare

        // Check that not all offsets are identical
        var firstOffset = plan.Devices[0].Offset;
        var allSame = plan.Devices.All(d =>
            d.Offset.X == firstOffset.X && d.Offset.Y == firstOffset.Y && d.Offset.Z == firstOffset.Z);

        Assert.False(allSame,
            "All devices should not be at the exact same location");
    }

    // =====================================================================
    //  DesignToDevicePlan — plans include both devices AND wiring
    // =====================================================================

    [Fact]
    public void DesignToDevicePlan_AllModes_IncludeBothDevicesAndWiring()
    {
        var modes = new[]
        {
            "team deathmatch for 8 players",
            "free for all elimination",
            "capture the flag",
            "tycoon with upgrades",
            "zone wars"
        };

        foreach (var mode in modes)
        {
            var design = _designer.DesignGame(mode);
            var plan = _designer.DesignToDevicePlan(design);

            Assert.True(plan.Devices.Count > 0, $"Mode '{mode}' should have devices");
            Assert.True(plan.Wiring.Count > 0, $"Mode '{mode}' should have wiring");
        }
    }

    // =====================================================================
    //  Economy / Round-based plans
    // =====================================================================

    [Fact]
    public void DesignToDevicePlan_TycoonIncludesVendorDevices()
    {
        var design = _designer.DesignGame("tycoon with shops and upgrades");
        var plan = _designer.DesignToDevicePlan(design);

        var hasVendor = plan.Devices.Any(d =>
            d.DeviceClass.Contains("Vending", StringComparison.OrdinalIgnoreCase) ||
            d.DeviceClass.Contains("Granter", StringComparison.OrdinalIgnoreCase) ||
            d.Role.Contains("vendor", StringComparison.OrdinalIgnoreCase) ||
            d.Role.Contains("shop", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasVendor, "Tycoon plan should include vendor/granter devices");
    }

    [Fact]
    public void DesignToDevicePlan_RoundBased_IncludesTimerAndRoundSettings()
    {
        var design = _designer.DesignGame("zone wars with rounds and shrinking storm");
        var plan = _designer.DesignToDevicePlan(design);

        var hasTimer = plan.Devices.Any(d =>
            d.DeviceClass.Contains("Timer", StringComparison.OrdinalIgnoreCase) ||
            d.DeviceClass.Contains("Round", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasTimer, "Round-based plan should include timer or round settings devices");
    }
}
