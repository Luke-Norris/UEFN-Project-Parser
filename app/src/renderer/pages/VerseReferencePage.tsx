import { useEffect, useState, useMemo, useCallback, useRef } from 'react'
import { VerseHighlighter } from '../components/VerseHighlighter'

// ─── Types ───────────────────────────────────────────────────────────────────

interface Chapter {
  id: string
  filename: string
  title: string
  order: number
}

// Hardcoded path to the verse-book docs directory.
// This is local dev tooling, not a shipped product.
const VERSE_BOOK_DOCS = 'C:/Users/Luke/dev/verse-book/docs'

const CHAPTERS: Chapter[] = [
  { id: '00', filename: '00_overview.md', title: 'Overview', order: 0 },
  { id: '01', filename: '01_expressions.md', title: 'Expressions', order: 1 },
  { id: '02', filename: '02_primitives.md', title: 'Primitives', order: 2 },
  { id: '03', filename: '03_containers.md', title: 'Containers', order: 3 },
  { id: '04', filename: '04_operators.md', title: 'Operators', order: 4 },
  { id: '05', filename: '05_mutability.md', title: 'Mutability', order: 5 },
  { id: '06', filename: '06_functions.md', title: 'Functions', order: 6 },
  { id: '07', filename: '07_control.md', title: 'Control Flow', order: 7 },
  { id: '08', filename: '08_failure.md', title: 'Failure', order: 8 },
  { id: '09', filename: '09_structs_enums.md', title: 'Structs & Enums', order: 9 },
  { id: '10', filename: '10_classes_interfaces.md', title: 'Classes & Interfaces', order: 10 },
  { id: '11', filename: '11_types.md', title: 'Types', order: 11 },
  { id: '12', filename: '12_access.md', title: 'Access', order: 12 },
  { id: '13', filename: '13_effects.md', title: 'Effects', order: 13 },
  { id: '14', filename: '14_concurrency.md', title: 'Concurrency', order: 14 },
  { id: '15', filename: '15_live_variables.md', title: 'Live Variables', order: 15 },
  { id: '16', filename: '16_modules.md', title: 'Modules', order: 16 },
  { id: '17', filename: '17_persistable.md', title: 'Persistable', order: 17 },
  { id: '18', filename: '18_evolution.md', title: 'Evolution', order: 18 },
  { id: 'concept', filename: 'concept_index.md', title: 'Concept Index', order: 19 },
]

// ─── Markdown Renderer ──────────────────────────────────────────────────────

interface MdNode {
  type: 'h1' | 'h2' | 'h3' | 'h4' | 'code-block' | 'inline-code' | 'paragraph' | 'list-item' | 'hr' | 'blockquote' | 'table'
  content: string
  language?: string
  rows?: string[][]
}

function parseMarkdown(source: string): MdNode[] {
  const lines = source.split('\n')
  const nodes: MdNode[] = []
  let i = 0

  while (i < lines.length) {
    const line = lines[i]

    // Fenced code block
    if (line.trimStart().startsWith('```')) {
      const indent = line.indexOf('```')
      const lang = line.slice(indent + 3).trim()
      const codeLines: string[] = []
      i++
      while (i < lines.length) {
        if (lines[i].trimStart().startsWith('```')) {
          i++
          break
        }
        codeLines.push(lines[i])
        i++
      }
      nodes.push({ type: 'code-block', content: codeLines.join('\n'), language: lang || 'verse' })
      continue
    }

    // Horizontal rule
    if (/^---+$/.test(line.trim()) || /^\*\*\*+$/.test(line.trim())) {
      nodes.push({ type: 'hr', content: '' })
      i++
      continue
    }

    // Table: detect lines starting with |
    if (line.trim().startsWith('|') && line.trim().endsWith('|')) {
      const tableRows: string[][] = []
      while (i < lines.length && lines[i].trim().startsWith('|') && lines[i].trim().endsWith('|')) {
        const row = lines[i].trim()
        // Skip separator rows (|---|---|)
        if (/^\|[\s\-:|]+\|$/.test(row)) {
          i++
          continue
        }
        const cells = row.split('|').slice(1, -1).map(c => c.trim())
        tableRows.push(cells)
        i++
      }
      if (tableRows.length > 0) {
        nodes.push({ type: 'table', content: '', rows: tableRows })
      }
      continue
    }

    // Headings
    if (line.startsWith('#### ')) {
      nodes.push({ type: 'h4', content: line.slice(5) })
      i++
      continue
    }
    if (line.startsWith('### ')) {
      nodes.push({ type: 'h3', content: line.slice(4) })
      i++
      continue
    }
    if (line.startsWith('## ')) {
      nodes.push({ type: 'h2', content: line.slice(3) })
      i++
      continue
    }
    if (line.startsWith('# ')) {
      nodes.push({ type: 'h1', content: line.slice(2) })
      i++
      continue
    }

    // Blockquote
    if (line.startsWith('> ')) {
      const quoteLines: string[] = []
      while (i < lines.length && lines[i].startsWith('> ')) {
        quoteLines.push(lines[i].slice(2))
        i++
      }
      nodes.push({ type: 'blockquote', content: quoteLines.join('\n') })
      continue
    }

    // List items (- or * or numbered)
    if (/^\s*[-*]\s/.test(line) || /^\s*\d+\.\s/.test(line)) {
      const content = line.replace(/^\s*[-*]\s/, '').replace(/^\s*\d+\.\s/, '')
      nodes.push({ type: 'list-item', content })
      i++
      continue
    }

    // Empty lines
    if (line.trim() === '') {
      i++
      continue
    }

    // Paragraph: collect consecutive non-empty, non-special lines
    const paraLines: string[] = []
    while (i < lines.length && lines[i].trim() !== '' &&
      !lines[i].startsWith('#') && !lines[i].startsWith('```') &&
      !lines[i].startsWith('> ') && !/^\s*[-*]\s/.test(lines[i]) &&
      !/^\s*\d+\.\s/.test(lines[i]) && !lines[i].trim().startsWith('|') &&
      !/^---+$/.test(lines[i].trim()) && !/^\*\*\*+$/.test(lines[i].trim())) {
      paraLines.push(lines[i])
      i++
    }
    if (paraLines.length > 0) {
      nodes.push({ type: 'paragraph', content: paraLines.join(' ') })
    }
  }

  return nodes
}

// Render inline markdown: **bold**, *italic*, `code`, [link](url)
function renderInline(text: string, searchHighlight?: string): JSX.Element[] {
  const parts: JSX.Element[] = []
  let remaining = text
  let key = 0

  while (remaining.length > 0) {
    // Bold: **text**
    let match = remaining.match(/^(.*?)\*\*(.+?)\*\*(.*)$/s)
    if (match) {
      if (match[1]) parts.push(...renderInline(match[1], searchHighlight))
      parts.push(<strong key={key++} className="font-semibold text-white">{match[2]}</strong>)
      remaining = match[3]
      continue
    }

    // Italic: *text*
    match = remaining.match(/^(.*?)\*(.+?)\*(.*)$/s)
    if (match) {
      if (match[1]) parts.push(...renderInline(match[1], searchHighlight))
      parts.push(<em key={key++} className="italic text-gray-300">{match[2]}</em>)
      remaining = match[3]
      continue
    }

    // Inline code: `code`
    match = remaining.match(/^(.*?)`(.+?)`(.*)$/s)
    if (match) {
      if (match[1]) parts.push(...renderInline(match[1], searchHighlight))
      parts.push(
        <code key={key++} className="px-1 py-0.5 bg-fn-darker border border-fn-border rounded text-[11px] text-cyan-400 font-mono">
          {match[2]}
        </code>
      )
      remaining = match[3]
      continue
    }

    // Link: [text](url)
    match = remaining.match(/^(.*?)\[(.+?)\]\((.+?)\)(.*)$/s)
    if (match) {
      if (match[1]) parts.push(...renderInline(match[1], searchHighlight))
      parts.push(
        <span key={key++} className="text-blue-400 underline underline-offset-2 decoration-blue-400/40">
          {match[2]}
        </span>
      )
      remaining = match[4]
      continue
    }

    // Apply search highlight to plain text
    if (searchHighlight && remaining.toLowerCase().includes(searchHighlight.toLowerCase())) {
      const lowerRem = remaining.toLowerCase()
      const lowerSearch = searchHighlight.toLowerCase()
      let pos = 0
      const fragments: JSX.Element[] = []
      let idx = lowerRem.indexOf(lowerSearch, pos)
      while (idx !== -1) {
        if (idx > pos) {
          fragments.push(<span key={key++}>{remaining.slice(pos, idx)}</span>)
        }
        fragments.push(
          <mark key={key++} className="bg-yellow-400/30 text-yellow-200 rounded-sm px-[1px]">
            {remaining.slice(idx, idx + searchHighlight.length)}
          </mark>
        )
        pos = idx + searchHighlight.length
        idx = lowerRem.indexOf(lowerSearch, pos)
      }
      if (pos < remaining.length) {
        fragments.push(<span key={key++}>{remaining.slice(pos)}</span>)
      }
      parts.push(...fragments)
      remaining = ''
      continue
    }

    // Plain text
    parts.push(<span key={key++}>{remaining}</span>)
    remaining = ''
  }

  return parts
}

// ─── Component ───────────────────────────────────────────────────────────────

export function VerseReferencePage() {
  const [activeChapter, setActiveChapter] = useState<Chapter>(CHAPTERS[0])
  const [chapterContent, setChapterContent] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [searchResults, setSearchResults] = useState<Array<{ chapter: Chapter; matches: number }> | null>(null)
  const [searching, setSearching] = useState(false)
  const contentRef = useRef<HTMLDivElement>(null)

  // Load chapter content
  const loadChapter = useCallback(async (chapter: Chapter) => {
    setActiveChapter(chapter)
    setLoading(true)
    setError(null)
    try {
      const filePath = `${VERSE_BOOK_DOCS}/${chapter.filename}`
      const result = await window.electronAPI.forgeReadTextFile(filePath)
      if (result.error) {
        setError(result.error)
        setChapterContent(null)
      } else {
        setChapterContent(result.content ?? null)
      }
    } catch (err) {
      setError(`Failed to load chapter: ${err}`)
      setChapterContent(null)
    } finally {
      setLoading(false)
    }
  }, [])

  // Load first chapter on mount
  useEffect(() => {
    loadChapter(CHAPTERS[0])
  }, [loadChapter])

  // Scroll to top when chapter changes
  useEffect(() => {
    contentRef.current?.scrollTo(0, 0)
  }, [activeChapter])

  // Search across all chapters
  const handleSearch = useCallback(async (query: string) => {
    if (!query.trim()) {
      setSearchResults(null)
      return
    }
    setSearching(true)
    const results: Array<{ chapter: Chapter; matches: number }> = []
    const lowerQuery = query.toLowerCase()

    for (const chapter of CHAPTERS) {
      try {
        const filePath = `${VERSE_BOOK_DOCS}/${chapter.filename}`
        const result = await window.electronAPI.forgeReadTextFile(filePath)
        if (result.content) {
          const lowerContent = result.content.toLowerCase()
          let count = 0
          let idx = lowerContent.indexOf(lowerQuery)
          while (idx !== -1) {
            count++
            idx = lowerContent.indexOf(lowerQuery, idx + 1)
          }
          if (count > 0) {
            results.push({ chapter, matches: count })
          }
        }
      } catch {
        // Skip chapters that fail to load
      }
    }

    results.sort((a, b) => b.matches - a.matches)
    setSearchResults(results)
    setSearching(false)
  }, [])

  // Debounced search
  useEffect(() => {
    const timeout = setTimeout(() => handleSearch(search), 300)
    return () => clearTimeout(timeout)
  }, [search, handleSearch])

  // Parse the current chapter
  const parsedNodes = useMemo(() => {
    if (!chapterContent) return []
    return parseMarkdown(chapterContent)
  }, [chapterContent])

  return (
    <div className="flex-1 flex bg-fn-darker overflow-hidden">
      {/* ─── Left Sidebar: Chapter List ─── */}
      <div className="w-[200px] border-r border-fn-border bg-fn-dark flex flex-col shrink-0 overflow-hidden">
        {/* Header */}
        <div className="px-3 py-2.5 border-b border-fn-border shrink-0">
          <div className="flex items-center gap-2">
            <svg className="w-4 h-4 text-purple-400 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path d="M12 6.253v13m0-13C10.832 5.477 9.246 5 7.5 5S4.168 5.477 3 6.253v13C4.168 18.477 5.754 18 7.5 18s3.332.477 4.5 1.253m0-13C13.168 5.477 14.754 5 16.5 5c1.747 0 3.332.477 4.5 1.253v13C19.832 18.477 18.247 18 16.5 18c-1.746 0-3.332.477-4.5 1.253" />
            </svg>
            <span className="text-[11px] font-semibold text-white">Verse Reference</span>
          </div>
        </div>

        {/* Search */}
        <div className="p-2 border-b border-fn-border shrink-0">
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search docs..."
            className="w-full bg-fn-darker border border-fn-border rounded px-2 py-1.5 text-[10px] text-white placeholder-gray-600 focus:outline-none focus:border-purple-400/50"
          />
        </div>

        {/* Search results or chapter list */}
        <div className="flex-1 overflow-y-auto">
          {search.trim() && searchResults ? (
            <>
              <div className="px-2 py-1.5 text-[9px] text-gray-500 border-b border-fn-border/30">
                {searching ? 'Searching...' : `${searchResults.length} chapter${searchResults.length !== 1 ? 's' : ''} matched`}
              </div>
              {searchResults.map((result) => (
                <button
                  key={result.chapter.id}
                  onClick={() => loadChapter(result.chapter)}
                  className={`w-full flex items-center justify-between px-2.5 py-1.5 text-left transition-colors border-b border-fn-border/10 ${
                    activeChapter.id === result.chapter.id
                      ? 'bg-purple-400/10 text-purple-400'
                      : 'text-gray-400 hover:text-white hover:bg-white/[0.03]'
                  }`}
                >
                  <span className="text-[10px] truncate">{result.chapter.title}</span>
                  <span className="text-[8px] text-purple-400/70 bg-purple-400/10 px-1.5 py-0.5 rounded shrink-0 ml-1">
                    {result.matches}
                  </span>
                </button>
              ))}
            </>
          ) : (
            CHAPTERS.map((chapter) => (
              <button
                key={chapter.id}
                onClick={() => loadChapter(chapter)}
                className={`w-full flex items-center gap-2 px-2.5 py-1.5 text-left transition-colors border-b border-fn-border/10 ${
                  activeChapter.id === chapter.id
                    ? 'bg-purple-400/10 text-purple-400'
                    : 'text-gray-400 hover:text-white hover:bg-white/[0.03]'
                }`}
              >
                <span className="text-[9px] text-gray-600 font-mono w-5 shrink-0 text-right">
                  {chapter.id}
                </span>
                <span className="text-[10px] truncate">{chapter.title}</span>
              </button>
            ))
          )}
        </div>

        {/* Footer */}
        <div className="px-2 py-1.5 border-t border-fn-border text-[9px] text-gray-600 shrink-0">
          {CHAPTERS.length} chapters
        </div>
      </div>

      {/* ─── Center: Chapter Content ─── */}
      <div className="flex-1 flex flex-col overflow-hidden">
        {/* Chapter header */}
        <div className="flex items-center gap-3 px-6 py-2.5 border-b border-fn-border bg-fn-dark shrink-0">
          <svg className="w-4 h-4 text-purple-400/60 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
          </svg>
          <span className="text-[12px] font-semibold text-white">{activeChapter.title}</span>
          <span className="text-[10px] text-gray-500">{activeChapter.filename}</span>

          {/* Navigate prev/next */}
          <div className="ml-auto flex items-center gap-1 shrink-0">
            {activeChapter.order > 0 && (
              <button
                onClick={() => loadChapter(CHAPTERS[activeChapter.order - 1])}
                className="px-2 py-1 text-[10px] text-gray-400 hover:text-white bg-fn-darker border border-fn-border rounded transition-colors"
              >
                Prev
              </button>
            )}
            {activeChapter.order < CHAPTERS.length - 1 && (
              <button
                onClick={() => loadChapter(CHAPTERS[activeChapter.order + 1])}
                className="px-2 py-1 text-[10px] text-gray-400 hover:text-white bg-fn-darker border border-fn-border rounded transition-colors"
              >
                Next
              </button>
            )}
          </div>
        </div>

        {/* Content */}
        <div ref={contentRef} className="flex-1 overflow-y-auto">
          {loading ? (
            <div className="flex items-center justify-center h-full">
              <div className="text-[11px] text-gray-400">Loading chapter...</div>
            </div>
          ) : error ? (
            <div className="flex items-center justify-center h-full">
              <div className="text-center">
                <div className="text-[11px] text-red-400 mb-2">{error}</div>
                <button
                  onClick={() => loadChapter(activeChapter)}
                  className="px-3 py-1.5 text-[10px] text-white bg-fn-panel border border-fn-border rounded hover:bg-white/[0.06] transition-colors"
                >
                  Retry
                </button>
              </div>
            </div>
          ) : (
            <div className="max-w-[800px] mx-auto px-8 py-6 space-y-4">
              {parsedNodes.map((node, i) => {
                switch (node.type) {
                  case 'h1':
                    return (
                      <h1 key={i} className="text-[22px] font-bold text-white mt-6 mb-3 pb-2 border-b border-fn-border">
                        {renderInline(node.content, search || undefined)}
                      </h1>
                    )
                  case 'h2':
                    return (
                      <h2 key={i} className="text-[17px] font-bold text-gray-100 mt-5 mb-2">
                        {renderInline(node.content, search || undefined)}
                      </h2>
                    )
                  case 'h3':
                    return (
                      <h3 key={i} className="text-[14px] font-semibold text-gray-200 mt-4 mb-1.5">
                        {renderInline(node.content, search || undefined)}
                      </h3>
                    )
                  case 'h4':
                    return (
                      <h4 key={i} className="text-[12px] font-semibold text-gray-300 mt-3 mb-1">
                        {renderInline(node.content, search || undefined)}
                      </h4>
                    )
                  case 'code-block':
                    return (
                      <div key={i} className="bg-fn-dark border border-fn-border rounded-lg overflow-hidden my-3">
                        <div className="flex items-center justify-between px-3 py-1 border-b border-fn-border/50 bg-fn-darker/50">
                          <span className="text-[9px] text-gray-500 font-mono uppercase">
                            {node.language || 'verse'}
                          </span>
                        </div>
                        <div className="p-3 overflow-x-auto">
                          <VerseHighlighter
                            source={node.content}
                            fontSize={11}
                            searchHighlight={search || undefined}
                          />
                        </div>
                      </div>
                    )
                  case 'paragraph':
                    return (
                      <p key={i} className="text-[12px] text-gray-300 leading-6">
                        {renderInline(node.content, search || undefined)}
                      </p>
                    )
                  case 'list-item':
                    return (
                      <div key={i} className="flex gap-2 pl-4 text-[12px] text-gray-300 leading-6">
                        <span className="text-gray-600 shrink-0 mt-[1px]">&#x2022;</span>
                        <span>{renderInline(node.content, search || undefined)}</span>
                      </div>
                    )
                  case 'blockquote':
                    return (
                      <div key={i} className="border-l-2 border-purple-400/40 pl-4 py-1 my-2">
                        <p className="text-[12px] text-gray-400 italic leading-6">
                          {renderInline(node.content, search || undefined)}
                        </p>
                      </div>
                    )
                  case 'hr':
                    return <hr key={i} className="border-fn-border my-4" />
                  case 'table':
                    return (
                      <div key={i} className="overflow-x-auto my-3">
                        <table className="w-full text-[11px] border border-fn-border rounded">
                          {node.rows && node.rows.length > 0 && (
                            <>
                              <thead>
                                <tr className="bg-fn-dark">
                                  {node.rows[0].map((cell, ci) => (
                                    <th key={ci} className="px-3 py-1.5 text-left text-gray-300 font-semibold border-b border-fn-border">
                                      {renderInline(cell, search || undefined)}
                                    </th>
                                  ))}
                                </tr>
                              </thead>
                              <tbody>
                                {node.rows.slice(1).map((row, ri) => (
                                  <tr key={ri} className="border-b border-fn-border/30 hover:bg-white/[0.02]">
                                    {row.map((cell, ci) => (
                                      <td key={ci} className="px-3 py-1.5 text-gray-400">
                                        {renderInline(cell, search || undefined)}
                                      </td>
                                    ))}
                                  </tr>
                                ))}
                              </tbody>
                            </>
                          )}
                        </table>
                      </div>
                    )
                  default:
                    return null
                }
              })}

              {/* Bottom padding */}
              <div className="h-16" />
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
