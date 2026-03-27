using WellVersed.Core.Services;
using Xunit;

namespace WellVersed.Tests;

/// <summary>
/// Tests for the VerseErrorTranslator — verifies pattern matching, category classification,
/// compiler output parsing, and explanation quality for Verse compiler errors.
/// </summary>
public class VerseErrorTranslatorTests
{
    private readonly VerseErrorTranslator _translator = new();

    // =====================================================================
    //  TranslateOutput — multi-error compiler output parsing
    // =====================================================================

    [Fact]
    public void TranslateOutput_ParsesMultipleErrors()
    {
        var rawOutput = @"
game.verse(12,5): error: Expected expression of type `void` but found `int`

game.verse(25,10): error: This call may fail but is not in a failable context

game.verse(30): error: Cannot modify immutable variable
";

        var result = _translator.TranslateOutput(rawOutput);

        Assert.True(result.TotalErrors >= 3, $"Expected >= 3 errors, got {result.TotalErrors}");
        Assert.True(result.Recognized >= 3, $"Expected >= 3 recognized, got {result.Recognized}");
        Assert.Equal(0, result.Unrecognized);
    }

    [Fact]
    public void TranslateOutput_ExtractsFileAndLineInfo()
    {
        var rawOutput = "player_manager.verse(42,10): error: Expected expression of type `void` but found `int`";

        var result = _translator.TranslateOutput(rawOutput);

        Assert.Single(result.Errors);
        var error = result.Errors[0];
        Assert.Equal("player_manager.verse", error.FileName);
        Assert.Equal(42, error.Line);
        Assert.Equal(10, error.Column);
    }

    [Fact]
    public void TranslateOutput_HandlesEmptyInput()
    {
        var result = _translator.TranslateOutput("");
        Assert.Equal(0, result.TotalErrors);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void TranslateOutput_CountsRecognizedAndUnrecognized()
    {
        var rawOutput = @"
game.verse(1): error: Expected expression of type `void` but found `int`
game.verse(2): error: Some completely novel error nobody has seen before xyz789
";
        var result = _translator.TranslateOutput(rawOutput);

        Assert.True(result.TotalErrors >= 2);
        Assert.True(result.Recognized >= 1);
        // The novel error should be unrecognized
        Assert.True(result.Unrecognized >= 1);
    }

    // =====================================================================
    //  ExplainSingle — individual error explanation
    // =====================================================================

    [Fact]
    public void ExplainSingle_ReturnsExplanationAndFix()
    {
        var error = _translator.ExplainSingle("Expected expression of type `void` but found `int`");

        Assert.NotNull(error.Translation);
        Assert.False(string.IsNullOrEmpty(error.Translation!.HumanExplanation));
        Assert.False(string.IsNullOrEmpty(error.Translation.Fix));
        Assert.False(string.IsNullOrEmpty(error.Translation.CodeExample));
    }

    [Fact]
    public void ExplainSingle_ReturnsNullTranslationForUnknownError()
    {
        var error = _translator.ExplainSingle("Completely unknown error message xyz123abc");

        Assert.Null(error.Translation);
    }

    // =====================================================================
    //  TYPE SYSTEM category matching
    // =====================================================================

    [Theory]
    [InlineData("Expected expression of type `void` but found `int`")]
    [InlineData("Expected type int but found string")]
    public void TypeMismatch_MatchesTypeSystemCategory(string errorMsg)
    {
        var error = _translator.ExplainSingle(errorMsg);
        Assert.NotNull(error.Translation);
        Assert.Equal(VerseErrorCategory.TypeSystem, error.Translation!.Category);
    }

    [Theory]
    [InlineData("Cannot convert from int to string")]
    [InlineData("Cannot convert float to int")]
    public void TypeConversion_MatchesTypeSystemCategory(string errorMsg)
    {
        var error = _translator.ExplainSingle(errorMsg);
        Assert.NotNull(error.Translation);
        Assert.Equal(VerseErrorCategory.TypeSystem, error.Translation!.Category);
    }

    [Theory]
    [InlineData("Type MyWidget is not a subtype of creative_device")]
    public void TypeSubtype_MatchesTypeSystemCategory(string errorMsg)
    {
        var error = _translator.ExplainSingle(errorMsg);
        Assert.NotNull(error.Translation);
        Assert.Equal(VerseErrorCategory.TypeSystem, error.Translation!.Category);
    }

    // =====================================================================
    //  FAILABLE category matching
    // =====================================================================

    [Theory]
    [InlineData("This call may fail but is not in a failable context")]
    [InlineData("failable expression is not in a failable context")]
    public void Failable_MatchesFailableCategory(string errorMsg)
    {
        var error = _translator.ExplainSingle(errorMsg);
        Assert.NotNull(error.Translation);
        Assert.Equal(VerseErrorCategory.Failable, error.Translation!.Category);
    }

    [Theory]
    [InlineData("<decides> effect required")]
    [InlineData("requires <decides> effect")]
    public void DecidesRequired_MatchesFailableCategory(string errorMsg)
    {
        var error = _translator.ExplainSingle(errorMsg);
        Assert.NotNull(error.Translation);
        Assert.Equal(VerseErrorCategory.Failable, error.Translation!.Category);
    }

    // =====================================================================
    //  SCOPE / VARIABLE category matching
    // =====================================================================

    [Theory]
    [InlineData("Cannot modify immutable variable")]
    [InlineData("cannot assign to a constant")]
    [InlineData("cannot set a immutable variable")]
    public void ImmutableVariable_MatchesScopeCategory(string errorMsg)
    {
        var error = _translator.ExplainSingle(errorMsg);
        Assert.NotNull(error.Translation);
        Assert.Equal(VerseErrorCategory.Scope, error.Translation!.Category);
    }

    [Theory]
    [InlineData("Unknown identifier `MyVar`")]
    [InlineData("Variable 'Score' is not defined")]
    public void UndefinedVariable_MatchesScopeCategory(string errorMsg)
    {
        var error = _translator.ExplainSingle(errorMsg);
        Assert.NotNull(error.Translation);
        Assert.Equal(VerseErrorCategory.Scope, error.Translation!.Category);
    }

    // =====================================================================
    //  SYNTAX category matching
    // =====================================================================

    [Theory]
    [InlineData("Indentation error")]
    [InlineData("Expected indentation")]
    [InlineData("Unexpected indent")]
    public void IndentationError_MatchesSyntaxCategory(string errorMsg)
    {
        var error = _translator.ExplainSingle(errorMsg);
        Assert.NotNull(error.Translation);
        Assert.Equal(VerseErrorCategory.Syntax, error.Translation!.Category);
    }

    [Theory]
    [InlineData("Unexpected token")]
    [InlineData("Syntax error")]
    [InlineData("Parse error")]
    public void SyntaxError_MatchesSyntaxCategory(string errorMsg)
    {
        var error = _translator.ExplainSingle(errorMsg);
        Assert.NotNull(error.Translation);
        Assert.Equal(VerseErrorCategory.Syntax, error.Translation!.Category);
    }

    // =====================================================================
    //  EFFECT category matching
    // =====================================================================

    [Theory]
    [InlineData("Cannot call <suspends> function from non-suspending context")]
    [InlineData("<suspends> not allowed outside of async")]
    public void SuspendsContext_MatchesEffectCategory(string errorMsg)
    {
        var error = _translator.ExplainSingle(errorMsg);
        Assert.NotNull(error.Translation);
        Assert.Equal(VerseErrorCategory.Effect, error.Translation!.Category);
    }

    [Fact]
    public void TransactsRequired_MatchesEffectCategory()
    {
        var error = _translator.ExplainSingle("requires <transacts> effect");
        Assert.NotNull(error.Translation);
        Assert.Equal(VerseErrorCategory.Effect, error.Translation!.Category);
    }

    // =====================================================================
    //  DEVICE category matching
    // =====================================================================

    [Theory]
    [InlineData("No such member 'OnActivated' on type 'button_device'")]
    [InlineData("'Trigger' is not a member of 'timer_device'")]
    public void DeviceNoMember_MatchesDeviceCategory(string errorMsg)
    {
        var error = _translator.ExplainSingle(errorMsg);
        Assert.NotNull(error.Translation);
        Assert.Equal(VerseErrorCategory.Device, error.Translation!.Category);
    }

    // =====================================================================
    //  CONCURRENCY category matching
    // =====================================================================

    [Fact]
    public void SpawnInvalid_MatchesConcurrencyCategory()
    {
        var error = _translator.ExplainSingle("Cannot spawn this expression");
        Assert.NotNull(error.Translation);
        Assert.Equal(VerseErrorCategory.Concurrency, error.Translation!.Category);
    }

    // =====================================================================
    //  Pattern database coverage
    // =====================================================================

    [Fact]
    public void GetAllPatterns_ReturnsNonEmpty()
    {
        var patterns = _translator.GetAllPatterns();
        Assert.NotEmpty(patterns);
        Assert.True(patterns.Count >= 20, $"Expected >= 20 patterns, found {patterns.Count}");
    }

    [Fact]
    public void AllPatterns_HaveRequiredFields()
    {
        var patterns = _translator.GetAllPatterns();
        foreach (var pattern in patterns)
        {
            Assert.False(string.IsNullOrEmpty(pattern.Id), $"Pattern missing Id");
            Assert.False(string.IsNullOrEmpty(pattern.Title), $"Pattern {pattern.Id} missing Title");
            Assert.False(string.IsNullOrEmpty(pattern.HumanExplanation), $"Pattern {pattern.Id} missing HumanExplanation");
            Assert.False(string.IsNullOrEmpty(pattern.Fix), $"Pattern {pattern.Id} missing Fix");
            Assert.False(string.IsNullOrEmpty(pattern.CodeExample), $"Pattern {pattern.Id} missing CodeExample");
            Assert.NotNull(pattern.Regex);
        }
    }

    [Fact]
    public void AllPatterns_HaveUniqueIds()
    {
        var patterns = _translator.GetAllPatterns();
        var ids = patterns.Select(p => p.Id).ToList();
        var uniqueIds = ids.Distinct().ToList();

        Assert.Equal(uniqueIds.Count, ids.Count);
    }

    [Fact]
    public void AllCategories_HaveAtLeastOnePattern()
    {
        var patterns = _translator.GetAllPatterns();
        var categories = Enum.GetValues<VerseErrorCategory>();

        foreach (var category in categories)
        {
            var count = patterns.Count(p => p.Category == category);
            Assert.True(count >= 1, $"Category {category} has no patterns");
        }
    }

    // =====================================================================
    //  File:line extraction edge cases
    // =====================================================================

    [Fact]
    public void TranslateOutput_HandlesColonFileFormat()
    {
        var rawOutput = "my_device.verse:42: error: Unknown identifier `foo`";
        var result = _translator.TranslateOutput(rawOutput);

        Assert.True(result.TotalErrors >= 1);
        var error = result.Errors[0];
        Assert.Equal("my_device.verse", error.FileName);
        Assert.Equal(42, error.Line);
    }
}
