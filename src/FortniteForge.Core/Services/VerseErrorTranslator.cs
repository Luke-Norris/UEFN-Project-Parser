using System.Text.RegularExpressions;

namespace WellVersed.Core.Services;

/// <summary>
/// Translates raw UEFN Verse compiler output into actionable, human-readable
/// error explanations with concrete fix examples.
/// </summary>
public class VerseErrorTranslator
{
    private static readonly List<VerseErrorPattern> Patterns = BuildPatternDatabase();

    /// <summary>
    /// Translates a block of raw UEFN compiler output, parsing individual errors
    /// and matching each against the pattern database.
    /// </summary>
    public VerseTranslationResult TranslateOutput(string rawOutput)
    {
        var parsed = ParseCompilerOutput(rawOutput);
        var translations = new List<TranslatedError>();

        foreach (var error in parsed)
        {
            var match = FindBestMatch(error.Message);
            translations.Add(new TranslatedError
            {
                Raw = error.Raw,
                FileName = error.FileName,
                Line = error.Line,
                Column = error.Column,
                Message = error.Message,
                Translation = match
            });
        }

        return new VerseTranslationResult
        {
            TotalErrors = translations.Count,
            Recognized = translations.Count(t => t.Translation != null),
            Unrecognized = translations.Count(t => t.Translation == null),
            Errors = translations
        };
    }

    /// <summary>
    /// Explains a single error message in detail.
    /// </summary>
    public TranslatedError ExplainSingle(string errorMessage)
    {
        var match = FindBestMatch(errorMessage);
        return new TranslatedError
        {
            Raw = errorMessage,
            Message = errorMessage,
            Translation = match
        };
    }

    /// <summary>
    /// Returns all known error patterns for documentation/export.
    /// </summary>
    public List<VerseErrorPattern> GetAllPatterns() => Patterns;

    // ─── Compiler Output Parser ─────────────────────────────────────────────

    private static List<ParsedCompilerError> ParseCompilerOutput(string rawOutput)
    {
        var errors = new List<ParsedCompilerError>();
        var lines = rawOutput.Split('\n');
        var currentLines = new List<string>();

        void Flush()
        {
            if (currentLines.Count == 0) return;
            var raw = string.Join("\n", currentLines).Trim();
            if (string.IsNullOrEmpty(raw)) return;

            // Try to extract file:line pattern
            // Patterns: "file.verse(42,10): error" or "file.verse:42:" or "file.verse(42)"
            string? fileName = null;
            int? line = null;
            int? column = null;

            var fileMatch = Regex.Match(raw, @"([^\s(]+\.verse)\s*\((\d+)(?:,\s*(\d+))?\)");
            if (fileMatch.Success)
            {
                fileName = fileMatch.Groups[1].Value;
                line = int.Parse(fileMatch.Groups[2].Value);
                if (fileMatch.Groups[3].Success)
                    column = int.Parse(fileMatch.Groups[3].Value);
            }
            else
            {
                var fileMatch2 = Regex.Match(raw, @"([^\s(]+\.verse):(\d+):");
                if (fileMatch2.Success)
                {
                    fileName = fileMatch2.Groups[1].Value;
                    line = int.Parse(fileMatch2.Groups[2].Value);
                }
            }

            // Extract just the error message (strip file/line prefix)
            var message = raw;
            var msgMatch = Regex.Match(raw, @"(?:error|warning)\s*:\s*(.+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (msgMatch.Success)
                message = msgMatch.Groups[1].Value.Trim();

            errors.Add(new ParsedCompilerError
            {
                Raw = raw,
                FileName = fileName,
                Line = line,
                Column = column,
                Message = message
            });

            currentLines.Clear();
        }

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                Flush();
                continue;
            }

            // Detect new error start
            var isErrorStart =
                Regex.IsMatch(trimmed, @"^(error|warning|Error|Warning|ERR|WARN)\s*:", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(trimmed, @"\.verse[\s(:]+\d+", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(trimmed, @"^.*\.verse\(\d+");

            if (isErrorStart && currentLines.Count > 0)
                Flush();

            currentLines.Add(trimmed);
        }

        Flush();
        return errors;
    }

    private static VerseErrorPattern? FindBestMatch(string message)
    {
        foreach (var pattern in Patterns)
        {
            if (pattern.Regex.IsMatch(message))
                return pattern;
        }
        return null;
    }

    // ─── Pattern Database ───────────────────────────────────────────────────

    private static List<VerseErrorPattern> BuildPatternDatabase()
    {
        return new List<VerseErrorPattern>
        {
            // ═══════════════════════════════════════════════════════════════
            // TYPE SYSTEM ERRORS
            // ═══════════════════════════════════════════════════════════════

            new()
            {
                Id = "type_expected_found",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Ee]xpected\s+(?:expression\s+of\s+)?type\s+[`']?(\w+)[`']?\s+but\s+found\s+[`']?(\w+)[`']?", RegexOptions.Compiled),
                Title = "Type Mismatch",
                HumanExplanation = "The compiler expected one type but received another. This happens when passing arguments to functions, assigning to typed variables, or returning the wrong type from a function.",
                Fix = "Check that the value matches the expected type. You may need a conversion function (e.g., Floor() for float-to-int, ToString() for int-to-string), or change the variable/parameter type.",
                CodeExample = "# Before (broken):\nMyHealth : int = 100.0  # float assigned to int\n\n# After (fixed):\nMyHealth : int = 100\n# Or convert:\nMyHealth : int = Floor[100.0]",
                DocSection = "Types"
            },

            new()
            {
                Id = "type_cannot_convert",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Cc]annot\s+convert\s+(?:from\s+)?[`']?(\w+)[`']?\s+to\s+[`']?(\w+)[`']?", RegexOptions.Compiled),
                Title = "Cannot Convert Between Types",
                HumanExplanation = "There is no implicit or explicit conversion between these two types. Verse is strictly typed and does not allow arbitrary casts between unrelated types.",
                Fix = "Use an appropriate conversion function (e.g., ToString(), Floor(), Ceil(), Round()) or restructure your code to use the correct type from the start.",
                CodeExample = "# Before (broken):\nMyString : string = 42  # No implicit int->string\n\n# After (fixed):\nMyString : string = \"{42}\"\n\n# float to int:\nMyInt : int = Floor[3.14]",
                DocSection = "Types"
            },

            new()
            {
                Id = "type_not_subtype",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Tt]ype\s+[`']?(\w+)[`']?\s+is\s+not\s+a\s+subtype\s+of\s+[`']?(\w+)[`']?", RegexOptions.Compiled),
                Title = "Type Is Not a Subtype",
                HumanExplanation = "You're trying to use a type where a more specific subtype is required. For example, assigning a base class where a derived class is expected, or using a type that doesn't inherit from the required parent.",
                Fix = "Ensure your type inherits from (extends) the required parent type, or use the correct type that matches the constraint. Check your class definition's parent type in the parentheses after 'class'.",
                CodeExample = "# Before (broken):\nmy_widget := class(creative_device):  # creative_device is not a widget type\n    ...\n\n# After (fixed):\nmy_widget := class(creative_device):\n    # creative_device IS correct for devices\n    # For widgets, use the appropriate base class\n    ...",
                DocSection = "Types"
            },

            new()
            {
                Id = "type_parametric_mismatch",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Pp]arametric\s+type\s+mismatch|[Gg]eneric\s+(?:type\s+)?constraint|type\s+parameter.*(?:does\s+not\s+(?:match|satisfy)|mismatch)", RegexOptions.Compiled),
                Title = "Parametric Type / Generic Constraint Mismatch",
                HumanExplanation = "A generic/parametric type does not satisfy its constraints. The type argument you provided does not meet the requirements specified by the generic type parameter.",
                Fix = "Check the generic constraints on the type/function definition. Ensure your type argument implements the required interfaces or extends the required base type.",
                CodeExample = "# If a function requires comparable(t):\nSort(Items : []t where t : comparable) : []t = ...\n\n# Before (broken):\nSort(MyCustomObjects)  # custom type not comparable\n\n# After (fixed) — make type comparable:\nmy_type := class<comparable>:\n    Value : int\n    Equals(Other : my_type)<computes><decides> : void = ...",
                DocSection = "Types"
            },

            new()
            {
                Id = "type_no_return_all_paths",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"does\s+not\s+return\s+a?\s*value\s+in\s+all\s+cases|not\s+all\s+(?:code\s+)?paths\s+return", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Missing Return Value",
                HumanExplanation = "Every code path in your function must produce a value of the declared return type. If you have an 'if' without an 'else', the function has no return value when the condition is false.",
                Fix = "Add an 'else' branch that returns a default value, or restructure so all paths return. In Verse, the last expression in a block is the return value.",
                CodeExample = "# Before (broken):\nGetScore():int =\n    if (HasWon?):\n        100\n\n# After (fixed):\nGetScore():int =\n    if (HasWon?):\n        100\n    else:\n        0",
                DocSection = "Functions"
            },

            // ═══════════════════════════════════════════════════════════════
            // FAILABLE CONTEXT ERRORS
            // ═══════════════════════════════════════════════════════════════

            new()
            {
                Id = "failable_not_in_context",
                Category = VerseErrorCategory.Failable,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"failable\s+expression.*not\s+in\s+(?:a\s+)?failable\s+context|may\s+fail\s+but\s+is\s+not\s+in\s+a\s+failable\s+context", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Failable Expression Outside Failable Context",
                HumanExplanation = "Operations that can fail (like array access with [], map lookups, casting, or calling <decides> functions) must be wrapped in a failable context. Verse enforces this because these operations might not succeed at runtime.",
                Fix = "Wrap the expression in an 'if' block (most common), a 'for' loop, or provide a default with the 'or' operator. The 'if' block gives you a success branch and an optional 'else' for the failure case.",
                CodeExample = "# Before (broken):\nItem := MyArray[Index]\n\n# After (fixed) — using if:\nif (Item := MyArray[Index]):\n    # use Item safely here\nelse:\n    # handle missing item\n\n# Or with default value:\nItem := MyArray[Index] or DefaultItem",
                DocSection = "Failure"
            },

            new()
            {
                Id = "failable_propagate_question",
                Category = VerseErrorCategory.Failable,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"might\s+fail.*use\s+[`']?\?[`']?\s+to\s+propagate|propagate\s+failure\s+with\s+\?|add\s+[`']?\?[`']?\s+to\s+propagate", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Failure Propagation Required",
                HumanExplanation = "This expression can fail, and you're inside a function that is already <decides> (failable). You can propagate the failure upward by adding '?' after the call, which means 'if this fails, my function fails too'.",
                Fix = "Add '?' after the failable call to propagate failure to the caller. This only works inside <decides> functions.",
                CodeExample = "# Before (broken):\nGetItem()<decides><transacts> : item =\n    Items.Find(Key)  # Can fail, not propagated\n\n# After (fixed):\nGetItem()<decides><transacts> : item =\n    Items.Find(Key)?  # ? propagates failure",
                DocSection = "Failure"
            },

            new()
            {
                Id = "failable_decides_required",
                Category = VerseErrorCategory.Failable,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"<decides>\s+effect\s+required|requires?\s+<decides>|needs?\s+(?:the\s+)?<decides>\s+effect", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "<decides> Effect Required",
                HumanExplanation = "You're calling a failable function or using a failable expression, but your function doesn't have the <decides> effect. The <decides> effect marks a function as one that may fail, allowing it to use failable operations directly.",
                Fix = "Add <decides> (and usually <transacts>) to your function signature. If you don't want the function to be failable, wrap the call in 'if' instead.",
                CodeExample = "# Before (broken):\nFindPlayer(Name : string)<computes> : player =\n    PlayerMap[Name]  # Failable, needs <decides>\n\n# After (fixed) — make function failable:\nFindPlayer(Name : string)<decides><transacts> : player =\n    PlayerMap[Name]\n\n# Or handle inline:\nFindPlayer(Name : string)<computes> : ?player =\n    if (P := PlayerMap[Name]):\n        option{P}\n    else:\n        false",
                DocSection = "Failure"
            },

            new()
            {
                Id = "failable_call_may_fail",
                Category = VerseErrorCategory.Failable,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"this\s+call\s+may\s+fail|call.*may\s+fail.*not\s+(?:in\s+)?(?:a\s+)?failable", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Call May Fail",
                HumanExplanation = "You're calling a function that has the <decides> effect (it can fail), but you're not in a context that handles failure. Every failable call must be handled.",
                Fix = "Wrap the call in 'if' to handle success/failure, or add <decides> to your own function and use '?' to propagate failure.",
                CodeExample = "# Before (broken):\nDoSomething():void =\n    Player := FindPlayer(\"Luke\")  # FindPlayer is <decides>\n\n# After (fixed):\nDoSomething():void =\n    if (Player := FindPlayer(\"Luke\")):\n        # Use Player\n    else:\n        Print(\"Player not found\")",
                DocSection = "Failure"
            },

            // ═══════════════════════════════════════════════════════════════
            // EFFECT SYSTEM ERRORS
            // ═══════════════════════════════════════════════════════════════

            new()
            {
                Id = "effect_no_rollback_vs_transacts",
                Category = VerseErrorCategory.Effect,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Ee]xpected\s+<no_rollback>.*<transacts>|<no_rollback>.*incompatible.*<transacts>|cannot.*<transacts>.*<no_rollback>", RegexOptions.Compiled),
                Title = "Effect Conflict: <no_rollback> vs <transacts>",
                HumanExplanation = "<no_rollback> and <transacts> are mutually exclusive effects. <transacts> allows rollback on failure (e.g., undoing variable mutations if a <decides> branch fails), while <no_rollback> explicitly forbids rollback behavior.",
                Fix = "Remove one of the conflicting effects. Most functions that modify variables need <transacts>. Only use <no_rollback> when you explicitly want to prevent rollback semantics.",
                CodeExample = "# Before (broken):\nUpdate()<no_rollback><transacts> : void =  # Conflict!\n    set Score += 1\n\n# After (fixed):\nUpdate()<transacts> : void =\n    set Score += 1",
                DocSection = "Effects"
            },

            new()
            {
                Id = "effect_suspends_context",
                Category = VerseErrorCategory.Effect,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Cc]annot\s+call.*<suspends>.*(?:non-suspending|from)|<suspends>.*(?:outside|not\s+allowed|requires)|suspends.*not\s+(?:allowed|available)", RegexOptions.Compiled),
                Title = "Cannot Call <suspends> from Non-Suspending Context",
                HumanExplanation = "A <suspends> function (one that can pause execution, like Sleep() or Await()) can only be called from within another <suspends> context. This is how Verse manages async concurrency.",
                Fix = "Either mark the calling function as <suspends>, or use 'spawn' to run the suspending function concurrently.",
                CodeExample = "# Before (broken):\nOnBegin():void =\n    Sleep(1.0)  # Sleep is <suspends>\n\n# After (fixed) — make function suspends:\nOnBegin()<suspends>:void =\n    Sleep(1.0)\n\n# Or use spawn:\nOnBegin():void =\n    spawn { MyAsyncLoop() }",
                DocSection = "Concurrency"
            },

            new()
            {
                Id = "effect_mismatch_general",
                Category = VerseErrorCategory.Effect,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Ee]ffect\s+mismatch|cannot\s+call.*<decides>.*<computes>|<computes>.*vs.*<transacts>|incompatible\s+effects?", RegexOptions.Compiled),
                Title = "Effect Mismatch",
                HumanExplanation = "Verse functions have effect specifiers (<computes>, <decides>, <transacts>, <suspends>) that form a hierarchy. A function with weaker effects cannot call a function with stronger effects. <computes> < <transacts> < <decides>.",
                Fix = "Add the required effect to your function, or wrap the call to handle it (e.g., wrap <decides> calls in 'if'). Effect hierarchy: <computes> is the weakest, <decides> and <transacts> are stronger.",
                CodeExample = "# Before (broken):\nGetItem()<computes> : item =\n    Items.Find(Key)  # Find is <decides><transacts>\n\n# After (fixed) — escalate effects:\nGetItem()<decides><transacts> : item =\n    Items.Find(Key)\n\n# Or handle failure:\nGetItem()<computes> : item =\n    if (Result := Items.Find(Key)):\n        Result\n    else:\n        DefaultItem",
                DocSection = "Effects"
            },

            new()
            {
                Id = "effect_transacts_required",
                Category = VerseErrorCategory.Effect,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"requires?\s+<transacts>|<transacts>\s+effect\s+required|cannot\s+(?:modify|set).*without\s+<transacts>", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "<transacts> Effect Required",
                HumanExplanation = "Modifying mutable variables (using 'set') requires the <transacts> effect. This is because Verse needs to track mutations for potential rollback in failable contexts.",
                Fix = "Add <transacts> to your function signature. If the function also uses <decides>, add both.",
                CodeExample = "# Before (broken):\nvar Score : int = 0\nAddScore(Points : int)<computes> : void =\n    set Score += Points  # Needs <transacts>\n\n# After (fixed):\nAddScore(Points : int)<transacts> : void =\n    set Score += Points",
                DocSection = "Effects"
            },

            // ═══════════════════════════════════════════════════════════════
            // VARIABLE / SCOPE ERRORS
            // ═══════════════════════════════════════════════════════════════

            new()
            {
                Id = "var_immutable",
                Category = VerseErrorCategory.Scope,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Cc]annot\s+modify\s+immutable|cannot\s+assign\s+to\s+(?:a\s+)?(?:constant|immutable)|cannot\s+set\s+(?:a\s+)?(?:constant|immutable|non-mutable)", RegexOptions.Compiled),
                Title = "Cannot Modify Immutable Variable",
                HumanExplanation = "By default, Verse variables are immutable (constants). Once assigned, their value cannot change. To create a mutable variable, you must declare it with 'var' and use 'set' to change it.",
                Fix = "Declare the variable with 'var' and use 'set' to modify it.",
                CodeExample = "# Before (broken):\nScore : int = 0\nScore = 10  # Cannot modify!\n\n# After (fixed):\nvar Score : int = 0\nset Score = 10",
                DocSection = "Variables"
            },

            new()
            {
                Id = "var_not_defined",
                Category = VerseErrorCategory.Scope,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Uu]nknown\s+identifier\s+[`']?(\w+)[`']?|[Vv]ariable\s+[`']?(\w+)[`']?\s+(?:is\s+)?not\s+(?:defined|declared)|[`']?(\w+)[`']?\s+is\s+(?:not\s+)?(?:undefined|undeclared)", RegexOptions.Compiled),
                Title = "Unknown Identifier / Variable Not Defined",
                HumanExplanation = "The compiler does not recognize this name. Common causes: typo in the name, missing 'using' import for the module it lives in, or the variable is out of scope.",
                Fix = "Check for typos. If the identifier comes from a Fortnite module, add the appropriate 'using' statement. Check that the variable was declared in an accessible scope.",
                CodeExample = "# Before (broken):\nusing { /Fortnite.com/Devices }\nMyButton : Buttn_Device = ...  # Typo!\n\n# After (fixed):\nusing { /Fortnite.com/Devices }\nMyButton : button_device = ...  # Correct\n\n# Common using statements:\nusing { /Fortnite.com/Devices }\nusing { /Fortnite.com/Characters }\nusing { /Fortnite.com/UI }\nusing { /Verse.org/Simulation }\nusing { /UnrealEngine.com/Temporary/SpatialMath }",
                DocSection = "Modules"
            },

            new()
            {
                Id = "var_shadow",
                Category = VerseErrorCategory.Scope,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Cc]annot\s+shadow\s+variable|shadows?\s+(?:existing\s+)?(?:variable|binding)|already\s+(?:defined|bound)\s+in\s+(?:this\s+)?scope", RegexOptions.Compiled),
                Title = "Variable Shadowing Not Allowed",
                HumanExplanation = "Verse does not allow variable shadowing within the same scope level. You cannot declare a new variable with the same name as an existing one in the same block.",
                Fix = "Rename the second variable to something different, or remove the duplicate declaration.",
                CodeExample = "# Before (broken):\nScore : int = 0\nif (Condition?):\n    Score : int = 10  # Shadows outer Score!\n\n# After (fixed):\nvar Score : int = 0\nif (Condition?):\n    set Score = 10  # Modifies outer Score",
                DocSection = "Variables"
            },

            new()
            {
                Id = "var_invalid_set",
                Category = VerseErrorCategory.Scope,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Ii]nvalid\s+[`']?set[`']?\s+target|cannot\s+set\s+(?!.*immutable)|[`']?set[`']?\s+requires\s+(?:a\s+)?mutable", RegexOptions.Compiled),
                Title = "Invalid Set Target",
                HumanExplanation = "The 'set' keyword can only be used on mutable variables (declared with 'var'), mutable struct fields, or mutable map/array entries. You cannot 'set' a function call result, a temporary, or a read-only binding.",
                Fix = "Ensure the target is a 'var' variable or a mutable field. If it's a computed value, store it in a 'var' first.",
                CodeExample = "# Before (broken):\nset GetPosition().X = 10.0  # Cannot set function result\n\n# After (fixed):\nvar Pos : vector3 = GetPosition()\nset Pos = vector3{X := 10.0, Y := Pos.Y, Z := Pos.Z}\nSetPosition(Pos)",
                DocSection = "Variables"
            },

            new()
            {
                Id = "var_duplicate",
                Category = VerseErrorCategory.Scope,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Dd]uplicate\s+definition|already\s+defined|[Rr]edefinition\s+of", RegexOptions.Compiled),
                Title = "Duplicate Definition",
                HumanExplanation = "A variable, function, or type with this name already exists in the same scope. Verse does not allow duplicate definitions at the same scope level.",
                Fix = "Rename one of the duplicates. If they are meant to be function overloads, ensure their parameter types differ.",
                CodeExample = "# Before (broken):\nScore : int = 0\nScore : int = 10  # Duplicate!\n\n# After (fixed):\nScore : int = 0\nBonusScore : int = 10",
                DocSection = "Variables"
            },

            // ═══════════════════════════════════════════════════════════════
            // DEVICE / API ERRORS
            // ═══════════════════════════════════════════════════════════════

            new()
            {
                Id = "device_no_member",
                Category = VerseErrorCategory.Device,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Nn]o\s+such\s+member\s+[`']?(\w+)[`']?\s+on\s+(?:type\s+)?[`']?(\w+)[`']?|[`']?(\w+)[`']?\s+does\s+not\s+have\s+(?:a\s+)?member\s+[`']?(\w+)[`']?|[`']?(\w+)[`']?\s+is\s+not\s+a\s+member\s+of\s+[`']?(\w+)[`']?", RegexOptions.Compiled),
                Title = "No Such Member on Type",
                HumanExplanation = "You're trying to access a member (method, property, or field) that doesn't exist on this type. This could be a typo, or the member might be on a different type or require a specific 'using' import.",
                Fix = "Check the device/class documentation for the correct member name. Member names in Verse are case-sensitive and use snake_case for device APIs. Ensure you have the right 'using' statement.",
                CodeExample = "# Before (broken):\nMyButton.OnActivated.Subscribe(OnPressed)  # Wrong event name\n\n# After (fixed):\nMyButton.InteractedWithEvent.Subscribe(OnPressed)\n\n# Common device events:\n# button_device: InteractedWithEvent\n# trigger_device: TriggeredEvent\n# item_granter_device: ItemGrantedEvent",
                DocSection = "Devices"
            },

            new()
            {
                Id = "device_wrong_args",
                Category = VerseErrorCategory.Device,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Ww]rong\s+number\s+of\s+arguments|too\s+(?:many|few)\s+arguments|expected\s+(\d+)\s+argument|incorrect\s+(?:number\s+of\s+)?arguments", RegexOptions.Compiled),
                Title = "Wrong Number of Arguments",
                HumanExplanation = "You're passing the wrong number of arguments to a function or method. The function signature expects a specific number of parameters.",
                Fix = "Check the function signature for the correct parameter list. For device event handlers, the callback typically takes an ?agent parameter.",
                CodeExample = "# Before (broken):\nMyButton.InteractedWithEvent.Subscribe(OnPressed)\nOnPressed():void =  # Missing agent parameter!\n    Print(\"Pressed\")\n\n# After (fixed):\nMyButton.InteractedWithEvent.Subscribe(OnPressed)\nOnPressed(Agent : ?agent):void =\n    Print(\"Pressed\")",
                DocSection = "Functions"
            },

            new()
            {
                Id = "device_missing_arg",
                Category = VerseErrorCategory.Device,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Mm]issing\s+required\s+argument|argument\s+[`']?(\w+)[`']?\s+(?:is\s+)?required|required\s+(?:parameter|argument)\s+[`']?(\w+)[`']?\s+not\s+provided", RegexOptions.Compiled),
                Title = "Missing Required Argument",
                HumanExplanation = "A required argument was not provided in the function call. All non-optional parameters must be supplied.",
                Fix = "Add the missing argument to the function call. Check the function signature for all required parameters and their types.",
                CodeExample = "# Before (broken):\nMyGranter.GrantItem()  # Missing player argument\n\n# After (fixed):\nMyGranter.GrantItem(Player)",
                DocSection = "Functions"
            },

            new()
            {
                Id = "device_missing_using",
                Category = VerseErrorCategory.Device,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"missing\s+[`']?using[`']?\s+statement|module\s+[`']?([^`']+)[`']?\s+not\s+found|cannot\s+find\s+module", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Missing Using Statement / Module Not Found",
                HumanExplanation = "The type or function you're using lives in a module that hasn't been imported. Verse requires explicit 'using' declarations for each module you reference.",
                Fix = "Add the appropriate 'using' statement at the top of your file.",
                CodeExample = "# Common UEFN using statements:\nusing { /Fortnite.com/Devices }          # Device types\nusing { /Fortnite.com/Characters }       # Character/agent types\nusing { /Fortnite.com/UI }               # UI widgets\nusing { /Fortnite.com/Game }             # Game framework\nusing { /Verse.org/Simulation }          # Simulation framework\nusing { /Verse.org/Random }              # Random numbers\nusing { /UnrealEngine.com/Temporary/Diagnostics }  # Print()\nusing { /UnrealEngine.com/Temporary/SpatialMath }  # Vectors, rotations",
                DocSection = "Modules"
            },

            // ═══════════════════════════════════════════════════════════════
            // SYNTAX ERRORS
            // ═══════════════════════════════════════════════════════════════

            new()
            {
                Id = "syntax_indent",
                Category = VerseErrorCategory.Syntax,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Ee]xpected\s+indentation|[Ii]ndentation\s+error|[Uu]nexpected\s+indent", RegexOptions.Compiled),
                Title = "Indentation Error",
                HumanExplanation = "Verse uses significant whitespace (like Python). Incorrect indentation breaks the block structure. UEFN requires exactly 4 spaces per indentation level. Tabs are not allowed.",
                Fix = "Use consistent 4-space indentation. Ensure all lines in a block are aligned. Do NOT mix tabs and spaces. Configure your editor to insert spaces when you press Tab.",
                CodeExample = "# Before (broken):\nif (Condition?):\nDoSomething()  # Not indented!\n\n# After (fixed):\nif (Condition?):\n    DoSomething()  # 4-space indent\n\n# Nested blocks:\nif (X > 0):\n    if (Y > 0):\n        DoSomething()  # 8 spaces for nested",
                DocSection = "Syntax"
            },

            new()
            {
                Id = "syntax_type_annotation",
                Category = VerseErrorCategory.Syntax,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Ee]xpected\s+[`']?:[`']?\s+(?:in|after|for)|[Mm]issing\s+type\s+annotation|[Tt]ype\s+annotation\s+required", RegexOptions.Compiled),
                Title = "Missing Type Annotation",
                HumanExplanation = "Verse requires explicit type annotations in many contexts: function parameters, return types, class fields, and some variable declarations. The compiler cannot always infer types.",
                Fix = "Add the type annotation using ': type' syntax. For function parameters, each parameter needs 'Name : type'. For return types, use ') : return_type ='.",
                CodeExample = "# Before (broken):\nMyFunc(X, Y) = X + Y  # Missing types\n\n# After (fixed):\nMyFunc(X : int, Y : int) : int = X + Y\n\n# Class fields:\nmy_class := class:\n    Health : float = 100.0  # Type annotation required",
                DocSection = "Types"
            },

            new()
            {
                Id = "syntax_assignment",
                Category = VerseErrorCategory.Syntax,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Ee]xpected\s+[`']?=[`']?\s+(?:in|after)|[Ee]xpected\s+[`']?:=[`']?\s+(?:in|for)|use\s+[`']?:=[`']?\s+(?:for|instead)", RegexOptions.Compiled),
                Title = "Assignment Syntax Error",
                HumanExplanation = "Verse uses ':=' for new bindings (creating variables) and 'set X =' for mutation. Regular '=' is used in definitions (class fields, function definitions). Mixing these up causes syntax errors.",
                Fix = "Use ':=' to create new bindings, 'set X =' to modify existing mutable variables, and '=' in class field definitions.",
                CodeExample = "# Binding (new variable):\nScore := 42\nName := \"Player\"\n\n# Definition (class field):\nmy_class := class:\n    Health : float = 100.0  # = in definitions\n\n# Mutation (changing existing var):\nvar Counter : int = 0\nset Counter = Counter + 1  # set ... = for mutation",
                DocSection = "Variables"
            },

            new()
            {
                Id = "syntax_unexpected_token",
                Category = VerseErrorCategory.Syntax,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Uu]nexpected\s+token|[Ss]yntax\s+error|[Pp]arse\s+error", RegexOptions.Compiled),
                Title = "Syntax Error / Unexpected Token",
                HumanExplanation = "The compiler encountered something it didn't expect at this position. Common causes: missing parentheses, extra commas, wrong operator, mismatched brackets, or incorrect Verse syntax.",
                Fix = "Check the line for missing or extra punctuation. Ensure parentheses and brackets are balanced. Verify you're using Verse syntax (not C++/Python/etc).",
                CodeExample = "# Common syntax fixes:\n# Missing closing paren:\nif (X > 0:     # Bad\nif (X > 0):    # Good\n\n# Trailing comma:\narray{1, 2, 3,}  # Bad\narray{1, 2, 3}   # Good\n\n# Block expression:\nResult := block:\n    X := 10\n    X + 5  # Last expression is the value",
                DocSection = "Syntax"
            },

            new()
            {
                Id = "syntax_colon_block",
                Category = VerseErrorCategory.Syntax,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Ee]xpected\s+[`']?:[`']?\s+(?:to\s+)?(?:start|begin)\s+(?:a\s+)?block|missing\s+[`']?:[`']?\s+after", RegexOptions.Compiled),
                Title = "Missing Colon to Start Block",
                HumanExplanation = "In Verse, blocks (after if, for, class, function definitions, etc.) start with a colon ':' followed by an indented block on the next line.",
                Fix = "Add ':' at the end of the line before the indented block.",
                CodeExample = "# Before (broken):\nif (X > 0)\n    DoSomething()\n\n# After (fixed):\nif (X > 0):\n    DoSomething()\n\n# Same for functions:\nMyFunc() : void =\n    DoSomething()",
                DocSection = "Syntax"
            },

            // ═══════════════════════════════════════════════════════════════
            // CONCURRENCY ERRORS
            // ═══════════════════════════════════════════════════════════════

            new()
            {
                Id = "concurrency_spawn_invalid",
                Category = VerseErrorCategory.Concurrency,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Cc]annot\s+spawn|spawn.*requires|spawn.*<suspends>|invalid\s+spawn", RegexOptions.Compiled),
                Title = "Invalid Spawn Usage",
                HumanExplanation = "The 'spawn' expression requires a <suspends> function or block. You can only spawn async tasks. The enclosing context may also need specific effects.",
                Fix = "Ensure the expression inside 'spawn { }' calls a <suspends> function. Create an async wrapper if needed.",
                CodeExample = "# Before (broken):\nspawn { DoSyncWork() }  # Not a <suspends> function\n\n# After (fixed):\nMyAsync()<suspends>:void =\n    Sleep(1.0)\n    DoWork()\n\nOnBegin()<suspends>:void =\n    spawn { MyAsync() }",
                DocSection = "Concurrency"
            },

            new()
            {
                Id = "concurrency_race_sync",
                Category = VerseErrorCategory.Concurrency,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"race.*requires\s+<suspends>|sync.*requires\s+<suspends>|rush.*requires\s+<suspends>", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Concurrency Primitive Requires <suspends>",
                HumanExplanation = "Concurrency primitives like race{}, sync{}, and rush{} require a <suspends> context because they manage async tasks that can pause and resume.",
                Fix = "Ensure the enclosing function has the <suspends> effect, and all branches inside the concurrency block are also <suspends>.",
                CodeExample = "# Before (broken):\nDoGameLoop():void =\n    race:\n        WaitForTimer()\n        WaitForKill()\n\n# After (fixed):\nDoGameLoop()<suspends>:void =\n    race:\n        WaitForTimer()  # Must be <suspends>\n        WaitForKill()   # Must be <suspends>",
                DocSection = "Concurrency"
            },

            // ═══════════════════════════════════════════════════════════════
            // CLASS / STRUCT / INTERFACE ERRORS
            // ═══════════════════════════════════════════════════════════════

            new()
            {
                Id = "class_interface_not_implemented",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"interface.*not\s+implemented|does\s+not\s+implement|missing\s+implementation", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Interface Not Implemented",
                HumanExplanation = "Your class declares it implements an interface but is missing one or more required methods. All methods defined in the interface must have matching implementations in your class.",
                Fix = "Add the missing method implementations. Check the interface definition for exact method signatures (name, parameters, return type, and effects).",
                CodeExample = "# Interface:\ndamageable := interface:\n    TakeDamage(Amount : float) : void\n    GetHealth() : float\n\n# Before (broken):\nmy_actor := class(damageable):\n    TakeDamage(Amount : float) : void = {}\n    # Missing GetHealth!\n\n# After (fixed):\nmy_actor := class(damageable):\n    TakeDamage(Amount : float) : void = {}\n    GetHealth() : float = 100.0",
                DocSection = "Interfaces"
            },

            new()
            {
                Id = "class_field_not_found",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"field\s+[`']?(\w+)[`']?\s+not\s+found|struct.*does\s+not\s+have\s+(?:member|field)|class.*does\s+not\s+have\s+(?:member|field)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Struct/Class Field Not Found",
                HumanExplanation = "The struct or class you're accessing does not have a field with that name. This could be a typo, the field may be private, or it may be on a different type.",
                Fix = "Check the struct/class definition for the correct field name. Field names are case-sensitive in Verse.",
                CodeExample = "# If the struct is:\nplayer_data := struct:\n    Name : string\n    Score : int\n\n# Before (broken):\nData.Points  # No field \"Points\"\n\n# After (fixed):\nData.Score  # Correct field name",
                DocSection = "Structs"
            },

            new()
            {
                Id = "class_access_violation",
                Category = VerseErrorCategory.Scope,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"access\s+(?:specifier\s+)?violation|cannot\s+access\s+(?:private|protected)|not\s+accessible|[`']?(\w+)[`']?\s+is\s+(?:private|protected)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Access Specifier Violation",
                HumanExplanation = "You're trying to access a member marked <private> or <protected> from outside its allowed scope. Private members can only be accessed within the same class; protected within the class hierarchy.",
                Fix = "If you need external access, change the specifier to <public>. Otherwise, provide a public getter/setter method.",
                CodeExample = "# Before (broken):\nmy_class := class:\n    <private> Secret : int = 42\n\n# External code:\nObj.Secret  # Access violation!\n\n# After (fixed) — add a getter:\nmy_class := class:\n    <private> var InternalSecret : int = 42\n    GetSecret()<computes> : int = InternalSecret",
                DocSection = "Access Specifiers"
            },

            new()
            {
                Id = "class_recursive_type",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Rr]ecursive\s+type|[Cc]ircular\s+(?:reference|dependency|type)", RegexOptions.Compiled),
                Title = "Recursive / Circular Type",
                HumanExplanation = "A type refers to itself directly or through a chain of other types, creating an infinite-size type. Verse does not allow directly recursive value types.",
                Fix = "Use 'option' (?) or a reference type to break the recursion. For tree structures, make the recursive field optional.",
                CodeExample = "# Before (broken):\ntree_node := struct:\n    Value : int\n    Children : []tree_node  # Infinite size!\n\n# After (fixed) — use option:\ntree_node := class:\n    Value : int = 0\n    var Children : []tree_node = array{}  # class is reference type",
                DocSection = "Types"
            },

            new()
            {
                Id = "class_self_before_init",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Cc]annot\s+use\s+[`']?[Ss]elf[`']?\s+before|[Ss]elf.*not\s+initialized|use\s+of\s+[`']?[Ss]elf[`']?\s+before\s+(?:all\s+)?fields?\s+(?:are\s+)?initialized", RegexOptions.Compiled),
                Title = "Self Used Before Initialization",
                HumanExplanation = "You're referencing 'Self' in a constructor or initializer before all fields have been assigned. All fields must be initialized before 'Self' can be used.",
                Fix = "Reorder field initializations so all fields are set before any reference to 'Self'. Consider using a post-construction init method instead.",
                CodeExample = "# Before (broken):\nmy_class := class:\n    Other : my_class = Self  # Self not ready yet!\n    Value : int = 0\n\n# After (fixed):\nmy_class := class:\n    Value : int = 0\n    # Reference Self only in methods, not field initializers",
                DocSection = "Classes"
            },

            // ═══════════════════════════════════════════════════════════════
            // COLLECTION ERRORS
            // ═══════════════════════════════════════════════════════════════

            new()
            {
                Id = "collection_subscript_range",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"subscript\s+out\s+of\s+range|index\s+out\s+of\s+(?:bounds|range)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Subscript / Index Out of Range",
                HumanExplanation = "An array or container access used an index outside the valid range. In Verse, array indexing is failable — you must handle the case where the index is invalid.",
                Fix = "Always access arrays within a failable context ('if' or 'for') to handle out-of-range indices. Never assume an index is valid.",
                CodeExample = "# Before (broken):\nFirstItem := MyArray[0]  # Failable, not handled\n\n# After (fixed):\nif (FirstItem := MyArray[0]):\n    # Safe to use FirstItem\nelse:\n    # Handle empty array",
                DocSection = "Arrays"
            },

            new()
            {
                Id = "collection_array_error",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"type\s+[`']?array[`']?\s+does\s+not\s+have|cannot\s+use.*on\s+array|array\s+type\s+error", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Array Type Error",
                HumanExplanation = "You're trying to use an operation or method that doesn't exist on the array type. Arrays in Verse have a specific API.",
                Fix = "Use correct array operations: .Length for count, [] for indexed access (failable), 'for' for iteration, '+' for concatenation.",
                CodeExample = "# Common array operations:\nvar Items : []int = array{1, 2, 3}\nCount := Items.Length\n\n# Add element (creates new array):\nset Items = Items + array{4}\n\n# Safe access:\nif (First := Items[0]):\n    Print(\"{First}\")\n\n# Iteration:\nfor (Item : Items):\n    Print(\"{Item}\")",
                DocSection = "Arrays"
            },

            new()
            {
                Id = "collection_map_error",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"map\s+type\s+error|cannot\s+use.*on\s+map|type\s+[`']?\[.*\].*[`']?\s+does\s+not", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Map Type Error",
                HumanExplanation = "You're using an invalid operation on a map type. Maps in Verse use [key_type]value_type syntax and lookups are failable.",
                Fix = "Use the correct map syntax. Lookups require a failable context. Use 'if (set Map[Key] = Value)' for insertion.",
                CodeExample = "# Map declaration:\nvar Scores : [string]int = map{}\n\n# Insert/update:\nif (set Scores[\"Player1\"] = 100) {}\n\n# Lookup (failable):\nif (PlayerScore := Scores[\"Player1\"]):\n    Print(\"Score: {PlayerScore}\")",
                DocSection = "Maps"
            },

            // ═══════════════════════════════════════════════════════════════
            // AMBIGUITY / OVERLOAD ERRORS
            // ═══════════════════════════════════════════════════════════════

            new()
            {
                Id = "ambiguous_call",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Aa]mbiguous\s+(?:call|reference|overload)|multiple\s+(?:overloads|functions)\s+match", RegexOptions.Compiled),
                Title = "Ambiguous Function Call",
                HumanExplanation = "Multiple function overloads match the given arguments, and the compiler cannot determine which one to use. This happens when argument types are compatible with more than one overload.",
                Fix = "Add explicit type annotations to disambiguate. Cast arguments to the specific type expected by the overload you want.",
                CodeExample = "# Before (broken):\nPrint(0)  # Ambiguous: multiple overloads match\n\n# After (fixed):\nMyVal : int = 0\nPrint(\"{MyVal}\")  # Use string interpolation",
                DocSection = "Functions"
            },

            // ═══════════════════════════════════════════════════════════════
            // VERSE-SPECIFIC / UEFN-SPECIFIC ERRORS
            // ═══════════════════════════════════════════════════════════════

            new()
            {
                Id = "verse_editable_type",
                Category = VerseErrorCategory.Device,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"@editable.*(?:invalid|unsupported)\s+type|(?:invalid|unsupported)\s+type.*@editable|cannot\s+use\s+@editable\s+(?:on|with)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "@editable with Invalid Type",
                HumanExplanation = "The @editable attribute can only be used with types that UEFN can display in its property editor. Not all Verse types are supported — complex custom types, functions, and some generics cannot be edited in the UEFN UI.",
                Fix = "Use supported types for @editable: int, float, string, bool, device references, enums, arrays of simple types, and color/vector types.",
                CodeExample = "# Supported @editable types:\n@editable MyDevice : button_device = button_device{}\n@editable Health : float = 100.0\n@editable Name : string = \"Default\"\n@editable Enabled : logic = true\n@editable SpawnPoints : []player_spawner_device = array{}\n\n# NOT supported:\n# @editable MyFunc : type{} = ...  # Functions not editable\n# @editable Data : my_custom_struct = ...  # Custom structs usually not editable",
                DocSection = "Devices"
            },

            new()
            {
                Id = "verse_creative_device_onbegin",
                Category = VerseErrorCategory.Device,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"OnBegin.*(?:not\s+found|not\s+a\s+member|override)|(?:override|overriding)\s+OnBegin|cannot\s+override\s+OnBegin", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "OnBegin Override Error",
                HumanExplanation = "OnBegin() is the entry point for creative_device classes. It must have the correct signature: OnBegin<override>()<suspends>:void. Missing <override> or <suspends> will cause errors.",
                Fix = "Use the exact signature: OnBegin<override>()<suspends>:void =",
                CodeExample = "# Before (broken):\nmy_device := class(creative_device):\n    OnBegin():void =  # Missing <override> and <suspends>\n        Print(\"Started\")\n\n# After (fixed):\nmy_device := class(creative_device):\n    OnBegin<override>()<suspends>:void =\n        Print(\"Started\")",
                DocSection = "Devices"
            },

            new()
            {
                Id = "verse_event_subscribe",
                Category = VerseErrorCategory.Device,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"Subscribe.*(?:type\s+mismatch|wrong\s+(?:type|signature))|(?:handler|callback)\s+(?:type|signature)\s+(?:mismatch|incorrect)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Event Handler Signature Mismatch",
                HumanExplanation = "The function you're subscribing to an event doesn't match the expected handler signature. Each device event expects a specific callback signature (usually taking an agent? parameter).",
                Fix = "Match the handler signature to what the event expects. Most device events require (Agent : ?agent) : void.",
                CodeExample = "# Before (broken):\nMyButton.InteractedWithEvent.Subscribe(OnPress)\nOnPress() : void =  # Wrong signature!\n    Print(\"Pressed\")\n\n# After (fixed):\nMyButton.InteractedWithEvent.Subscribe(OnPress)\nOnPress(Agent : ?agent) : void =\n    Print(\"Pressed by agent\")\n\n# For awaitable events (in async context):\nOnBegin<override>()<suspends>:void =\n    MyButton.InteractedWithEvent.Await()",
                DocSection = "Devices"
            },

            new()
            {
                Id = "verse_option_type",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"option\s+type\s+(?:error|mismatch)|cannot\s+(?:use|access)\s+option\s+(?:value|type)|expected\s+option|[`']?\?[`']?\s+type\s+error", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Option Type Error",
                HumanExplanation = "Option types (?type) represent values that may or may not exist. You cannot use an option value directly — you must unwrap it first using a failable context.",
                Fix = "Unwrap the option value using 'if' to check whether it has a value before using it.",
                CodeExample = "# Before (broken):\nMaybePlayer : ?player = GetPlayer()\nMaybePlayer.GetName()  # Cannot call directly on option!\n\n# After (fixed):\nif (Player := MaybePlayer?):\n    Name := Player.GetName()\n    Print(\"Player: {Name}\")\nelse:\n    Print(\"No player\")",
                DocSection = "Types"
            },

            new()
            {
                Id = "verse_logic_vs_bool",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"expected\s+[`']?logic[`']?.*found\s+[`']?(?:bool|void)[`']?|[`']?logic[`']?\s+vs\s+[`']?(?:bool|void)[`']?|cannot\s+use.*(?:true|false).*logic", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Logic vs Bool Type Confusion",
                HumanExplanation = "Verse uses 'logic' type (with values 'true' and 'false') instead of 'bool'. Conditions in if-expressions are failable expressions, not boolean checks. Verse's type system treats these differently from traditional boolean logic.",
                Fix = "Use 'logic' type for true/false values. For conditions, use failable expressions rather than boolean comparisons.",
                CodeExample = "# Verse logic type:\nvar IsActive : logic = true\nvar IsReady : logic = false\n\n# Conditional check (failable pattern):\nif (IsActive?):\n    Print(\"Active\")\n\n# NOT like other languages:\n# if (IsActive == true):  # Wrong pattern\n# if (IsActive?):  # Correct Verse pattern",
                DocSection = "Types"
            },

            new()
            {
                Id = "verse_string_interpolation",
                Category = VerseErrorCategory.Syntax,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"(?:invalid|error\s+in)\s+string\s+interpolation|string\s+interpolation.*(?:error|fail|invalid)|cannot\s+interpolate|interpolation.*type", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "String Interpolation Error",
                HumanExplanation = "Verse uses curly braces {expression} inside double-quoted strings for interpolation. The expression inside must be convertible to string. Incorrect syntax or non-stringable types cause errors.",
                Fix = "Use \"{Expression}\" syntax. Ensure the expression inside braces can be converted to string (most primitive types work). For complex types, call a .ToString() or format method.",
                CodeExample = "# Correct string interpolation:\nScore : int = 42\nPrint(\"Score: {Score}\")\n\n# Multiple values:\nName := \"Player\"\nPrint(\"{Name} scored {Score} points\")\n\n# With expressions:\nPrint(\"Double: {Score * 2}\")",
                DocSection = "Syntax"
            },

            new()
            {
                Id = "verse_unreachable_code",
                Category = VerseErrorCategory.Syntax,
                Severity = VerseErrorSeverity.Warning,
                Regex = new Regex(@"[Uu]nreachable\s+code", RegexOptions.Compiled),
                Title = "Unreachable Code",
                HumanExplanation = "Code after a 'return', 'break', or unconditional jump will never execute. The compiler warns about this dead code.",
                Fix = "Remove the unreachable code, or restructure the control flow so the code can actually be reached.",
                CodeExample = "# Before (warning):\nDoSomething():void =\n    return\n    Print(\"Never reached\")  # Unreachable!\n\n# After (fixed):\nDoSomething():void =\n    Print(\"This runs\")\n    return",
                DocSection = "Control Flow"
            },

            // ═══════════════════════════════════════════════════════════════
            // COMMON UEFN WORKFLOW ERRORS
            // ═══════════════════════════════════════════════════════════════

            new()
            {
                Id = "uefn_compile_not_saved",
                Category = VerseErrorCategory.Device,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"file\s+(?:has\s+)?(?:not\s+been\s+saved|is\s+not\s+saved|unsaved)|save\s+(?:the\s+)?file\s+before\s+compil", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "File Not Saved Before Compilation",
                HumanExplanation = "The Verse file hasn't been saved before attempting to compile. UEFN compiles from the saved file on disk, not from the editor buffer.",
                Fix = "Save the file (Ctrl+S) before pressing the Verse compile button or building.",
                CodeExample = "# This is a workflow error, not a code error.\n# Steps:\n# 1. Edit your .verse file\n# 2. Save (Ctrl+S)\n# 3. Then compile (Ctrl+Shift+B or the build button)",
                DocSection = "Syntax"
            },

            new()
            {
                Id = "uefn_module_scope",
                Category = VerseErrorCategory.Scope,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"(?:not\s+)?(?:visible|accessible)\s+(?:from|in)\s+(?:this\s+)?module|module\s+(?:scope|visibility)|cross[- ]module\s+access", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Cross-Module Visibility Error",
                HumanExplanation = "You're trying to access a type, function, or variable from another Verse module that isn't marked as public, or the module hasn't been properly imported.",
                Fix = "Ensure the target is marked <public> in its module. Add the correct 'using' statement to import the module. Module paths in UEFN follow the project folder structure.",
                CodeExample = "# In module A (my_utils.verse):\nusing { /Verse.org/Simulation }\n\nmy_utils_module := module:\n    # Must be <public> to be visible outside\n    <public> Helper()<computes> : int = 42\n\n# In module B (my_device.verse):\nusing { /Verse.org/Simulation }\nusing { my_utils_module }  # Import the module\n\nmy_device := class(creative_device):\n    OnBegin<override>()<suspends>:void =\n        Value := my_utils_module.Helper()",
                DocSection = "Modules"
            },

            new()
            {
                Id = "verse_for_generator",
                Category = VerseErrorCategory.Failable,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"for.*(?:requires|expected)\s+(?:a\s+)?(?:generator|iterable)|cannot\s+iterate\s+over|not\s+(?:iterable|a\s+generator)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "For Loop Iteration Error",
                HumanExplanation = "The 'for' expression expects an iterable (like an array) or a failable filter. You might be trying to iterate over a non-iterable type, or using incorrect for-loop syntax.",
                Fix = "Use 'for (Item : Collection)' for iteration, or 'for (Index := 0..Count-1)' for index ranges. Remember 'for' is also a failable context in Verse.",
                CodeExample = "# Array iteration:\nfor (Item : MyArray):\n    Print(\"{Item}\")\n\n# Index range:\nfor (I := 0..MyArray.Length-1):\n    if (Item := MyArray[I]):\n        Print(\"{I}: {Item}\")\n\n# For as failable context:\nfor:\n    Player := FindPlayer(\"Luke\")?  # Failable\n    Score := GetScore(Player)?      # Also failable\ndo:\n    Print(\"Score: {Score}\")",
                DocSection = "Control Flow"
            },

            new()
            {
                Id = "verse_tuple_type",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"tuple.*(?:type\s+)?(?:error|mismatch)|cannot\s+(?:destructure|unpack)\s+tuple|wrong\s+(?:number\s+of\s+)?tuple\s+elements", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Tuple Type Error",
                HumanExplanation = "Tuple destructuring or construction has a type mismatch. The number of elements or their types don't match what's expected.",
                Fix = "Ensure the number and types of elements match. Use parentheses for tuple construction and destructuring.",
                CodeExample = "# Tuple construction:\nPair : tuple(int, string) = (42, \"hello\")\n\n# Destructuring:\n(MyInt, MyString) := Pair\n\n# Function returning tuple:\nGetCoords() : tuple(float, float) =\n    (10.0, 20.0)\n\n(X, Y) := GetCoords()",
                DocSection = "Types"
            },

            new()
            {
                Id = "verse_where_clause",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"where\s+clause.*(?:not\s+satisfied|error|fail)|constraint.*where.*not\s+(?:met|satisfied)|type.*does\s+not\s+satisfy\s+(?:the\s+)?where", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Where Clause Constraint Not Satisfied",
                HumanExplanation = "A 'where' clause specifies a constraint that a type parameter must satisfy. The type you're using doesn't meet this constraint.",
                Fix = "Ensure your type implements the required interface or extends the required base class specified in the 'where' clause.",
                CodeExample = "# Function with where clause:\nFindMax(Items : []t where t : comparable) : ?t =\n    ...\n\n# Your type must be comparable:\nmy_scored := class<comparable>:\n    Score : int = 0\n    Equals(Other : my_scored)<computes><decides> : void =\n        Score = Other.Score\n    Hash()<computes> : int = Score",
                DocSection = "Types"
            },

            new()
            {
                Id = "verse_set_on_array",
                Category = VerseErrorCategory.Scope,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"cannot\s+(?:set|modify)\s+(?:element\s+(?:of|in)\s+)?(?:an?\s+)?(?:immutable\s+)?array|array\s+(?:element\s+)?(?:is\s+)?(?:not\s+)?(?:mutable|immutable|modifiable)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Cannot Modify Array Element",
                HumanExplanation = "Arrays in Verse are immutable by default. You cannot modify individual elements in-place. To 'change' an array, you must create a new one.",
                Fix = "Declare the array as 'var' and create a new array with the changed element. Or use a map if you need frequent updates by key.",
                CodeExample = "# Before (broken):\nItems : []int = array{1, 2, 3}\nset Items[0] = 10  # Cannot modify array element!\n\n# After (fixed) — rebuild array:\nvar Items : []int = array{1, 2, 3}\n# To change element at index 0:\nvar NewItems : []int = array{}\nfor (I := 0..Items.Length-1):\n    if (I = 0):\n        set NewItems = NewItems + array{10}\n    else if (Item := Items[I]):\n        set NewItems = NewItems + array{Item}\nset Items = NewItems",
                DocSection = "Arrays"
            },

            new()
            {
                Id = "verse_await_outside_suspends",
                Category = VerseErrorCategory.Concurrency,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"[Aa]wait.*(?:outside|not\s+in|requires)\s+(?:a\s+)?(?:<suspends>|suspending|async)|cannot\s+(?:use\s+)?[Aa]wait\s+(?:here|in\s+this)", RegexOptions.Compiled),
                Title = "Await Outside <suspends> Context",
                HumanExplanation = "Await() pauses execution until an event fires or a condition is met. It can only be used inside a <suspends> function because it needs the ability to pause and resume.",
                Fix = "Mark your function as <suspends> to use Await(). For event-driven patterns, consider using Subscribe() instead if you don't need to block.",
                CodeExample = "# Before (broken):\nWaitForButton():void =\n    MyButton.InteractedWithEvent.Await()  # Needs <suspends>\n\n# After (fixed):\nWaitForButton()<suspends>:void =\n    MyButton.InteractedWithEvent.Await()\n    Print(\"Button was pressed!\")\n\n# Or use Subscribe (non-blocking):\nSetupButton():void =\n    MyButton.InteractedWithEvent.Subscribe(OnButtonPressed)\n\nOnButtonPressed(Agent : ?agent):void =\n    Print(\"Pressed!\")",
                DocSection = "Concurrency"
            },

            new()
            {
                Id = "verse_branch_type_mismatch",
                Category = VerseErrorCategory.TypeSystem,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"(?:if|case|branch).*branches?\s+(?:have|return)\s+(?:different|incompatible)\s+types|(?:then|else)\s+branch.*type\s+mismatch|all\s+branches\s+must\s+(?:have|return)\s+(?:the\s+)?same\s+type", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "If/Else Branch Type Mismatch",
                HumanExplanation = "When an if-else expression is used as a value, both branches must produce the same type. The 'if' branch and 'else' branch return different types.",
                Fix = "Ensure both branches return the same type. Convert one branch's value if needed, or change the logic so types match.",
                CodeExample = "# Before (broken):\nResult := if (Condition?):\n    42        # int\nelse:\n    \"none\"    # string — type mismatch!\n\n# After (fixed):\nResult : int = if (Condition?):\n    42\nelse:\n    0  # Same type as then-branch",
                DocSection = "Types"
            },

            new()
            {
                Id = "verse_specifier_conflict",
                Category = VerseErrorCategory.Syntax,
                Severity = VerseErrorSeverity.Error,
                Regex = new Regex(@"conflicting\s+specifiers?|specifier.*(?:conflict|incompatible|duplicate)|duplicate\s+(?:effect\s+)?specifier|cannot\s+combine\s+specifiers?", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Title = "Conflicting Specifiers",
                HumanExplanation = "You've used specifiers that conflict with each other, or duplicated a specifier. Some specifier combinations are invalid in Verse.",
                Fix = "Remove the conflicting or duplicate specifier. Check which specifiers are compatible.",
                CodeExample = "# Before (broken):\n<public><private> MyField : int = 0  # Cannot be both!\n\n# After (fixed):\n<public> MyField : int = 0\n\n# Valid combinations:\n<public><transacts><decides>  # OK\n<override><suspends>          # OK\n<private><transacts>          # OK",
                DocSection = "Access Specifiers"
            },
        };
    }
}

// ─── Result Models ──────────────────────────────────────────────────────────

/// <summary>
/// Result of translating a block of compiler output.
/// </summary>
public class VerseTranslationResult
{
    public int TotalErrors { get; set; }
    public int Recognized { get; set; }
    public int Unrecognized { get; set; }
    public List<TranslatedError> Errors { get; set; } = new();
}

/// <summary>
/// A single translated error with its pattern match (if found).
/// </summary>
public class TranslatedError
{
    public string Raw { get; set; } = "";
    public string? FileName { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public string Message { get; set; } = "";
    public VerseErrorPattern? Translation { get; set; }
}

/// <summary>
/// A single error pattern definition with regex, explanation, and fix.
/// </summary>
public class VerseErrorPattern
{
    public string Id { get; set; } = "";
    public VerseErrorCategory Category { get; set; }
    public VerseErrorSeverity Severity { get; set; }
    public Regex Regex { get; set; } = null!;
    public string Title { get; set; } = "";
    public string HumanExplanation { get; set; } = "";
    public string Fix { get; set; } = "";
    public string CodeExample { get; set; } = "";
    public string? DocSection { get; set; }
}

/// <summary>
/// Parsed error from raw compiler output (before pattern matching).
/// </summary>
public class ParsedCompilerError
{
    public string Raw { get; set; } = "";
    public string? FileName { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public string Message { get; set; } = "";
}

public enum VerseErrorCategory
{
    TypeSystem,
    Failable,
    Effect,
    Scope,
    Device,
    Syntax,
    Concurrency
}

public enum VerseErrorSeverity
{
    Error,
    Warning,
    Info
}
