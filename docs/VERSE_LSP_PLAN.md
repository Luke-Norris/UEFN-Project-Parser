# Verse Language Server Plan

## Goal
Build Verse language intelligence into WellVersed — autocomplete, go-to-definition, hover info, diagnostics. Either standalone or by leveraging Epic's existing LSP.

## Available Resources

### Local
- `verse-book/verse_lexer/verse.py` — Complete Pygments lexer with all keywords, specifiers, operators, token types
- `verse-book/docs/` — 20 chapters covering every language feature (00-18 + concept index)
- Existing regex parser in `VerseFilesPage.tsx` — detects classes, functions, devices, imports, structs
- `.digest` files in UEFN projects — device class/property/event schemas

### External
- **Epic's Verse LSP binary** — `verse-lsp-latest.exe` ships with UEFN at `<FortniteInstall>/vscode/`
  - Standard LSP protocol — could spawn as subprocess and communicate directly
  - Already handles autocomplete, diagnostics, go-to-def
  - Requires UEFN to be installed
- **Verse Calculus paper** — https://simon.peytonjones.org/assets/pdfs/verse-icfp23.pdf
  - Formal semantics for the core language
- **Epic's docs** — https://dev.epicgames.com/documentation/en-us/fortnite/verse-language-reference
  - Specifiers, attributes, types, operators, expressions, failure contexts
- **VerseReferenceExplorer** — https://github.com/cronofear-dev/VerseReferenceExplorer
  - VS Code extension with reference graph, symbol navigation
  - Works on top of Epic's LSP — shows how to consume LSP data

## Architecture Options

### Option A: Proxy Epic's LSP
Spawn `verse-lsp-latest.exe` as a subprocess, communicate via LSP protocol.
- **Pros**: Full language support instantly, handles all edge cases
- **Cons**: Requires UEFN installed, Epic could change/break it, no control
- **Effort**: 2-3 days

### Option B: Build Our Own
Recursive descent parser → AST → symbol table → language features.
- **Pros**: Full control, works without UEFN, can extend with custom features
- **Cons**: Significant effort, will miss edge cases initially
- **Effort**: 2-4 weeks for useful coverage

### Option C: Hybrid (Recommended)
Use Epic's LSP when available, fall back to our own parser when not.
- Build our parser for project-level intelligence (symbols, structs, devices)
- Proxy to Epic's LSP for type checking and advanced diagnostics
- **Effort**: Option A first (2-3 days), then incrementally build Option B

## Implementation Steps

### Phase 1: Epic's LSP Proxy (2-3 days)
1. Detect UEFN installation path
2. Find `verse-lsp-latest.exe` in the UEFN install
3. Spawn it as LSP subprocess from the .NET sidecar
4. Forward LSP messages between WellVersed UI and the LSP server
5. Surface completions, hover, diagnostics in the verse viewer

### Phase 2: Symbol Index (3-5 days)
1. Upgrade regex parser to recursive descent (use verse_lexer tokens as foundation)
2. Build project-wide symbol table: classes, functions, variables, imports
3. Resolve `using` imports against digest schemas
4. Go-to-definition across files
5. Find all references

### Phase 3: Autocomplete (3-5 days)
1. Struct field completion (`player.` → fields from player_profile)
2. Device property/event completion from digest schemas
3. Import path completion (`using { /Fortnite.com/ }` → available modules)
4. Keyword/specifier completion

### Phase 4: Diagnostics (5-7 days)
1. Missing imports detection
2. Undefined variable/function references
3. Basic type checking (int vs string)
4. Unused variable warnings

## Key Language Features to Handle

From `verse_lexer/verse.py` and the docs:

### Specifiers (in angle brackets)
`<public>`, `<private>`, `<protected>`, `<final>`, `<abstract>`, `<override>`,
`<decides>`, `<transacts>`, `<suspends>`, `<computes>`, `<reads>`, `<writes>`,
`<persistable>`, `<native>`, `<constructor>`, `<converges>`, `<unique>`

### Block Keywords
`if`, `then`, `else`, `for`, `block`, `loop`, `array`, `case`

### Data Structures
`module`, `interface`, `class`, `struct`, `enum`

### Declarations
`var`, `set`, `using`, `@editable`

### Operators
`:=` (definition), `=` (binding), `<-` (assignment), `=>` (lambda),
`.` (member access), `?` (option), `()` (call/grouping)

### Types
`int`, `float`, `string`, `char`, `logic`, `void`, `any`, `type`,
`comparable`, `subtype`, `false`, `true`, `array`, `map`, `tuple`,
`?T` (option), `[]T` (array), `[K]V` (map), `weak_map_key`

### Effects System
- `<decides>` — failable expression context
- `<transacts>` — can be rolled back
- `<suspends>` — async/coroutine
- `<computes>` — pure computation
- `<reads>` / `<writes>` — side effect tracking

### Failure Context
Everything is an expression. Failure is control flow.
`if (x := Foo[]):` — failable lookup in if-context

## File Locations
- Parser source: `app/src/renderer/pages/VerseFilesPage.tsx` (parseVerseSource function)
- Lexer reference: `verse-book/verse_lexer/verse.py`
- Language docs: `verse-book/docs/`
- Digest parser: `src/WellVersed.Core/Services/DigestService.cs`
- Sidecar verse handler: `src/WellVersed.CLI/Program.cs` (HandleReadVerse)
