/**
 * Recursive descent parser for Verse source code.
 * Produces an AST focused on declarations and structure (not full expressions).
 * Handles indent-based scoping via indent level tracking.
 */

import type {
  Program, Declaration, UsingDecl, ModuleDecl, ClassDecl,
  FunctionDecl, FieldDecl, VarDecl, ExpressionStmt,
  Parameter, Specifier, Decorator, Comment, SourceRange, StructureKind,
} from './verse-ast'

// ─── Parser ──────────────────────────────────────────────────────────────────

interface ParseLine {
  text: string
  trimmed: string
  indent: number
  lineNum: number  // 1-based
}

export function parseVerseFile(source: string, filePath: string = ''): Program {
  if (!source) {
    return {
      type: 'Program',
      body: [],
      comments: [],
      range: { startLine: 1, startCol: 0, endLine: 1, endCol: 0 },
    }
  }

  const rawLines = source.split('\n')
  const lines: ParseLine[] = rawLines.map((text, i) => ({
    text,
    trimmed: text.trim(),
    indent: text.search(/\S/),
    lineNum: i + 1,
  }))

  // Fix indent for blank lines (set to -1)
  for (const line of lines) {
    if (line.indent === -1 || line.trimmed === '') {
      line.indent = -1
    }
  }

  const comments: Comment[] = []
  const ctx = { lines, pos: 0, comments, filePath }
  const body = parseBlock(ctx, -1)

  return {
    type: 'Program',
    body,
    comments,
    range: {
      startLine: 1,
      startCol: 0,
      endLine: rawLines.length,
      endCol: rawLines[rawLines.length - 1]?.length ?? 0,
    },
  }
}

// ─── Parse context ───────────────────────────────────────────────────────────

interface ParseContext {
  lines: ParseLine[]
  pos: number
  comments: Comment[]
  filePath: string
}

// ─── Block parsing (indent-based) ────────────────────────────────────────────

function parseBlock(ctx: ParseContext, parentIndent: number): Declaration[] {
  const declarations: Declaration[] = []

  while (ctx.pos < ctx.lines.length) {
    const line = ctx.lines[ctx.pos]

    // Skip blank lines
    if (line.trimmed === '') {
      ctx.pos++
      continue
    }

    // If indent <= parent indent, this line belongs to an outer scope
    if (line.indent >= 0 && line.indent <= parentIndent) {
      break
    }

    const decl = parseDeclaration(ctx, parentIndent)
    if (decl) {
      declarations.push(decl)
    }
  }

  return declarations
}

// ─── Declaration parsing ─────────────────────────────────────────────────────

function parseDeclaration(ctx: ParseContext, parentIndent: number): Declaration | null {
  const line = ctx.lines[ctx.pos]
  if (!line || line.trimmed === '') {
    ctx.pos++
    return null
  }

  const { trimmed } = line

  // Comments (standalone line comments and block comments)
  if (trimmed.startsWith('#') && !trimmed.startsWith('#>')) {
    return parseComment(ctx)
  }
  if (trimmed.startsWith('<#')) {
    return parseBlockComment(ctx)
  }

  // Using declarations
  if (trimmed.startsWith('using')) {
    return parseUsing(ctx)
  }

  // Try to parse structured declarations
  // Pattern: Name<specifiers> := class/struct/module/interface/enum
  const structureDecl = tryParseStructure(ctx, parentIndent)
  if (structureDecl) return structureDecl

  // Pattern: @editable / @decorator before a field
  if (trimmed.startsWith('@')) {
    return parseDecoratedDecl(ctx, parentIndent)
  }

  // Pattern: var Name : Type = value
  if (trimmed.startsWith('var ') || trimmed.startsWith('set ')) {
    return parseVarOrSet(ctx)
  }

  // Pattern: Name(params)<specifiers>: ReturnType =
  const funcDecl = tryParseFunction(ctx, parentIndent)
  if (funcDecl) return funcDecl

  // Pattern: Name<specifiers> : Type = DefaultValue
  const fieldDecl = tryParseField(ctx)
  if (fieldDecl) return fieldDecl

  // Catch-all: expression statement
  return parseExpressionStmt(ctx)
}

// ─── Using ───────────────────────────────────────────────────────────────────

function parseUsing(ctx: ParseContext): UsingDecl {
  const line = ctx.lines[ctx.pos]
  ctx.pos++

  // using { /Path/To/Module }
  const bracedMatch = line.trimmed.match(/^using\s*\{\s*(.+?)\s*\}/)
  if (bracedMatch) {
    return {
      type: 'UsingDecl',
      path: bracedMatch[1],
      braced: true,
      range: lineRange(line),
    }
  }

  // using /Path/To/Module
  const plainMatch = line.trimmed.match(/^using\s+(.+)/)
  if (plainMatch) {
    return {
      type: 'UsingDecl',
      path: plainMatch[1].trim(),
      braced: false,
      range: lineRange(line),
    }
  }

  return {
    type: 'UsingDecl',
    path: '',
    braced: false,
    range: lineRange(line),
  }
}

// ─── Structure (class, struct, module, interface, enum) ──────────────────────

function tryParseStructure(ctx: ParseContext, parentIndent: number): ClassDecl | ModuleDecl | null {
  const line = ctx.lines[ctx.pos]
  const { trimmed } = line

  // Pattern: Name<specs> := class/struct/interface/enum(Parent)<specs>:
  // Also:    Name := module:
  const match = trimmed.match(
    /^([a-zA-Z_]\w*)(?:<([^>]+)>)?\s*:=\s*(class|struct|interface|enum|module)(?:<([^>]+)>)?(?:\(([^)]*)\))?\s*:?\s*$/
  )
  if (!match) return null

  const [, name, preSpecs, kindStr, postSpecs, parentStr] = match
  const kind = kindStr as StructureKind | 'module'
  const specifiers = parseSpecifierString(preSpecs, postSpecs)
  const startLine = line.lineNum
  const currentIndent = line.indent

  ctx.pos++

  // Parse body — everything indented more than this line
  const body = parseBlock(ctx, currentIndent)

  const endLine = ctx.pos > 0 ? ctx.lines[Math.min(ctx.pos - 1, ctx.lines.length - 1)].lineNum : startLine
  const range: SourceRange = {
    startLine,
    startCol: line.indent,
    endLine,
    endCol: 0,
  }

  if (kind === 'module') {
    return {
      type: 'ModuleDecl',
      name,
      specifiers,
      body,
      range,
    }
  }

  return {
    type: 'ClassDecl',
    name,
    kind,
    specifiers,
    decorators: [],
    parent: parentStr || undefined,
    body,
    range,
  }
}

// ─── Decorated declarations (@editable, @localizes, etc.) ────────────────────

function parseDecoratedDecl(ctx: ParseContext, parentIndent: number): Declaration {
  const decorators: Decorator[] = []
  const startLine = ctx.lines[ctx.pos].lineNum

  // Collect decorators (may span multiple lines)
  while (ctx.pos < ctx.lines.length) {
    const line = ctx.lines[ctx.pos]
    if (!line.trimmed.startsWith('@')) break

    const decoMatch = line.trimmed.match(/^@([a-zA-Z_]\w*)/)
    if (decoMatch) {
      decorators.push({ name: decoMatch[1] })
    }

    // Check if the decorator line also has a field declaration after it
    const afterDeco = line.trimmed.replace(/^@[a-zA-Z_]\w*\s*/, '')
    if (afterDeco && !afterDeco.startsWith('@')) {
      // Field is on the same line as decorator: @editable Button:button_device = button_device{}
      ctx.pos++
      const field = parseFieldFromText(afterDeco, ctx.lines[ctx.pos - 1])
      if (field) {
        field.decorators = decorators
        field.isEditable = decorators.some(d => d.name === 'editable')
        field.range.startLine = startLine
        return field
      }
      // Couldn't parse as field, return as expression
      return {
        type: 'ExpressionStmt',
        text: ctx.lines[ctx.pos - 1].trimmed,
        range: lineRange(ctx.lines[ctx.pos - 1]),
      }
    }

    ctx.pos++
  }

  // Next line should be the declaration
  if (ctx.pos < ctx.lines.length) {
    const decl = parseDeclaration(ctx, parentIndent)
    if (decl) {
      if (decl.type === 'FieldDecl' || decl.type === 'FunctionDecl' || decl.type === 'ClassDecl') {
        decl.decorators = decorators
        if (decl.type === 'FieldDecl') {
          decl.isEditable = decorators.some(d => d.name === 'editable')
        }
        decl.range.startLine = startLine
      }
      return decl
    }
  }

  // Fallback
  return {
    type: 'ExpressionStmt',
    text: decorators.map(d => '@' + d.name).join(' '),
    range: { startLine, startCol: 0, endLine: startLine, endCol: 0 },
  }
}

// ─── Function ────────────────────────────────────────────────────────────────

function tryParseFunction(ctx: ParseContext, parentIndent: number): FunctionDecl | null {
  const line = ctx.lines[ctx.pos]
  const { trimmed } = line

  // Pattern: Name(params)<specifiers>: ReturnType =
  // Also: Name<specifier>(params)<specifiers>: ReturnType =
  // ReturnType can include parens like tuple(vector3, float)
  const match = trimmed.match(
    /^([a-zA-Z_]\w*)(?:<([^>]+)>)?\s*\(([^)]*)\)((?:\s*<[^>]+>)*)\s*(?::\s*(.+?))?\s*=\s*(.*)$/
  )
  if (!match) return null

  const [, name, preSpecs, paramsStr, postSpecsStr, returnType, inlineBody] = match

  const specifiers = parseSpecifierString(preSpecs, undefined)
  // Parse post-specifiers like <suspends><decides>
  const postSpecMatches = postSpecsStr.matchAll(/<([^>]+)>/g)
  for (const m of postSpecMatches) {
    specifiers.push({ name: m[1] })
  }

  const parameters = parseParameterList(paramsStr)
  const isOverride = specifiers.some(s => s.name === 'override')
  const isSuspends = specifiers.some(s => s.name === 'suspends')

  const startLine = line.lineNum
  const currentIndent = line.indent

  ctx.pos++

  // If there's an inline body (e.g. `Foo():void = expr`), don't consume more lines
  // Otherwise skip indented body block
  let bodyStartLine: number | undefined
  if (!inlineBody?.trim()) {
    bodyStartLine = ctx.pos < ctx.lines.length ? ctx.lines[ctx.pos].lineNum : undefined
    skipIndentedBlock(ctx, currentIndent)
  }

  const endLine = ctx.pos > 0 ? ctx.lines[Math.min(ctx.pos - 1, ctx.lines.length - 1)].lineNum : startLine

  return {
    type: 'FunctionDecl',
    name,
    specifiers,
    decorators: [],
    parameters,
    returnType,
    isOverride,
    isSuspends,
    bodyStartLine,
    range: {
      startLine,
      startCol: line.indent,
      endLine,
      endCol: 0,
    },
  }
}

// ─── Field ───────────────────────────────────────────────────────────────────

function tryParseField(ctx: ParseContext): FieldDecl | null {
  const line = ctx.lines[ctx.pos]
  const field = parseFieldFromText(line.trimmed, line)
  if (field) {
    ctx.pos++
    return field
  }
  return null
}

function parseFieldFromText(text: string, line: ParseLine): FieldDecl | null {
  // Pattern: Name<specs> : Type = DefaultValue
  // Pattern: Name : Type = DefaultValue
  // Pattern: Name := value (inference)
  const match = text.match(
    /^([a-zA-Z_]\w*)(?:<([^>]+)>)?\s*:\s*([a-zA-Z_][\w.[\]?]*(?:\([^)]*\))?)\s*=\s*(.+)$/
  )
  if (match) {
    const [, name, specs, typeAnnotation, defaultValue] = match
    return {
      type: 'FieldDecl',
      name,
      specifiers: parseSpecifierString(specs, undefined),
      decorators: [],
      typeAnnotation,
      defaultValue: defaultValue.trim(),
      isVar: false,
      isEditable: false,
      range: lineRange(line),
    }
  }

  // Pattern: Name<specs> : Type (no default)
  const noDefault = text.match(
    /^([a-zA-Z_]\w*)(?:<([^>]+)>)?\s*:\s*([a-zA-Z_][\w.[\]?]*)\s*$/
  )
  if (noDefault) {
    const [, name, specs, typeAnnotation] = noDefault
    return {
      type: 'FieldDecl',
      name,
      specifiers: parseSpecifierString(specs, undefined),
      decorators: [],
      typeAnnotation,
      isVar: false,
      isEditable: false,
      range: lineRange(line),
    }
  }

  // Pattern: Name := initializer (inferred type)
  const inferredMatch = text.match(
    /^([a-zA-Z_]\w*)\s*:=\s*(.+)$/
  )
  if (inferredMatch) {
    const [, name, initializer] = inferredMatch
    // Don't match if it's a class/struct/module definition
    if (/^(class|struct|module|interface|enum)(\s*[(<:]|$)/.test(initializer)) {
      return null
    }
    return {
      type: 'FieldDecl',
      name,
      specifiers: [],
      decorators: [],
      defaultValue: initializer.trim(),
      isVar: false,
      isEditable: false,
      range: lineRange(line),
    }
  }

  return null
}

// ─── var / set ───────────────────────────────────────────────────────────────

function parseVarOrSet(ctx: ParseContext): VarDecl {
  const line = ctx.lines[ctx.pos]
  ctx.pos++

  const isVar = line.trimmed.startsWith('var ')
  const rest = line.trimmed.replace(/^(var|set)\s+/, '')

  // var Name : Type = Value
  const typedMatch = rest.match(/^([a-zA-Z_]\w*)\s*:\s*([a-zA-Z_][\w.[\]?]*)\s*=\s*(.+)$/)
  if (typedMatch) {
    return {
      type: 'VarDecl',
      name: typedMatch[1],
      typeAnnotation: typedMatch[2],
      initializer: typedMatch[3].trim(),
      isVar,
      range: lineRange(line),
    }
  }

  // var Name := Value or set Name = Value
  const inferredMatch = rest.match(/^([a-zA-Z_]\w*)\s*:?=\s*(.+)$/)
  if (inferredMatch) {
    return {
      type: 'VarDecl',
      name: inferredMatch[1],
      initializer: inferredMatch[2].trim(),
      isVar,
      range: lineRange(line),
    }
  }

  // Fallback: just the name
  const nameMatch = rest.match(/^([a-zA-Z_]\w*)/)
  return {
    type: 'VarDecl',
    name: nameMatch?.[1] ?? rest,
    isVar,
    range: lineRange(line),
  }
}

// ─── Comments ────────────────────────────────────────────────────────────────

function parseComment(ctx: ParseContext): ExpressionStmt {
  const line = ctx.lines[ctx.pos]
  ctx.comments.push({
    type: 'Comment',
    text: line.trimmed,
    isBlock: false,
    range: lineRange(line),
  })
  ctx.pos++
  // Comments don't produce declarations — return as expression stmt
  return {
    type: 'ExpressionStmt',
    text: line.trimmed,
    range: lineRange(line),
  }
}

function parseBlockComment(ctx: ParseContext): ExpressionStmt {
  const startLine = ctx.lines[ctx.pos]
  let text = startLine.trimmed
  const startLineNum = startLine.lineNum
  ctx.pos++

  // Consume until #>
  while (ctx.pos < ctx.lines.length && !text.includes('#>')) {
    text += '\n' + ctx.lines[ctx.pos].text
    ctx.pos++
  }

  const endLineNum = ctx.pos > 0 ? ctx.lines[ctx.pos - 1].lineNum : startLineNum
  ctx.comments.push({
    type: 'Comment',
    text,
    isBlock: true,
    range: { startLine: startLineNum, startCol: startLine.indent, endLine: endLineNum, endCol: 0 },
  })

  return {
    type: 'ExpressionStmt',
    text,
    range: { startLine: startLineNum, startCol: startLine.indent, endLine: endLineNum, endCol: 0 },
  }
}

// ─── Expression statement (catch-all) ────────────────────────────────────────

function parseExpressionStmt(ctx: ParseContext): ExpressionStmt {
  const line = ctx.lines[ctx.pos]
  ctx.pos++
  return {
    type: 'ExpressionStmt',
    text: line.trimmed,
    range: lineRange(line),
  }
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

function lineRange(line: ParseLine): SourceRange {
  return {
    startLine: line.lineNum,
    startCol: line.indent >= 0 ? line.indent : 0,
    endLine: line.lineNum,
    endCol: line.text.length,
  }
}

function skipIndentedBlock(ctx: ParseContext, baseIndent: number): void {
  while (ctx.pos < ctx.lines.length) {
    const line = ctx.lines[ctx.pos]
    if (line.trimmed === '') {
      ctx.pos++
      continue
    }
    if (line.indent <= baseIndent) break
    ctx.pos++
  }
}

function parseSpecifierString(pre?: string, post?: string): Specifier[] {
  const specs: Specifier[] = []
  for (const str of [pre, post]) {
    if (!str) continue
    // Could be comma-separated or space-separated specifiers
    const matches = str.matchAll(/([a-zA-Z_]\w*)(?:\{([^}]*)\})?/g)
    for (const m of matches) {
      specs.push({ name: m[1], param: m[2] })
    }
  }
  return specs
}

function parseParameterList(paramsStr: string): Parameter[] {
  if (!paramsStr.trim()) return []

  const params: Parameter[] = []
  // Split on commas, but be aware of nested generics
  const parts = splitParams(paramsStr)

  for (const part of parts) {
    const trimmed = part.trim()
    if (!trimmed) continue

    // Pattern: Name : Type
    const match = trimmed.match(/^([a-zA-Z_]\w*)\s*:\s*(.+)$/)
    if (match) {
      params.push({
        type: 'Parameter',
        name: match[1],
        typeAnnotation: match[2].trim(),
        range: { startLine: 0, startCol: 0, endLine: 0, endCol: 0 },
      })
    } else {
      params.push({
        type: 'Parameter',
        name: trimmed,
        range: { startLine: 0, startCol: 0, endLine: 0, endCol: 0 },
      })
    }
  }

  return params
}

function splitParams(str: string): string[] {
  const parts: string[] = []
  let depth = 0
  let current = ''

  for (const ch of str) {
    if (ch === '(' || ch === '[' || ch === '<') depth++
    else if (ch === ')' || ch === ']' || ch === '>') depth--
    else if (ch === ',' && depth === 0) {
      parts.push(current)
      current = ''
      continue
    }
    current += ch
  }
  if (current.trim()) parts.push(current)
  return parts
}
