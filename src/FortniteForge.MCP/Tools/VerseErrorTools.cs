using System.ComponentModel;
using System.Text.Json;
using WellVersed.Core.Services;
using ModelContextProtocol.Server;

namespace WellVersed.MCP.Tools;

[McpServerToolType]
public class VerseErrorTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Translates raw UEFN Verse compiler output into actionable, human-readable error explanations. " +
        "Paste the full compiler output and get back structured translations with: " +
        "what went wrong (plain English), how to fix it, before/after code examples, " +
        "error category, severity, and links to relevant Verse documentation. " +
        "Handles 45+ error patterns across type system, failable contexts, effects, " +
        "scope/variables, device API, syntax, and concurrency.")]
    public static string translate_verse_errors(
        VerseErrorTranslator translator,
        [Description("Raw UEFN compiler output (paste the full build log or error messages)")] string rawOutput)
    {
        var result = translator.TranslateOutput(rawOutput);

        return JsonSerializer.Serialize(new
        {
            summary = new
            {
                result.TotalErrors,
                result.Recognized,
                result.Unrecognized
            },
            errors = result.Errors.Select(e => new
            {
                e.FileName,
                e.Line,
                e.Column,
                e.Message,
                recognized = e.Translation != null,
                translation = e.Translation != null ? new
                {
                    e.Translation.Id,
                    e.Translation.Title,
                    category = e.Translation.Category.ToString(),
                    severity = e.Translation.Severity.ToString(),
                    whatWentWrong = e.Translation.HumanExplanation,
                    howToFix = e.Translation.Fix,
                    codeExample = e.Translation.CodeExample,
                    e.Translation.DocSection
                } : null
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Explains a single Verse error message in detail. " +
        "Provide just the error text (e.g., 'Expected expression of type int but found float') " +
        "and get back a detailed explanation with the fix and code example. " +
        "Use translate_verse_errors for bulk compiler output; use this for individual errors.")]
    public static string explain_verse_error(
        VerseErrorTranslator translator,
        [Description("A single Verse error message to explain")] string errorMessage)
    {
        var result = translator.ExplainSingle(errorMessage);

        if (result.Translation == null)
        {
            return JsonSerializer.Serialize(new
            {
                recognized = false,
                errorMessage,
                suggestion = "This error pattern is not in the database. " +
                    "Try pasting the full compiler output to translate_verse_errors " +
                    "for better context matching. Check the Verse documentation " +
                    "or Epic Dev Community forums for this specific error."
            }, JsonOpts);
        }

        return JsonSerializer.Serialize(new
        {
            recognized = true,
            errorMessage,
            translation = new
            {
                result.Translation.Id,
                result.Translation.Title,
                category = result.Translation.Category.ToString(),
                severity = result.Translation.Severity.ToString(),
                whatWentWrong = result.Translation.HumanExplanation,
                howToFix = result.Translation.Fix,
                codeExample = result.Translation.CodeExample,
                result.Translation.DocSection
            }
        }, JsonOpts);
    }
}
