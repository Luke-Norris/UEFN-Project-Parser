using WellVersed.Core.Config;
using WellVersed.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WellVersed.Tests;

public class DigestServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WellVersedConfig _config;
    private readonly DigestService _service;

    public DigestServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ForgeDigestTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var contentDir = Path.Combine(_tempDir, "Content");
        Directory.CreateDirectory(contentDir);

        _config = new WellVersedConfig { ProjectPath = _tempDir };
        _service = new DigestService(_config, NullLogger<DigestService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void LoadDigests_NoFiles_Succeeds()
    {
        _service.LoadDigests();

        var types = _service.ListDeviceTypes();
        Assert.Empty(types);
    }

    [Fact]
    public void LoadDigests_ParsesClassDefinitions()
    {
        var digestContent = @"
trigger_device := class(creative_device):
    Enabled : logic = true
    TriggerDelay : float = 0.0
    MaxTriggerCount : int = 0
    OnTriggered : event() = external{}
";
        CreateDigestFile("fortnite.digest", digestContent);

        _service.LoadDigests();

        var schema = _service.GetDeviceSchema("trigger_device");
        Assert.NotNull(schema);
        Assert.Equal("trigger_device", schema.Name);
        Assert.Equal("creative_device", schema.ParentClass);
    }

    [Fact]
    public void LoadDigests_ParsesProperties()
    {
        var digestContent = @"
my_device := class(creative_device):
    Health : float = 100.0
    DisplayName : string = ""Default""
    IsActive : logic = true
";
        CreateDigestFile("fortnite.digest", digestContent);

        _service.LoadDigests();

        var schema = _service.GetDeviceSchema("my_device");
        Assert.NotNull(schema);
        Assert.Equal(3, schema.Properties.Count);
        Assert.Contains(schema.Properties, p => p.Name == "Health" && p.Type == "float");
        Assert.Contains(schema.Properties, p => p.Name == "DisplayName" && p.Type == "string");
        Assert.Contains(schema.Properties, p => p.Name == "IsActive" && p.Type == "logic");
    }

    [Fact]
    public void LoadDigests_ParsesEvents()
    {
        var digestContent = @"
event_device := class(creative_device):
    OnActivated : event() = external{}
    OnDeactivated : event() = external{}
    Timeout : float = 5.0
";
        CreateDigestFile("fortnite.digest", digestContent);

        _service.LoadDigests();

        var schema = _service.GetDeviceSchema("event_device");
        Assert.NotNull(schema);
        Assert.Contains(schema.Events, e => e == "OnActivated");
        Assert.Contains(schema.Events, e => e == "OnDeactivated");
        Assert.Single(schema.Properties); // Only Timeout
    }

    [Fact]
    public void ListDeviceTypes_ReturnsSorted()
    {
        var digestContent = @"
zebra_device := class(creative_device):
    Prop1 : int = 0

alpha_device := class(creative_device):
    Prop1 : int = 0
";
        CreateDigestFile("fortnite.digest", digestContent);

        _service.LoadDigests();

        var types = _service.ListDeviceTypes();
        Assert.Equal(2, types.Count);
        Assert.Equal("alpha_device", types[0]);
        Assert.Equal("zebra_device", types[1]);
    }

    [Fact]
    public void ValidateProperty_ValidProperty_ReturnsValid()
    {
        var digestContent = @"
test_device := class(creative_device):
    Health : float = 100.0
";
        CreateDigestFile("fortnite.digest", digestContent);
        _service.LoadDigests();

        var result = _service.ValidateProperty("test_device", "Health");

        Assert.True(result.IsValid);
        Assert.Equal("float", result.PropertyType);
    }

    [Fact]
    public void ValidateProperty_InvalidProperty_ReturnsInvalid()
    {
        var digestContent = @"
test_device := class(creative_device):
    Health : float = 100.0
";
        CreateDigestFile("fortnite.digest", digestContent);
        _service.LoadDigests();

        var result = _service.ValidateProperty("test_device", "NonExistent");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateProperty_UnknownDevice_ReturnsInvalid()
    {
        _service.LoadDigests();

        var result = _service.ValidateProperty("unknown_device", "SomeProp");

        Assert.False(result.IsValid);
        Assert.Contains("Unknown device type", result.Reason);
    }

    [Fact]
    public void SearchSchemas_FindsByName()
    {
        var digestContent = @"
trigger_device := class(creative_device):
    Enabled : logic = true

spawner_device := class(creative_device):
    Enabled : logic = true
";
        CreateDigestFile("fortnite.digest", digestContent);
        _service.LoadDigests();

        var results = _service.SearchSchemas("trigger");

        Assert.Single(results);
        Assert.Equal("trigger_device", results[0].Name);
    }

    [Fact]
    public void SearchSchemas_FindsByPropertyName()
    {
        var digestContent = @"
my_device := class(creative_device):
    UniqueProperty : int = 0
";
        CreateDigestFile("fortnite.digest", digestContent);
        _service.LoadDigests();

        var results = _service.SearchSchemas("UniqueProperty");

        Assert.Single(results);
        Assert.Equal("my_device", results[0].Name);
    }

    private void CreateDigestFile(string fileName, string content)
    {
        var path = Path.Combine(_config.ContentPath, fileName);
        File.WriteAllText(path, content);
    }
}
