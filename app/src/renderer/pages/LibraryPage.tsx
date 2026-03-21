import { useEffect, useState, useMemo } from 'react'
import { useSettingsStore } from '../stores/settingsStore'
import type { ContentEntry } from '../../shared/types'

export function LibraryPage() {
  const libraryPaths = useSettingsStore((s) => s.libraryPaths)
  const setSetting = useSettingsStore((s) => s.setSetting)

  const [entries, setEntries] = useState<ContentEntry[]>([])
  const [currentPath, setCurrentPath] = useState<string[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [addingPath, setAddingPath] = useState(false)
  const [newPath, setNewPath] = useState('')

  const activeLibraryPath = libraryPaths[0] || null

  useEffect(() => {
    if (activeLibraryPath) loadDir()
  }, [activeLibraryPath]) // eslint-disable-line

  async function loadDir(subPath?: string) {
    if (!activeLibraryPath) return
    try {
      setLoading(true)
      setError(null)
      const result = await window.electronAPI.forgeBrowseLibraryDir(
        subPath || currentPath.join('/') || undefined
      )
      if ((result as any).error) {
        setError((result as any).error as string)
        return
      }
      setEntries((result.entries as unknown as ContentEntry[]) || [])
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to browse library')
    } finally {
      setLoading(false)
    }
  }

  function navigateInto(folderName: string) {
    const newPath2 = [...currentPath, folderName]
    setCurrentPath(newPath2)
    loadDirAt(newPath2)
  }

  function navigateUp() {
    const newPath2 = currentPath.slice(0, -1)
    setCurrentPath(newPath2)
    loadDirAt(newPath2)
  }

  function navigateToBreadcrumb(index: number) {
    const newPath2 = currentPath.slice(0, index + 1)
    setCurrentPath(newPath2)
    loadDirAt(newPath2)
  }

  function navigateToRoot() {
    setCurrentPath([])
    loadDirAt([])
  }

  async function loadDirAt(path: string[]) {
    if (!activeLibraryPath) return
    try {
      setLoading(true)
      setError(null)
      const result = await window.electronAPI.forgeBrowseLibraryDir(
        path.join('/') || undefined
      )
      setEntries((result.entries as unknown as ContentEntry[]) || [])
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to browse')
    } finally {
      setLoading(false)
    }
  }

  async function handleAddPath() {
    const dir = await window.electronAPI.selectDirectory()
    if (dir) {
      setSetting('libraryPaths', [...libraryPaths, dir])
      setAddingPath(false)
    }
  }

  function handleRemovePath(path: string) {
    setSetting('libraryPaths', libraryPaths.filter((p) => p !== path))
  }

  const filteredEntries = useMemo(() => {
    if (!search.trim()) return entries
    const q = search.toLowerCase()
    return entries.filter((e) => e.name.toLowerCase().includes(q))
  }, [entries, search])

  // No library configured — show setup
  if (libraryPaths.length === 0) {
    return (
      <div className="flex-1 bg-fn-darker flex items-center justify-center">
        <div className="text-center max-w-md">
          <svg className="w-12 h-12 mx-auto mb-3 text-gray-700" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
            <path d="M8 14v3m4-3v3m4-3v3M3 21h18M3 10h18M3 7l9-4 9 4M4 10h16v11H4V10z" />
          </svg>
          <h2 className="text-sm font-semibold text-white mb-1">No Library Configured</h2>
          <p className="text-[11px] text-gray-500 mb-4">
            Add a path to your UEFN reference collection — maps, assets, widgets, verse files.
            The library is read-only and used as a development resource.
          </p>
          <button
            onClick={handleAddPath}
            className="px-4 py-2 text-xs font-medium text-white bg-fn-rare rounded hover:opacity-90 transition-opacity"
          >
            Add Library Path
          </button>
        </div>
      </div>
    )
  }

  return (
    <div className="flex-1 bg-fn-darker overflow-y-auto">
      <div className="p-4 space-y-3">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-sm font-semibold text-white">Library</h1>
            <p className="text-[10px] text-gray-500">Reference collection — read-only</p>
          </div>
          <button
            onClick={() => setAddingPath(!addingPath)}
            className="px-2.5 py-1 text-[10px] font-medium text-gray-400 bg-fn-dark border border-fn-border rounded hover:text-white transition-colors"
          >
            Manage Paths
          </button>
        </div>

        {/* Library paths management */}
        {addingPath && (
          <div className="bg-fn-dark border border-fn-border rounded-lg p-3 space-y-2">
            <div className="text-[10px] text-gray-400 font-semibold uppercase tracking-wider">Library Sources</div>
            {libraryPaths.map((p) => (
              <div key={p} className="flex items-center gap-2 text-[10px]">
                <span className="w-2 h-2 rounded-full bg-emerald-400 shrink-0" />
                <span className="text-gray-300 truncate flex-1">{p}</span>
                <button
                  onClick={() => handleRemovePath(p)}
                  className="text-gray-600 hover:text-red-400 transition-colors"
                >
                  Remove
                </button>
              </div>
            ))}
            <button
              onClick={handleAddPath}
              className="text-[10px] text-fn-rare hover:underline"
            >
              + Add another path
            </button>
          </div>
        )}

        {/* Breadcrumb */}
        <div className="flex items-center gap-1 text-[10px]">
          <button onClick={navigateToRoot} className="text-fn-rare hover:underline">
            Library
          </button>
          {currentPath.map((segment, i) => (
            <span key={i} className="flex items-center gap-1">
              <span className="text-gray-600">/</span>
              <button
                onClick={() => navigateToBreadcrumb(i)}
                className="text-fn-rare hover:underline truncate max-w-[120px]"
              >
                {segment}
              </button>
            </span>
          ))}
        </div>

        {/* Search */}
        <input
          type="text"
          className="input-field"
          placeholder="Search library..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />

        {/* Content */}
        {loading ? (
          <div className="text-center py-8">
            <div className="w-5 h-5 mx-auto mb-2 border-2 border-fn-rare/30 border-t-fn-rare rounded-full animate-spin" />
            <div className="text-[11px] text-gray-400">Loading...</div>
          </div>
        ) : error ? (
          <div className="text-center py-8">
            <div className="text-[11px] text-red-400 mb-2">{error}</div>
            <button onClick={() => loadDir()} className="text-[10px] text-fn-rare hover:underline">Retry</button>
          </div>
        ) : (
          <div className="bg-fn-dark border border-fn-border rounded-lg overflow-hidden">
            {/* Column headers */}
            <div className="flex items-center gap-3 px-3 py-1.5 border-b border-fn-border/40 bg-fn-darker/50 text-[9px] font-semibold text-gray-500 uppercase tracking-wider">
              <span className="w-4 shrink-0" />
              <span className="flex-1">Name</span>
              <span className="w-14 text-center shrink-0">Type</span>
              <span className="w-16 text-right shrink-0">Size</span>
            </div>
            {currentPath.length > 0 && (
              <button
                onClick={navigateUp}
                className="w-full flex items-center gap-3 px-3 py-2 border-b border-fn-border/20 hover:bg-white/[0.02] text-left"
              >
                <svg className="w-4 h-4 text-gray-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path d="M15 19l-7-7 7-7" />
                </svg>
                <span className="text-[11px] text-gray-400">..</span>
              </button>
            )}
            {filteredEntries.length === 0 ? (
              <div className="px-3 py-6 text-center text-[11px] text-gray-600">
                {search ? 'No matches' : 'Empty directory'}
              </div>
            ) : (
              filteredEntries.map((entry) => (
                <button
                  key={entry.path}
                  onClick={() => {
                    if (entry.type === 'folder') navigateInto(entry.name)
                  }}
                  className="w-full flex items-center gap-3 px-3 py-1.5 border-b border-fn-border/10 hover:bg-white/[0.02] text-left transition-colors"
                >
                  {/* Icon */}
                  <span className="w-4 shrink-0">
                    {entry.type === 'folder' ? (
                      <svg className="w-4 h-4 text-yellow-400/70" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                        <path d="M3 7v10a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-6l-2-2H5a2 2 0 00-2 2z" />
                      </svg>
                    ) : (
                      <svg className="w-4 h-4 text-gray-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                        <path d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                      </svg>
                    )}
                  </span>
                  <span className="text-[11px] text-white truncate flex-1">{entry.name}</span>
                  <span className={`text-[8px] font-bold px-1.5 py-0.5 rounded shrink-0 w-14 text-center ${
                    entry.type === 'folder' ? 'text-yellow-400/60' :
                    entry.type === 'uasset' ? 'text-blue-400/60' :
                    entry.type === 'verse' ? 'text-green-400/60' :
                    entry.type === 'umap' ? 'text-purple-400/60' :
                    'text-gray-500/60'
                  }`}>
                    {entry.type === 'folder' ? 'DIR' : entry.type.toUpperCase()}
                  </span>
                  {entry.type !== 'folder' && entry.size != null && (
                    <span className="text-[10px] text-gray-600 tabular-nums w-16 text-right shrink-0">
                      {entry.size > 1024 * 1024 ? `${(entry.size / 1024 / 1024).toFixed(1)} MB` :
                       entry.size > 1024 ? `${(entry.size / 1024).toFixed(0)} KB` :
                       `${entry.size} B`}
                    </span>
                  )}
                  {entry.type === 'folder' && (
                    <svg className="w-3 h-3 text-gray-600 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path d="M9 5l7 7-7 7" />
                    </svg>
                  )}
                </button>
              ))
            )}
          </div>
        )}
      </div>
    </div>
  )
}
