import { useState, useMemo, useCallback, useEffect, useRef } from 'react'

// ─── Token Categories (ported from Epic's official verse.py Pygments lexer) ──

const SPECIFIERS = new Set([
  'abstract', 'computes', 'constructor', 'private', 'public', 'protected',
  'final', 'decides', 'inline', 'native', 'override', 'suspends', 'transacts',
  'internal', 'reads', 'writes', 'allocates', 'scoped', 'converges',
  'castable', 'concrete', 'unique', 'final_super', 'open', 'closed',
  'native_callable', 'module_scoped_var_weak_map_key', 'epic_internal',
  'persistable',
])

const BLOCK_KEYWORDS = new Set([
  'if', 'then', 'else', 'for', 'block', 'loop', 'array', 'case', 'map', 'option',
])

const STRUCTURE_KEYWORDS = new Set([
  'module', 'interface', 'class', 'struct', 'enum',
])

const DECL_KEYWORDS = new Set([
  'var', 'set', 'using',
])

const TYPE_KEYWORDS = new Set([
  'int', 'float', 'string', 'logic', 'char', 'any', 'void',
  'comparable', 'rational', 'tuple', 'type',
])

const RESERVED_WORDS = new Set([
  'do', 'while', 'break', 'return', 'yield', 'spawn', 'sync',
  'race', 'branch', 'Self', 'where', 'continue', 'defer', 'not',
])

const LOGICAL_OPERATORS = new Set(['and', 'or', 'not'])

const BOOLEAN_CONSTANTS = new Set(['true', 'false'])

// ─── Token types ─────────────────────────────────────────────────────────────

type TokenType =
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
  | 'search-highlight'
  | 'infile-match'
  | 'infile-match-active'

interface Token {
  text: string
  type: TokenType
}

const TOKEN_CLASSES: Record<TokenType, string> = {
  'keyword': 'text-blue-400',
  'specifier': 'text-purple-400',
  'type': 'text-cyan-400',
  'string': 'text-green-400',
  'number': 'text-orange-400',
  'comment': 'text-gray-500 italic',
  'decorator': 'text-yellow-400',
  'function-def': 'text-amber-300',
  'class-def': 'text-emerald-400 font-bold',
  'operator': 'text-gray-300',
  'punctuation': 'text-gray-400',
  'boolean': 'text-blue-400',
  'namespace': 'text-orange-400',
  'plain': '',
  'search-highlight': 'bg-yellow-400/30 text-yellow-200 rounded-sm px-[1px]',
  'infile-match': 'bg-amber-500/25 rounded-sm px-[1px]',
  'infile-match-active': 'bg-amber-400/50 outline outline-1 outline-amber-400/70 rounded-sm px-[1px]',
}

// ─── Tokenizer ───────────────────────────────────────────────────────────────

function tokenizeLine(line: string, inBlockComment: boolean): { tokens: Token[]; stillInBlockComment: boolean } {
  const tokens: Token[] = []
  let pos = 0
  const len = line.length
  let blockComment = inBlockComment

  function push(text: string, type: TokenType) {
    tokens.push({ text, type })
  }

  // If we're continuing a block comment from previous lines
  if (blockComment) {
    const endIdx = line.indexOf('#>')
    if (endIdx === -1) {
      push(line, 'comment')
      return { tokens, stillInBlockComment: true }
    }
    push(line.slice(0, endIdx + 2), 'comment')
    pos = endIdx + 2
    blockComment = false
  }

  while (pos < len) {
    // Block comment start: <#
    if (line[pos] === '<' && pos + 1 < len && line[pos + 1] === '#') {
      const endIdx = line.indexOf('#>', pos + 2)
      if (endIdx === -1) {
        push(line.slice(pos), 'comment')
        return { tokens, stillInBlockComment: true }
      }
      push(line.slice(pos, endIdx + 2), 'comment')
      pos = endIdx + 2
      continue
    }

    // Line comment: # to end
    if (line[pos] === '#') {
      push(line.slice(pos), 'comment')
      break
    }

    // Decorators: @word
    if (line[pos] === '@') {
      const match = line.slice(pos).match(/^@[a-zA-Z_]\w*/)
      if (match) {
        push(match[0], 'decorator')
        pos += match[0].length
        continue
      }
    }

    // Specifiers in angle brackets: <public>, <suspends>, <decides>{...}>
    if (line[pos] === '<') {
      const match = line.slice(pos).match(/^<([a-zA-Z_]\w*)(\{[^}]*\})?>/)
      if (match && SPECIFIERS.has(match[1])) {
        push(match[0], 'specifier')
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
      if (end < len) {
        str += '"'
        end++
      }
      push(str, 'string')
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
      if (end < len) {
        str += "'"
        end++
      }
      push(str, 'string')
      pos = end
      continue
    }

    // Numbers: hex, binary, octal, float, integer
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
        push(numMatch[0], 'number')
        pos += numMatch[0].length
        continue
      }
    }

    // Multi-char operators: :=, =>, ->, .., +=, -=, *=, /=, ==, !=, <=, >=
    if (pos + 1 < len) {
      const two = line.slice(pos, pos + 2)
      if ([':=', '=>', '->', '..', '+=', '-=', '*=', '/=', '==', '!=', '<=', '>='].includes(two)) {
        push(two, 'operator')
        pos += 2
        continue
      }
    }

    // Single-char operators
    if ('+-*/%=<>?:'.includes(line[pos])) {
      push(line[pos], 'operator')
      pos++
      continue
    }

    // Punctuation
    if ('{}[](),;.'.includes(line[pos])) {
      push(line[pos], 'punctuation')
      pos++
      continue
    }

    // Words: keywords, types, identifiers
    if (/[a-zA-Z_]/.test(line[pos])) {
      const match = line.slice(pos).match(/^[a-zA-Z_]\w*/)
      if (match) {
        const word = match[0]

        // Check what follows for context-sensitive classification
        const afterWord = line.slice(pos + word.length)

        // Data structure definition: class/struct/enum/interface followed by identifier
        if (STRUCTURE_KEYWORDS.has(word)) {
          // Check if this is `:= class` pattern (definition name comes before)
          push(word, 'keyword')
          pos += word.length
          // Try to capture the class name after whitespace
          const nameMatch = afterWord.match(/^(\s*)(\(?)/)
          if (nameMatch && nameMatch[2] !== '(') {
            // Might be: `class<public>` or `class(Parent)`
            const trailingName = afterWord.match(/^(\s+)([a-zA-Z_]\w*)/)
            if (trailingName) {
              push(trailingName[1], 'plain')
              push(trailingName[2], 'class-def')
              pos += trailingName[0].length
            }
          }
          continue
        }

        // Function call or definition: word followed by optional specifiers then (
        const funcCheck = afterWord.match(/^(\s*(?:<[^>]+>\s*)*)(\()/)
        if (funcCheck) {
          push(word, 'function-def')
          pos += word.length
          continue
        }

        if (BLOCK_KEYWORDS.has(word)) {
          push(word, 'keyword')
        } else if (DECL_KEYWORDS.has(word)) {
          push(word, 'keyword')
        } else if (TYPE_KEYWORDS.has(word)) {
          push(word, 'type')
        } else if (RESERVED_WORDS.has(word)) {
          push(word, 'keyword')
        } else if (LOGICAL_OPERATORS.has(word)) {
          push(word, 'operator')
        } else if (BOOLEAN_CONSTANTS.has(word)) {
          push(word, 'boolean')
        } else if (SPECIFIERS.has(word)) {
          push(word, 'specifier')
        } else {
          push(word, 'plain')
        }

        pos += word.length
        continue
      }
    }

    // Whitespace
    if (/\s/.test(line[pos])) {
      let end = pos + 1
      while (end < len && /\s/.test(line[end])) end++
      push(line.slice(pos, end), 'plain')
      pos = end
      continue
    }

    // Catch-all: any other character
    push(line[pos], 'plain')
    pos++
  }

  return { tokens, stillInBlockComment: blockComment }
}

// Post-process: identify class-def names in `:= class/struct/enum` patterns
function postProcessTokens(tokens: Token[]): Token[] {
  for (let i = 0; i < tokens.length; i++) {
    // Look for pattern: [identifier] [:=] [class/struct/enum/interface]
    if (
      tokens[i].type === 'operator' &&
      tokens[i].text === ':=' &&
      i >= 1 && i + 1 < tokens.length
    ) {
      // Find preceding non-whitespace token
      let prevIdx = i - 1
      while (prevIdx >= 0 && tokens[prevIdx].type === 'plain' && tokens[prevIdx].text.trim() === '') {
        prevIdx--
      }
      // Find following non-whitespace token
      let nextIdx = i + 1
      while (nextIdx < tokens.length && tokens[nextIdx].type === 'plain' && tokens[nextIdx].text.trim() === '') {
        nextIdx++
      }
      // If what follows is a structure keyword, the preceding identifier is a class-def
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

// ─── Tokenize complete source ────────────────────────────────────────────────

interface TokenizedLine {
  tokens: Token[]
}

function tokenizeSource(source: string): TokenizedLine[] {
  if (!source) return [{ tokens: [{ type: 'comment', text: '// Empty file' }] }]
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

// ─── Search highlight overlay ────────────────────────────────────────────────

function applySearchHighlight(tokens: Token[], searchTerm: string): Token[] {
  if (!searchTerm) return tokens
  const lowerSearch = searchTerm.toLowerCase()
  const result: Token[] = []

  for (const token of tokens) {
    const lowerText = token.text.toLowerCase()
    let pos = 0
    let searchIdx = lowerText.indexOf(lowerSearch, pos)

    if (searchIdx === -1) {
      result.push(token)
      continue
    }

    while (searchIdx !== -1) {
      // Text before match
      if (searchIdx > pos) {
        result.push({ text: token.text.slice(pos, searchIdx), type: token.type })
      }
      // The match
      result.push({
        text: token.text.slice(searchIdx, searchIdx + searchTerm.length),
        type: 'search-highlight',
      })
      pos = searchIdx + searchTerm.length
      searchIdx = lowerText.indexOf(lowerSearch, pos)
    }

    // Remaining text after last match
    if (pos < token.text.length) {
      result.push({ text: token.text.slice(pos), type: token.type })
    }
  }

  return result
}

// ─── In-file search highlight (with active match tracking) ───────────────────

function applyInFileHighlight(
  tokens: Token[],
  searchTerm: string,
  lineNumber: number,
  activeMatchLine: number,
  activeMatchIndex: number,
  matchCountBefore: number,
): Token[] {
  if (!searchTerm) return tokens
  const lowerSearch = searchTerm.toLowerCase()
  const result: Token[] = []
  let matchInLine = 0

  for (const token of tokens) {
    const lowerText = token.text.toLowerCase()
    let pos = 0
    let searchIdx = lowerText.indexOf(lowerSearch, pos)

    if (searchIdx === -1) {
      result.push(token)
      continue
    }

    while (searchIdx !== -1) {
      if (searchIdx > pos) {
        result.push({ text: token.text.slice(pos, searchIdx), type: token.type })
      }
      const globalMatchIdx = matchCountBefore + matchInLine
      const isActive = lineNumber === activeMatchLine && globalMatchIdx === activeMatchIndex
      result.push({
        text: token.text.slice(searchIdx, searchIdx + searchTerm.length),
        type: isActive ? 'infile-match-active' : 'infile-match',
      })
      matchInLine++
      pos = searchIdx + searchTerm.length
      searchIdx = lowerText.indexOf(lowerSearch, pos)
    }

    if (pos < token.text.length) {
      result.push({ text: token.text.slice(pos), type: token.type })
    }
  }

  return result
}

// Count matches in a line's raw text
function countMatchesInLine(lineText: string, searchTerm: string): number {
  if (!searchTerm) return 0
  const lower = lineText.toLowerCase()
  const lowerSearch = searchTerm.toLowerCase()
  let count = 0
  let pos = 0
  while (true) {
    const idx = lower.indexOf(lowerSearch, pos)
    if (idx === -1) break
    count++
    pos = idx + lowerSearch.length
  }
  return count
}

// ─── Component ───────────────────────────────────────────────────────────────

interface VerseHighlighterProps {
  source: string
  fontSize?: number
  searchHighlight?: string
  scrollToLine?: number
  highlightLines?: number[]
  onLineClick?: (lineNum: number) => void
}

export function VerseHighlighter({
  source,
  fontSize = 12,
  searchHighlight,
  scrollToLine,
  highlightLines,
  onLineClick,
}: VerseHighlighterProps) {
  const [copiedLine, setCopiedLine] = useState<number | null>(null)
  const [flashLine, setFlashLine] = useState<number | null>(null)

  // In-file search state
  const [showSearch, setShowSearch] = useState(false)
  const [inFileSearch, setInFileSearch] = useState('')
  const [activeMatchIdx, setActiveMatchIdx] = useState(0)
  const searchInputRef = useRef<HTMLInputElement>(null)
  const containerRef = useRef<HTMLDivElement>(null)

  const tokenizedLines = useMemo(() => tokenizeSource(source), [source])

  const sourceLines = useMemo(() => (source || '').split('\n'), [source])

  // Compute per-line match counts and total
  const { matchesPerLine, totalMatches, matchLineMap } = useMemo(() => {
    if (!inFileSearch) return { matchesPerLine: [] as number[], totalMatches: 0, matchLineMap: [] as { line: number; indexInLine: number }[] }
    const perLine: number[] = []
    const lineMap: { line: number; indexInLine: number }[] = []
    let total = 0
    for (let i = 0; i < sourceLines.length; i++) {
      const c = countMatchesInLine(sourceLines[i], inFileSearch)
      perLine.push(c)
      for (let j = 0; j < c; j++) {
        lineMap.push({ line: i, indexInLine: j })
      }
      total += c
    }
    return { matchesPerLine: perLine, totalMatches: total, matchLineMap: lineMap }
  }, [sourceLines, inFileSearch])

  // Cumulative match count before each line (for active match tracking)
  const cumulativeMatchesBefore = useMemo(() => {
    const cum: number[] = []
    let total = 0
    for (const c of matchesPerLine) {
      cum.push(total)
      total += c
    }
    return cum
  }, [matchesPerLine])

  // Active match's line number
  const activeMatchLine = useMemo(() => {
    if (totalMatches === 0 || !matchLineMap[activeMatchIdx]) return -1
    return matchLineMap[activeMatchIdx].line
  }, [totalMatches, activeMatchIdx, matchLineMap])

  // Reset active match when search changes
  useEffect(() => {
    setActiveMatchIdx(0)
  }, [inFileSearch])

  // Scroll to active match line
  useEffect(() => {
    if (activeMatchLine >= 0 && containerRef.current) {
      const el = containerRef.current.querySelector(`[data-line-id="verse-line-${activeMatchLine + 1}"]`)
      if (el) {
        el.scrollIntoView({ behavior: 'smooth', block: 'center' })
      }
    }
  }, [activeMatchLine, activeMatchIdx])

  // Keyboard shortcut: Ctrl+F to open search
  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if ((e.ctrlKey || e.metaKey) && e.key === 'f') {
        // Only capture if our container or its children are focused, or it's a global intent
        if (containerRef.current) {
          e.preventDefault()
          setShowSearch(true)
          setTimeout(() => searchInputRef.current?.focus(), 50)
        }
      }
      if (e.key === 'Escape' && showSearch) {
        setShowSearch(false)
        setInFileSearch('')
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [showSearch])

  // scrollToLine support
  useEffect(() => {
    if (scrollToLine != null && scrollToLine > 0 && containerRef.current) {
      const el = containerRef.current.querySelector(`[data-line-id="verse-line-${scrollToLine}"]`)
      if (el) {
        el.scrollIntoView({ behavior: 'smooth', block: 'center' })
        setFlashLine(scrollToLine)
        const timer = setTimeout(() => setFlashLine(null), 1200)
        return () => clearTimeout(timer)
      }
    }
  }, [scrollToLine])

  const handleCopyLine = useCallback(async (lineIdx: number) => {
    const lineText = source.split('\n')[lineIdx] ?? ''
    await navigator.clipboard.writeText(lineText)
    setCopiedLine(lineIdx)
    setTimeout(() => setCopiedLine(null), 1500)
  }, [source])

  const goToPrevMatch = useCallback(() => {
    if (totalMatches === 0) return
    setActiveMatchIdx((prev) => (prev - 1 + totalMatches) % totalMatches)
  }, [totalMatches])

  const goToNextMatch = useCallback(() => {
    if (totalMatches === 0) return
    setActiveMatchIdx((prev) => (prev + 1) % totalMatches)
  }, [totalMatches])

  const handleSearchKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      if (e.shiftKey) goToPrevMatch()
      else goToNextMatch()
    }
    if (e.key === 'Escape') {
      setShowSearch(false)
      setInFileSearch('')
    }
  }, [goToNextMatch, goToPrevMatch])

  const lineNumWidth = String(tokenizedLines.length).length * 8 + 16

  const highlightSet = useMemo(() => new Set(highlightLines ?? []), [highlightLines])

  return (
    <div ref={containerRef} className="relative flex flex-col h-full">
      {/* In-file search bar */}
      {showSearch && (
        <div className="flex items-center gap-2 px-3 py-1.5 bg-fn-dark border-b border-fn-border shrink-0">
          <svg className="w-3.5 h-3.5 text-gray-500 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
          </svg>
          <input
            ref={searchInputRef}
            type="text"
            value={inFileSearch}
            onChange={(e) => setInFileSearch(e.target.value)}
            onKeyDown={handleSearchKeyDown}
            placeholder="Find in file..."
            className="flex-1 bg-fn-darker border border-fn-border rounded px-2 py-1 text-[11px] text-white placeholder-gray-600 focus:outline-none focus:border-blue-400/50 min-w-0"
            autoFocus
          />
          <span className="text-[10px] text-gray-500 shrink-0 tabular-nums min-w-[70px] text-center">
            {inFileSearch ? (
              totalMatches > 0
                ? `${activeMatchIdx + 1} of ${totalMatches}`
                : 'No matches'
            ) : ''}
          </span>
          <button
            onClick={goToPrevMatch}
            disabled={totalMatches === 0}
            className="p-1 text-gray-400 hover:text-white disabled:text-gray-700 disabled:cursor-default transition-colors"
            title="Previous match (Shift+Enter)"
          >
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M5 15l7-7 7 7" />
            </svg>
          </button>
          <button
            onClick={goToNextMatch}
            disabled={totalMatches === 0}
            className="p-1 text-gray-400 hover:text-white disabled:text-gray-700 disabled:cursor-default transition-colors"
            title="Next match (Enter)"
          >
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M19 9l-7 7-7-7" />
            </svg>
          </button>
          <button
            onClick={() => { setShowSearch(false); setInFileSearch('') }}
            className="p-1 text-gray-400 hover:text-white transition-colors"
            title="Close (Esc)"
          >
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>
      )}

      <div className="flex-1 overflow-auto">
        <pre
          className="leading-5 text-gray-300 whitespace-pre"
          style={{ fontFamily: "'JetBrains Mono', 'Cascadia Code', 'Fira Code', monospace", fontSize: `${fontSize}px` }}
        >
          {tokenizedLines.map((line, i) => {
            const lineNum = i + 1
            const isHighlighted = highlightSet.has(lineNum)
            const isFlashing = flashLine === lineNum

            // Apply search highlights
            let displayTokens = line.tokens
            if (searchHighlight) {
              displayTokens = applySearchHighlight(displayTokens, searchHighlight)
            }
            if (inFileSearch && matchesPerLine[i] > 0) {
              displayTokens = applyInFileHighlight(
                line.tokens, // use original tokens for in-file, not double-processed
                inFileSearch,
                i,
                activeMatchLine,
                activeMatchIdx,
                cumulativeMatchesBefore[i],
              )
              // Also apply searchHighlight on top if both exist
              if (searchHighlight) {
                displayTokens = applySearchHighlight(displayTokens, searchHighlight)
              }
            }

            return (
              <div
                key={i}
                data-line-id={`verse-line-${lineNum}`}
                className={`flex hover:bg-white/[0.02] group relative ${
                  isHighlighted ? 'bg-blue-400/10' : ''
                } ${isFlashing ? 'animate-flash-line' : ''}`}
                onDoubleClick={() => handleCopyLine(i)}
                title="Double-click to copy line"
              >
                {/* Line number */}
                <span
                  className={`inline-block pr-3 text-right select-none shrink-0 sticky left-0 bg-fn-darker border-r border-fn-border/30 cursor-pointer hover:text-blue-400 ${
                    isHighlighted ? 'text-blue-400 bg-blue-400/5' : 'text-gray-600'
                  }`}
                  style={{ width: lineNumWidth, fontSize: `${fontSize}px` }}
                  onClick={() => onLineClick?.(lineNum)}
                >
                  {lineNum}
                </span>

                {/* Tokens */}
                <span className="flex-1 pl-3">
                  {displayTokens.length === 0 ? '\n' : displayTokens.map((token, j) => (
                    <span key={j} className={TOKEN_CLASSES[token.type]}>
                      {token.text}
                    </span>
                  ))}
                </span>

                {/* Copy indicator */}
                {copiedLine === i && (
                  <span className="absolute right-2 top-0 text-[9px] text-green-400 bg-fn-darker px-1.5 py-0.5 rounded border border-green-400/20">
                    Copied
                  </span>
                )}
              </div>
            )
          })}
        </pre>
      </div>

      {/* Flash animation style */}
      <style>{`
        @keyframes flash-line-bg {
          0% { background-color: rgba(96, 165, 250, 0.25); }
          50% { background-color: rgba(96, 165, 250, 0.15); }
          100% { background-color: transparent; }
        }
        .animate-flash-line {
          animation: flash-line-bg 1.2s ease-out;
        }
      `}</style>
    </div>
  )
}
