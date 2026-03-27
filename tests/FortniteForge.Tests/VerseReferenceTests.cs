using WellVersed.Core.Config;
using WellVersed.Core.Models;
using WellVersed.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace WellVersed.Tests;

public class VerseReferenceServiceTests : IDisposable
{
    private readonly string _tempDir;

    public VerseReferenceServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"VerseRefTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private VerseReferenceService CreateService(string? path = null)
    {
        var config = new WellVersedConfig { ReferenceLibraryPath = path ?? _tempDir };
        return new VerseReferenceService(config, NullLogger<VerseReferenceService>.Instance);
    }

    private void WriteVerseFile(string name, string content)
    {
        File.WriteAllText(Path.Combine(_tempDir, name), content);
    }

    // --- Index tests ---

    [Fact]
    public void BuildIndex_EmptyDir_Succeeds()
    {
        var service = CreateService();
        service.BuildIndex();
        var stats = service.GetStats();
        Assert.Equal(0, stats.TotalFiles);
    }

    [Fact]
    public void BuildIndex_NullPath_Succeeds()
    {
        var config = new WellVersedConfig { ReferenceLibraryPath = null };
        var service = new VerseReferenceService(config, NullLogger<VerseReferenceService>.Instance);
        service.BuildIndex();
        var stats = service.GetStats();
        Assert.Equal(0, stats.TotalFiles);
    }

    [Fact]
    public void BuildIndex_FindsVerseFiles()
    {
        WriteVerseFile("test.verse", "using { /Verse.org/Simulation }\nMyClass := class(creative_device):\n    OnBegin<override>()<suspends>:void=\n        Print(\"hello\")\n");
        var service = CreateService();
        service.BuildIndex();
        var stats = service.GetStats();
        Assert.Equal(1, stats.TotalFiles);
    }

    [Fact]
    public void BuildIndex_FindsFilesInSubdirs()
    {
        var sub = Path.Combine(_tempDir, "sub", "nested");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "nested.verse"), "using { /Verse.org/Simulation }\n");
        var service = CreateService();
        service.BuildIndex();
        Assert.Equal(1, service.GetStats().TotalFiles);
    }

    // --- Parsing tests ---

    [Fact]
    public void Parse_ExtractsUsingStatements()
    {
        WriteVerseFile("test.verse",
            "using { /Fortnite.com/Devices }\nusing { /Verse.org/Simulation }\nusing { /UnrealEngine.com/Temporary/UI }\n");
        var service = CreateService();
        service.BuildIndex();
        var results = service.SearchKeyword("test");
        Assert.Single(results);
    }

    [Fact]
    public void Parse_ExtractsClassDefinitions()
    {
        WriteVerseFile("game_manager.verse",
            "game_manager := class(creative_device):\n    OnBegin<override>()<suspends>:void=\n        Print(\"hi\")\n");
        var service = CreateService();
        service.BuildIndex();
        var results = service.SearchKeyword("game_manager");
        Assert.NotEmpty(results);
        Assert.Contains("game_manager", results[0].ClassNames);
    }

    [Fact]
    public void Parse_ExtractsClassWithModifiers()
    {
        // Verse classes can have visibility modifiers before :=
        // e.g., player_data<public> := class<concrete><unique>():
        WriteVerseFile("data.verse",
            "player_data<public> := class<concrete><unique>():\n    var Score : int = 0\n");
        var service = CreateService();
        service.BuildIndex();
        var results = service.SearchKeyword("player_data");
        Assert.NotEmpty(results);
        Assert.Contains("player_data", results[0].ClassNames);
    }

    [Fact]
    public void Parse_ExtractsDeviceTypes()
    {
        WriteVerseFile("spawner.verse",
            "test := class(creative_device):\n    @editable Spawner : player_spawner_device = player_spawner_device{}\n    @editable Tracker : tracker_device = tracker_device{}\n");
        var service = CreateService();
        service.BuildIndex();
        var results = service.FindByDevice("tracker_device");
        Assert.Single(results);
        Assert.Contains("player_spawner_device", results[0].DeviceTypesUsed);
    }

    [Fact]
    public void Parse_ExtractsEditableProperties()
    {
        WriteVerseFile("props.verse",
            "test := class(creative_device):\n    @editable var Speed : float = 1.0\n    @editable MaxPlayers : int = 16\n");
        var service = CreateService();
        service.BuildIndex();
        var results = service.SearchKeyword("Speed");
        Assert.NotEmpty(results);
    }

    [Fact]
    public void Parse_ExtractsFunctions()
    {
        WriteVerseFile("funcs.verse",
            "test := class(creative_device):\n    OnBegin<override>()<suspends>:void=\n        Print(\"hi\")\n    HandleSpawn(Agent : agent):void=\n        Print(\"spawned\")\n");
        var service = CreateService();
        service.BuildIndex();
        var results = service.SearchKeyword("HandleSpawn");
        Assert.NotEmpty(results);
    }

    // --- Categorization tests ---

    [Fact]
    public void Categorize_UISystem_DetectsCanvas()
    {
        WriteVerseFile("ui.verse",
            "using { /UnrealEngine.com/Temporary/UI }\nui := class(creative_device):\n    var MyCanvas : canvas = canvas{}\n");
        var service = CreateService();
        service.BuildIndex();
        var results = service.FindByCategory(VerseCategory.UISystem);
        Assert.Single(results);
    }

    [Fact]
    public void Categorize_PlayerEconomy_DetectsTracker()
    {
        WriteVerseFile("economy.verse",
            "eco := class(creative_device):\n    @editable MoneyTracker : tracker_device = tracker_device{}\n    var money : float = 0.0\n");
        var service = CreateService();
        service.BuildIndex();
        var results = service.FindByCategory(VerseCategory.PlayerEconomy);
        Assert.Single(results);
    }

    [Fact]
    public void Categorize_GameManagement_DetectsManager()
    {
        WriteVerseFile("gm.verse",
            "game_manager := class(creative_device):\n    var round : int = 0\n");
        var service = CreateService();
        service.BuildIndex();
        var results = service.FindByCategory(VerseCategory.GameManagement);
        Assert.Single(results);
    }

    [Fact]
    public void Categorize_Utility_FallbackForUnknown()
    {
        WriteVerseFile("helper.verse",
            "helper := class(creative_device):\n    DoThing():void=\n        Print(\"done\")\n");
        var service = CreateService();
        service.BuildIndex();
        var results = service.FindByCategory(VerseCategory.Utility);
        Assert.Single(results);
    }

    // --- Search tests ---

    [Fact]
    public void SearchKeyword_RanksFileNameHighest()
    {
        WriteVerseFile("tracker_helper.verse", "tracker_helper := class(creative_device):\n    OnBegin<override>()<suspends>:void=\n        Print(\"tracker\")\n");
        WriteVerseFile("other.verse", "y := class(creative_device):\n    OnBegin<override>()<suspends>:void=\n        Print(\"tracker\")\n");
        var service = CreateService();
        service.BuildIndex();
        var results = service.SearchKeyword("tracker");
        Assert.True(results.Count >= 2);
        // File with "tracker" in filename + class name should rank above body-only match
        Assert.Equal("tracker_helper.verse", results[0].FileName);
    }

    [Fact]
    public void SearchKeyword_CaseInsensitive()
    {
        WriteVerseFile("test.verse", "MyClass := class(creative_device):\n    OnBegin<override>()<suspends>:void=\n        Print(\"hi\")\n");
        var service = CreateService();
        service.BuildIndex();
        var results = service.SearchKeyword("myclass");
        Assert.NotEmpty(results);
    }

    [Fact]
    public void SearchKeyword_RespectsMaxResults()
    {
        for (int i = 0; i < 10; i++)
            WriteVerseFile($"file_{i}.verse", $"cls_{i} := class(creative_device):\n    OnBegin<override>()<suspends>:void=\n        Print(\"common\")\n");
        var service = CreateService();
        service.BuildIndex();
        var results = service.SearchKeyword("common", maxResults: 3);
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void FindByDevice_NoMatches_ReturnsEmpty()
    {
        WriteVerseFile("test.verse", "x := class(creative_device):\n    OnBegin<override>()<suspends>:void=\n        Print(\"hi\")\n");
        var service = CreateService();
        service.BuildIndex();
        var results = service.FindByDevice("nonexistent_device");
        Assert.Empty(results);
    }

    [Fact]
    public void FindByCategory_NoMatches_ReturnsEmpty()
    {
        WriteVerseFile("test.verse", "x := class(creative_device):\n    OnBegin<override>()<suspends>:void=\n        Print(\"hi\")\n");
        var service = CreateService();
        service.BuildIndex();
        var results = service.FindByCategory(VerseCategory.TycoonBuilding);
        Assert.Empty(results);
    }

    // --- GetFileContent ---

    [Fact]
    public void GetFileContent_ExistingFile_ReturnsContent()
    {
        var content = "test := class(creative_device):\n    OnBegin<override>()<suspends>:void=\n        Print(\"hi\")\n";
        WriteVerseFile("readable.verse", content);
        var service = CreateService();
        var result = service.GetFileContent(Path.Combine(_tempDir, "readable.verse"));
        Assert.Equal(content, result);
    }

    [Fact]
    public void GetFileContent_MissingFile_ReturnsNull()
    {
        var service = CreateService();
        var result = service.GetFileContent(Path.Combine(_tempDir, "missing.verse"));
        Assert.Null(result);
    }

    // --- Stats ---

    [Fact]
    public void GetStats_CalculatesCorrectly()
    {
        WriteVerseFile("a.verse", "using { /Fortnite.com/Devices }\na := class(creative_device):\n    @editable T : tracker_device = tracker_device{}\n    var money : float = 0.0\n");
        WriteVerseFile("b.verse", "using { /Fortnite.com/Devices }\nb := class(creative_device):\n    @editable T : tracker_device = tracker_device{}\n    var canvas : canvas = canvas{}\n");
        var service = CreateService();
        service.BuildIndex();
        var stats = service.GetStats();
        Assert.Equal(2, stats.TotalFiles);
        Assert.True(stats.TotalLines > 0);
        Assert.True(stats.DeviceUsageCounts.ContainsKey("tracker_device"));
    }

    // --- Rebuild ---

    [Fact]
    public void Rebuild_RefreshesIndex()
    {
        var service = CreateService();
        service.BuildIndex();
        Assert.Equal(0, service.GetStats().TotalFiles);

        WriteVerseFile("new.verse", "x := class(creative_device):\n    OnBegin<override>()<suspends>:void=\n        Print(\"hi\")\n");
        service.Rebuild();
        Assert.Equal(1, service.GetStats().TotalFiles);
    }
}

/// <summary>
/// Integration tests that run against the real UEFN resources library at Z:\UEFN_Resources.
/// </summary>
public class VerseReferenceIntegrationTests
{
    private const string LibraryPath = @"Z:\UEFN_Resources";
    private static bool LibraryExists => Directory.Exists(LibraryPath);
    private readonly ITestOutputHelper _output;

    public VerseReferenceIntegrationTests(ITestOutputHelper output) => _output = output;

    private VerseReferenceService CreateRealService()
    {
        var config = new WellVersedConfig { ReferenceLibraryPath = LibraryPath };
        return new VerseReferenceService(config, NullLogger<VerseReferenceService>.Instance);
    }

    [SkippableFact]
    public void Index_RealLibrary_LoadsSuccessfully()
    {
        Skip.IfNot(LibraryExists, "UEFN resources not available");
        var service = CreateRealService();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        service.BuildIndex();
        sw.Stop();

        var stats = service.GetStats();
        _output.WriteLine($"Indexed {stats.TotalFiles} files ({stats.TotalLines} lines) in {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Size: {stats.TotalSizeBytes / 1024.0:F1} KB");

        Assert.True(stats.TotalFiles > 100, $"Expected 100+ files, got {stats.TotalFiles}");
        Assert.True(stats.TotalLines > 10000, $"Expected 10000+ lines, got {stats.TotalLines}");
    }

    [SkippableFact]
    public void Stats_RealLibrary_ShowsCategories()
    {
        Skip.IfNot(LibraryExists, "UEFN resources not available");
        var service = CreateRealService();
        var stats = service.GetStats();

        _output.WriteLine("=== Categories ===");
        foreach (var (cat, count) in stats.FilesByCategory)
            _output.WriteLine($"  {cat}: {count}");

        _output.WriteLine("\n=== Top Devices ===");
        foreach (var (device, count) in stats.DeviceUsageCounts.Take(15))
            _output.WriteLine($"  {device}: {count}");

        _output.WriteLine("\n=== Top Imports ===");
        foreach (var stmt in stats.TopUsingStatements)
            _output.WriteLine($"  {stmt}");

        Assert.NotEmpty(stats.FilesByCategory);
        Assert.NotEmpty(stats.DeviceUsageCounts);
    }

    [SkippableFact]
    public void Search_Tracker_FindsResults()
    {
        Skip.IfNot(LibraryExists, "UEFN resources not available");
        var service = CreateRealService();
        var results = service.SearchKeyword("tracker");

        _output.WriteLine($"Found {results.Count} results for 'tracker'");
        foreach (var r in results.Take(5))
            _output.WriteLine($"  [{r.Relevance}] {r.FileName} — {string.Join("; ", r.MatchReasons)}");

        Assert.NotEmpty(results);
    }

    [SkippableFact]
    public void FindByDevice_PlayerSpawner_FindsResults()
    {
        Skip.IfNot(LibraryExists, "UEFN resources not available");
        var service = CreateRealService();
        var results = service.FindByDevice("player_spawner_device");

        _output.WriteLine($"Found {results.Count} files using player_spawner_device");
        foreach (var r in results.Take(5))
            _output.WriteLine($"  {r.FileName} ({r.RelativePath})");

        Assert.NotEmpty(results);
    }

    [SkippableFact]
    public void FindByCategory_UISystem_FindsResults()
    {
        Skip.IfNot(LibraryExists, "UEFN resources not available");
        var service = CreateRealService();
        var results = service.FindByCategory(VerseCategory.UISystem);

        _output.WriteLine($"Found {results.Count} UI system files");
        foreach (var r in results.Take(5))
            _output.WriteLine($"  {r.FileName} — classes: {string.Join(", ", r.ClassNames)}");

        Assert.NotEmpty(results);
    }
}
