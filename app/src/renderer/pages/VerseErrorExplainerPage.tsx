import { useState, useCallback, useMemo } from 'react'
import { VerseHighlighter } from '../components/VerseHighlighter'

// ─── Error Pattern Database ─────────────────────────────────────────────────

type ErrorCategory = 'type_system' | 'failable' | 'effect' | 'scope' | 'device' | 'syntax' | 'concurrency'

interface ErrorPattern {
  pattern: RegExp
  title: string
  severity: 'error' | 'warning' | 'info'
  category: ErrorCategory
  explanation: string
  fix: string
  example: string
  docSection?: string
}

const ERROR_PATTERNS: ErrorPattern[] = [
  // ═══════════════════════════════════════════════════════════════════════
  // TYPE SYSTEM ERRORS
  // ═══════════════════════════════════════════════════════════════════════
  {
    pattern: /does\s+not\s+return\s+a?\s*value\s+in\s+all\s+cases|not\s+all\s+(?:code\s+)?paths\s+return/i,
    title: 'Missing Return Value',
    severity: 'error',
    category: 'type_system',
    explanation:
      'Every code path in your function must produce a value of the declared return type. If you have an `if` without an `else`, the function has no return value when the condition is false.',
    fix: 'Add an `else` branch that returns a default value, or restructure so all paths return. In Verse, the last expression in a block is the return value.',
    example:
      '# Before (broken):\nGetScore():int =\n    if (HasWon?):\n        100\n\n# After (fixed):\nGetScore():int =\n    if (HasWon?):\n        100\n    else:\n        0',
    docSection: 'Functions',
  },
  {
    pattern: /[Ee]xpected\s+(?:expression\s+of\s+)?type\s+[`']?(\w+)[`']?\s+but\s+found\s+[`']?(\w+)[`']?/,
    title: 'Type Mismatch',
    severity: 'error',
    category: 'type_system',
    explanation:
      'The compiler expected one type but received another. This happens when passing arguments to functions, assigning to typed variables, or returning the wrong type from a function.',
    fix: 'Check that the value matches the expected type. You may need a conversion function (e.g., `Floor()` for float-to-int, string interpolation for int-to-string), or change the variable/parameter type.',
    example:
      '# Before (broken):\nMyHealth : int = 100.0  # float assigned to int\n\n# After (fixed):\nMyHealth : int = 100\n# Or convert:\nMyHealth : int = Floor[100.0]',
    docSection: 'Types',
  },
  {
    pattern: /[Cc]annot\s+convert\s+(?:from\s+)?[`']?(\w+)[`']?\s+to\s+[`']?(\w+)[`']?/,
    title: 'Cannot Convert Between Types',
    severity: 'error',
    category: 'type_system',
    explanation:
      'There is no implicit or explicit conversion between these two types. Verse is strictly typed and does not allow arbitrary casts between unrelated types.',
    fix: 'Use an appropriate conversion function (e.g., `ToString()`, `Floor()`, `Ceil()`, `Round()`) or restructure your code to use the correct type from the start.',
    example:
      '# Before (broken):\nMyString : string = 42  # No implicit int->string\n\n# After (fixed):\nMyString : string = "{42}"\n\n# float to int:\nMyInt : int = Floor[3.14]',
    docSection: 'Types',
  },
  {
    pattern: /[Tt]ype\s+[`']?(\w+)[`']?\s+is\s+not\s+a\s+subtype\s+of\s+[`']?(\w+)[`']?/,
    title: 'Type Is Not a Subtype',
    severity: 'error',
    category: 'type_system',
    explanation:
      'You\'re trying to use a type where a more specific subtype is required. For example, assigning a base class where a derived class is expected.',
    fix: 'Ensure your type inherits from the required parent type. Check the class definition\'s parent type in the parentheses after `class`.',
    example:
      '# Ensure correct inheritance:\nmy_device := class(creative_device):\n    # creative_device is the base for UEFN devices\n    OnBegin<override>()<suspends>:void =\n        Print("Started")',
    docSection: 'Types',
  },
  {
    pattern: /[Pp]arametric\s+type\s+mismatch|[Gg]eneric\s+(?:type\s+)?constraint|type\s+parameter.*(?:does\s+not\s+(?:match|satisfy)|mismatch)/,
    title: 'Parametric Type / Generic Constraint Mismatch',
    severity: 'error',
    category: 'type_system',
    explanation:
      'A generic/parametric type does not satisfy its constraints. The type argument you provided does not meet the requirements specified by the generic type parameter.',
    fix: 'Check the generic constraints on the type/function definition. Ensure your type argument implements the required interfaces or extends the required base type.',
    example:
      '# If a function requires comparable(t):\nSort(Items : []t where t : comparable) : []t = ...\n\n# Your type must be comparable:\nmy_type := class<comparable>:\n    Value : int\n    Equals(Other : my_type)<computes><decides> : void = ...',
    docSection: 'Types',
  },
  {
    pattern: /option\s+type\s+(?:error|mismatch)|cannot\s+(?:use|access)\s+option\s+(?:value|type)|expected\s+option|[`']?\?[`']?\s+type\s+error/i,
    title: 'Option Type Error',
    severity: 'error',
    category: 'type_system',
    explanation:
      'Option types (`?type`) represent values that may or may not exist. You cannot use an option value directly — you must unwrap it first using a failable context.',
    fix: 'Unwrap the option value using `if` to check whether it has a value before using it.',
    example:
      '# Before (broken):\nMaybePlayer : ?player = GetPlayer()\nMaybePlayer.GetName()  # Cannot call directly on option!\n\n# After (fixed):\nif (Player := MaybePlayer?):\n    Name := Player.GetName()\n    Print("Player: {Name}")\nelse:\n    Print("No player")',
    docSection: 'Types',
  },
  {
    pattern: /expected\s+[`']?logic[`']?.*found\s+[`']?(?:bool|void)[`']?|[`']?logic[`']?\s+vs\s+[`']?(?:bool|void)[`']?|cannot\s+use.*(?:true|false).*logic/i,
    title: 'Logic vs Bool Type Confusion',
    severity: 'error',
    category: 'type_system',
    explanation:
      'Verse uses `logic` type (with values `true` and `false`) instead of `bool`. Conditions in if-expressions are failable expressions, not boolean checks.',
    fix: 'Use `logic` type for true/false values. For conditions, use failable expressions with `?` rather than boolean comparisons.',
    example:
      '# Verse logic type:\nvar IsActive : logic = true\nvar IsReady : logic = false\n\n# Conditional check (failable pattern):\nif (IsActive?):\n    Print("Active")\n\n# NOT like other languages:\n# if (IsActive == true):  # Wrong\n# if (IsActive?):         # Correct',
    docSection: 'Types',
  },
  {
    pattern: /(?:if|case|branch).*branches?\s+(?:have|return)\s+(?:different|incompatible)\s+types|(?:then|else)\s+branch.*type\s+mismatch|all\s+branches\s+must\s+(?:have|return)\s+(?:the\s+)?same\s+type/i,
    title: 'If/Else Branch Type Mismatch',
    severity: 'error',
    category: 'type_system',
    explanation:
      'When an if-else expression is used as a value, both branches must produce the same type. The `if` and `else` branches return different types.',
    fix: 'Ensure both branches return the same type. Convert one branch\'s value if needed.',
    example:
      '# Before (broken):\nResult := if (Condition?):\n    42        # int\nelse:\n    "none"    # string — type mismatch!\n\n# After (fixed):\nResult : int = if (Condition?):\n    42\nelse:\n    0  # Same type as then-branch',
    docSection: 'Types',
  },
  {
    pattern: /tuple.*(?:type\s+)?(?:error|mismatch)|cannot\s+(?:destructure|unpack)\s+tuple|wrong\s+(?:number\s+of\s+)?tuple\s+elements/i,
    title: 'Tuple Type Error',
    severity: 'error',
    category: 'type_system',
    explanation:
      'Tuple destructuring or construction has a type mismatch. The number of elements or their types don\'t match what\'s expected.',
    fix: 'Ensure the number and types of elements match. Use parentheses for tuple construction and destructuring.',
    example:
      '# Tuple construction:\nPair : tuple(int, string) = (42, "hello")\n\n# Destructuring:\n(MyInt, MyString) := Pair\n\n# Function returning tuple:\nGetCoords() : tuple(float, float) =\n    (10.0, 20.0)\n\n(X, Y) := GetCoords()',
    docSection: 'Types',
  },
  {
    pattern: /where\s+clause.*(?:not\s+satisfied|error|fail)|constraint.*where.*not\s+(?:met|satisfied)|type.*does\s+not\s+satisfy\s+(?:the\s+)?where/i,
    title: 'Where Clause Constraint Not Satisfied',
    severity: 'error',
    category: 'type_system',
    explanation:
      'A `where` clause specifies a constraint that a type parameter must satisfy. The type you\'re using doesn\'t meet this constraint.',
    fix: 'Ensure your type implements the required interface or extends the required base class specified in the `where` clause.',
    example:
      '# Function with where clause:\nFindMax(Items : []t where t : comparable) : ?t =\n    ...\n\n# Your type must be comparable:\nmy_scored := class<comparable>:\n    Score : int = 0\n    Equals(Other : my_scored)<computes><decides> : void =\n        Score = Other.Score',
    docSection: 'Types',
  },
  {
    pattern: /[Rr]ecursive\s+type|[Cc]ircular\s+(?:reference|dependency|type)/,
    title: 'Recursive / Circular Type',
    severity: 'error',
    category: 'type_system',
    explanation:
      'A type refers to itself directly or through a chain of other types, creating an infinite-size type. Verse does not allow directly recursive value types.',
    fix: 'Use `option` (?) or a reference type (class) to break the recursion.',
    example:
      '# Before (broken):\ntree_node := struct:\n    Value : int\n    Children : []tree_node  # Infinite size!\n\n# After (fixed) — use class (reference type):\ntree_node := class:\n    Value : int = 0\n    var Children : []tree_node = array{}',
    docSection: 'Types',
  },
  {
    pattern: /[Cc]annot\s+use\s+[`']?[Ss]elf[`']?\s+before|[Ss]elf.*not\s+initialized|use\s+of\s+[`']?[Ss]elf[`']?\s+before\s+(?:all\s+)?fields?\s+(?:are\s+)?initialized/,
    title: 'Self Used Before Initialization',
    severity: 'error',
    category: 'type_system',
    explanation:
      'You\'re referencing `Self` in a constructor or initializer before all fields have been assigned. All fields must be initialized before `Self` can be used.',
    fix: 'Reorder field initializations so all fields are set before any reference to `Self`. Reference `Self` only in methods, not field initializers.',
    example:
      '# Before (broken):\nmy_class := class:\n    Other : my_class = Self  # Self not ready yet!\n    Value : int = 0\n\n# After (fixed):\nmy_class := class:\n    Value : int = 0\n    # Reference Self only in methods, not field initializers',
    docSection: 'Classes',
  },
  {
    pattern: /interface.*not\s+implemented|does\s+not\s+implement|missing\s+implementation/i,
    title: 'Interface Not Implemented',
    severity: 'error',
    category: 'type_system',
    explanation:
      'Your class declares it implements an interface but is missing one or more required methods. All methods defined in the interface must have matching implementations.',
    fix: 'Add the missing method implementations. Check the interface definition for exact signatures (name, parameters, return type, and effects).',
    example:
      '# Interface:\ndamageable := interface:\n    TakeDamage(Amount : float) : void\n    GetHealth() : float\n\n# Before (broken):\nmy_actor := class(damageable):\n    TakeDamage(Amount : float) : void = {}\n    # Missing GetHealth!\n\n# After (fixed):\nmy_actor := class(damageable):\n    TakeDamage(Amount : float) : void = {}\n    GetHealth() : float = 100.0',
    docSection: 'Interfaces',
  },
  {
    pattern: /field\s+[`']?(\w+)[`']?\s+not\s+found|(?:struct|class).*does\s+not\s+have\s+(?:member|field)/i,
    title: 'Struct/Class Field Not Found',
    severity: 'error',
    category: 'type_system',
    explanation:
      'The struct or class does not have a field with that name. This could be a typo, the field may be private, or it may be on a different type.',
    fix: 'Check the struct/class definition for the correct field name. Field names are case-sensitive in Verse.',
    example:
      '# If the struct is:\nplayer_data := struct:\n    Name : string\n    Score : int\n\n# Before (broken):\nData.Points  # No field "Points"\n\n# After (fixed):\nData.Score  # Correct field name',
    docSection: 'Structs',
  },
  {
    pattern: /[Aa]mbiguous\s+(?:call|reference|overload)|multiple\s+(?:overloads|functions)\s+match/,
    title: 'Ambiguous Function Call',
    severity: 'error',
    category: 'type_system',
    explanation:
      'Multiple function overloads match the given arguments, and the compiler cannot determine which one to use.',
    fix: 'Add explicit type annotations to disambiguate. Cast arguments to the specific type expected by the overload you want.',
    example:
      '# Before (broken):\nPrint(0)  # Ambiguous: multiple overloads match\n\n# After (fixed):\nMyVal : int = 0\nPrint("{MyVal}")  # Use string interpolation',
    docSection: 'Functions',
  },
  {
    pattern: /subscript\s+out\s+of\s+range|index\s+out\s+of\s+(?:bounds|range)/i,
    title: 'Subscript / Index Out of Range',
    severity: 'error',
    category: 'type_system',
    explanation:
      'An array or container access used an index outside the valid range. In Verse, array indexing is failable — you must handle the case where the index is invalid.',
    fix: 'Always access arrays within a failable context (`if` or `for`) to handle out-of-range indices.',
    example:
      '# Before (broken):\nFirstItem := MyArray[0]  # Failable, not handled\n\n# After (fixed):\nif (FirstItem := MyArray[0]):\n    # Safe to use FirstItem\nelse:\n    # Handle empty array',
    docSection: 'Arrays',
  },
  {
    pattern: /type\s+[`']?array[`']?\s+does\s+not\s+have|cannot\s+use.*on\s+array|array\s+type\s+error/i,
    title: 'Array Type Error',
    severity: 'error',
    category: 'type_system',
    explanation:
      'You\'re trying to use an operation or method that doesn\'t exist on the array type. Arrays in Verse have a specific API.',
    fix: 'Use correct array operations: `.Length` for count, `[]` for indexed access (failable), `for` for iteration, `+` for concatenation.',
    example:
      '# Common array operations:\nvar Items : []int = array{1, 2, 3}\nCount := Items.Length\n\n# Add element (creates new array):\nset Items = Items + array{4}\n\n# Safe access:\nif (First := Items[0]):\n    Print("{First}")\n\n# Iteration:\nfor (Item : Items):\n    Print("{Item}")',
    docSection: 'Arrays',
  },
  {
    pattern: /map\s+type\s+error|cannot\s+use.*on\s+map|type\s+[`']?\[.*\].*[`']?\s+does\s+not/i,
    title: 'Map Type Error',
    severity: 'error',
    category: 'type_system',
    explanation:
      'You\'re using an invalid operation on a map type. Maps in Verse use `[key_type]value_type` syntax and lookups are failable.',
    fix: 'Use the correct map syntax. Lookups require a failable context. Use `if (set Map[Key] = Value)` for insertion.',
    example:
      '# Map declaration:\nvar Scores : [string]int = map{}\n\n# Insert/update:\nif (set Scores["Player1"] = 100) {}\n\n# Lookup (failable):\nif (PlayerScore := Scores["Player1"]):\n    Print("Score: {PlayerScore}")',
    docSection: 'Maps',
  },

  // ═══════════════════════════════════════════════════════════════════════
  // FAILABLE CONTEXT ERRORS
  // ═══════════════════════════════════════════════════════════════════════
  {
    pattern: /failable\s+expression.*not\s+in\s+(?:a\s+)?failable\s+context|may\s+fail\s+but\s+is\s+not\s+in\s+a\s+failable\s+context/i,
    title: 'Failable Expression Outside Failable Context',
    severity: 'error',
    category: 'failable',
    explanation:
      'Operations that can fail (like array access with `[]`, map lookups, casting, or calling `<decides>` functions) must be wrapped in a failable context. Verse enforces this because these operations might not succeed at runtime.',
    fix: 'Wrap the expression in an `if` block (most common), a `for` loop, or provide a default with the `or` operator.',
    example:
      '# Before (broken):\nItem := MyArray[Index]\n\n# After (fixed) — using if:\nif (Item := MyArray[Index]):\n    # use Item safely here\nelse:\n    # handle missing item\n\n# Or with default value:\nItem := MyArray[Index] or DefaultItem',
    docSection: 'Failure',
  },
  {
    pattern: /might\s+fail.*use\s+[`']?\?[`']?\s+to\s+propagate|propagate\s+failure\s+with\s+\?|add\s+[`']?\?[`']?\s+to\s+propagate/i,
    title: 'Failure Propagation Required',
    severity: 'error',
    category: 'failable',
    explanation:
      'This expression can fail, and you\'re inside a function that is already `<decides>` (failable). You can propagate the failure upward by adding `?` after the call.',
    fix: 'Add `?` after the failable call to propagate failure to the caller. This only works inside `<decides>` functions.',
    example:
      '# Before (broken):\nGetItem()<decides><transacts> : item =\n    Items.Find(Key)  # Can fail, not propagated\n\n# After (fixed):\nGetItem()<decides><transacts> : item =\n    Items.Find(Key)?  # ? propagates failure',
    docSection: 'Failure',
  },
  {
    pattern: /<decides>\s+effect\s+required|requires?\s+<decides>|needs?\s+(?:the\s+)?<decides>\s+effect/i,
    title: '<decides> Effect Required',
    severity: 'error',
    category: 'failable',
    explanation:
      'You\'re calling a failable function or using a failable expression, but your function doesn\'t have the `<decides>` effect. The `<decides>` effect marks a function as one that may fail.',
    fix: 'Add `<decides>` (and usually `<transacts>`) to your function signature. If you don\'t want the function to be failable, wrap the call in `if` instead.',
    example:
      '# Before (broken):\nFindPlayer(Name : string)<computes> : player =\n    PlayerMap[Name]  # Failable, needs <decides>\n\n# After (fixed) — make function failable:\nFindPlayer(Name : string)<decides><transacts> : player =\n    PlayerMap[Name]\n\n# Or handle inline with if:\nFindPlayerSafe(Name : string) : ?player =\n    if (P := PlayerMap[Name]):\n        option{P}\n    else:\n        false',
    docSection: 'Failure',
  },
  {
    pattern: /this\s+call\s+may\s+fail|call.*may\s+fail.*not\s+(?:in\s+)?(?:a\s+)?failable/i,
    title: 'Call May Fail',
    severity: 'error',
    category: 'failable',
    explanation:
      'You\'re calling a function that has the `<decides>` effect (it can fail), but you\'re not in a context that handles failure.',
    fix: 'Wrap the call in `if` to handle success/failure, or add `<decides>` to your own function and use `?` to propagate failure.',
    example:
      '# Before (broken):\nDoSomething():void =\n    Player := FindPlayer("Luke")  # FindPlayer is <decides>\n\n# After (fixed):\nDoSomething():void =\n    if (Player := FindPlayer("Luke")):\n        # Use Player\n    else:\n        Print("Player not found")',
    docSection: 'Failure',
  },
  {
    pattern: /for.*(?:requires|expected)\s+(?:a\s+)?(?:generator|iterable)|cannot\s+iterate\s+over|not\s+(?:iterable|a\s+generator)/i,
    title: 'For Loop Iteration Error',
    severity: 'error',
    category: 'failable',
    explanation:
      'The `for` expression expects an iterable (like an array) or a failable filter. You might be trying to iterate over a non-iterable type.',
    fix: 'Use `for (Item : Collection)` for iteration, or `for (Index := 0..Count-1)` for index ranges. Remember `for` is also a failable context in Verse.',
    example:
      '# Array iteration:\nfor (Item : MyArray):\n    Print("{Item}")\n\n# Index range:\nfor (I := 0..MyArray.Length-1):\n    if (Item := MyArray[I]):\n        Print("{I}: {Item}")\n\n# For as failable context:\nfor:\n    Player := FindPlayer("Luke")?\n    Score := GetScore(Player)?\ndo:\n    Print("Score: {Score}")',
    docSection: 'Control Flow',
  },

  // ═══════════════════════════════════════════════════════════════════════
  // EFFECT SYSTEM ERRORS
  // ═══════════════════════════════════════════════════════════════════════
  {
    pattern: /[Ee]ffect\s+mismatch|cannot\s+call.*<decides>.*<computes>|<computes>.*vs.*<transacts>|incompatible\s+effects?/,
    title: 'Effect Mismatch',
    severity: 'error',
    category: 'effect',
    explanation:
      'Verse functions have effect specifiers (`<computes>`, `<decides>`, `<transacts>`, `<suspends>`) that form a hierarchy. A function with weaker effects cannot call a function with stronger effects.',
    fix: 'Add the required effect to your function, or wrap the call to handle it (e.g., wrap `<decides>` calls in `if`). Effect hierarchy: `<computes>` is weakest, `<decides>` and `<transacts>` are stronger.',
    example:
      '# Before (broken):\nGetItem()<computes> : item =\n    Items.Find(Key)  # Find is <decides><transacts>\n\n# After (fixed) — escalate effects:\nGetItem()<decides><transacts> : item =\n    Items.Find(Key)\n\n# Or handle failure:\nGetItem()<computes> : item =\n    if (Result := Items.Find(Key)):\n        Result\n    else:\n        DefaultItem',
    docSection: 'Effects',
  },
  {
    pattern: /[Ee]xpected\s+<no_rollback>.*<transacts>|<no_rollback>.*incompatible.*<transacts>|cannot.*<transacts>.*<no_rollback>/,
    title: 'Effect Conflict: <no_rollback> vs <transacts>',
    severity: 'error',
    category: 'effect',
    explanation:
      '`<no_rollback>` and `<transacts>` are mutually exclusive effects. `<transacts>` allows rollback on failure, while `<no_rollback>` explicitly forbids it.',
    fix: 'Remove one of the conflicting effects. Most functions that modify variables need `<transacts>`.',
    example:
      '# Before (broken):\nUpdate()<no_rollback><transacts> : void =  # Conflict!\n    set Score += 1\n\n# After (fixed):\nUpdate()<transacts> : void =\n    set Score += 1',
    docSection: 'Effects',
  },
  {
    pattern: /requires?\s+<transacts>|<transacts>\s+effect\s+required|cannot\s+(?:modify|set).*without\s+<transacts>/i,
    title: '<transacts> Effect Required',
    severity: 'error',
    category: 'effect',
    explanation:
      'Modifying mutable variables (using `set`) requires the `<transacts>` effect. Verse needs to track mutations for potential rollback in failable contexts.',
    fix: 'Add `<transacts>` to your function signature.',
    example:
      '# Before (broken):\nvar Score : int = 0\nAddScore(Points : int)<computes> : void =\n    set Score += Points  # Needs <transacts>\n\n# After (fixed):\nAddScore(Points : int)<transacts> : void =\n    set Score += Points',
    docSection: 'Effects',
  },
  {
    pattern: /[Cc]annot\s+call.*<suspends>.*(?:non-suspending|from)|<suspends>.*(?:outside|not\s+allowed|requires)|suspends.*not\s+(?:allowed|available)/,
    title: 'Cannot Call <suspends> from Non-Suspending Context',
    severity: 'error',
    category: 'effect',
    explanation:
      'A `<suspends>` function (one that can pause execution, like `Sleep()` or `Await()`) can only be called from within another `<suspends>` context.',
    fix: 'Either mark the calling function as `<suspends>`, or use `spawn` to run the suspending function concurrently.',
    example:
      '# Before (broken):\nOnBegin():void =\n    Sleep(1.0)  # Sleep is <suspends>\n\n# After (fixed) — make function suspends:\nOnBegin()<suspends>:void =\n    Sleep(1.0)\n\n# Or use spawn:\nOnBegin():void =\n    spawn { MyAsyncLoop() }',
    docSection: 'Concurrency',
  },

  // ═══════════════════════════════════════════════════════════════════════
  // VARIABLE / SCOPE ERRORS
  // ═══════════════════════════════════════════════════════════════════════
  {
    pattern: /[Cc]annot\s+modify\s+immutable|cannot\s+assign\s+to\s+(?:a\s+)?(?:constant|immutable)|cannot\s+set\s+(?:a\s+)?(?:constant|immutable|non-mutable)/,
    title: 'Cannot Modify Immutable Variable',
    severity: 'error',
    category: 'scope',
    explanation:
      'By default, Verse variables are immutable. Once assigned, their value cannot change. To create a mutable variable, declare it with `var` and use `set` to change it.',
    fix: 'Declare the variable with `var` and use `set` to modify it.',
    example:
      '# Before (broken):\nScore : int = 0\nScore = 10  # Cannot modify!\n\n# After (fixed):\nvar Score : int = 0\nset Score = 10',
    docSection: 'Variables',
  },
  {
    pattern: /[Uu]nknown\s+identifier\s+[`']?(\w+)[`']?|[Vv]ariable\s+[`']?(\w+)[`']?\s+(?:is\s+)?not\s+(?:defined|declared)|[`']?(\w+)[`']?\s+is\s+(?:not\s+)?(?:undefined|undeclared)/,
    title: 'Unknown Identifier / Variable Not Defined',
    severity: 'error',
    category: 'scope',
    explanation:
      'The compiler does not recognize this name. Common causes: typo, missing `using` import, or the variable is out of scope.',
    fix: 'Check for typos. If the identifier comes from a Fortnite module, add the appropriate `using` statement.',
    example:
      '# Before (broken):\nusing { /Fortnite.com/Devices }\nMyButton : Buttn_Device = ...  # Typo!\n\n# After (fixed):\nusing { /Fortnite.com/Devices }\nMyButton : button_device = ...  # Correct\n\n# Common using statements:\nusing { /Fortnite.com/Devices }\nusing { /Fortnite.com/Characters }\nusing { /Fortnite.com/UI }\nusing { /Verse.org/Simulation }\nusing { /UnrealEngine.com/Temporary/SpatialMath }',
    docSection: 'Modules',
  },
  {
    pattern: /[Cc]annot\s+shadow\s+variable|shadows?\s+(?:existing\s+)?(?:variable|binding)|already\s+(?:defined|bound)\s+in\s+(?:this\s+)?scope/,
    title: 'Variable Shadowing Not Allowed',
    severity: 'error',
    category: 'scope',
    explanation:
      'Verse does not allow variable shadowing within the same scope level. You cannot declare a new variable with the same name as an existing one in the same block.',
    fix: 'Rename the second variable, or use `set` to modify the existing one.',
    example:
      '# Before (broken):\nScore : int = 0\nif (Condition?):\n    Score : int = 10  # Shadows outer Score!\n\n# After (fixed):\nvar Score : int = 0\nif (Condition?):\n    set Score = 10  # Modifies outer Score',
    docSection: 'Variables',
  },
  {
    pattern: /[Ii]nvalid\s+[`']?set[`']?\s+target|cannot\s+set\s+(?!.*immutable)|[`']?set[`']?\s+requires\s+(?:a\s+)?mutable/,
    title: 'Invalid Set Target',
    severity: 'error',
    category: 'scope',
    explanation:
      'The `set` keyword can only be used on mutable variables (declared with `var`), mutable struct fields, or mutable map/array entries.',
    fix: 'Ensure the target is a `var` variable or a mutable field. Store computed values in a `var` first.',
    example:
      '# Before (broken):\nset GetPosition().X = 10.0  # Cannot set function result\n\n# After (fixed):\nvar Pos : vector3 = GetPosition()\nset Pos = vector3{X := 10.0, Y := Pos.Y, Z := Pos.Z}\nSetPosition(Pos)',
    docSection: 'Variables',
  },
  {
    pattern: /[Dd]uplicate\s+definition|already\s+defined|[Rr]edefinition\s+of/,
    title: 'Duplicate Definition',
    severity: 'error',
    category: 'scope',
    explanation:
      'A variable, function, or type with this name already exists in the same scope.',
    fix: 'Rename one of the duplicates. For function overloads, ensure parameter types differ.',
    example:
      '# Before (broken):\nScore : int = 0\nScore : int = 10  # Duplicate!\n\n# After (fixed):\nScore : int = 0\nBonusScore : int = 10',
    docSection: 'Variables',
  },
  {
    pattern: /access\s+(?:specifier\s+)?violation|cannot\s+access\s+(?:private|protected)|not\s+accessible|[`']?(\w+)[`']?\s+is\s+(?:private|protected)/i,
    title: 'Access Specifier Violation',
    severity: 'error',
    category: 'scope',
    explanation:
      'You\'re trying to access a member marked `<private>` or `<protected>` from outside its allowed scope.',
    fix: 'If you need external access, change the specifier to `<public>`. Otherwise, provide a public getter/setter method.',
    example:
      '# Before (broken):\nmy_class := class:\n    <private> Secret : int = 42\n\n# External code:\nObj.Secret  # Access violation!\n\n# After (fixed) — add a getter:\nmy_class := class:\n    <private> var InternalSecret : int = 42\n    GetSecret()<computes> : int = InternalSecret',
    docSection: 'Access Specifiers',
  },
  {
    pattern: /(?:not\s+)?(?:visible|accessible)\s+(?:from|in)\s+(?:this\s+)?module|module\s+(?:scope|visibility)|cross[- ]module\s+access/i,
    title: 'Cross-Module Visibility Error',
    severity: 'error',
    category: 'scope',
    explanation:
      'You\'re trying to access a type, function, or variable from another Verse module that isn\'t marked as `<public>`, or the module hasn\'t been properly imported.',
    fix: 'Ensure the target is marked `<public>` in its module. Add the correct `using` statement.',
    example:
      '# In module A (my_utils.verse):\n<public> Helper()<computes> : int = 42\n\n# In module B (my_device.verse):\nusing { my_utils_module }\n\nmy_device := class(creative_device):\n    OnBegin<override>()<suspends>:void =\n        Value := Helper()',
    docSection: 'Modules',
  },
  {
    pattern: /cannot\s+(?:set|modify)\s+(?:element\s+(?:of|in)\s+)?(?:an?\s+)?(?:immutable\s+)?array|array\s+(?:element\s+)?(?:is\s+)?(?:not\s+)?(?:mutable|immutable|modifiable)/i,
    title: 'Cannot Modify Array Element',
    severity: 'error',
    category: 'scope',
    explanation:
      'Arrays in Verse are immutable by default. You cannot modify individual elements in-place. To "change" an array, you must create a new one.',
    fix: 'Declare the array as `var` and rebuild it with changes. Or use a map if you need frequent updates by key.',
    example:
      '# Before (broken):\nItems : []int = array{1, 2, 3}\nset Items[0] = 10  # Cannot modify!\n\n# After (fixed) — rebuild array:\nvar Items : []int = array{1, 2, 3}\n# To replace, rebuild with new values:\nvar NewItems : []int = array{}\nfor (I := 0..Items.Length-1):\n    if (I = 0):\n        set NewItems = NewItems + array{10}\n    else if (Item := Items[I]):\n        set NewItems = NewItems + array{Item}\nset Items = NewItems',
    docSection: 'Arrays',
  },

  // ═══════════════════════════════════════════════════════════════════════
  // DEVICE / API ERRORS
  // ═══════════════════════════════════════════════════════════════════════
  {
    pattern: /[Nn]o\s+such\s+member\s+[`']?(\w+)[`']?\s+on\s+(?:type\s+)?[`']?(\w+)[`']?|[`']?(\w+)[`']?\s+does\s+not\s+have\s+(?:a\s+)?member\s+[`']?(\w+)[`']?|[`']?(\w+)[`']?\s+is\s+not\s+a\s+member\s+of\s+[`']?(\w+)[`']?/,
    title: 'No Such Member on Type',
    severity: 'error',
    category: 'device',
    explanation:
      'You\'re trying to access a member that doesn\'t exist on this type. Could be a typo, or the member might be on a different type or require a specific `using` import.',
    fix: 'Check the device/class documentation for the correct member name. Names are case-sensitive and use `snake_case` for device APIs.',
    example:
      '# Before (broken):\nMyButton.OnActivated.Subscribe(OnPressed)  # Wrong!\n\n# After (fixed):\nMyButton.InteractedWithEvent.Subscribe(OnPressed)\n\n# Common device events:\n# button_device: InteractedWithEvent\n# trigger_device: TriggeredEvent\n# item_granter_device: ItemGrantedEvent',
    docSection: 'Devices',
  },
  {
    pattern: /[Ww]rong\s+number\s+of\s+arguments|too\s+(?:many|few)\s+arguments|expected\s+(\d+)\s+argument|incorrect\s+(?:number\s+of\s+)?arguments/,
    title: 'Wrong Number of Arguments',
    severity: 'error',
    category: 'device',
    explanation:
      'You\'re passing the wrong number of arguments to a function or method.',
    fix: 'Check the function signature for the correct parameter list. For device event handlers, the callback typically takes an `?agent` parameter.',
    example:
      '# Before (broken):\nMyButton.InteractedWithEvent.Subscribe(OnPressed)\nOnPressed():void =  # Missing agent parameter!\n    Print("Pressed")\n\n# After (fixed):\nMyButton.InteractedWithEvent.Subscribe(OnPressed)\nOnPressed(Agent : ?agent):void =\n    Print("Pressed")',
    docSection: 'Functions',
  },
  {
    pattern: /[Mm]issing\s+required\s+argument|argument\s+[`']?(\w+)[`']?\s+(?:is\s+)?required|required\s+(?:parameter|argument)\s+[`']?(\w+)[`']?\s+not\s+provided/,
    title: 'Missing Required Argument',
    severity: 'error',
    category: 'device',
    explanation:
      'A required argument was not provided in the function call.',
    fix: 'Add the missing argument. Check the function signature for all required parameters and their types.',
    example:
      '# Before (broken):\nMyGranter.GrantItem()  # Missing player argument\n\n# After (fixed):\nMyGranter.GrantItem(Player)',
    docSection: 'Functions',
  },
  {
    pattern: /missing\s+[`']?using[`']?\s+statement|module\s+[`']?([^`']+)[`']?\s+not\s+found|cannot\s+find\s+module/i,
    title: 'Missing Using Statement / Module Not Found',
    severity: 'error',
    category: 'device',
    explanation:
      'The type or function you\'re using lives in a module that hasn\'t been imported.',
    fix: 'Add the appropriate `using` statement at the top of your file.',
    example:
      '# Common UEFN using statements:\nusing { /Fortnite.com/Devices }          # Device types\nusing { /Fortnite.com/Characters }       # Character/agent types\nusing { /Fortnite.com/UI }               # UI widgets\nusing { /Fortnite.com/Game }             # Game framework\nusing { /Verse.org/Simulation }          # Simulation framework\nusing { /Verse.org/Random }              # Random numbers\nusing { /UnrealEngine.com/Temporary/Diagnostics }  # Print()\nusing { /UnrealEngine.com/Temporary/SpatialMath }  # Vectors',
    docSection: 'Modules',
  },
  {
    pattern: /@editable.*(?:invalid|unsupported)\s+type|(?:invalid|unsupported)\s+type.*@editable|cannot\s+use\s+@editable\s+(?:on|with)/i,
    title: '@editable with Invalid Type',
    severity: 'error',
    category: 'device',
    explanation:
      'The `@editable` attribute can only be used with types that UEFN can display in its property editor. Complex custom types, functions, and some generics are not supported.',
    fix: 'Use supported types: `int`, `float`, `string`, `logic`, device references, enums, arrays of simple types, and color/vector types.',
    example:
      '# Supported @editable types:\n@editable MyDevice : button_device = button_device{}\n@editable Health : float = 100.0\n@editable Name : string = "Default"\n@editable Enabled : logic = true\n@editable SpawnPoints : []player_spawner_device = array{}\n\n# NOT supported:\n# @editable MyFunc : type{} = ...\n# @editable Data : my_custom_struct = ...',
    docSection: 'Devices',
  },
  {
    pattern: /OnBegin.*(?:not\s+found|not\s+a\s+member|override)|(?:override|overriding)\s+OnBegin|cannot\s+override\s+OnBegin/i,
    title: 'OnBegin Override Error',
    severity: 'error',
    category: 'device',
    explanation:
      '`OnBegin()` is the entry point for `creative_device` classes. It must have the correct signature: `OnBegin<override>()<suspends>:void`. Missing `<override>` or `<suspends>` will cause errors.',
    fix: 'Use the exact signature: `OnBegin<override>()<suspends>:void =`',
    example:
      '# Before (broken):\nmy_device := class(creative_device):\n    OnBegin():void =  # Missing <override> and <suspends>\n        Print("Started")\n\n# After (fixed):\nmy_device := class(creative_device):\n    OnBegin<override>()<suspends>:void =\n        Print("Started")',
    docSection: 'Devices',
  },
  {
    pattern: /Subscribe.*(?:type\s+mismatch|wrong\s+(?:type|signature))|(?:handler|callback)\s+(?:type|signature)\s+(?:mismatch|incorrect)/i,
    title: 'Event Handler Signature Mismatch',
    severity: 'error',
    category: 'device',
    explanation:
      'The function you\'re subscribing to an event doesn\'t match the expected handler signature.',
    fix: 'Match the handler signature. Most device events require `(Agent : ?agent) : void`.',
    example:
      '# Before (broken):\nMyButton.InteractedWithEvent.Subscribe(OnPress)\nOnPress() : void =  # Wrong!\n    Print("Pressed")\n\n# After (fixed):\nMyButton.InteractedWithEvent.Subscribe(OnPress)\nOnPress(Agent : ?agent) : void =\n    Print("Pressed by agent")\n\n# For awaitable events:\nOnBegin<override>()<suspends>:void =\n    MyButton.InteractedWithEvent.Await()',
    docSection: 'Devices',
  },

  // ═══════════════════════════════════════════════════════════════════════
  // SYNTAX ERRORS
  // ═══════════════════════════════════════════════════════════════════════
  {
    pattern: /[Ee]xpected\s+indentation|[Ii]ndentation\s+error|[Uu]nexpected\s+indent/,
    title: 'Indentation Error',
    severity: 'error',
    category: 'syntax',
    explanation:
      'Verse uses significant whitespace (like Python). UEFN requires exactly 4 spaces per indentation level. Tabs are not allowed.',
    fix: 'Use consistent 4-space indentation. Do NOT mix tabs and spaces. Configure your editor to insert spaces when pressing Tab.',
    example:
      '# Before (broken):\nif (Condition?):\nDoSomething()  # Not indented!\n\n# After (fixed):\nif (Condition?):\n    DoSomething()  # 4-space indent\n\n# Nested blocks:\nif (X > 0):\n    if (Y > 0):\n        DoSomething()  # 8 spaces for nested',
    docSection: 'Syntax',
  },
  {
    pattern: /[Ee]xpected\s+[`']?:[`']?\s+(?:in|after|for)|[Mm]issing\s+type\s+annotation|[Tt]ype\s+annotation\s+required/,
    title: 'Missing Type Annotation',
    severity: 'error',
    category: 'syntax',
    explanation:
      'Verse requires explicit type annotations in many contexts: function parameters, return types, class fields.',
    fix: 'Add the type annotation using `: type` syntax.',
    example:
      '# Before (broken):\nMyFunc(X, Y) = X + Y  # Missing types\n\n# After (fixed):\nMyFunc(X : int, Y : int) : int = X + Y\n\n# Class fields:\nmy_class := class:\n    Health : float = 100.0',
    docSection: 'Types',
  },
  {
    pattern: /[Ee]xpected\s+[`']?=[`']?\s+(?:in|after)|[Ee]xpected\s+[`']?:=[`']?\s+(?:in|for)|use\s+[`']?:=[`']?\s+(?:for|instead)/,
    title: 'Assignment Syntax Error',
    severity: 'error',
    category: 'syntax',
    explanation:
      'Verse uses `:=` for new bindings (creating variables) and `set X =` for mutation. Regular `=` is used in definitions.',
    fix: 'Use `:=` to create new bindings, `set X =` to modify existing `var` variables, and `=` in class field definitions.',
    example:
      '# Binding (new variable):\nScore := 42\nName := "Player"\n\n# Definition (class field):\nmy_class := class:\n    Health : float = 100.0\n\n# Mutation (changing existing var):\nvar Counter : int = 0\nset Counter = Counter + 1',
    docSection: 'Variables',
  },
  {
    pattern: /[Uu]nexpected\s+token|[Ss]yntax\s+error|[Pp]arse\s+error/,
    title: 'Syntax Error / Unexpected Token',
    severity: 'error',
    category: 'syntax',
    explanation:
      'The compiler encountered something unexpected. Common causes: missing parentheses, extra commas, wrong operator, mismatched brackets.',
    fix: 'Check for missing or extra punctuation. Ensure parentheses and brackets are balanced.',
    example:
      '# Common syntax fixes:\n# Missing closing paren:\nif (X > 0:     # Bad\nif (X > 0):    # Good\n\n# Trailing comma:\narray{1, 2, 3,}  # Bad\narray{1, 2, 3}   # Good\n\n# Block expression:\nResult := block:\n    X := 10\n    X + 5  # Last expression is the value',
    docSection: 'Syntax',
  },
  {
    pattern: /[Ee]xpected\s+[`']?:[`']?\s+(?:to\s+)?(?:start|begin)\s+(?:a\s+)?block|missing\s+[`']?:[`']?\s+after/,
    title: 'Missing Colon to Start Block',
    severity: 'error',
    category: 'syntax',
    explanation:
      'In Verse, blocks (after `if`, `for`, class, function definitions, etc.) start with a colon `:` followed by an indented block.',
    fix: 'Add `:` at the end of the line before the indented block.',
    example:
      '# Before (broken):\nif (X > 0)\n    DoSomething()\n\n# After (fixed):\nif (X > 0):\n    DoSomething()\n\n# Same for functions:\nMyFunc() : void =\n    DoSomething()',
    docSection: 'Syntax',
  },
  {
    pattern: /(?:invalid|error\s+in)\s+string\s+interpolation|string\s+interpolation.*(?:error|fail|invalid)|cannot\s+interpolate|interpolation.*type/i,
    title: 'String Interpolation Error',
    severity: 'error',
    category: 'syntax',
    explanation:
      'Verse uses `{expression}` inside double-quoted strings for interpolation. The expression must be convertible to string.',
    fix: 'Use `"{Expression}"` syntax. Most primitive types work. For complex types, format the value first.',
    example:
      '# Correct string interpolation:\nScore : int = 42\nPrint("Score: {Score}")\n\n# Multiple values:\nName := "Player"\nPrint("{Name} scored {Score} points")\n\n# With expressions:\nPrint("Double: {Score * 2}")',
    docSection: 'Syntax',
  },
  {
    pattern: /[Uu]nreachable\s+code/,
    title: 'Unreachable Code',
    severity: 'warning',
    category: 'syntax',
    explanation:
      'Code after a `return`, `break`, or unconditional jump will never execute.',
    fix: 'Remove the unreachable code, or restructure the control flow.',
    example:
      '# Before (warning):\nDoSomething():void =\n    return\n    Print("Never reached")  # Unreachable!\n\n# After (fixed):\nDoSomething():void =\n    Print("This runs")\n    return',
    docSection: 'Control Flow',
  },
  {
    pattern: /conflicting\s+specifiers?|specifier.*(?:conflict|incompatible|duplicate)|duplicate\s+(?:effect\s+)?specifier|cannot\s+combine\s+specifiers?/i,
    title: 'Conflicting Specifiers',
    severity: 'error',
    category: 'syntax',
    explanation:
      'You\'ve used specifiers that conflict with each other, or duplicated a specifier.',
    fix: 'Remove the conflicting or duplicate specifier.',
    example:
      '# Before (broken):\n<public><private> MyField : int = 0  # Cannot be both!\n\n# After (fixed):\n<public> MyField : int = 0\n\n# Valid combinations:\n<public><transacts><decides>  # OK\n<override><suspends>          # OK',
    docSection: 'Access Specifiers',
  },

  // ═══════════════════════════════════════════════════════════════════════
  // CONCURRENCY ERRORS
  // ═══════════════════════════════════════════════════════════════════════
  {
    pattern: /[Cc]annot\s+spawn|spawn.*requires|spawn.*<suspends>|invalid\s+spawn/,
    title: 'Invalid Spawn Usage',
    severity: 'error',
    category: 'concurrency',
    explanation:
      'The `spawn` expression requires a `<suspends>` function or block.',
    fix: 'Ensure the expression inside `spawn { }` calls a `<suspends>` function. Create an async wrapper if needed.',
    example:
      '# Before (broken):\nspawn { DoSyncWork() }  # Not <suspends>\n\n# After (fixed):\nMyAsync()<suspends>:void =\n    Sleep(1.0)\n    DoWork()\n\nOnBegin()<suspends>:void =\n    spawn { MyAsync() }',
    docSection: 'Concurrency',
  },
  {
    pattern: /race.*requires\s+<suspends>|sync.*requires\s+<suspends>|rush.*requires\s+<suspends>/i,
    title: 'Concurrency Primitive Requires <suspends>',
    severity: 'error',
    category: 'concurrency',
    explanation:
      'Concurrency primitives like `race{}`, `sync{}`, and `rush{}` require a `<suspends>` context because they manage async tasks.',
    fix: 'Ensure the enclosing function has `<suspends>`, and all branches inside the concurrency block are also `<suspends>`.',
    example:
      '# Before (broken):\nDoGameLoop():void =\n    race:\n        WaitForTimer()\n        WaitForKill()\n\n# After (fixed):\nDoGameLoop()<suspends>:void =\n    race:\n        WaitForTimer()  # Must be <suspends>\n        WaitForKill()   # Must be <suspends>',
    docSection: 'Concurrency',
  },
  {
    pattern: /[Aa]wait.*(?:outside|not\s+in|requires)\s+(?:a\s+)?(?:<suspends>|suspending|async)|cannot\s+(?:use\s+)?[Aa]wait\s+(?:here|in\s+this)/,
    title: 'Await Outside <suspends> Context',
    severity: 'error',
    category: 'concurrency',
    explanation:
      '`Await()` pauses execution until an event fires. It can only be used inside a `<suspends>` function.',
    fix: 'Mark your function as `<suspends>` to use `Await()`. Or use `Subscribe()` for non-blocking event handling.',
    example:
      '# Before (broken):\nWaitForButton():void =\n    MyButton.InteractedWithEvent.Await()  # Needs <suspends>\n\n# After (fixed):\nWaitForButton()<suspends>:void =\n    MyButton.InteractedWithEvent.Await()\n    Print("Button was pressed!")\n\n# Or use Subscribe (non-blocking):\nSetupButton():void =\n    MyButton.InteractedWithEvent.Subscribe(OnButtonPressed)\n\nOnButtonPressed(Agent : ?agent):void =\n    Print("Pressed!")',
    docSection: 'Concurrency',
  },

  // ═══════════════════════════════════════════════════════════════════════
  // UEFN WORKFLOW ERRORS
  // ═══════════════════════════════════════════════════════════════════════
  {
    pattern: /file\s+(?:has\s+)?(?:not\s+been\s+saved|is\s+not\s+saved|unsaved)|save\s+(?:the\s+)?file\s+before\s+compil/i,
    title: 'File Not Saved Before Compilation',
    severity: 'error',
    category: 'syntax',
    explanation:
      'The Verse file hasn\'t been saved before compiling. UEFN compiles from the saved file on disk, not from the editor buffer.',
    fix: 'Save the file (Ctrl+S) before pressing the Verse compile button or building.',
    example:
      '# This is a workflow error, not a code error.\n# Steps:\n# 1. Edit your .verse file\n# 2. Save (Ctrl+S)\n# 3. Then compile (Ctrl+Shift+B)',
    docSection: 'Syntax',
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
    const match = ERROR_PATTERNS.find((ep) => ep.pattern.test(p.message)) ?? null
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
  Devices: 'https://dev.epicgames.com/documentation/en-us/uefn/verse-api-reference',
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
    <div className="flex-1 flex bg-fn-darker overflow-hidden min-h-0">
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
                  Supports 45+ Verse error patterns across types, effects, failable, scope, devices, syntax, and concurrency
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
