import { useEffect, useState, useMemo, useCallback, useRef } from 'react'
import { useLibraryStore } from '../stores/libraryStore'
import { useSettingsStore } from '../stores/settingsStore'
import { VerseHighlighter } from '../components/VerseHighlighter'

interface VerseFileEntry {
  name: string
  filePath: string
  relativePath: string
  lineCount: number
  projectFolder?: string
  projectName?: string
  classes?: string[]
  functions?: string[]
}

interface VerseFileContent {
  filePath: string
  name: string
  content: string
  lineCount: number
}

interface ParsedClass {
  name: string
  parent?: string
  startLine: number
}

interface ParsedFunction {
  name: string
  signature: string
  startLine: number
}

interface ParsedDevice {
  name: string
  type: string
  line: number
}

interface ParsedImport {
  module: string
  line: number
}

// ─── Verse Parser ────────────────────────────────────────────────────────────

function parseVerseSource(content: string) {
  const lines = content.split('\n')
  const classes: ParsedClass[] = []
  const functions: ParsedFunction[] = []
  const devices: ParsedDevice[] = []
  const imports: ParsedImport[] = []

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i]
    const trimmed = line.trim()

    // Imports: using { ... }
    const usingMatch = trimmed.match(/^using\s*\{\s*(.+?)\s*\}/)
    if (usingMatch) {
      imports.push({ module: usingMatch[1], line: i + 1 })
      continue
    }
    const usingMatch2 = trimmed.match(/^using\s+(.+)/)
    if (usingMatch2) {
      imports.push({ module: usingMatch2[1], line: i + 1 })
      continue
    }

    // Classes: ClassName := class(Parent):
    const classMatch = trimmed.match(/^(\w+)\s*:=\s*class(?:\((\w+)\))?\s*:?\s*$/)
    if (classMatch) {
      classes.push({ name: classMatch[1], parent: classMatch[2], startLine: i + 1 })
      continue
    }

    // Also match: ClassName<public> := class(Parent):
    const classMatch2 = trimmed.match(/^(\w+)\s*<[^>]*>\s*:=\s*class(?:\((\w+)\))?\s*:?\s*$/)
    if (classMatch2) {
      classes.push({ name: classMatch2[1], parent: classMatch2[2], startLine: i + 1 })
      continue
    }

    // Functions: FuncName(params)<decides><transacts>: ReturnType =
    const funcMatch = trimmed.match(/^(\w+)\s*\(([^)]*)\)(?:\s*<[^>]+>)*\s*(?::\s*(\w+))?\s*=\s*$/)
    if (funcMatch) {
      const sig = trimmed.replace(/\s*=\s*$/, '')
      functions.push({ name: funcMatch[1], signature: sig, startLine: i + 1 })
      continue
    }

    // Also match methods with access specifiers: Name<public>(params): Type =
    const funcMatch2 = trimmed.match(/^(\w+)\s*<[^>]*>\s*\(([^)]*)\)(?:\s*<[^>]+>)*\s*(?::\s*(\w+))?\s*=\s*$/)
    if (funcMatch2) {
      const sig = trimmed.replace(/\s*=\s*$/, '')
      functions.push({ name: funcMatch2[1], signature: sig, startLine: i + 1 })
      continue
    }

    // Devices: @editable VarName : DeviceType = DeviceType{}
    const deviceMatch = trimmed.match(/^@editable\s+(\w+)\s*:\s*(\w+)/)
    if (deviceMatch) {
      devices.push({ name: deviceMatch[1], type: deviceMatch[2], line: i + 1 })
      continue
    }
  }

  return { classes, functions, devices, imports }
}

// ─── Collapsible Section ─────────────────────────────────────────────────────

function CollapsibleSection({
  title,
  count,
  color,
  defaultOpen = true,
  children,
}: {
  title: string
  count: number
  color: string
  defaultOpen?: boolean
  children: React.ReactNode
}) {
  const [open, setOpen] = useState(defaultOpen)

  return (
    <div className="border-b border-fn-border/30 last:border-b-0">
      <button
        onClick={() => setOpen(!open)}
        className="w-full flex items-center gap-2 px-2 py-1.5 text-left hover:bg-white/[0.03] transition-colors"
      >
        <svg
          className={`w-3 h-3 text-gray-500 shrink-0 transition-transform ${open ? 'rotate-90' : ''}`}
          fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}
        >
          <path d="M9 5l7 7-7 7" />
        </svg>
        <span className="text-[10px] font-semibold text-gray-400 uppercase tracking-wider">{title}</span>
        {count > 0 && (
          <span className={`text-[9px] font-medium px-1.5 py-0.5 rounded-full ${color}`}>
            {count}
          </span>
        )}
      </button>
      {open && (
        <div className="px-2 pb-2">
          {children}
        </div>
      )}
    </div>
  )
}

// ─── Sort modes ──────────────────────────────────────────────────────────────

type SortMode = 'project' | 'az' | 'lines' | 'recent'

const SORT_LABELS: Record<SortMode, string> = {
  project: 'Group by Project',
  az: 'A-Z',
  lines: 'Lines',
  recent: 'Recent',
}

// ─── Component ──────────────────────────────────────────────────────────────

export function LibraryVersePage() {
  const activeLibrary = useLibraryStore((s) => s.activeLibrary)
  const verseFiles = useLibraryStore((s) => s.verseFiles)
  const verseFilesLoading = useLibraryStore((s) => s.verseFilesLoading)
  const verseFilesError = useLibraryStore((s) => s.verseFilesError)
  const fetchVerseFiles = useLibraryStore((s) => s.fetchVerseFiles)
  const favoriteVerseFiles = useLibraryStore((s) => s.favoriteVerseFiles)
  const toggleFavorite = useLibraryStore((s) => s.toggleFavorite)
  const verseEditorFontSize = useSettingsStore((s) => s.verseEditorFontSize)

  const [search, setSearch] = useState('')
  const [showFavoritesOnly, setShowFavoritesOnly] = useState(false)
  const [sortMode, setSortMode] = useState<SortMode>('project')
  const [sortReversed, setSortReversed] = useState(false)

  function handleSortClick(mode: SortMode) {
    if (sortMode === mode) {
      setSortReversed(!sortReversed)
    } else {
      setSortMode(mode)
      setSortReversed(false)
    }
  }
  const [selectedFile, setSelectedFile] = useState<VerseFileEntry | null>(null)
  const [fileContent, setFileContent] = useState<VerseFileContent | null>(null)
  const [fileLoading, setFileLoading] = useState(false)
  const [copiedLine, setCopiedLine] = useState<string | null>(null)

  // Analysis navigation state
  const [scrollToLine, setScrollToLine] = useState<number | undefined>(undefined)
  const [highlightLines, setHighlightLines] = useState<number[]>([])
  const scrollKeyRef = useRef(0)

  // Collapsed state for analysis sections
  const [collapsedSections, setCollapsedSections] = useState<Record<string, boolean>>({})

  useEffect(() => {
    if (activeLibrary) fetchVerseFiles()
  }, [activeLibrary, fetchVerseFiles])

  // Normalize files from store
  const allFiles = useMemo((): VerseFileEntry[] => {
    const raw = verseFiles
    return Array.isArray(raw) ? raw : Array.isArray((raw as any)?.verseFiles) ? (raw as any).verseFiles : []
  }, [verseFiles])

  // Filter files by search
  const filteredFiles = useMemo(() => {
    let filtered = allFiles

    if (search.trim()) {
      const q = search.toLowerCase()
      filtered = filtered.filter((f: any) =>
        f.name?.toLowerCase().includes(q) ||
        f.filePath?.toLowerCase().includes(q) ||
        f.projectName?.toLowerCase().includes(q) ||
        f.classes?.some((c: string) => c.toLowerCase().includes(q)) ||
        f.functions?.some((fn: string) => fn.toLowerCase().includes(q))
      )
    }

    if (showFavoritesOnly) {
      filtered = filtered.filter((f) => favoriteVerseFiles.includes(f.filePath))
    }

    return filtered
  }, [allFiles, search, showFavoritesOnly, favoriteVerseFiles])

  // Favorite files for pinned section
  const favoriteFiles = useMemo(() => {
    return filteredFiles.filter((f) => favoriteVerseFiles.includes(f.filePath))
  }, [filteredFiles, favoriteVerseFiles])

  // Helper to get a file's display name (handles different field names from sidecar)
  const getName = (f: any): string => f.name || f.Name || (f.filePath || f.FilePath || '').split(/[/\\]/).pop()?.replace('.verse', '') || ''
  const getLines = (f: any): number => f.lineCount || f.LineCount || 0
  const getProject = (f: any): string => f.projectName || f.ProjectName || f.projectFolder || ''

  // Sort files — this is the single source of truth for display order
  const sortedFileList = useMemo((): VerseFileEntry[] => {
    const files = [...filteredFiles]
    if (sortMode === 'az') {
      files.sort((a, b) => getName(a).localeCompare(getName(b)))
    } else if (sortMode === 'lines') {
      files.sort((a, b) => getLines(b) - getLines(a))
    } else if (sortMode === 'recent') {
      files.reverse()
    } else {
      // Project mode — sort by project folder then file name
      files.sort((a, b) => {
        const fa = getProject(a)
        const fb = getProject(b)
        if (fa !== fb) return fa.localeCompare(fb)
        return getName(a).localeCompare(getName(b))
      })
    }
    return sortReversed ? files.reverse() : files
  }, [filteredFiles, sortMode, sortReversed])

  // Grouped files — derived from sortedFileList so it respects sort direction
  const groupedFiles = useMemo((): Record<string, VerseFileEntry[]> | null => {
    if (sortMode !== 'project') return null
    const groups: Record<string, VerseFileEntry[]> = {}
    for (const file of sortedFileList) {
      const folder = getProject(file) || (file.filePath || '').split(/[/\\]/).slice(-2, -1)[0] || 'Root'
      if (!groups[folder]) groups[folder] = []
      groups[folder].push(file)
    }
    return groups
  }, [sortedFileList, sortMode])

  const totalFilteredCount = filteredFiles.length

  const parsed = useMemo(() => {
    if (!fileContent) return null
    return parseVerseSource(fileContent.content)
  }, [fileContent])

  const handleSelectFile = useCallback(async (file: VerseFileEntry) => {
    setSelectedFile(file)
    setFileLoading(true)
    setScrollToLine(undefined)
    setHighlightLines([])
    try {
      const result = await window.electronAPI.forgeReadVerse(file.filePath)
      const r = result as any
      setFileContent({
        filePath: file.filePath,
        name: r.name || file.name,
        content: r.source || r.content || '',
        lineCount: r.lineCount || 0,
      })
    } catch (err) {
      console.error('Failed to read verse file:', err)
      setFileContent(null)
    } finally {
      setFileLoading(false)
    }
  }, [])

  const handleNavigateToLine = useCallback((line: number) => {
    scrollKeyRef.current++
    setScrollToLine(line)
    setHighlightLines([line])
  }, [])

  async function handleCopyAll() {
    if (!fileContent) return
    await navigator.clipboard.writeText(fileContent.content)
    setCopiedLine('all')
    setTimeout(() => setCopiedLine(null), 1500)
  }

  async function handleCopyText(text: string, key: string) {
    await navigator.clipboard.writeText(text)
    setCopiedLine(key)
    setTimeout(() => setCopiedLine(null), 1500)
  }

  // ─── Render file list item ──────────────────────────────────────────────
  function renderFileItem(file: VerseFileEntry) {
    const isSelected = selectedFile?.filePath === file.filePath
    const isFav = favoriteVerseFiles.includes(file.filePath)
    return (
      <button
        key={file.filePath}
        onClick={() => handleSelectFile(file)}
        className={`w-full flex items-center gap-1.5 px-2 py-1.5 text-left transition-colors border-b border-fn-border/10 ${
          isSelected
            ? 'bg-blue-400/10 text-white'
            : 'text-gray-400 hover:text-white hover:bg-white/[0.03]'
        }`}
      >
        <svg className="w-3 h-3 text-green-400/60 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
          <path d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4" />
        </svg>
        <span className="text-[10px] truncate flex-1">{getName(file)}</span>
        <span className="text-[8px] text-gray-600 shrink-0">{getLines(file)}L</span>
        <button
          onClick={(e) => {
            e.stopPropagation()
            toggleFavorite(file.filePath)
          }}
          className={`shrink-0 p-0.5 transition-colors ${
            isFav ? 'text-yellow-400' : 'text-gray-700 hover:text-yellow-400/60'
          }`}
        >
          <svg className="w-3 h-3" fill={isFav ? 'currentColor' : 'none'} viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path d="M11.049 2.927c.3-.921 1.603-.921 1.902 0l1.519 4.674a1 1 0 00.95.69h4.915c.969 0 1.371 1.24.588 1.81l-3.976 2.888a1 1 0 00-.363 1.118l1.518 4.674c.3.922-.755 1.688-1.538 1.118l-3.976-2.888a1 1 0 00-1.176 0l-3.976 2.888c-.783.57-1.838-.197-1.538-1.118l1.518-4.674a1 1 0 00-.363-1.118l-3.976-2.888c-.784-.57-.38-1.81.588-1.81h4.914a1 1 0 00.951-.69l1.519-4.674z" />
          </svg>
        </button>
      </button>
    )
  }

  if (!activeLibrary) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <svg className="w-10 h-10 mx-auto mb-2 text-gray-700" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
            <path d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4" />
          </svg>
          <p className="text-[11px] text-gray-400 mb-1">No active library</p>
          <p className="text-[10px] text-gray-600">Select a library to browse Verse files.</p>
        </div>
      </div>
    )
  }

  if (verseFilesLoading && !verseFiles) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-[11px] text-gray-400">Loading verse files...</div>
      </div>
    )
  }

  if (verseFilesError) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="text-[11px] text-red-400 mb-3">{verseFilesError}</div>
          <button
            onClick={() => {
              useLibraryStore.setState({ verseFilesFetchedAt: null })
              fetchVerseFiles()
            }}
            className="px-3 py-1.5 text-[10px] font-medium text-white bg-fn-panel border border-fn-border rounded hover:bg-white/[0.06] transition-colors"
          >
            Retry
          </button>
        </div>
      </div>
    )
  }

  return (
    <div className="flex-1 flex bg-fn-darker overflow-hidden">
      {/* ─── Left Panel: File List ─── */}
      <div className="w-[250px] border-r border-fn-border bg-fn-dark flex flex-col shrink-0 overflow-hidden">
        {/* Search + Sort */}
        <div className="p-2 border-b border-fn-border space-y-1.5 shrink-0">
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search files, classes, functions..."
            className="w-full bg-fn-darker border border-fn-border rounded px-2 py-1.5 text-[10px] text-white placeholder-gray-600 focus:outline-none focus:border-blue-400/50"
          />
          {/* Sort buttons */}
          <div className="flex gap-1">
            {(['project', 'az', 'lines', 'recent'] as SortMode[]).map((mode) => {
              const isActive = sortMode === mode
              const label = mode === 'project' ? 'Project' : mode === 'az' ? 'A-Z' : mode === 'lines' ? 'Lines' : 'Recent'
              return (
                <button
                  key={mode}
                  onClick={() => handleSortClick(mode)}
                  className={`flex-1 flex items-center justify-center gap-0.5 px-1 py-1 text-[9px] font-medium rounded transition-colors ${
                    isActive
                      ? 'text-blue-400 bg-blue-400/10 border border-blue-400/30'
                      : 'text-gray-500 hover:text-gray-300 bg-fn-darker border border-fn-border/50 hover:bg-white/[0.03]'
                  }`}
                  title={SORT_LABELS[mode]}
                >
                  {label}
                  {isActive && (
                    <svg className={`w-2.5 h-2.5 transition-transform ${sortReversed ? 'rotate-180' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.5}>
                      <path d="M19 14l-7-7-7 7" />
                    </svg>
                  )}
                </button>
              )
            })}
          </div>
          <button
            onClick={() => setShowFavoritesOnly(!showFavoritesOnly)}
            className={`w-full flex items-center gap-1.5 px-2 py-1 rounded text-[10px] transition-colors ${
              showFavoritesOnly
                ? 'text-yellow-400 bg-yellow-400/10'
                : 'text-gray-500 hover:text-gray-300 hover:bg-white/[0.03]'
            }`}
          >
            <svg className="w-3 h-3" fill={showFavoritesOnly ? 'currentColor' : 'none'} viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path d="M11.049 2.927c.3-.921 1.603-.921 1.902 0l1.519 4.674a1 1 0 00.95.69h4.915c.969 0 1.371 1.24.588 1.81l-3.976 2.888a1 1 0 00-.363 1.118l1.518 4.674c.3.922-.755 1.688-1.538 1.118l-3.976-2.888a1 1 0 00-1.176 0l-3.976 2.888c-.783.57-1.838-.197-1.538-1.118l1.518-4.674a1 1 0 00-.363-1.118l-3.976-2.888c-.784-.57-.38-1.81.588-1.81h4.914a1 1 0 00.951-.69l1.519-4.674z" />
            </svg>
            Favorites ({favoriteVerseFiles.length})
          </button>
        </div>

        {/* File list */}
        <div className="flex-1 overflow-y-auto">
          {totalFilteredCount === 0 ? (
            <div className="p-3 text-center text-[10px] text-gray-600">
              {search ? 'No matching files' : showFavoritesOnly ? 'No favorites yet' : 'No verse files found'}
            </div>
          ) : (
            <>
              {/* Pinned favorites section (when not in favorites-only mode and there are favorites) */}
              {!showFavoritesOnly && favoriteFiles.length > 0 && (
                <div>
                  <div className="px-2 py-1 text-[9px] font-semibold text-amber-400/80 uppercase tracking-wider bg-amber-400/5 sticky top-0 z-10 border-b border-amber-400/20 flex items-center gap-1">
                    <svg className="w-2.5 h-2.5" fill="currentColor" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
                      <path d="M11.049 2.927c.3-.921 1.603-.921 1.902 0l1.519 4.674a1 1 0 00.95.69h4.915c.969 0 1.371 1.24.588 1.81l-3.976 2.888a1 1 0 00-.363 1.118l1.518 4.674c.3.922-.755 1.688-1.538 1.118l-3.976-2.888a1 1 0 00-1.176 0l-3.976 2.888c-.783.57-1.838-.197-1.538-1.118l1.518-4.674a1 1 0 00-.363-1.118l-3.976-2.888c-.784-.57-.38-1.81.588-1.81h4.914a1 1 0 00.951-.69l1.519-4.674z" />
                    </svg>
                    Favorites
                    <span className="text-amber-400/50">({favoriteFiles.length})</span>
                  </div>
                  {favoriteFiles.map((file) => renderFileItem(file))}
                </div>
              )}

              {/* Main file list */}
              {sortMode === 'project' && groupedFiles ? (
                Object.entries(groupedFiles).map(([folder, files]) => (
                  <div key={folder}>
                    <div className="px-2 py-1 text-[9px] font-semibold text-gray-600 uppercase tracking-wider bg-fn-darker/50 sticky top-0 z-10 border-b border-fn-border/30">
                      {folder}
                      <span className="ml-1 text-gray-700">({files.length})</span>
                    </div>
                    {files.map((file) => renderFileItem(file))}
                  </div>
                ))
              ) : (
                sortedFileList.map((file) => renderFileItem(file))
              )}
            </>
          )}
        </div>

        {/* File count footer */}
        <div className="px-2 py-1.5 border-t border-fn-border text-[9px] text-gray-600 shrink-0">
          {totalFilteredCount} file{totalFilteredCount !== 1 ? 's' : ''}
          {search && ` matching "${search}"`}
        </div>
      </div>

      {/* ─── Center Panel: Source Viewer ─── */}
      <div className="flex-1 flex flex-col overflow-hidden">
        {fileLoading ? (
          <div className="flex-1 flex items-center justify-center">
            <div className="text-[11px] text-gray-400">Loading source...</div>
          </div>
        ) : fileContent ? (
          <>
            {/* Source header */}
            <div className="flex items-center gap-3 px-4 py-2 border-b border-fn-border bg-fn-dark shrink-0">
              <svg className="w-4 h-4 text-green-400/60 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4" />
              </svg>
              <span className="text-[11px] font-medium text-white truncate">{fileContent.name}</span>
              <span className="text-[10px] text-gray-500">{fileContent.lineCount} lines</span>
              <div className="ml-auto shrink-0">
                <button
                  onClick={handleCopyAll}
                  className="px-2 py-1 text-[10px] text-gray-400 hover:text-white bg-fn-darker border border-fn-border rounded transition-colors"
                >
                  {copiedLine === 'all' ? 'Copied!' : 'Copy All'}
                </button>
              </div>
            </div>

            {/* Source code */}
            <div className="flex-1 overflow-auto">
              <VerseHighlighter
                key={scrollKeyRef.current}
                source={fileContent.content}
                fontSize={verseEditorFontSize}
                searchHighlight={search || undefined}
                scrollToLine={scrollToLine}
                highlightLines={highlightLines}
                onLineClick={handleNavigateToLine}
              />
            </div>
          </>
        ) : (
          <div className="flex-1 flex items-center justify-center">
            <div className="text-center">
              <svg className="w-12 h-12 mx-auto mb-3 text-gray-800" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={0.5}>
                <path d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4" />
              </svg>
              <p className="text-[11px] text-gray-500">Select a verse file to view its source</p>
              <p className="text-[10px] text-gray-600 mt-0.5">
                {allFiles.length} files available in library
              </p>
            </div>
          </div>
        )}
      </div>

      {/* ─── Right Panel: Analysis (Stacked Collapsible Sections) ─── */}
      {fileContent && parsed && (
        <div className="w-[280px] border-l border-fn-border bg-fn-dark flex flex-col shrink-0 overflow-hidden">
          <div className="px-2 py-1.5 border-b border-fn-border shrink-0">
            <span className="text-[10px] font-semibold text-gray-400 uppercase tracking-wider">Analysis</span>
          </div>

          <div className="flex-1 overflow-y-auto">
            {/* Classes */}
            <CollapsibleSection
              title="Classes"
              count={parsed.classes.length}
              color="text-blue-400 bg-blue-400/10"
              defaultOpen={!collapsedSections['classes']}
            >
              {parsed.classes.length === 0 ? (
                <div className="py-2 text-center text-[10px] text-gray-600">No classes found</div>
              ) : (
                parsed.classes.map((cls, i) => (
                  <button
                    key={i}
                    onClick={() => handleNavigateToLine(cls.startLine)}
                    className="w-full flex items-center gap-1.5 px-2 py-1 text-left hover:bg-white/[0.03] transition-colors group/entry"
                  >
                    <span className="text-[10px] font-semibold text-blue-400 truncate">{cls.name}</span>
                    {cls.parent && <span className="text-[9px] text-gray-600">: {cls.parent}</span>}
                    <span className="ml-auto text-[8px] text-gray-700 tabular-nums shrink-0">L{cls.startLine}</span>
                  </button>
                ))
              )}
            </CollapsibleSection>

            {/* Functions */}
            <CollapsibleSection
              title="Functions"
              count={parsed.functions.length}
              color="text-cyan-400 bg-cyan-400/10"
              defaultOpen={!collapsedSections['functions']}
            >
              {parsed.functions.length === 0 ? (
                <div className="py-2 text-center text-[10px] text-gray-600">No functions found</div>
              ) : (
                parsed.functions.map((fn, i) => (
                  <button
                    key={i}
                    onClick={() => handleNavigateToLine(fn.startLine)}
                    className="w-full flex items-center gap-1.5 px-2 py-1 text-left hover:bg-white/[0.03] transition-colors group/entry"
                  >
                    <span className="text-[10px] font-semibold text-cyan-400 truncate flex-1 min-w-0">{fn.name}</span>
                    <button
                      onClick={(e) => { e.stopPropagation(); handleCopyText(fn.signature, `fn-${i}`) }}
                      className="text-[8px] text-gray-700 hover:text-white px-1 rounded opacity-0 group-hover/entry:opacity-100 transition-opacity shrink-0"
                    >
                      {copiedLine === `fn-${i}` ? '✓' : 'cp'}
                    </button>
                    <span className="text-[8px] text-gray-700 tabular-nums shrink-0">L{fn.startLine}</span>
                  </button>
                ))
              )}
            </CollapsibleSection>

            {/* Devices */}
            <CollapsibleSection
              title="Devices"
              count={parsed.devices.length}
              color="text-purple-400 bg-purple-400/10"
              defaultOpen={!collapsedSections['devices']}
            >
              {parsed.devices.length === 0 ? (
                <div className="py-2 text-center text-[10px] text-gray-600">No @editable devices</div>
              ) : (
                parsed.devices.map((dev, i) => (
                  <button
                    key={i}
                    onClick={() => handleNavigateToLine(dev.line)}
                    className="w-full flex items-center gap-1.5 px-2 py-1 text-left hover:bg-white/[0.03] transition-colors"
                  >
                    <span className="text-[9px] text-purple-400/70">@</span>
                    <span className="text-[10px] font-semibold text-white truncate">{dev.name}</span>
                    <span className="text-[9px] text-gray-600 truncate">{dev.type}</span>
                    <span className="ml-auto text-[8px] text-gray-700 tabular-nums shrink-0">L{dev.line}</span>
                  </button>
                ))
              )}
            </CollapsibleSection>

            {/* Imports */}
            <CollapsibleSection
              title="Imports"
              count={parsed.imports.length}
              color="text-orange-400 bg-orange-400/10"
              defaultOpen={!collapsedSections['imports']}
            >
              {parsed.imports.length === 0 ? (
                <div className="py-2 text-center text-[10px] text-gray-600">No imports</div>
              ) : (
                parsed.imports.map((imp, i) => (
                  <button
                    key={i}
                    onClick={() => handleNavigateToLine(imp.line)}
                    className="w-full flex items-center gap-1.5 px-2 py-1 text-left hover:bg-white/[0.03] transition-colors group/entry"
                  >
                    <span className="text-[10px] font-semibold text-orange-400 truncate font-mono flex-1 min-w-0">{imp.module}</span>
                    <button
                      onClick={(e) => { e.stopPropagation(); handleCopyText(imp.module, `imp-${i}`) }}
                      className="text-[8px] text-gray-700 hover:text-white px-1 rounded opacity-0 group-hover/entry:opacity-100 transition-opacity shrink-0"
                    >
                      {copiedLine === `imp-${i}` ? '✓' : 'cp'}
                    </button>
                  </button>
                ))
              )}
            </CollapsibleSection>
          </div>
        </div>
      )}
    </div>
  )
}
