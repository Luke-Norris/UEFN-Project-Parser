using WellVersed.Core.Config;
using WellVersed.Core.Models;
using WellVersed.Core.Safety;
using WellVersed.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WellVersed.Tests;

/// <summary>
/// Tests for VerseIntelligence — semantic analysis, anti-pattern detection,
/// refactoring suggestions, and device-binding scaffolding for .verse files.
///
/// Each test writes temporary .verse content, analyzes it, then cleans up.
/// </summary>
public class VerseIntelligenceTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch { }
        }
    }

    private string WriteTempVerse(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.verse");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    // =====================================================================
    //  AnalyzeVerseFile — class extraction
    // =====================================================================

    [Fact]
    public void AnalyzeVerseFile_ExtractsClasses()
    {
        var code = @"
using { /Fortnite.com/Devices }
using { /Verse.org/Simulation }

my_game_device := class(creative_device):
    OnBegin<override>()<suspends>:void =
        Print(""Hello"")
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var analysis = vi.AnalyzeVerseFile(path);

        Assert.Contains("my_game_device", analysis.Classes);
        Assert.True(analysis.LineCount > 0);
    }

    [Fact]
    public void AnalyzeVerseFile_ExtractsMultipleClasses()
    {
        var code = @"
first_device := class(creative_device):
    OnBegin<override>()<suspends>:void =
        Print(""A"")

second_device := class(creative_device):
    OnBegin<override>()<suspends>:void =
        Print(""B"")
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var analysis = vi.AnalyzeVerseFile(path);

        Assert.Contains("first_device", analysis.Classes);
        Assert.Contains("second_device", analysis.Classes);
        Assert.Equal(2, analysis.Classes.Count);
    }

    // =====================================================================
    //  AnalyzeVerseFile — pattern detection
    // =====================================================================

    [Fact]
    public void AnalyzeVerseFile_DetectsTimerLoopPattern()
    {
        var code = @"
game_loop := class(creative_device):
    OnBegin<override>()<suspends>:void =
        loop:
            Sleep(1.0)
            Print(""tick"")
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var analysis = vi.AnalyzeVerseFile(path);

        Assert.Contains("timer_loop", analysis.DetectedPatterns);
    }

    [Fact]
    public void AnalyzeVerseFile_DetectsEliminationHandlerPattern()
    {
        var code = @"
elim_manager := class(creative_device):
    @editable EliminationManager : elimination_manager_device = elimination_manager_device{}
    OnBegin<override>()<suspends>:void =
        EliminationManager.EliminationEvent.Await()
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var analysis = vi.AnalyzeVerseFile(path);

        Assert.Contains("elimination_handler", analysis.DetectedPatterns);
    }

    [Fact]
    public void AnalyzeVerseFile_DetectsScoreTrackingPattern()
    {
        var code = @"
score_device := class(creative_device):
    @editable ScoreManager : score_manager_device = score_manager_device{}
    var PlayerScore : int = 0
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var analysis = vi.AnalyzeVerseFile(path);

        Assert.Contains("score_tracking", analysis.DetectedPatterns);
    }

    [Fact]
    public void AnalyzeVerseFile_DetectsUIUpdatePattern()
    {
        var code = @"
ui_device := class(creative_device):
    var MyCanvas : canvas = canvas{}
    UpdateUI():void =
        MyCanvas.SetVisibility(true)
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var analysis = vi.AnalyzeVerseFile(path);

        Assert.Contains("ui_update", analysis.DetectedPatterns);
    }

    [Fact]
    public void AnalyzeVerseFile_DetectsSpawnSystemPattern()
    {
        var code = @"
spawn_mgr := class(creative_device):
    @editable PlayerSpawner : player_spawner_device = player_spawner_device{}
    OnBegin<override>()<suspends>:void =
        PlayerSpawner.SpawnedEvent.Await()
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var analysis = vi.AnalyzeVerseFile(path);

        Assert.Contains("spawn_system", analysis.DetectedPatterns);
    }

    // =====================================================================
    //  AnalyzeVerseFile — editable properties
    // =====================================================================

    [Fact]
    public void AnalyzeVerseFile_ExtractsEditableProperties()
    {
        var code = @"
my_device := class(creative_device):
    @editable MyTimer : timer_device = timer_device{}
    @editable MyTrigger : trigger_device = trigger_device{}
    @editable MaxScore : int = 100
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var analysis = vi.AnalyzeVerseFile(path);

        Assert.True(analysis.EditableProperties.Count >= 2,
            $"Expected at least 2 editable props, got {analysis.EditableProperties.Count}");
    }

    // =====================================================================
    //  AnalyzeVerseFile — complexity score
    // =====================================================================

    [Fact]
    public void AnalyzeVerseFile_ComplexityScore_InRange()
    {
        var code = @"
simple_device := class(creative_device):
    OnBegin<override>()<suspends>:void =
        Print(""Hello"")
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var analysis = vi.AnalyzeVerseFile(path);

        Assert.InRange(analysis.ComplexityScore, 1, 10);
    }

    // =====================================================================
    //  DetectAntiPatterns — Sleep without <suspends>
    // =====================================================================

    [Fact]
    public void DetectAntiPatterns_FindsSleepWithoutSuspends()
    {
        var code = @"
my_device := class(creative_device):
    OnBegin<override>()<suspends>:void =
        DoWork()

    DoWork():void =
        Sleep(1.0)
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var issues = vi.DetectAntiPatterns(path);

        Assert.Contains(issues, i => i.Id == "sleep_without_suspends");
        Assert.Contains(issues, i => i.Severity == "error");
    }

    [Fact]
    public void DetectAntiPatterns_NoFalsePositive_SleepInSuspendsContext()
    {
        var code = @"
my_device := class(creative_device):
    OnBegin<override>()<suspends>:void =
        Sleep(1.0)
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var issues = vi.DetectAntiPatterns(path);

        // OnBegin has <override> which implies <suspends> — should not flag
        Assert.DoesNotContain(issues, i => i.Id == "sleep_without_suspends");
    }

    // =====================================================================
    //  DetectAntiPatterns — infinite loop without Sleep
    // =====================================================================

    [Fact]
    public void DetectAntiPatterns_FindsInfiniteLoopWithoutSleep()
    {
        var code = @"
my_device := class(creative_device):
    OnBegin<override>()<suspends>:void =
        loop:
            DoSomething()
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var issues = vi.DetectAntiPatterns(path);

        Assert.Contains(issues, i => i.Id == "infinite_loop_no_sleep");
        Assert.Contains(issues, i => i.Severity == "error");
    }

    [Fact]
    public void DetectAntiPatterns_NoFalsePositive_LoopWithSleep()
    {
        var code = @"
my_device := class(creative_device):
    OnBegin<override>()<suspends>:void =
        loop:
            Sleep(0.0)
            DoSomething()
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var issues = vi.DetectAntiPatterns(path);

        Assert.DoesNotContain(issues, i => i.Id == "infinite_loop_no_sleep");
    }

    [Fact]
    public void DetectAntiPatterns_NoFalsePositive_LoopWithBreak()
    {
        var code = @"
my_device := class(creative_device):
    OnBegin<override>()<suspends>:void =
        loop:
            if (Done?):
                break
            DoSomething()
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var issues = vi.DetectAntiPatterns(path);

        Assert.DoesNotContain(issues, i => i.Id == "infinite_loop_no_sleep");
    }

    [Fact]
    public void DetectAntiPatterns_NoFalsePositive_LoopWithAwait()
    {
        var code = @"
my_device := class(creative_device):
    OnBegin<override>()<suspends>:void =
        loop:
            Await(SomeEvent.Fired())
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var issues = vi.DetectAntiPatterns(path);

        Assert.DoesNotContain(issues, i => i.Id == "infinite_loop_no_sleep");
    }

    // =====================================================================
    //  DetectAntiPatterns — unused variables
    // =====================================================================

    [Fact]
    public void DetectAntiPatterns_FindsUnusedVariables()
    {
        var code = @"
my_device := class(creative_device):
    var UnusedCounter : int = 0
    OnBegin<override>()<suspends>:void =
        Print(""Hello"")
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var issues = vi.DetectAntiPatterns(path);

        Assert.Contains(issues, i => i.Id == "unused_variable" && i.Title.Contains("UnusedCounter"));
    }

    [Fact]
    public void DetectAntiPatterns_NoFalsePositive_UsedVariable()
    {
        var code = @"
my_device := class(creative_device):
    var Counter : int = 0
    OnBegin<override>()<suspends>:void =
        set Counter = 1
        Print(""{Counter}"")
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var issues = vi.DetectAntiPatterns(path);

        Assert.DoesNotContain(issues, i => i.Id == "unused_variable" && i.Title.Contains("Counter"));
    }

    // =====================================================================
    //  DetectAntiPatterns — file not found
    // =====================================================================

    [Fact]
    public void DetectAntiPatterns_ThrowsForMissingFile()
    {
        var vi = CreateVerseIntelligence();
        Assert.Throws<FileNotFoundException>(() => vi.DetectAntiPatterns("/nonexistent/file.verse"));
    }

    // =====================================================================
    //  SuggestRefactoring — hardcoded values -> @editable
    // =====================================================================

    [Fact]
    public void SuggestRefactoring_SuggestsEditableForHardcodedValues()
    {
        var code = @"
my_device := class(creative_device):
    OnBegin<override>()<suspends>:void =
        Sleep(15.0)
        SetHealth(200)
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var suggestions = vi.SuggestRefactoring(path);

        Assert.Contains(suggestions, s => s.Category == "add_editable");
    }

    [Fact]
    public void SuggestRefactoring_ThrowsForMissingFile()
    {
        var vi = CreateVerseIntelligence();
        Assert.Throws<FileNotFoundException>(() => vi.SuggestRefactoring("/nonexistent/file.verse"));
    }

    // =====================================================================
    //  AnalyzeVerseFile — file not found
    // =====================================================================

    [Fact]
    public void AnalyzeVerseFile_ThrowsForMissingFile()
    {
        var vi = CreateVerseIntelligence();
        Assert.Throws<FileNotFoundException>(() => vi.AnalyzeVerseFile("/nonexistent/file.verse"));
    }

    // =====================================================================
    //  Model defaults
    // =====================================================================

    [Fact]
    public void VerseAnalysis_DefaultValues()
    {
        var analysis = new VerseAnalysis();
        Assert.Equal("", analysis.FilePath);
        Assert.Equal(0, analysis.LineCount);
        Assert.Empty(analysis.Classes);
        Assert.Empty(analysis.Functions);
        Assert.Empty(analysis.EditableProperties);
        Assert.Empty(analysis.EventSubscriptions);
        Assert.Empty(analysis.Variables);
        Assert.Empty(analysis.DetectedPatterns);
        Assert.Empty(analysis.AntiPatterns);
        Assert.Equal(0, analysis.ComplexityScore);
    }

    [Fact]
    public void VerseAntiPattern_DefaultValues()
    {
        var pattern = new VerseAntiPattern();
        Assert.Equal("", pattern.Id);
        Assert.Equal(0, pattern.Line);
        Assert.Equal("warning", pattern.Severity);
        Assert.Equal("", pattern.Title);
        Assert.Equal("", pattern.Description);
        Assert.Equal("", pattern.Fix);
        Assert.Null(pattern.CodeBefore);
        Assert.Null(pattern.CodeAfter);
    }

    [Fact]
    public void VerseDeviceReference_DefaultValues()
    {
        var reference = new VerseDeviceReference();
        Assert.Equal("", reference.VariableName);
        Assert.Equal("", reference.VerseType);
        Assert.Equal(0, reference.Line);
        Assert.False(reference.FoundInLevel);
        Assert.Null(reference.MatchedActorName);
        Assert.Null(reference.MatchedDeviceClass);
    }

    [Fact]
    public void RefactoringSuggestion_DefaultValues()
    {
        var suggestion = new RefactoringSuggestion();
        Assert.Equal("", suggestion.Category);
        Assert.Equal("", suggestion.Description);
        Assert.Equal(0, suggestion.LineStart);
        Assert.Equal(0, suggestion.LineEnd);
        Assert.Equal("", suggestion.CodeBefore);
        Assert.Equal("", suggestion.CodeAfter);
        Assert.Equal("medium", suggestion.Impact);
    }

    // =====================================================================
    //  AnalyzeVerseFile — event subscriptions
    // =====================================================================

    [Fact]
    public void AnalyzeVerseFile_ExtractsEventSubscriptions()
    {
        var code = @"
my_device := class(creative_device):
    @editable MyTrigger : trigger_device = trigger_device{}
    OnBegin<override>()<suspends>:void =
        MyTrigger.SubscribeTriggeredEvent(OnTriggered)
";
        var path = WriteTempVerse(code);
        var vi = CreateVerseIntelligence();

        var analysis = vi.AnalyzeVerseFile(path);

        Assert.True(analysis.EventSubscriptions.Count >= 1,
            "Should detect event subscription");
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static VerseIntelligence CreateVerseIntelligence()
    {
        var config = new WellVersedConfig { ProjectPath = "." };
        var detector = new UefnDetector(config, NullLogger<UefnDetector>.Instance);
        var fileAccess = new SafeFileAccess(config, detector, NullLogger<SafeFileAccess>.Instance);
        var guard = new AssetGuard(config, detector, NullLogger<AssetGuard>.Instance);
        var assetService = new AssetService(config, guard, fileAccess, NullLogger<AssetService>.Instance);
        var digestService = new DigestService(config, NullLogger<DigestService>.Instance);
        var deviceService = new DeviceService(config, assetService, digestService, NullLogger<DeviceService>.Instance);
        return new VerseIntelligence(config, deviceService, NullLogger<VerseIntelligence>.Instance);
    }
}
