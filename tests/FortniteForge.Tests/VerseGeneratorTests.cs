using WellVersed.Core.Config;
using WellVersed.Core.Models;
using WellVersed.Core.Services.VerseGeneration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WellVersed.Tests;

public class VerseCodeBuilderTests
{
    [Fact]
    public void Line_AddsTextWithNewline()
    {
        var b = new VerseCodeBuilder();
        b.Line("hello");
        Assert.Contains("hello", b.ToString());
    }

    [Fact]
    public void Indent_IncreasesIndentation()
    {
        var b = new VerseCodeBuilder();
        b.Indent().Line("indented");
        var output = b.ToString();
        Assert.StartsWith("    indented", output);
    }

    [Fact]
    public void Dedent_DecreasesIndentation()
    {
        var b = new VerseCodeBuilder();
        b.Indent().Indent().Dedent().Line("one level");
        Assert.StartsWith("    one level", b.ToString());
    }

    [Fact]
    public void Dedent_NeverGoesNegative()
    {
        var b = new VerseCodeBuilder();
        b.Dedent().Dedent().Line("no indent");
        Assert.StartsWith("no indent", b.ToString());
    }

    [Fact]
    public void Comment_AddsHashPrefix()
    {
        var b = new VerseCodeBuilder();
        b.Comment("my comment");
        Assert.Contains("# my comment", b.ToString());
    }

    [Fact]
    public void Using_WrapsInBraces()
    {
        var b = new VerseCodeBuilder();
        b.Using("/Fortnite.com/Devices");
        Assert.Contains("using { /Fortnite.com/Devices }", b.ToString());
    }

    [Fact]
    public void StandardUsings_IncludesRequiredModules()
    {
        var b = new VerseCodeBuilder();
        b.StandardUsings();
        var output = b.ToString();
        Assert.Contains("/Fortnite.com/Devices", output);
        Assert.Contains("/Verse.org/Simulation", output);
        Assert.Contains("/UnrealEngine.com/Temporary/SpatialMath", output);
    }

    [Fact]
    public void UIUsings_IncludesUIModules()
    {
        var b = new VerseCodeBuilder();
        b.UIUsings();
        var output = b.ToString();
        Assert.Contains("/Fortnite.com/UI", output);
        Assert.Contains("/UnrealEngine.com/Temporary/UI", output);
    }

    [Fact]
    public void ClassDef_CreatesProperSyntax()
    {
        var b = new VerseCodeBuilder();
        b.ClassDef("my_device");
        b.Line("prop : int = 0");
        var output = b.ToString();
        Assert.Contains("my_device := class(creative_device):", output);
        Assert.Contains("    prop : int = 0", output);
    }

    [Fact]
    public void Editable_AddsAnnotation()
    {
        var b = new VerseCodeBuilder();
        b.Editable("MyButton", "button_device", "button_device{}");
        var output = b.ToString();
        Assert.Contains("@editable", output);
        Assert.Contains("MyButton : button_device = button_device{}", output);
    }

    [Fact]
    public void OnBegin_CreatesOverrideSyntax()
    {
        var b = new VerseCodeBuilder();
        b.OnBegin();
        b.Line("Print(\"hello\")");
        var output = b.ToString();
        Assert.Contains("OnBegin<override>()<suspends>:void=", output);
        Assert.Contains("    Print(\"hello\")", output);
    }

    [Fact]
    public void Var_CreatesMutableVar()
    {
        var b = new VerseCodeBuilder();
        b.Var("Score", "int", "0");
        Assert.Contains("var Score : int = 0", b.ToString());
    }
}

public class VerseUIGeneratorTests
{
    private readonly VerseUIGenerator _generator;
    private readonly string _tempDir;

    public VerseUIGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ForgeVerseTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var config = new WellVersedConfig { ProjectPath = _tempDir };
        _generator = new VerseUIGenerator(config, NullLogger<VerseUIGenerator>.Instance);
    }

    [Fact]
    public void GetUITemplates_ReturnsAllTemplates()
    {
        var templates = VerseUIGenerator.GetUITemplates();

        Assert.True(templates.Count >= 7, "Should have at least 7 UI templates");
        Assert.Contains(templates, t => t.Type == VerseTemplateType.HudOverlay);
        Assert.Contains(templates, t => t.Type == VerseTemplateType.Scoreboard);
        Assert.Contains(templates, t => t.Type == VerseTemplateType.InteractionMenu);
        Assert.Contains(templates, t => t.Type == VerseTemplateType.NotificationPopup);
        Assert.Contains(templates, t => t.Type == VerseTemplateType.ItemTracker);
        Assert.Contains(templates, t => t.Type == VerseTemplateType.ProgressBar);
        Assert.Contains(templates, t => t.Type == VerseTemplateType.CustomWidget);
    }

    [Theory]
    [InlineData(VerseTemplateType.HudOverlay)]
    [InlineData(VerseTemplateType.Scoreboard)]
    [InlineData(VerseTemplateType.InteractionMenu)]
    [InlineData(VerseTemplateType.NotificationPopup)]
    [InlineData(VerseTemplateType.ItemTracker)]
    [InlineData(VerseTemplateType.ProgressBar)]
    [InlineData(VerseTemplateType.CustomWidget)]
    public void Generate_AllUITemplates_ProduceValidVerse(VerseTemplateType templateType)
    {
        var request = new VerseFileRequest
        {
            TemplateType = templateType,
            ClassName = $"test_{templateType.ToString().ToLower()}_device",
            OutputDirectory = _tempDir
        };

        var result = _generator.Generate(request);

        Assert.True(result.Success, $"Generation failed: {result.Error}");
        Assert.NotEmpty(result.SourceCode);

        // All Verse files should have these basics
        Assert.Contains("using {", result.SourceCode);
        Assert.Contains(":= class(creative_device):", result.SourceCode);
        Assert.Contains("OnBegin<override>()<suspends>:void=", result.SourceCode);
    }

    [Fact]
    public void Generate_HUD_IncludesRequestedElements()
    {
        var request = new VerseFileRequest
        {
            TemplateType = VerseTemplateType.HudOverlay,
            ClassName = "test_hud",
            OutputDirectory = _tempDir,
            Parameters = new Dictionary<string, string>
            {
                ["show_health"] = "true",
                ["show_shield"] = "true",
                ["show_score"] = "true",
                ["show_timer"] = "true",
                ["timer_seconds"] = "120"
            }
        };

        var result = _generator.Generate(request);
        var code = result.SourceCode;

        Assert.Contains("HealthText", code);
        Assert.Contains("ShieldText", code);
        Assert.Contains("ScoreText", code);
        Assert.Contains("TimerText", code);
        Assert.Contains("120.0", code);
    }

    [Fact]
    public void Generate_HUD_ExcludesDisabledElements()
    {
        var request = new VerseFileRequest
        {
            TemplateType = VerseTemplateType.HudOverlay,
            ClassName = "minimal_hud",
            OutputDirectory = _tempDir,
            Parameters = new Dictionary<string, string>
            {
                ["show_health"] = "false",
                ["show_shield"] = "false",
                ["show_score"] = "false",
                ["show_timer"] = "true"
            }
        };

        var result = _generator.Generate(request);
        var code = result.SourceCode;

        Assert.DoesNotContain("HealthText", code);
        Assert.DoesNotContain("ShieldText", code);
        Assert.DoesNotContain("ScoreText", code);
        Assert.Contains("TimerText", code);
    }

    [Fact]
    public void Generate_WritesFileToDisk()
    {
        var request = new VerseFileRequest
        {
            TemplateType = VerseTemplateType.CustomWidget,
            ClassName = "disk_test_widget",
            FileName = "disk_test_widget",
            OutputDirectory = _tempDir
        };

        var result = _generator.Generate(request);

        Assert.True(result.Success);
        Assert.True(File.Exists(result.FilePath), $"File should exist at {result.FilePath}");
        Assert.Contains(".verse", result.FilePath);

        var fileContent = File.ReadAllText(result.FilePath);
        Assert.Equal(result.SourceCode, fileContent);
    }

    [Fact]
    public void Generate_ItemTracker_UsesCustomParameters()
    {
        var request = new VerseFileRequest
        {
            TemplateType = VerseTemplateType.ItemTracker,
            ClassName = "gem_tracker",
            OutputDirectory = _tempDir,
            Parameters = new Dictionary<string, string>
            {
                ["total_items"] = "50",
                ["item_name"] = "Gems"
            }
        };

        var result = _generator.Generate(request);
        var code = result.SourceCode;

        Assert.Contains("50", code);
        Assert.Contains("Gems", code);
    }

    [Fact]
    public void Generate_NonUITemplate_Throws()
    {
        var request = new VerseFileRequest
        {
            TemplateType = VerseTemplateType.GameManager,
            ClassName = "test",
            OutputDirectory = _tempDir
        };

        var result = _generator.Generate(request);

        Assert.False(result.Success);
        Assert.Contains("not a UI template", result.Error);
    }
}

public class VerseDeviceGeneratorTests
{
    private readonly VerseDeviceGenerator _generator;
    private readonly string _tempDir;

    public VerseDeviceGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ForgeVerseDevTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var config = new WellVersedConfig { ProjectPath = _tempDir };
        _generator = new VerseDeviceGenerator(config, NullLogger<VerseDeviceGenerator>.Instance);
    }

    [Fact]
    public void GetDeviceTemplates_ReturnsAllTemplates()
    {
        var templates = VerseDeviceGenerator.GetDeviceTemplates();

        Assert.True(templates.Count >= 7, "Should have at least 7 device templates");
        Assert.Contains(templates, t => t.Type == VerseTemplateType.GameManager);
        Assert.Contains(templates, t => t.Type == VerseTemplateType.TimerController);
        Assert.Contains(templates, t => t.Type == VerseTemplateType.TeamManager);
        Assert.Contains(templates, t => t.Type == VerseTemplateType.EliminationTracker);
        Assert.Contains(templates, t => t.Type == VerseTemplateType.CollectibleSystem);
        Assert.Contains(templates, t => t.Type == VerseTemplateType.ZoneController);
        Assert.Contains(templates, t => t.Type == VerseTemplateType.EmptyDevice);
    }

    [Theory]
    [InlineData(VerseTemplateType.GameManager)]
    [InlineData(VerseTemplateType.TimerController)]
    [InlineData(VerseTemplateType.TeamManager)]
    [InlineData(VerseTemplateType.EliminationTracker)]
    [InlineData(VerseTemplateType.CollectibleSystem)]
    [InlineData(VerseTemplateType.ZoneController)]
    [InlineData(VerseTemplateType.MovementMutator)]
    [InlineData(VerseTemplateType.EmptyDevice)]
    public void Generate_AllDeviceTemplates_ProduceValidVerse(VerseTemplateType templateType)
    {
        var request = new VerseFileRequest
        {
            TemplateType = templateType,
            ClassName = $"test_{templateType.ToString().ToLower()}",
            OutputDirectory = _tempDir
        };

        var result = _generator.Generate(request);

        Assert.True(result.Success, $"Generation failed: {result.Error}");
        Assert.NotEmpty(result.SourceCode);
        Assert.Contains("using {", result.SourceCode);
        Assert.Contains(":= class(creative_device):", result.SourceCode);
    }

    [Fact]
    public void Generate_GameManager_HasRoundLogic()
    {
        var request = new VerseFileRequest
        {
            TemplateType = VerseTemplateType.GameManager,
            ClassName = "my_game_mgr",
            OutputDirectory = _tempDir,
            Parameters = new Dictionary<string, string>
            {
                ["round_count"] = "5",
                ["round_duration"] = "120",
                ["win_score"] = "50"
            }
        };

        var result = _generator.Generate(request);
        var code = result.SourceCode;

        Assert.Contains("5", code);   // round count
        Assert.Contains("120.0", code); // round duration
        Assert.Contains("50", code);   // win score
        Assert.Contains("GameLoop", code);
        Assert.Contains("game_phase", code);
    }

    [Fact]
    public void Generate_EmptyDevice_IsMinimal()
    {
        var request = new VerseFileRequest
        {
            TemplateType = VerseTemplateType.EmptyDevice,
            ClassName = "my_blank_device",
            OutputDirectory = _tempDir
        };

        var result = _generator.Generate(request);
        var code = result.SourceCode;
        var lineCount = code.Split('\n').Length;

        Assert.True(result.Success);
        Assert.True(lineCount < 25, $"Empty device should be concise, got {lineCount} lines");
        Assert.Contains("OnBegin", code);
    }

    [Fact]
    public void Generate_NonDeviceTemplate_Throws()
    {
        var request = new VerseFileRequest
        {
            TemplateType = VerseTemplateType.HudOverlay,
            ClassName = "test",
            OutputDirectory = _tempDir
        };

        var result = _generator.Generate(request);

        Assert.False(result.Success);
        Assert.Contains("not a device template", result.Error);
    }

    [Fact]
    public void Generate_EliminationTracker_HasStreakLogic()
    {
        var request = new VerseFileRequest
        {
            TemplateType = VerseTemplateType.EliminationTracker,
            ClassName = "elim_tracker",
            OutputDirectory = _tempDir,
            Parameters = new Dictionary<string, string>
            {
                ["points_per_elim"] = "25",
                ["streak_bonus"] = "10",
                ["streak_threshold"] = "5"
            }
        };

        var result = _generator.Generate(request);
        var code = result.SourceCode;

        Assert.Contains("25", code);
        Assert.Contains("10", code);
        Assert.Contains("5", code);
        Assert.Contains("KillStreaks", code);
        Assert.Contains("OnElimination", code);
    }
}

public class LevelAnalyticsTests
{
    [Fact]
    public void PerformanceEstimate_DefaultsExist()
    {
        var perf = new PerformanceEstimate();

        Assert.NotNull(perf.Concerns);
        Assert.NotNull(perf.Optimizations);
        Assert.Equal(0, perf.ComplexityScore);
    }

    [Fact]
    public void LevelAnalytics_DefaultsExist()
    {
        var analytics = new LevelAnalytics();

        Assert.NotNull(analytics.ActorsByClass);
        Assert.NotNull(analytics.ActorsByCategory);
        Assert.NotNull(analytics.Duplicates);
        Assert.NotNull(analytics.WorldBounds);
        Assert.NotNull(analytics.Performance);
        Assert.NotNull(analytics.Wiring);
    }

    [Fact]
    public void BoundsInfo_DefaultsToZero()
    {
        var bounds = new BoundsInfo();

        Assert.Equal(0, bounds.Min.X);
        Assert.Equal(0, bounds.Max.X);
        Assert.Equal(0, bounds.Size.X);
        Assert.Equal(0, bounds.Center.X);
    }
}
