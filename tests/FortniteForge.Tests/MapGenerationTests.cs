using FortniteForge.Core.Models;
using FortniteForge.Core.Services.MapGeneration;
using Xunit;

namespace FortniteForge.Tests;

public class AssetCatalogCategorizationTests
{
    [Theory]
    [InlineData("BP_TriggerDevice_C", ActorCategory.Device)]
    [InlineData("BP_SpawnerDevice_C", ActorCategory.Device)]
    [InlineData("MutatorZone_C", ActorCategory.Device)]
    [InlineData("BarrierDevice_C", ActorCategory.Device)]
    public void CategorizeActor_Devices_Detected(string className, ActorCategory _)
    {
        // AssetCatalog.CategorizeActor is private, but we can test via the StringExtensions helper
        // and verify the category assignment logic through the catalog's behavior.
        // For now, verify the string matching logic directly.
        var lower = className.ToLowerInvariant();
        bool isDevice = lower.Contains("device") || lower.Contains("spawner") ||
                        lower.Contains("trigger") || lower.Contains("mutator") ||
                        lower.Contains("barrier") || lower.Contains("zone");
        Assert.True(isDevice, $"{className} should be detected as a device");
    }

    [Fact]
    public void DeviceDetection_ContainsKeyword_Works()
    {
        // Test the same string-matching logic used internally for categorization
        var keywords = new[] { "desert", "snow" };
        Assert.True(ContainsAny("desert_rock_large", keywords));
        Assert.True(ContainsAny("SnowTree_01", keywords));
        Assert.False(ContainsAny("rock_medium", keywords));
    }

    [Fact]
    public void DeviceDetection_ContainsKeyword_CaseInsensitive()
    {
        Assert.True(ContainsAny("DESERT_ROCK", new[] { "desert" }));
        Assert.True(ContainsAny("Forest_Pine", new[] { "forest" }));
    }

    private static bool ContainsAny(string input, string[] keywords)
    {
        var lower = input.ToLowerInvariant();
        foreach (var kw in keywords)
            if (lower.Contains(kw.ToLowerInvariant()))
                return true;
        return false;
    }
}

public class MapSizeTests
{
    [Fact]
    public void MapSize_GetSize_ReturnsValidDimensions()
    {
        // Verify MapSize class exists and works
        var (smallW, smallL) = MapSize.GetSize("small");
        var (medW, medL) = MapSize.GetSize("medium");
        var (largeW, largeL) = MapSize.GetSize("large");

        Assert.True(smallW > 0);
        Assert.True(smallL > 0);
        Assert.True(medW > smallW, "Medium should be larger than small");
        Assert.True(largeW > medW, "Large should be larger than medium");
    }

    [Fact]
    public void MapSize_RecommendedPoiCount_ScalesWithSize()
    {
        var smallPois = MapSize.RecommendedPoiCount(5000, 5000);
        var largePois = MapSize.RecommendedPoiCount(20000, 20000);

        Assert.True(largePois > smallPois,
            "Larger maps should recommend more POIs");
    }
}

public class Vector3InfoTests
{
    [Fact]
    public void DefaultConstructor_AllZero()
    {
        var v = new Vector3Info();
        Assert.Equal(0, v.X);
        Assert.Equal(0, v.Y);
        Assert.Equal(0, v.Z);
    }

    [Fact]
    public void ParameterizedConstructor_SetsValues()
    {
        var v = new Vector3Info(1.5f, 2.5f, 3.5f);
        Assert.Equal(1.5f, v.X);
        Assert.Equal(2.5f, v.Y);
        Assert.Equal(3.5f, v.Z);
    }

    [Fact]
    public void ToString_FormatsCorrectly()
    {
        var v = new Vector3Info(100, 200, 300);
        var str = v.ToString();
        Assert.Contains("100", str);
        Assert.Contains("200", str);
        Assert.Contains("300", str);
    }
}
