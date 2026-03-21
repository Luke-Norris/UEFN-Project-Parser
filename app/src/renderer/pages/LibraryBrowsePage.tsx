import { useEffect, useState, useMemo } from 'react'
import { useLibraryStore } from '../stores/libraryStore'

interface BrowseEntry {
  name: string
  path: string
  relativePath: string
  isDirectory: boolean
  extension?: string
  size?: number
  lastModified?: string
  type?: string
}

interface BrowseResult {
  currentPath: string
  relativePath: string
  entries: BrowseEntry[]
}

export function LibraryBrowsePage() {
  const activeLibrary = useLibraryStore((s) => s.activeLibrary)
  const [data, setData] = useState<BrowseResult | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [pathHistory, setPathHistory] = useState<string[]>([])
  const [viewMode, setViewMode] = useState<'list' | 'grid'>('list')

  useEffect(() => {
    if (activeLibrary) loadContent()
  }, [activeLibrary]) // eslint-disable-line

  async function loadContent(subPath?: string) {
    try {
      setLoading(true)
      setError(null)
      const result = await (window.electronAPI as any).forgeBrowseLibraryDir(subPath ?? '')
      setData(result as BrowseResult)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to browse library')
    } finally {
      setLoading(false)
    }
  }

  async function navigateInto(entry: BrowseEntry) {
    if (!entry.isDirectory) return
    setPathHistory((prev) => [...prev, data?.currentPath ?? ''])
    setSearch('')
    await loadContent(entry.path)
  }

  async function navigateBack() {
    const prev = pathHistory[pathHistory.length - 1]
    setPathHistory((h) => h.slice(0, -1))
    setSearch('')
    await loadContent(prev || undefined)
  }

  async function navigateToRoot() {
    setPathHistory([])
    setSearch('')
    await loadContent()
  }

  const filteredEntries = useMemo(() => {
    const entries = data?.entries ?? []
    if (!search.trim()) return entries
    const q = search.toLowerCase()
    return entries.filter((e) => e.name.toLowerCase().includes(q))
  }, [data, search])

  const sortedEntries = useMemo(() => {
    return [...filteredEntries].sort((a, b) => {
      if (a.isDirectory !== b.isDirectory) return a.isDirectory ? -1 : 1
      return a.name.localeCompare(b.name)
    })
  }, [filteredEntries])

  const breadcrumbSegments = useMemo(() => {
    const rel = data?.relativePath ?? ''
    if (!rel) return []
    return rel.split(/[/\\]/).filter(Boolean)
  }, [data])

  function formatSize(bytes?: number): string {
    if (bytes == null) return ''
    if (bytes < 1024) return `${bytes} B`
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  }

  function formatDate(iso?: string): string {
    if (!iso) return ''
    const d = new Date(iso)
    const now = new Date()
    const diffMs = now.getTime() - d.getTime()
    const diffDays = Math.floor(diffMs / 86400000)
    if (diffDays === 0) return 'Today'
    if (diffDays === 1) return 'Yesterday'
    if (diffDays < 7) return `${diffDays}d ago`
    return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
  }

  function getFileIcon(entry: BrowseEntry) {
    if (entry.isDirectory) {
      return (
        <svg className="w-4 h-4 text-yellow-500/70" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
          <path d="M3 7v10a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-6l-2-2H5a2 2 0 00-2 2z" />
        </svg>
      )
    }
    const ext = entry.extension?.toLowerCase()
    if (ext === '.uasset') {
      return (
        <svg className="w-4 h-4 text-fn-rare/70" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
          <path d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4" />
        </svg>
      )
    }
    if (ext === '.verse') {
      return (
        <svg className="w-4 h-4 text-green-400/70" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
          <path d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4" />
        </svg>
      )
    }
    if (ext === '.umap') {
      return (
        <svg className="w-4 h-4 text-purple-400/70" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
          <path d="M3.055 11H5a2 2 0 012 2v1a2 2 0 002 2 2 2 0 012 2v2.945M8 3.935V5.5A2.5 2.5 0 0010.5 8h.5a2 2 0 012 2 2 2 0 104 0 2 2 0 012-2h1.064M15 20.488V18a2 2 0 012-2h3.064M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
        </svg>
      )
    }
    return (
      <svg className="w-4 h-4 text-gray-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
        <path d="M7 21h10a2 2 0 002-2V9.414a1 1 0 00-.293-.707l-5.414-5.414A1 1 0 0012.586 3H7a2 2 0 00-2 2v14a2 2 0 002 2z" />
      </svg>
    )
  }

  function getTypeBadge(entry: BrowseEntry) {
    if (entry.isDirectory) return null
    const ext = entry.extension?.toLowerCase()
    if (ext === '.uasset') return <span className="px-1 py-0.5 rounded text-[8px] font-medium text-fn-rare bg-fn-rare/10">UASSET</span>
    if (ext === '.verse') return <span className="px-1 py-0.5 rounded text-[8px] font-medium text-green-400 bg-green-400/10">VERSE</span>
    if (ext === '.umap') return <span className="px-1 py-0.5 rounded text-[8px] font-medium text-purple-400 bg-purple-400/10">UMAP</span>
    if (ext === '.uexp') return <span className="px-1 py-0.5 rounded text-[8px] font-medium text-gray-500/80 bg-gray-500/10">UEXP</span>
    return <span className="px-1 py-0.5 rounded text-[8px] font-medium text-gray-500 bg-gray-500/10">{ext?.toUpperCase()?.slice(1) ?? 'FILE'}</span>
  }

  if (!activeLibrary) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <svg className="w-10 h-10 mx-auto mb-2 text-gray-700" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
            <path d="M8 14v3m4-3v3m4-3v3M3 21h18M3 10h18M3 7l9-4 9 4M4 10h16v11H4V10z" />
          </svg>
          <p className="text-[11px] text-gray-400 mb-1">No active library</p>
          <p className="text-[10px] text-gray-600">Select a library from the sidebar dropdown to browse.</p>
        </div>
      </div>
    )
  }

  if (loading && !data) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-[11px] text-gray-400">Loading library...</div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="text-[11px] text-red-400 mb-3">{error}</div>
          <button
            onClick={() => loadContent()}
            className="px-3 py-1.5 text-[10px] font-medium text-white bg-fn-panel border border-fn-border rounded hover:bg-white/[0.06] transition-colors"
          >
            Retry
          </button>
        </div>
      </div>
    )
  }

  return (
    <div className="flex-1 flex flex-col bg-fn-darker overflow-hidden">
      {/* Breadcrumb + search bar */}
      <div className="px-4 py-2 border-b border-fn-border bg-fn-dark shrink-0 space-y-2">
        {/* Header + breadcrumb */}
        <div className="flex items-center gap-1 text-[10px] min-h-[20px]">
          {pathHistory.length > 0 && (
            <button
              onClick={navigateBack}
              className="flex items-center gap-1 px-1.5 py-0.5 text-gray-400 hover:text-white bg-fn-darker border border-fn-border rounded transition-colors mr-1"
            >
              <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path d="M15 19l-7-7 7-7" />
              </svg>
            </button>
          )}
          <span className="text-[8px] font-bold px-1 py-0.5 rounded border shrink-0 text-blue-400 bg-blue-400/10 border-blue-400/20 mr-1">
            REF
          </span>
          <button
            onClick={navigateToRoot}
            className="text-blue-400 hover:text-blue-300 transition-colors font-medium"
          >
            {activeLibrary.name}
          </button>
          {breadcrumbSegments.map((seg, i) => (
            <span key={i} className="flex items-center gap-1">
              <span className="text-gray-600">/</span>
              {i < breadcrumbSegments.length - 1 ? (
                <button
                  onClick={() => {
                    const targetSegs = breadcrumbSegments.slice(0, i + 1)
                    setPathHistory((h) => h.slice(0, i + 1))
                    setSearch('')
                    loadContent(targetSegs.join('/'))
                  }}
                  className="text-blue-400 hover:text-blue-300 transition-colors"
                >
                  {seg}
                </button>
              ) : (
                <span className="text-white font-medium">{seg}</span>
              )}
            </span>
          ))}
        </div>

        {/* Search + view toggle */}
        <div className="flex items-center gap-2">
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Filter in current directory..."
            className="flex-1 bg-fn-darker border border-fn-border rounded px-2.5 py-1.5 text-[11px] text-white placeholder-gray-600 focus:outline-none focus:border-blue-400/50"
          />
          <div className="flex items-center border border-fn-border rounded overflow-hidden shrink-0">
            <button
              onClick={() => setViewMode('list')}
              className={`p-1.5 transition-colors ${viewMode === 'list' ? 'text-blue-400 bg-blue-400/10' : 'text-gray-500 hover:text-gray-300'}`}
              title="List view"
            >
              <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path d="M4 6h16M4 12h16M4 18h16" />
              </svg>
            </button>
            <button
              onClick={() => setViewMode('grid')}
              className={`p-1.5 transition-colors ${viewMode === 'grid' ? 'text-blue-400 bg-blue-400/10' : 'text-gray-500 hover:text-gray-300'}`}
              title="Grid view"
            >
              <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path d="M4 5a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1H5a1 1 0 01-1-1V5zM14 5a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1h-4a1 1 0 01-1-1V5zM4 15a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1H5a1 1 0 01-1-1v-4zM14 15a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1h-4a1 1 0 01-1-1v-4z" />
              </svg>
            </button>
          </div>
        </div>
      </div>

      {/* File listing */}
      <div className="flex-1 overflow-y-auto">
        {loading ? (
          <div className="p-4 text-center text-[11px] text-gray-400">Loading...</div>
        ) : sortedEntries.length === 0 ? (
          <div className="p-8 text-center">
            <p className="text-[11px] text-gray-400 mb-1">
              {search ? 'No matching entries' : 'Empty directory'}
            </p>
          </div>
        ) : viewMode === 'list' ? (
          <div>
            {/* Column Headers */}
            <div className="flex items-center gap-3 px-4 py-1.5 border-b border-fn-border/40 bg-fn-dark/50 sticky top-0 z-10">
              <span className="w-4 shrink-0" />
              <span className="text-[9px] font-semibold text-gray-500 uppercase tracking-wider flex-1 min-w-0">Name</span>
              <span className="text-[9px] font-semibold text-gray-500 uppercase tracking-wider w-14 shrink-0 text-center">Type</span>
              <span className="text-[9px] font-semibold text-gray-500 uppercase tracking-wider w-16 shrink-0 text-right">Size</span>
              <span className="text-[9px] font-semibold text-gray-500 uppercase tracking-wider w-20 shrink-0 text-right">Modified</span>
              <span className="w-3 shrink-0" />
            </div>
            {sortedEntries.map((entry) => (
              <button
                key={entry.path}
                onClick={() => {
                  if (entry.isDirectory) navigateInto(entry)
                }}
                className="w-full flex items-center gap-3 px-4 py-2 border-b border-fn-border/20 hover:bg-white/[0.02] transition-colors text-left"
              >
                {getFileIcon(entry)}
                <span className="text-[11px] text-white truncate flex-1 min-w-0">{entry.name}</span>
                {getTypeBadge(entry)}
                {!entry.isDirectory && entry.size != null && (
                  <span className="text-[10px] text-gray-600 shrink-0 tabular-nums w-16 text-right">{formatSize(entry.size)}</span>
                )}
                {!entry.isDirectory && entry.lastModified && (
                  <span className="text-[10px] text-gray-600 tabular-nums w-20 shrink-0 text-right">{formatDate(entry.lastModified)}</span>
                )}
                {entry.isDirectory && (
                  <svg className="w-3 h-3 text-gray-600 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path d="M9 5l7 7-7 7" />
                  </svg>
                )}
              </button>
            ))}
          </div>
        ) : (
          /* Grid View */
          <div className="p-3 grid grid-cols-[repeat(auto-fill,minmax(120px,1fr))] gap-2">
            {sortedEntries.map((entry) => (
              <button
                key={entry.path}
                onClick={() => {
                  if (entry.isDirectory) navigateInto(entry)
                }}
                className={`flex flex-col items-center gap-1.5 p-3 rounded border transition-colors text-center ${
                  entry.isDirectory
                    ? 'bg-fn-dark/50 border-dashed border-fn-border/40 hover:bg-fn-panel hover:border-fn-border'
                    : 'bg-fn-darker border-fn-border/20 hover:bg-fn-panel hover:border-fn-border'
                }`}
              >
                {getFileIcon(entry)}
                <span className="text-[10px] text-white truncate max-w-full">{entry.name}</span>
                {getTypeBadge(entry)}
              </button>
            ))}
          </div>
        )}
      </div>

      {/* Status bar */}
      <div className="px-4 py-1.5 border-t border-fn-border bg-fn-dark shrink-0 text-[10px] text-gray-600">
        {sortedEntries.length} item{sortedEntries.length !== 1 ? 's' : ''}
        {search && ` (filtered from ${data?.entries.length ?? 0})`}
      </div>
    </div>
  )
}
