using FortniteForge.Core.Services.MapGeneration;
using Xunit;

namespace FortniteForge.Tests;

public class BiomeConfigTests
{
    [Theory]
    [InlineData("desert")]
    [InlineData("Desert")]
    [InlineData("DESERT")]
    public void GetBiome_BuiltInBiomes_CaseInsensitive(string name)
    {
        var biome = BiomeConfig.GetBiome(name);

        Assert.Equal("Desert", biome.Name);
        Assert.NotEmpty(biome.PreferredTags);
    }

    [Theory]
    [InlineData("forest", "Forest")]
    [InlineData("urban", "Urban")]
    [InlineData("snow", "Snow")]
    public void GetBiome_AllBuiltInBiomes_Exist(string input, string expectedName)
    {
        var biome = BiomeConfig.GetBiome(input);
        Assert.Equal(expectedName, biome.Name);
    }

    [Fact]
    public void GetBiome_UnknownBiome_ReturnsCustom()
    {
        var biome = BiomeConfig.GetBiome("alien_planet");

        Assert.Equal("alien_planet", biome.Name);
        Assert.Contains("Custom biome", biome.Description);
        Assert.Empty(biome.PreferredTags);
    }

    [Fact]
    public void DesertBiome_HasLowFoliageDensity()
    {
        var desert = BiomeConfig.Desert;

        Assert.True(desert.FoliageDensity < desert.PropDensity,
            "Desert should have less foliage than props");
        Assert.True(desert.FoliageDensity <= 0.15f);
    }

    [Fact]
    public void ForestBiome_HasHighFoliageDensity()
    {
        var forest = BiomeConfig.Forest;

        Assert.True(forest.FoliageDensity > forest.PropDensity,
            "Forest should have more foliage than props");
    }

    [Fact]
    public void UrbanBiome_HasMostBuildingsPerPoi()
    {
        var urban = BiomeConfig.Urban;
        var desert = BiomeConfig.Desert;
        var forest = BiomeConfig.Forest;

        Assert.True(urban.BuildingsPerPoi > desert.BuildingsPerPoi);
        Assert.True(urban.BuildingsPerPoi > forest.BuildingsPerPoi);
    }

    [Fact]
    public void AllBuiltInBiomes_HavePoiTemplates()
    {
        foreach (var biome in BiomeConfig.BuiltInBiomes.Values)
        {
            Assert.NotEmpty(biome.PoiTemplates);
        }
    }

    [Fact]
    public void AllBuiltInBiomes_HaveDevicePlacementRules()
    {
        foreach (var biome in BiomeConfig.BuiltInBiomes.Values)
        {
            Assert.NotNull(biome.Devices);
            Assert.True(biome.Devices.SpawnPointsPerPoi > 0);
            Assert.True(biome.Devices.ChestsPerPoi > 0);
        }
    }
}

public class PoiTemplateTests
{
    [Fact]
    public void AllTemplates_HaveNonZeroRadius()
    {
        var templates = new[]
        {
            PoiTemplate.SmallOutpost,
            PoiTemplate.AbandonedTown,
            PoiTemplate.Compound,
            PoiTemplate.Campsite,
            PoiTemplate.ForestClearing,
            PoiTemplate.CityBlock
        };

        foreach (var template in templates)
        {
            Assert.True(template.Radius > 0, $"{template.Name} should have non-zero radius");
            Assert.True(template.BuildingCount > 0, $"{template.Name} should have buildings");
            Assert.NotEmpty(template.Name);
        }
    }

    [Fact]
    public void CityBlock_HasMostBuildings()
    {
        var city = PoiTemplate.CityBlock;
        var camp = PoiTemplate.Campsite;

        Assert.True(city.BuildingCount > camp.BuildingCount);
    }

    [Fact]
    public void SmallOutpost_IsSmallerThanAbandonedTown()
    {
        Assert.True(PoiTemplate.SmallOutpost.Radius < PoiTemplate.AbandonedTown.Radius);
    }
}
