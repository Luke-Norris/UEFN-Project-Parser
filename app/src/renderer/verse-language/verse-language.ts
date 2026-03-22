/**
 * CodeMirror 6 language support for Verse.
 * Uses StreamLanguage to wrap our existing tokenizer — no Lezer build step needed.
 */

import { StreamLanguage, type StreamParser } from '@codemirror/language'
import { tags as t } from '@lezer/highlight'
import {
  SPECIFIERS, BLOCK_KEYWORDS, STRUCTURE_KEYWORDS, DECL_KEYWORDS,
  TYPE_KEYWORDS, RESERVED_WORDS, LOGICAL_OPERATORS, BOOLEAN_CONSTANTS,
} from './verse-tokenizer'

// ─── Stream parser state ─────────────────────────────────────────────────────

interface VerseState {
  inBlockComment: boolean
}

// ─── Stream parser definition ────────────────────────────────────────────────

const verseStreamParser: StreamParser<VerseState> = {
  name: 'verse',

  startState(): VerseState {
    return { inBlockComment: false }
  },

  copyState(state: VerseState): VerseState {
    return { inBlockComment: state.inBlockComment }
  },

  token(stream, state): string | null {
    // Handle block comments
    if (state.inBlockComment) {
      const endIdx = stream.string.indexOf('#>', stream.pos)
      if (endIdx === -1) {
        stream.skipToEnd()
        return 'blockComment'
      }
      stream.pos = endIdx + 2
      state.inBlockComment = false
      return 'blockComment'
    }

    // Skip whitespace
    if (stream.eatSpace()) return null

    // Block comment start: <#
    if (stream.match('<#')) {
      const endIdx = stream.string.indexOf('#>', stream.pos)
      if (endIdx === -1) {
        stream.skipToEnd()
        state.inBlockComment = true
        return 'blockComment'
      }
      stream.pos = endIdx + 2
      return 'blockComment'
    }

    // Line comment: # to end
    if (stream.match('#')) {
      stream.skipToEnd()
      return 'lineComment'
    }

    // Decorator: @word
    if (stream.match(/^@[a-zA-Z_]\w*/)) {
      return 'meta'
    }

    // Specifier in angle brackets: <public>, <suspends{...}>, etc.
    if (stream.peek() === '<') {
      const match = stream.string.slice(stream.pos).match(/^<([a-zA-Z_]\w*)(\{[^}]*\})?>/)
      if (match && SPECIFIERS.has(match[1])) {
        stream.pos += match[0].length
        return 'annotation'
      }
    }

    // String literals
    if (stream.peek() === '"' || stream.peek() === "'") {
      const quote = stream.next()!
      while (!stream.eol()) {
        const ch = stream.next()
        if (ch === '\\') stream.next() // skip escaped char
        else if (ch === quote) break
      }
      return 'string'
    }

    // Numbers
    if (stream.match(/^0b[01_]+/) ||
        stream.match(/^0o[0-7_]+/) ||
        stream.match(/^0x[0-9a-fA-F_]+/) ||
        stream.match(/^[0-9]+\.[0-9]*([eE][+-]?[0-9]+)?/) ||
        stream.match(/^\.[0-9]+([eE][+-]?[0-9]+)?/) ||
        stream.match(/^[0-9]+([eE][+-]?[0-9]+)/) ||
        stream.match(/^[0-9][0-9_]*/)) {
      return 'number'
    }

    // Multi-char operators
    if (stream.match(':=') || stream.match('=>') || stream.match('->') ||
        stream.match('..') || stream.match('+=') || stream.match('-=') ||
        stream.match('*=') || stream.match('/=') || stream.match('==') ||
        stream.match('!=') || stream.match('<=') || stream.match('>=')) {
      return 'operator'
    }

    // Single-char operators
    if (stream.match(/^[+\-*/%=<>?:]/)) {
      return 'operator'
    }

    // Punctuation
    if (stream.match(/^[{}[\](),;.]/)) {
      return 'punctuation'
    }

    // Words
    const wordMatch = stream.match(/^[a-zA-Z_]\w*/)
    if (wordMatch) {
      const word = wordMatch as unknown as string
      const w = typeof word === 'string' ? word : String(word)

      if (STRUCTURE_KEYWORDS.has(w)) return 'keyword'
      if (BLOCK_KEYWORDS.has(w)) return 'keyword'
      if (DECL_KEYWORDS.has(w)) return 'keyword'
      if (RESERVED_WORDS.has(w)) return 'keyword'
      if (TYPE_KEYWORDS.has(w)) return 'typeName'
      if (LOGICAL_OPERATORS.has(w)) return 'logicOperator'
      if (BOOLEAN_CONSTANTS.has(w)) return 'bool'
      if (SPECIFIERS.has(w)) return 'annotation'

      // Check if it's a function call/def: word followed by (
      const rest = stream.string.slice(stream.pos)
      if (/^(\s*(?:<[^>]+>\s*)*)(\()/.test(rest)) return 'function(definition)'

      // Check if it's a class definition: word := class/struct/enum
      // (post-process will handle this via decoration, but we can hint)
      return 'variableName'
    }

    // Skip anything else
    stream.next()
    return null
  },

  tokenTable: {
    'blockComment': t.blockComment,
    'lineComment': t.lineComment,
    'string': t.string,
    'number': t.number,
    'keyword': t.keyword,
    'typeName': t.typeName,
    'annotation': t.annotation,
    'meta': t.meta,
    'operator': t.operator,
    'logicOperator': t.logicOperator,
    'bool': t.bool,
    'punctuation': t.punctuation,
    'function(definition)': t.function(t.definition(t.variableName)),
    'variableName': t.variableName,
  },
}

// ─── Language instance ───────────────────────────────────────────────────────

export const verseLanguage = StreamLanguage.define(verseStreamParser)
