/**
 * AST node types for the Verse language.
 * Covers declarations, imports, and structural constructs.
 * Expression-level nodes are intentionally minimal — Phase 2 focuses on structure.
 */

// ─── Source location ─────────────────────────────────────────────────────────

export interface SourceRange {
  startLine: number   // 1-based
  startCol: number    // 0-based
  endLine: number     // 1-based
  endCol: number      // 0-based
}

export interface SourceLocation {
  file: string
  range: SourceRange
}

// ─── Base node ───────────────────────────────────────────────────────────────

export interface BaseNode {
  range: SourceRange
}

// ─── Top-level ───────────────────────────────────────────────────────────────

export interface Program extends BaseNode {
  type: 'Program'
  body: Declaration[]
  comments: Comment[]
}

export type Declaration =
  | UsingDecl
  | ModuleDecl
  | ClassDecl
  | FunctionDecl
  | FieldDecl
  | VarDecl
  | ExpressionStmt

// ─── Using / Imports ─────────────────────────────────────────────────────────

export interface UsingDecl extends BaseNode {
  type: 'UsingDecl'
  path: string           // e.g. "/Fortnite.com/Devices"
  braced: boolean        // using { /Path } vs using /Path
}

// ─── Module ──────────────────────────────────────────────────────────────────

export interface ModuleDecl extends BaseNode {
  type: 'ModuleDecl'
  name: string
  specifiers: Specifier[]
  body: Declaration[]
}

// ─── Class / Struct / Interface / Enum ───────────────────────────────────────

export type StructureKind = 'class' | 'struct' | 'interface' | 'enum'

export interface ClassDecl extends BaseNode {
  type: 'ClassDecl'
  name: string
  kind: StructureKind
  specifiers: Specifier[]
  decorators: Decorator[]
  parent?: string         // class(parent)
  typeParams?: string[]   // class<T> (parametric)
  body: Declaration[]
}

// ─── Function ────────────────────────────────────────────────────────────────

export interface Parameter extends BaseNode {
  type: 'Parameter'
  name: string
  typeAnnotation?: string
  defaultValue?: string
}

export interface FunctionDecl extends BaseNode {
  type: 'FunctionDecl'
  name: string
  specifiers: Specifier[]
  decorators: Decorator[]
  parameters: Parameter[]
  returnType?: string
  isOverride: boolean
  isSuspends: boolean
  bodyStartLine?: number   // where the body block begins
}

// ─── Field / Property ────────────────────────────────────────────────────────

export interface FieldDecl extends BaseNode {
  type: 'FieldDecl'
  name: string
  specifiers: Specifier[]
  decorators: Decorator[]
  typeAnnotation?: string
  defaultValue?: string
  isVar: boolean           // var keyword present
  isEditable: boolean      // @editable decorator
}

// ─── Variable declaration ────────────────────────────────────────────────────

export interface VarDecl extends BaseNode {
  type: 'VarDecl'
  name: string
  typeAnnotation?: string
  initializer?: string
  isVar: boolean           // var vs :=
}

// ─── Expression statement (catch-all for unparsed lines) ─────────────────────

export interface ExpressionStmt extends BaseNode {
  type: 'ExpressionStmt'
  text: string
}

// ─── Specifiers and decorators ───────────────────────────────────────────────

export interface Specifier {
  name: string             // e.g. "override", "suspends", "public"
  param?: string           // for parameterized specifiers like <native{/path}>
}

export interface Decorator {
  name: string             // e.g. "editable", "localizes"
}

// ─── Comments ────────────────────────────────────────────────────────────────

export interface Comment extends BaseNode {
  type: 'Comment'
  text: string
  isBlock: boolean         // <# ... #> vs #
}

// ─── Utility types ───────────────────────────────────────────────────────────

/** Symbol kinds for the symbol table */
export type SymbolKind =
  | 'module'
  | 'class'
  | 'struct'
  | 'interface'
  | 'enum'
  | 'function'
  | 'field'
  | 'variable'
  | 'parameter'
  | 'import'

/** A resolved symbol in the project index */
export interface VerseSymbol {
  name: string
  kind: SymbolKind
  type?: string            // type annotation if known
  location: SourceLocation
  parent?: string          // enclosing scope name
  specifiers: Specifier[]
  decorators: Decorator[]
  children?: VerseSymbol[] // members for classes/modules
  signature?: string       // display signature for functions
}
