/**
 * Symbol table and project index for Verse.
 * Collects symbols from parsed ASTs, resolves references across files.
 */

import type {
  Program, Declaration, ClassDecl, ModuleDecl, FunctionDecl,
  FieldDecl, VarDecl, UsingDecl, VerseSymbol, SymbolKind,
  SourceLocation, Specifier, Decorator,
} from './verse-ast'

// ─── File symbol table ───────────────────────────────────────────────────────

export interface FileSymbols {
  filePath: string
  symbols: VerseSymbol[]
  imports: ImportInfo[]
}

export interface ImportInfo {
  path: string
  line: number
}

/**
 * Extract symbols from a parsed AST for one file.
 */
export function extractFileSymbols(ast: Program, filePath: string): FileSymbols {
  const symbols: VerseSymbol[] = []
  const imports: ImportInfo[] = []

  for (const decl of ast.body) {
    extractDecl(decl, filePath, undefined, symbols, imports)
  }

  return { filePath, symbols, imports }
}

function extractDecl(
  decl: Declaration,
  filePath: string,
  parentName: string | undefined,
  symbols: VerseSymbol[],
  imports: ImportInfo[],
): void {
  switch (decl.type) {
    case 'UsingDecl':
      imports.push({ path: decl.path, line: decl.range.startLine })
      symbols.push({
        name: decl.path,
        kind: 'import',
        location: loc(filePath, decl.range),
        parent: parentName,
        specifiers: [],
        decorators: [],
      })
      break

    case 'ClassDecl':
      extractClass(decl, filePath, parentName, symbols, imports)
      break

    case 'ModuleDecl':
      extractModule(decl, filePath, parentName, symbols, imports)
      break

    case 'FunctionDecl':
      extractFunction(decl, filePath, parentName, symbols)
      break

    case 'FieldDecl':
      extractField(decl, filePath, parentName, symbols)
      break

    case 'VarDecl':
      symbols.push({
        name: decl.name,
        kind: 'variable',
        type: decl.typeAnnotation,
        location: loc(filePath, decl.range),
        parent: parentName,
        specifiers: [],
        decorators: [],
      })
      break
  }
}

function extractClass(
  decl: ClassDecl,
  filePath: string,
  parentName: string | undefined,
  symbols: VerseSymbol[],
  imports: ImportInfo[],
): void {
  const children: VerseSymbol[] = []

  for (const member of decl.body) {
    extractDecl(member, filePath, decl.name, children, imports)
  }

  const kindMap: Record<string, SymbolKind> = {
    class: 'class', struct: 'struct', interface: 'interface', enum: 'enum',
  }

  symbols.push({
    name: decl.name,
    kind: kindMap[decl.kind] ?? 'class',
    type: decl.parent ? decl.parent : undefined,
    location: loc(filePath, decl.range),
    parent: parentName,
    specifiers: decl.specifiers,
    decorators: decl.decorators,
    children,
  })
}

function extractModule(
  decl: ModuleDecl,
  filePath: string,
  parentName: string | undefined,
  symbols: VerseSymbol[],
  imports: ImportInfo[],
): void {
  const children: VerseSymbol[] = []

  for (const member of decl.body) {
    extractDecl(member, filePath, decl.name, children, imports)
  }

  symbols.push({
    name: decl.name,
    kind: 'module',
    location: loc(filePath, decl.range),
    parent: parentName,
    specifiers: decl.specifiers,
    decorators: [],
    children,
  })
}

function extractFunction(
  decl: FunctionDecl,
  filePath: string,
  parentName: string | undefined,
  symbols: VerseSymbol[],
): void {
  const params = decl.parameters.map(p =>
    `${p.name}${p.typeAnnotation ? ': ' + p.typeAnnotation : ''}`
  ).join(', ')

  const specs = decl.specifiers.map(s => `<${s.name}>`).join('')
  const sig = `${decl.name}${specs}(${params})${decl.returnType ? ': ' + decl.returnType : ''}`

  const children: VerseSymbol[] = decl.parameters.map(p => ({
    name: p.name,
    kind: 'parameter' as SymbolKind,
    type: p.typeAnnotation,
    location: loc(filePath, p.range),
    parent: decl.name,
    specifiers: [],
    decorators: [],
  }))

  symbols.push({
    name: decl.name,
    kind: 'function',
    type: decl.returnType,
    location: loc(filePath, decl.range),
    parent: parentName,
    specifiers: decl.specifiers,
    decorators: decl.decorators,
    children,
    signature: sig,
  })
}

function extractField(
  decl: FieldDecl,
  filePath: string,
  parentName: string | undefined,
  symbols: VerseSymbol[],
): void {
  symbols.push({
    name: decl.name,
    kind: 'field',
    type: decl.typeAnnotation,
    location: loc(filePath, decl.range),
    parent: parentName,
    specifiers: decl.specifiers,
    decorators: decl.decorators,
  })
}

function loc(file: string, range: { startLine: number; startCol: number; endLine: number; endCol: number }): SourceLocation {
  return { file, range }
}

// ─── Project Index ───────────────────────────────────────────────────────────

export class ProjectIndex {
  private files = new Map<string, FileSymbols>()
  private globalSymbols = new Map<string, VerseSymbol[]>()

  /** Add or update symbols for a file */
  indexFile(fileSymbols: FileSymbols): void {
    this.files.set(fileSymbols.filePath, fileSymbols)
    this.rebuildGlobal()
  }

  /** Remove a file from the index */
  removeFile(filePath: string): void {
    this.files.delete(filePath)
    this.rebuildGlobal()
  }

  /** Clear the entire index */
  clear(): void {
    this.files.clear()
    this.globalSymbols.clear()
  }

  /** Get all indexed files */
  getFiles(): string[] {
    return Array.from(this.files.keys())
  }

  /** Get symbols for a specific file */
  getFileSymbols(filePath: string): FileSymbols | undefined {
    return this.files.get(filePath)
  }

  /** Get imports for a specific file */
  getFileImports(filePath: string): ImportInfo[] {
    return this.files.get(filePath)?.imports ?? []
  }

  /** Find a symbol by name (searches all files) */
  findSymbol(name: string): VerseSymbol[] {
    return this.globalSymbols.get(name) ?? []
  }

  /** Find the definition of a symbol */
  findDefinition(name: string, fromFile?: string): VerseSymbol | undefined {
    const matches = this.findSymbol(name)
    if (matches.length === 0) return undefined

    // Prefer same-file matches
    if (fromFile) {
      const sameFile = matches.find(s => s.location.file === fromFile)
      if (sameFile) return sameFile
    }

    // Return the first match
    return matches[0]
  }

  /** Find all references to a symbol name across the project */
  findReferences(name: string): VerseSymbol[] {
    return this.findSymbol(name)
  }

  /** Get all symbols (flat list) */
  getAllSymbols(): VerseSymbol[] {
    const all: VerseSymbol[] = []
    for (const file of this.files.values()) {
      collectAll(file.symbols, all)
    }
    return all
  }

  /** Get top-level symbols only (classes, modules, top-level functions) */
  getTopLevelSymbols(): VerseSymbol[] {
    const top: VerseSymbol[] = []
    for (const file of this.files.values()) {
      top.push(...file.symbols)
    }
    return top
  }

  /**
   * Get completions for a given context.
   * @param parentType — if user typed `foo.`, this is the type of `foo`
   * @param fromFile — the file requesting completions
   */
  getCompletions(parentType?: string, fromFile?: string): VerseSymbol[] {
    if (parentType) {
      // Member completions: find all children of the type
      const typeSymbols = this.findSymbol(parentType)
      const members: VerseSymbol[] = []
      for (const sym of typeSymbols) {
        if (sym.children) members.push(...sym.children)
      }
      return members
    }

    // Global completions: all top-level symbols visible from this file
    const visible: VerseSymbol[] = []
    for (const file of this.files.values()) {
      for (const sym of file.symbols) {
        if (sym.kind !== 'import') {
          visible.push(sym)
        }
      }
    }
    return visible
  }

  /** Get summary stats */
  getStats(): { files: number; symbols: number; classes: number; functions: number; imports: number } {
    let symbols = 0, classes = 0, functions = 0, imports = 0
    for (const file of this.files.values()) {
      for (const sym of file.symbols) {
        countSymbol(sym)
      }
    }
    function countSymbol(sym: VerseSymbol) {
      symbols++
      if (sym.kind === 'class' || sym.kind === 'struct') classes++
      if (sym.kind === 'function') functions++
      if (sym.kind === 'import') imports++
      if (sym.children) sym.children.forEach(countSymbol)
    }
    return { files: this.files.size, symbols, classes, functions, imports }
  }

  // ─── Internal ────────────────────────────────────────────────────────────

  private rebuildGlobal(): void {
    this.globalSymbols.clear()
    for (const file of this.files.values()) {
      this.indexSymbols(file.symbols)
    }
  }

  private indexSymbols(symbols: VerseSymbol[]): void {
    for (const sym of symbols) {
      const existing = this.globalSymbols.get(sym.name)
      if (existing) {
        existing.push(sym)
      } else {
        this.globalSymbols.set(sym.name, [sym])
      }
      // Also index children
      if (sym.children) {
        this.indexSymbols(sym.children)
      }
    }
  }
}

function collectAll(symbols: VerseSymbol[], out: VerseSymbol[]): void {
  for (const sym of symbols) {
    out.push(sym)
    if (sym.children) collectAll(sym.children, out)
  }
}
