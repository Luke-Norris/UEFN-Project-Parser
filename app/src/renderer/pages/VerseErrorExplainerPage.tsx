import { useState, useCallback, useMemo } from 'react'
import { VerseHighlighter } from '../components/VerseHighlighter'

// ─── Error Pattern Database ─────────────────────────────────────────────────

interface ErrorPattern {
  pattern: RegExp
  title: string
  severity: 'error' | 'warning' | 'info'
  explanation: string
  fix: string
  example: string
  docSection?: string
}

const ERROR_PATTERNS: ErrorPattern[] = [
  {
    pattern: /This function does not return a value in all cases/i,
    title: 'Missing Return Value',
    severity: 'error',
    explanation:
      'Every code path in your function must return a value. If you have an `if` without an `else`, the function has no return value when the condition is false.',
    fix: 'Add an `else` branch that returns a default value, or restructure to ensure all paths return.',
    example:
      '# Before (broken):\nGetScore():int =\n    if (HasWon?):\n        return 100\n\n# After (fixed):\nGetScore():int =\n    if (HasWon?):\n        return 100\n    else:\n        return 0',
    docSection: 'Functions',
  },
  {
    pattern: /Expected\s+[`']?(\w+)[`']?\s+but\s+found\s+[`']?(\w+)[`']?/i,
    title: 'Type Mismatch',
    severity: 'error',
    explanation:
      'The compiler expected one type but received another. This often happens when passing arguments to functions, assigning to typed variables, or returning the wrong type.',
    fix: 'Check that the value you are using matches the expected type. You may need a conversion function, a cast, or to change the variable type.',
    example:
      '# Before (broken):\nMyHealth : int = 100.0  # float assigned to int\n\n# After (fixed):\nMyHealth : int = 100\n# Or use the correct type:\nMyHealth : float = 100.0',
    docSection: 'Types',
  },
  {
    pattern: /failable\s+expression.*not\s+in\s+(a\s+)?failable\s+context/i,
    title: 'Failable Expression Outside Failable Context',
    severity: 'error',
    explanation:
      'Operations that can fail (like array access, map lookups, or `?` expressions) must be wrapped in a failable context such as `if`, `for`, or a failure block `[]`.',
    fix: 'Wrap the failable expression in an `if` block, or use the failure operator `[]` to provide a default value.',
    example:
      '# Before (broken):\nItem := MyArray[Index]\n\n# After (fixed) — using if:\nif (Item := MyArray[Index]):\n    # use Item here\n\n# Or with default value:\nItem := MyArray[Index] or DefaultItem',
    docSection: 'Failure',
  },
  {
    pattern: /Unknown\s+identifier\s+[`']?(\w+)[`']?/i,
    title: 'Unknown Identifier',
    severity: 'error',
    explanation:
      'The compiler does not recognize this name. It could be a typo, a missing `using` import, or the symbol may not be in scope.',
    fix: 'Check for typos in the name. If it comes from another module, add the appropriate `using` statement at the top of your file.',
    example:
      '# Before (broken):\nusing { /Fortnite.com/Devices }\n# Typo: "Buttn" instead of "Button"\nMyButton : Buttn_Device = ...\n\n# After (fixed):\nusing { /Fortnite.com/Devices }\nMyButton : button_device = ...',
    docSection: 'Modules',
  },
  {
    pattern: /effect\s+mismatch|cannot\s+call.*<decides>.*from.*<computes>/i,
    title: 'Effect Mismatch',
    severity: 'error',
    explanation:
      'Verse functions have effect specifiers like `<decides>`, `<computes>`, `<suspends>`, and `<transacts>`. You cannot call a function with a stronger effect from one with a weaker effect. For example, you cannot call a `<decides>` function from a `<computes>` context.',
    fix: 'Either change your calling function to include the required effect specifier, or wrap the call in the appropriate context (e.g., an `if` for `<decides>`).',
    example:
      '# Before (broken):\nGetItem()<computes> : item =\n    Items.Find(Key)  # Find is <decides>\n\n# After (fixed):\nGetItem()<transacts><decides> : item =\n    Items.Find(Key)\n\n# Or handle failure:\nGetItem()<computes> : item =\n    if (Result := Items.Find(Key)):\n        Result\n    else:\n        DefaultItem',
    docSection: 'Effects',
  },
  {
    pattern: /Cannot\s+modify\s+immutable|cannot\s+assign\s+to\s+(a\s+)?(constant|immutable)/i,
    title: 'Cannot Modify Immutable Variable',
    severity: 'error',
    explanation:
      'By default, Verse variables are immutable (constants). To reassign a variable, it must be declared with `var`, and you must use `set` to change it.',
    fix: 'Declare the variable with `var` and use `set` to modify it.',
    example:
      '# Before (broken):\nScore : int = 0\nScore = 10  # Error: cannot modify\n\n# After (fixed):\nvar Score : int = 0\nset Score = 10',
    docSection: 'Variables',
  },
  {
    pattern: /Ambiguous\s+(call|reference|overload)|multiple\s+(overloads|functions)\s+match/i,
    title: 'Ambiguous Function Call',
    severity: 'error',
    explanation:
      'Multiple function overloads match the given arguments, and the compiler cannot determine which one to use.',
    fix: 'Add explicit type annotations to the arguments to disambiguate, or cast to a specific type.',
    example:
      '# Before (broken):\nPrint(0)  # Ambiguous: Print(int) or Print(float)?\n\n# After (fixed):\nPrint(0 : int)  # Explicit type annotation\n# Or:\nMyVal : int = 0\nPrint(MyVal)',
    docSection: 'Functions',
  },
  {
    pattern: /subscript\s+out\s+of\s+range|index\s+out\s+of\s+(bounds|range)/i,
    title: 'Subscript Out of Range',
    severity: 'error',
    explanation:
      'An array or container access used an index that is outside the valid range. In Verse, array indexing is failable, so this is typically a compile-time warning that you are not handling the failure case.',
    fix: 'Always access arrays within a failable context (`if` or `for`) to handle out-of-range indices gracefully.',
    example:
      '# Before (broken):\nFirstItem := MyArray[0]  # Failable, not handled\n\n# After (fixed):\nif (FirstItem := MyArray[0]):\n    # Safe to use FirstItem\nelse:\n    # Handle empty array',
    docSection: 'Arrays',
  },
  {
    pattern: /missing\s+[`']?using[`']?\s+statement|module\s+[`']?([^`']+)[`']?\s+not\s+found/i,
    title: 'Missing Using Statement',
    severity: 'error',
    explanation:
      'The type or function you are trying to use lives in a module that has not been imported. Verse requires explicit `using` declarations for each module.',
    fix: 'Add the appropriate `using` statement at the top of your file.',
    example:
      '# Common using statements for UEFN:\nusing { /Fortnite.com/Devices }\nusing { /Fortnite.com/Characters }\nusing { /Fortnite.com/UI }\nusing { /Verse.org/Simulation }\nusing { /UnrealEngine.com/Temporary/Diagnostics }\nusing { /UnrealEngine.com/Temporary/SpatialMath }',
    docSection: 'Modules',
  },
  {
    pattern: /Invalid\s+[`']?set[`']?\s+target|cannot\s+set\s+/i,
    title: 'Invalid Set Target',
    severity: 'error',
    explanation:
      'The `set` keyword can only be used on mutable variables (declared with `var`), mutable struct fields, or mutable map/array entries. You cannot `set` a function call result, a temporary, or an immutable binding.',
    fix: 'Ensure the target is a `var` variable or a mutable field. If it is a computed property or function result, store it in a `var` first.',
    example:
      '# Before (broken):\nset GetPosition().X = 10.0  # Cannot set a function result\n\n# After (fixed):\nvar Pos : vector3 = GetPosition()\nset Pos.X = 10.0\nSetPosition(Pos)',
    docSection: 'Variables',
  },
  {
    pattern: /Cannot\s+convert\s+(from\s+)?[`']?(\w+)[`']?\s+to\s+[`']?(\w+)[`']?/i,
    title: 'Cannot Convert Between Types',
    severity: 'error',
    explanation:
      'There is no implicit or explicit conversion between these two types. Verse is strictly typed and does not allow arbitrary casts.',
    fix: 'Use an appropriate conversion function (e.g., `ToString`, `Floor`, `Ceil`) or restructure your code to use the correct type from the start.',
    example:
      '# Before (broken):\nMyString : string = 42  # No implicit int->string\n\n# After (fixed):\nMyString : string = ToString(42)\n\n# float to int:\nMyInt : int = Floor(3.14)',
    docSection: 'Types',
  },
  {
    pattern: /Unreachable\s+code/i,
    title: 'Unreachable Code',
    severity: 'warning',
    explanation:
      'Code after a `return`, `break`, or unconditional jump will never execute. The compiler warns about this dead code.',
    fix: 'Remove the unreachable code, or restructure the control flow so the code can actually be reached.',
    example:
      '# Before (warning):\nDoSomething():void =\n    return\n    Print("Never reached")  # Unreachable\n\n# After (fixed):\nDoSomething():void =\n    Print("This runs")\n    return',
    docSection: 'Control Flow',
  },
  {
    pattern: /Duplicate\s+definition|already\s+defined|redefinition\s+of/i,
    title: 'Duplicate Definition',
    severity: 'error',
    explanation:
      'A variable, function, or type with this name already exists in the same scope. Verse does not allow shadowing within the same scope level.',
    fix: 'Rename one of the duplicates, or if they are meant to be overloads, ensure their parameter types differ.',
    example:
      '# Before (broken):\nScore : int = 0\nScore : int = 10  # Duplicate!\n\n# After (fixed):\nScore : int = 0\nBonusScore : int = 10',
    docSection: 'Variables',
  },
  {
    pattern: /<suspends>.*outside\s+(async|concurrent)|cannot\s+call.*<suspends>|suspends.*not\s+allowed/i,
    title: 'Suspends Outside Async Context',
    severity: 'error',
    explanation:
      'A `<suspends>` function (one that can pause execution, like `Sleep` or `Await`) can only be called from within another `<suspends>` context, or spawned with `spawn`.',
    fix: 'Mark the calling function as `<suspends>`, or use `spawn` to run the suspending function concurrently.',
    example:
      '# Before (broken):\nOnBegin():void =\n    Sleep(1.0)  # Sleep is <suspends>\n\n# After (fixed) — make function suspends:\nOnBegin()<suspends>:void =\n    Sleep(1.0)\n\n# Or use spawn:\nOnBegin():void =\n    spawn { Sleep(1.0) }',
    docSection: 'Concurrency',
  },
  {
    pattern: /type\s+[`']?array[`']?\s+does\s+not\s+have|cannot\s+use.*on\s+array|array\s+type\s+error/i,
    title: 'Array Type Error',
    severity: 'error',
    explanation:
      'You are trying to use an operation or method that does not exist on the array type. Common mistakes include calling methods that exist on other collection types, or using the wrong syntax for array operations.',
    fix: 'Check the Verse array API. Common operations: `array.Length`, indexing with `[]` (failable), `for` iteration. Use `Array` helpers for transformations.',
    example:
      '# Common array operations:\nvar Items : []int = array{1, 2, 3}\nCount := Items.Length\n\n# Add element (creates new array):\nset Items = Items + array{4}\n\n# Safe access:\nif (First := Items[0]):\n    Print("First: {First}")',
    docSection: 'Arrays',
  },
  {
    pattern: /map\s+type\s+error|cannot\s+use.*on\s+map|type\s+[`']?\[.*\].*[`']?\s+does\s+not/i,
    title: 'Map Type Error',
    severity: 'error',
    explanation:
      'You are using an invalid operation on a map type. Maps in Verse use `[key_type]value_type` syntax and lookups are failable.',
    fix: 'Use the correct map syntax. Lookups require a failable context. Insertion uses concatenation.',
    example:
      '# Map declaration:\nvar Scores : [string]int = map{}\n\n# Insert/update:\nif (set Scores["Player1"] = 100) {}\n\n# Lookup (failable):\nif (PlayerScore := Scores["Player1"]):\n    Print("Score: {PlayerScore}")',
    docSection: 'Maps',
  },
  {
    pattern: /field\s+[`']?(\w+)[`']?\s+not\s+found|struct.*does\s+not\s+have\s+(member|field)/i,
    title: 'Struct Field Not Found',
    severity: 'error',
    explanation:
      'The struct or class you are accessing does not have a field with that name. This could be a typo, or the field may be private, or it may be defined on a different type.',
    fix: 'Check the struct definition for the correct field name. If the field is on a parent class, ensure you are using the right type. Check access specifiers.',
    example:
      '# If the struct is:\nplayer_data := struct:\n    Name : string\n    Score : int\n\n# Before (broken):\nData.Points  # No field "Points"\n\n# After (fixed):\nData.Score  # Correct field name',
    docSection: 'Structs',
  },
  {
    pattern: /interface.*not\s+implemented|does\s+not\s+implement|missing\s+implementation/i,
    title: 'Interface Not Implemented',
    severity: 'error',
    explanation:
      'Your class claims to implement an interface but is missing one or more required methods. All methods defined in the interface must be implemented.',
    fix: 'Add the missing method implementations to your class. Check the interface definition for the exact signatures required.',
    example:
      '# Interface:\ndamageable := interface:\n    TakeDamage(Amount:float):void\n    GetHealth():float\n\n# Before (broken):\nmy_actor := class(damageable):\n    TakeDamage(Amount:float):void = {}\n    # Missing GetHealth!\n\n# After (fixed):\nmy_actor := class(damageable):\n    TakeDamage(Amount:float):void = {}\n    GetHealth():float = 100.0',
    docSection: 'Interfaces',
  },
  {
    pattern: /access\s+(specifier\s+)?violation|cannot\s+access\s+(private|protected)|not\s+accessible/i,
    title: 'Access Specifier Violation',
    severity: 'error',
    explanation:
      'You are trying to access a member that is marked `<private>` or `<protected>` from outside its allowed scope. Private members can only be accessed within the same class, protected within the class hierarchy.',
    fix: 'If you need external access, change the specifier to `<public>`. Otherwise, provide a public getter/setter method.',
    example:
      '# Before (broken):\nmy_class := class:\n    <private> Secret : int = 42\n\n# External code:\nObj.Secret  # Access violation!\n\n# After (fixed) — add a getter:\nmy_class := class:\n    <private> Secret : int = 42\n    GetSecret()<computes> : int = Secret',
    docSection: 'Access Specifiers',
  },
  {
    pattern: /Expected\s+indentation|indentation\s+error|unexpected\s+indent/i,
    title: 'Indentation Error',
    severity: 'error',
    explanation:
      'Verse uses significant whitespace (like Python). Incorrect indentation breaks the block structure. Mixing tabs and spaces can also cause this.',
    fix: 'Use consistent 4-space indentation. Ensure all lines in a block are aligned. Do not mix tabs and spaces.',
    example:
      '# Before (broken):\nif (Condition?):\nDoSomething()  # Not indented!\n\n# After (fixed):\nif (Condition?):\n    DoSomething()  # 4-space indent',
    docSection: 'Syntax',
  },
  {
    pattern: /Expected\s+[`']?:[`']?\s+in|missing\s+type\s+annotation|type\s+annotation\s+required/i,
    title: 'Missing Type Annotation',
    severity: 'error',
    explanation:
      'Verse requires explicit type annotations in many contexts, including function parameters, return types, and class fields. The compiler cannot always infer types.',
    fix: 'Add the type annotation using the `: type` syntax.',
    example:
      '# Before (broken):\nMyFunc(X, Y) = X + Y  # Missing types\n\n# After (fixed):\nMyFunc(X : int, Y : int) : int = X + Y',
    docSection: 'Types',
  },
  {
    pattern: /Recursive\s+type|circular\s+(reference|dependency|type)/i,
    title: 'Recursive Type Error',
    severity: 'error',
    explanation:
      'A type refers to itself directly or through a chain of other types, creating an infinite size. Verse does not allow directly recursive value types.',
    fix: 'Use `option` or a reference type to break the recursion. For tree structures, make the recursive field optional.',
    example:
      '# Before (broken):\ntree_node := struct:\n    Value : int\n    Children : []tree_node  # Infinite size!\n\n# After (fixed):\ntree_node := struct:\n    Value : int\n    Children : []?tree_node  # Optional breaks recursion',
    docSection: 'Types',
  },
  {
    pattern: /Cannot\s+use\s+[`']?self[`']?\s+before|self.*not\s+initialized/i,
    title: 'Self Used Before Initialization',
    severity: 'error',
    explanation:
      'You are referencing `Self` in a constructor or initializer before all fields have been assigned. All fields must be initialized before `Self` can be used.',
    fix: 'Reorder field initializations so that all fields are set before any reference to `Self`.',
    example:
      '# Before (broken):\nmy_class := class:\n    Other : my_class = Self  # Self not ready yet\n    Value : int = 0\n\n# After (fixed):\nmy_class := class:\n    Value : int = 0\n    # Reference Self only after construction',
    docSection: 'Classes',
  },
  {
    pattern: /Unexpected\s+token|syntax\s+error|parse\s+error/i,
    title: 'Syntax Error',
    severity: 'error',
    explanation:
      'The compiler encountered something it did not expect at this position. Common causes: missing parentheses, extra commas, wrong operator, or mismatched brackets.',
    fix: 'Check the line for missing or extra punctuation. Ensure parentheses and brackets are balanced. Verify operator usage.',
    example:
      '# Common syntax fixes:\n# Missing closing paren:\nif (X > 0:   # Bad\nif (X > 0):  # Good\n\n# Extra comma:\narray{1, 2, 3,}  # Bad (trailing comma)\narray{1, 2, 3}   # Good',
    docSection: 'Syntax',
  },
  {
    pattern: /Cannot\s+spawn|spawn.*requires|spawn.*<suspends>/i,
    title: 'Invalid Spawn Usage',
    severity: 'error',
    explanation:
      'The `spawn` expression requires a `<suspends>` function or block. You may also need to be within an appropriate async context to use `spawn`.',
    fix: 'Ensure the expression inside `spawn { }` calls a `<suspends>` function. The enclosing function may also need the `<suspends>` effect.',
    example:
      '# Before (broken):\nspawn { DoSyncWork() }  # Not a suspends function\n\n# After (fixed):\nMyAsync()<suspends>:void =\n    Sleep(1.0)\n    DoWork()\n\nOnBegin()<suspends>:void =\n    spawn { MyAsync() }',
    docSection: 'Concurrency',
  },
]

// ─── Error Parsing ──────────────────────────────────────────────────────────

interface ParsedError {
  raw: string
  lineNumber?: number
  fileName?: string
  message: string
}

interface ExplainedError {
  parsed: ParsedError
  match: ErrorPattern | null
}

function parseErrors(input: string): ParsedError[] {
  const lines = input.split('\n')
  const errors: ParsedError[] = []
  let currentError: string[] = []

  function flushError() {
    if (currentError.length === 0) return
    const raw = currentError.join('\n').trim()
    if (!raw) return

    // Try to extract file:line pattern
    // Patterns: "file.verse(42)" or "file.verse:42:" or "file.verse(42,10)" or just error message
    const fileLineMatch = raw.match(
      /([^\s(]+\.verse)[\s(:]+(\d+)/i
    )
    const lineNumberOnly = raw.match(/line\s+(\d+)/i)

    errors.push({
      raw,
      fileName: fileLineMatch?.[1],
      lineNumber: fileLineMatch
        ? parseInt(fileLineMatch[2])
        : lineNumberOnly
          ? parseInt(lineNumberOnly[1])
          : undefined,
      message: raw,
    })
    currentError = []
  }

  for (const line of lines) {
    const trimmed = line.trim()
    if (!trimmed) {
      flushError()
      continue
    }

    // Common error line patterns that start a new error
    const isErrorStart =
      /^(error|warning|Error|Warning|ERR|WARN)\s*[:]/i.test(trimmed) ||
      /\.verse[\s(:]+\d+/i.test(trimmed) ||
      /^(.*\.verse)\(\d+/.test(trimmed)

    if (isErrorStart && currentError.length > 0) {
      flushError()
    }
    currentError.push(trimmed)
  }
  flushError()

  return errors
}

function explainErrors(parsed: ParsedError[]): ExplainedError[] {
  return parsed.map((p) => {
    const match = ERROR_PATTERNS.find((ep) => ep.pattern.test(p.message))
    return { parsed: p, match }
  })
}

// ─── Severity Badge ─────────────────────────────────────────────────────────

function SeverityBadge({ severity }: { severity: 'error' | 'warning' | 'info' }) {
  const config = {
    error: { label: 'Error', color: 'text-red-400 bg-red-400/10 border-red-400/20' },
    warning: { label: 'Warning', color: 'text-yellow-400 bg-yellow-400/10 border-yellow-400/20' },
    info: { label: 'Info', color: 'text-blue-400 bg-blue-400/10 border-blue-400/20' },
  }
  const c = config[severity]
  return (
    <span className={`inline-flex items-center px-1.5 py-0.5 rounded text-[9px] font-semibold border ${c.color}`}>
      {c.label}
    </span>
  )
}

// ─── Verse Reference Link Map ───────────────────────────────────────────────

const DOC_SECTION_LINKS: Record<string, string> = {
  Functions: 'https://dev.epicgames.com/documentation/en-us/uefn/verse-language-reference#functions',
  Types: 'https://dev.epicgames.com/documentation/en-us/uefn/verse-language-reference#types',
  Failure: 'https://dev.epicgames.com/documentation/en-us/uefn/verse-language-reference#failure',
  Modules: 'https://dev.epicgames.com/documentation/en-us/uefn/verse-language-reference#modules',
  Effects: 'https://dev.epicgames.com/documentation/en-us/uefn/verse-language-reference#effects',
  Variables: 'https://dev.epicgames.com/documentation/en-us/uefn/verse-language-reference#variables',
  Concurrency: 'https://dev.epicgames.com/documentation/en-us/uefn/verse-language-reference#concurrency',
  Arrays: 'https://dev.epicgames.com/documentation/en-us/uefn/verse-language-reference#arrays',
  Maps: 'https://dev.epicgames.com/documentation/en-us/uefn/verse-language-reference#maps',
  Structs: 'https://dev.epicgames.com/documentation/en-us/uefn/verse-language-reference#structs',
  Classes: 'https://dev.epicgames.com/documentation/en-us/uefn/verse-language-reference#classes',
  Interfaces: 'https://dev.epicgames.com/documentation/en-us/uefn/verse-language-reference#interfaces',
  'Access Specifiers': 'https://dev.epicgames.com/documentation/en-us/uefn/verse-language-reference#specifiers',
  Syntax: 'https://dev.epicgames.com/documentation/en-us/uefn/verse-language-reference#expressions',
  'Control Flow': 'https://dev.epicgames.com/documentation/en-us/uefn/verse-language-reference#control-flow',
}

// ─── Component ──────────────────────────────────────────────────────────────

export function VerseErrorExplainerPage() {
  const [input, setInput] = useState('')
  const [results, setResults] = useState<ExplainedError[]>([])
  const [hasExplained, setHasExplained] = useState(false)
  const [copiedIdx, setCopiedIdx] = useState<number | null>(null)

  const handleExplain = useCallback(() => {
    const parsed = parseErrors(input)
    const explained = explainErrors(parsed)
    setResults(explained)
    setHasExplained(true)
  }, [input])

  const handleCopyFix = useCallback(
    async (idx: number, example: string) => {
      await navigator.clipboard.writeText(example)
      setCopiedIdx(idx)
      setTimeout(() => setCopiedIdx(null), 1500)
    },
    []
  )

  const handleClear = useCallback(() => {
    setInput('')
    setResults([])
    setHasExplained(false)
  }, [])

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
        handleExplain()
      }
    },
    [handleExplain]
  )

  const { matchedCount, unknownCount } = useMemo(() => {
    const matched = results.filter((r) => r.match !== null).length
    return { matchedCount: matched, unknownCount: results.length - matched }
  }, [results])

  return (
    <div className="flex-1 flex bg-fn-darker overflow-hidden">
      {/* Left Panel — Input */}
      <div className="w-[420px] shrink-0 flex flex-col border-r border-fn-border">
        <div className="px-4 py-3 border-b border-fn-border">
          <h1 className="text-sm font-semibold text-white">Verse Error Explainer</h1>
          <p className="text-[10px] text-gray-500 mt-0.5">
            Paste Verse compile errors to get plain-English explanations
          </p>
        </div>

        <div className="flex-1 p-3 flex flex-col gap-3 overflow-hidden">
          <textarea
            className="flex-1 bg-fn-dark border border-fn-border rounded-lg p-3 text-[11px] text-gray-300 font-mono resize-none focus:outline-none focus:border-fn-rare/40 placeholder-gray-600 leading-relaxed"
            placeholder={`Paste your Verse compile errors here...\n\nExamples:\n  error: This function does not return a value in all cases\n  my_device.verse(42): Unknown identifier 'MyVar'\n  Expected 'int' but found 'float'`}
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            spellCheck={false}
          />

          <div className="flex items-center gap-2">
            <button
              onClick={handleExplain}
              disabled={!input.trim()}
              className="flex-1 flex items-center justify-center gap-2 px-4 py-2 text-[11px] font-medium text-white bg-fn-rare/20 border border-fn-rare/30 rounded-lg hover:bg-fn-rare/30 transition-colors disabled:opacity-30 disabled:cursor-not-allowed"
            >
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path d="M9.663 17h4.673M12 3v1m6.364 1.636l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z" />
              </svg>
              Explain Errors
            </button>
            {input.trim() && (
              <button
                onClick={handleClear}
                className="px-3 py-2 text-[10px] text-gray-400 hover:text-white bg-fn-panel border border-fn-border rounded-lg hover:bg-white/[0.06] transition-colors"
              >
                Clear
              </button>
            )}
          </div>

          <p className="text-[9px] text-gray-600 text-center">
            Ctrl+Enter to explain
          </p>
        </div>
      </div>

      {/* Right Panel — Results */}
      <div className="flex-1 flex flex-col overflow-hidden">
        <div className="px-4 py-3 border-b border-fn-border flex items-center justify-between">
          <div>
            <h2 className="text-[11px] font-semibold text-gray-400 uppercase tracking-wider">
              Explanations
            </h2>
            {hasExplained && (
              <p className="text-[10px] text-gray-600 mt-0.5">
                {results.length} error{results.length !== 1 ? 's' : ''} found
                {matchedCount > 0 && (
                  <span className="text-green-400"> &middot; {matchedCount} recognized</span>
                )}
                {unknownCount > 0 && (
                  <span className="text-yellow-400"> &middot; {unknownCount} unrecognized</span>
                )}
              </p>
            )}
          </div>
        </div>

        <div className="flex-1 overflow-y-auto p-4 space-y-3">
          {!hasExplained && (
            <div className="flex-1 flex items-center justify-center h-full">
              <div className="text-center py-16">
                <svg
                  className="w-10 h-10 mx-auto mb-3 text-gray-700"
                  fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}
                >
                  <path d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4.5c-.77-.833-2.694-.833-3.464 0L3.34 16.5c-.77.833.192 2.5 1.732 2.5z" />
                </svg>
                <p className="text-[11px] text-gray-600">
                  Paste errors on the left and click Explain
                </p>
                <p className="text-[10px] text-gray-700 mt-1">
                  Supports 25+ common Verse error patterns
                </p>
              </div>
            </div>
          )}

          {hasExplained && results.length === 0 && (
            <div className="text-center py-16">
              <p className="text-[11px] text-gray-500">No errors could be parsed from the input.</p>
              <p className="text-[10px] text-gray-600 mt-1">
                Try pasting the full compiler output.
              </p>
            </div>
          )}

          {results.map((result, idx) => (
            <ErrorExplanationCard
              key={idx}
              index={idx}
              result={result}
              copied={copiedIdx === idx}
              onCopyFix={(example) => handleCopyFix(idx, example)}
            />
          ))}
        </div>
      </div>
    </div>
  )
}

// ─── Error Explanation Card ─────────────────────────────────────────────────

function ErrorExplanationCard({
  index,
  result,
  copied,
  onCopyFix,
}: {
  index: number
  result: ExplainedError
  copied: boolean
  onCopyFix: (example: string) => void
}) {
  const [expanded, setExpanded] = useState(true)

  if (!result.match) {
    // Unrecognized error
    return (
      <div className="bg-fn-panel border border-fn-border rounded-lg overflow-hidden">
        <div className="flex items-start gap-3 p-3">
          <span className="text-gray-600 shrink-0 mt-0.5">
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path d="M8.228 9c.549-1.165 2.03-2 3.772-2 2.21 0 4 1.343 4 3 0 1.4-1.278 2.575-3.006 2.907-.542.104-.994.54-.994 1.093m0 3h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          </span>
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 mb-1">
              <span className="text-[10px] font-semibold text-gray-500">#{index + 1}</span>
              <span className="text-[10px] text-gray-600 bg-gray-600/10 px-1.5 py-0.5 rounded border border-gray-600/20">
                Unrecognized
              </span>
            </div>
            <p className="text-[11px] text-gray-300 font-mono break-all">{result.parsed.raw}</p>
            <p className="text-[10px] text-gray-600 mt-2">
              This error pattern is not in the database. Check the Verse documentation or forums for this specific error.
            </p>
          </div>
        </div>
      </div>
    )
  }

  const { match, parsed } = result
  const docLink = match.docSection ? DOC_SECTION_LINKS[match.docSection] : null

  return (
    <div className="bg-fn-panel border border-fn-border rounded-lg overflow-hidden">
      {/* Header */}
      <button
        className="w-full flex items-start gap-3 p-3 hover:bg-white/[0.02] transition-colors text-left"
        onClick={() => setExpanded(!expanded)}
      >
        <span className={`shrink-0 mt-0.5 ${
          match.severity === 'error' ? 'text-red-400' :
          match.severity === 'warning' ? 'text-yellow-400' : 'text-blue-400'
        }`}>
          {match.severity === 'error' ? (
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M6 18L18 6M6 6l12 12" />
            </svg>
          ) : match.severity === 'warning' ? (
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4.5c-.77-.833-2.694-.833-3.464 0L3.34 16.5c-.77.833.192 2.5 1.732 2.5z" />
            </svg>
          ) : (
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          )}
        </span>
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 mb-1">
            <span className="text-[10px] font-semibold text-gray-500">#{index + 1}</span>
            <SeverityBadge severity={match.severity} />
            <span className="text-[11px] font-semibold text-white">{match.title}</span>
          </div>
          {parsed.fileName && (
            <p className="text-[10px] text-gray-600 font-mono truncate">
              {parsed.fileName}
              {parsed.lineNumber != null && `:${parsed.lineNumber}`}
            </p>
          )}
        </div>
        <svg
          className={`w-4 h-4 text-gray-600 shrink-0 transition-transform ${expanded ? '' : '-rotate-90'}`}
          fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}
        >
          <path d="M19 9l-7 7-7-7" />
        </svg>
      </button>

      {/* Expanded Content */}
      {expanded && (
        <div className="border-t border-fn-border/50">
          {/* Raw error */}
          <div className="px-3 py-2 bg-fn-darker/50">
            <p className="text-[10px] text-gray-500 font-mono break-all leading-relaxed">{parsed.raw}</p>
          </div>

          {/* Explanation */}
          <div className="p-3 space-y-3">
            <div>
              <h4 className="text-[9px] font-semibold text-gray-500 uppercase tracking-wider mb-1">
                What went wrong
              </h4>
              <p className="text-[11px] text-gray-300 leading-relaxed">{match.explanation}</p>
            </div>

            <div>
              <h4 className="text-[9px] font-semibold text-gray-500 uppercase tracking-wider mb-1">
                How to fix
              </h4>
              <p className="text-[11px] text-gray-300 leading-relaxed">{match.fix}</p>
            </div>

            {/* Code Example */}
            <div>
              <div className="flex items-center justify-between mb-1">
                <h4 className="text-[9px] font-semibold text-gray-500 uppercase tracking-wider">
                  Example
                </h4>
                <button
                  onClick={(e) => {
                    e.stopPropagation()
                    onCopyFix(match.example)
                  }}
                  className={`flex items-center gap-1 px-2 py-0.5 text-[9px] rounded border transition-colors ${
                    copied
                      ? 'text-green-400 bg-green-400/10 border-green-400/20'
                      : 'text-gray-500 hover:text-white bg-fn-darker border-fn-border hover:border-fn-border/80'
                  }`}
                >
                  {copied ? (
                    <>
                      <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                        <path d="M5 13l4 4L19 7" />
                      </svg>
                      Copied
                    </>
                  ) : (
                    <>
                      <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                        <path d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z" />
                      </svg>
                      Copy Fix
                    </>
                  )}
                </button>
              </div>
              <div className="rounded-lg border border-fn-border overflow-hidden max-h-[200px]">
                <VerseHighlighter source={match.example} fontSize={11} />
              </div>
            </div>

            {/* Doc Link */}
            {docLink && (
              <div className="flex items-center gap-1.5">
                <svg className="w-3 h-3 text-gray-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
                </svg>
                <span className="text-[10px] text-gray-600">
                  Verse Reference:{' '}
                  <span className="text-blue-400 hover:text-blue-300 cursor-pointer">
                    {match.docSection}
                  </span>
                </span>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
