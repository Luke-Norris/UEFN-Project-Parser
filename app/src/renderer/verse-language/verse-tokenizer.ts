/**
 * Verse language tokenizer — extracted from VerseHighlighter.tsx
 * Shared between VerseHighlighter (display) and CodeMirror language support.
 * Ported from Epic's official verse.py Pygments lexer.
 */

// ─── Token Categories ────────────────────────────────────────────────────────

export const SPECIFIERS = new Set([
  'abstract', 'computes', 'constructor', 'private', 'public', 'protected',
  'final', 'decides', 'inline', 'native', 'override', 'suspends', 'transacts',
  'internal', 'reads', 'writes', 'allocates', 'scoped', 'converges',
  'castable', 'concrete', 'unique', 'final_super', 'open', 'closed',
  'native_callable', 'module_scoped_var_weak_map_key', 'epic_internal',
  'persistable',
])

export const BLOCK_KEYWORDS = new Set([
  'if', 'then', 'else', 'for', 'block', 'loop', 'array', 'case', 'map', 'option',
])

export const STRUCTURE_KEYWORDS = new Set([
  'module', 'interface', 'class', 'struct', 'enum',
])

export const DECL_KEYWORDS = new Set([
  'var', 'set', 'using',
])

export const TYPE_KEYWORDS = new Set([
  'int', 'float', 'string', 'logic', 'char', 'any', 'void',
  'comparable', 'rational', 'tuple', 'type',
])

export const RESERVED_WORDS = new Set([
  'do', 'while', 'break', 'return', 'yield', 'spawn', 'sync',
  'race', 'branch', 'Self', 'where', 'continue', 'defer', 'not',
])

export const LOGICAL_OPERATORS = new Set(['and', 'or', 'not'])

export const BOOLEAN_CONSTANTS = new Set(['true', 'false'])

// ─── Token types ─────────────────────────────────────────────────────────────

export type TokenType =
  | 'keyword'
  | 'specifier'
  | 'type'
  | 'string'
  | 'number'
  | 'comment'
  | 'decorator'
  | 'function-def'
  | 'class-def'
  | 'operator'
  | 'punctuation'
  | 'boolean'
  | 'namespace'
  | 'plain'

export interface Token {
  text: string
  type: TokenType
  offset: number // character offset within the line
}

// ─── Multi-char operators ────────────────────────────────────────────────────

const MULTI_CHAR_OPS = [':=', '=>', '->', '..', '+=', '-=', '*=', '/=', '==', '!=', '<=', '>=']
const SINGLE_CHAR_OPS = '+-*/%=<>?:'
const PUNCTUATION = '{}[](),;.'

// ─── Tokenizer ───────────────────────────────────────────────────────────────

export function tokenizeLine(
  line: string,
  inBlockComment: boolean,
): { tokens: Token[]; stillInBlockComment: boolean } {
  const tokens: Token[] = []
  let pos = 0
  const len = line.length
  let blockComment = inBlockComment

  function push(text: string, type: TokenType, offset: number) {
    tokens.push({ text, type, offset })
  }

  // Continuing a block comment from previous lines
  if (blockComment) {
    const endIdx = line.indexOf('#>')
    if (endIdx === -1) {
      push(line, 'comment', 0)
      return { tokens, stillInBlockComment: true }
    }
    push(line.slice(0, endIdx + 2), 'comment', 0)
    pos = endIdx + 2
    blockComment = false
  }

  while (pos < len) {
    // Block comment start: <#
    if (line[pos] === '<' && pos + 1 < len && line[pos + 1] === '#') {
      const endIdx = line.indexOf('#>', pos + 2)
      if (endIdx === -1) {
        push(line.slice(pos), 'comment', pos)
        return { tokens, stillInBlockComment: true }
      }
      push(line.slice(pos, endIdx + 2), 'comment', pos)
      pos = endIdx + 2
      continue
    }

    // Line comment: # to end
    if (line[pos] === '#') {
      push(line.slice(pos), 'comment', pos)
      break
    }

    // Decorators: @word
    if (line[pos] === '@') {
      const match = line.slice(pos).match(/^@[a-zA-Z_]\w*/)
      if (match) {
        push(match[0], 'decorator', pos)
        pos += match[0].length
        continue
      }
    }

    // Specifiers in angle brackets: <public>, <suspends>, etc.
    if (line[pos] === '<') {
      const match = line.slice(pos).match(/^<([a-zA-Z_]\w*)(\{[^}]*\})?>/)
      if (match && SPECIFIERS.has(match[1])) {
        push(match[0], 'specifier', pos)
        pos += match[0].length
        continue
      }
    }

    // String literals: "..."
    if (line[pos] === '"') {
      let end = pos + 1
      let str = '"'
      while (end < len && line[end] !== '"') {
        if (line[end] === '\\' && end + 1 < len) {
          str += line[end] + line[end + 1]
          end += 2
        } else {
          str += line[end]
          end++
        }
      }
      if (end < len) { str += '"'; end++ }
      push(str, 'string', pos)
      pos = end
      continue
    }

    // Single-quoted strings: '...'
    if (line[pos] === "'") {
      let end = pos + 1
      let str = "'"
      while (end < len && line[end] !== "'") {
        if (line[end] === '\\' && end + 1 < len) {
          str += line[end] + line[end + 1]
          end += 2
        } else {
          str += line[end]
          end++
        }
      }
      if (end < len) { str += "'"; end++ }
      push(str, 'string', pos)
      pos = end
      continue
    }

    // Numbers
    if (/[0-9]/.test(line[pos]) || (line[pos] === '.' && pos + 1 < len && /[0-9]/.test(line[pos + 1]))) {
      const rest = line.slice(pos)
      const numMatch =
        rest.match(/^0b[01_]+/) ||
        rest.match(/^0o[0-7_]+/) ||
        rest.match(/^0x[0-9a-fA-F_]+/) ||
        rest.match(/^[0-9]+\.[0-9]*([eE][+-]?[0-9]+)?/) ||
        rest.match(/^\.[0-9]+([eE][+-]?[0-9]+)?/) ||
        rest.match(/^[0-9]+([eE][+-]?[0-9]+)/) ||
        rest.match(/^[0-9][0-9_]*/)
      if (numMatch) {
        push(numMatch[0], 'number', pos)
        pos += numMatch[0].length
        continue
      }
    }

    // Multi-char operators
    if (pos + 1 < len) {
      const two = line.slice(pos, pos + 2)
      if (MULTI_CHAR_OPS.includes(two)) {
        push(two, 'operator', pos)
        pos += 2
        continue
      }
    }

    // Single-char operators
    if (SINGLE_CHAR_OPS.includes(line[pos])) {
      push(line[pos], 'operator', pos)
      pos++
      continue
    }

    // Punctuation
    if (PUNCTUATION.includes(line[pos])) {
      push(line[pos], 'punctuation', pos)
      pos++
      continue
    }

    // Words: keywords, types, identifiers
    if (/[a-zA-Z_]/.test(line[pos])) {
      const match = line.slice(pos).match(/^[a-zA-Z_]\w*/)
      if (match) {
        const word = match[0]
        const afterWord = line.slice(pos + word.length)

        if (STRUCTURE_KEYWORDS.has(word)) {
          push(word, 'keyword', pos)
          pos += word.length
          const nameMatch = afterWord.match(/^(\s*)(\(?)/)
          if (nameMatch && nameMatch[2] !== '(') {
            const trailingName = afterWord.match(/^(\s+)([a-zA-Z_]\w*)/)
            if (trailingName) {
              push(trailingName[1], 'plain', pos)
              push(trailingName[2], 'class-def', pos + trailingName[1].length)
              pos += trailingName[0].length
            }
          }
          continue
        }

        // Function call/definition: word followed by optional specifiers then (
        const funcCheck = afterWord.match(/^(\s*(?:<[^>]+>\s*)*)(\()/)
        if (funcCheck) {
          push(word, 'function-def', pos)
          pos += word.length
          continue
        }

        if (BLOCK_KEYWORDS.has(word)) {
          push(word, 'keyword', pos)
        } else if (DECL_KEYWORDS.has(word)) {
          push(word, 'keyword', pos)
        } else if (TYPE_KEYWORDS.has(word)) {
          push(word, 'type', pos)
        } else if (RESERVED_WORDS.has(word)) {
          push(word, 'keyword', pos)
        } else if (LOGICAL_OPERATORS.has(word)) {
          push(word, 'operator', pos)
        } else if (BOOLEAN_CONSTANTS.has(word)) {
          push(word, 'boolean', pos)
        } else if (SPECIFIERS.has(word)) {
          push(word, 'specifier', pos)
        } else {
          push(word, 'plain', pos)
        }

        pos += word.length
        continue
      }
    }

    // Whitespace
    if (/\s/.test(line[pos])) {
      let end = pos + 1
      while (end < len && /\s/.test(line[end])) end++
      push(line.slice(pos, end), 'plain', pos)
      pos = end
      continue
    }

    // Catch-all
    push(line[pos], 'plain', pos)
    pos++
  }

  return { tokens, stillInBlockComment: blockComment }
}

// Post-process: identify class-def names in `:= class/struct/enum` patterns
export function postProcessTokens(tokens: Token[]): Token[] {
  for (let i = 0; i < tokens.length; i++) {
    if (
      tokens[i].type === 'operator' &&
      tokens[i].text === ':=' &&
      i >= 1 && i + 1 < tokens.length
    ) {
      let prevIdx = i - 1
      while (prevIdx >= 0 && tokens[prevIdx].type === 'plain' && tokens[prevIdx].text.trim() === '') {
        prevIdx--
      }
      let nextIdx = i + 1
      while (nextIdx < tokens.length && tokens[nextIdx].type === 'plain' && tokens[nextIdx].text.trim() === '') {
        nextIdx++
      }
      if (
        prevIdx >= 0 &&
        nextIdx < tokens.length &&
        tokens[nextIdx].type === 'keyword' &&
        STRUCTURE_KEYWORDS.has(tokens[nextIdx].text) &&
        tokens[prevIdx].type === 'plain' &&
        /^[a-zA-Z_]\w*$/.test(tokens[prevIdx].text)
      ) {
        tokens[prevIdx] = { ...tokens[prevIdx], type: 'class-def' }
      }
    }
  }
  return tokens
}

// Tokenize complete source into lines
export interface TokenizedLine {
  tokens: Token[]
}

export function tokenizeSource(source: string): TokenizedLine[] {
  if (!source) return [{ tokens: [{ type: 'comment', text: '// Empty file', offset: 0 }] }]
  const lines = source.split('\n')
  const result: TokenizedLine[] = []
  let inBlockComment = false

  for (const line of lines) {
    const { tokens, stillInBlockComment } = tokenizeLine(line, inBlockComment)
    const processed = postProcessTokens(tokens)
    result.push({ tokens: processed })
    inBlockComment = stillInBlockComment
  }

  return result
}
