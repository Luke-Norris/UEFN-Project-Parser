import { useEffect, useState, useMemo } from 'react'
import { prettifyAssetName } from '../lib/assetNames'
import type { ContentBrowseResult, ContentEntry, AssetInspectResult, VerseFileContent } from '../../shared/types'

function formatDate(iso: string): string {
  const d = new Date(iso)
  const now = new Date()
  const diffMs = now.getTime() - d.getTime()
  const diffDays = Math.floor(diffMs / 86400000)
  if (diffDays === 0) return 'Today'
  if (diffDays === 1) return 'Yesterday'
  if (diffDays < 7) return `${diffDays}d ago`
  if (diffDays < 365) return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
  return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

function getTooltipText(entry: ContentEntry): string | null {
  if (entry.isDirectory) {
    if (entry.name === '__ExternalActors__') {
      return 'External Actors — each file represents one placed actor in the level with transform and property overrides. Only non-default values are stored.'
    }
    if (entry.name === '__ExternalObjects__') {
      return 'External Objects — non-actor data assets referenced by the level. Rarely modified directly.'
    }
    return null
  }
  const ext = entry.extension?.toLowerCase()
  if (ext === '.uasset') return 'Unreal Asset — binary asset file containing blueprints, materials, textures, or widget definitions. Inspected via copy-on-read.'
  if (ext === '.umap') return 'Unreal Level Map — contains world settings, HLOD, and partitioning metadata.'
  if (ext === '.uexp') return 'Unreal Export Data — bulk data companion to a .uasset file.'
  if (ext === '.verse') return 'Verse Source — gameplay logic script for UEFN.'
  return null
}

function InfoTooltip({ text }: { text: string }) {
  return (
    <span className="relative group/info inline-flex ml-1">
      <svg className="w-3.5 h-3.5 text-gray-600 group-hover/info:text-fn-rare cursor-help" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
        <circle cx="12" cy="12" r="10" />
        <path d="M12 16v-4M12 8h.01" />
      </svg>
      <span className="absolute left-5 top-1/2 -translate-y-1/2 z-50 hidden group-hover/info:block bg-fn-dark border border-fn-border rounded px-2 py-1.5 text-[10px] text-gray-300 w-64 shadow-lg whitespace-normal leading-relaxed pointer-events-none">
        {text}
      </span>
    </span>
  )
}

export function ContentBrowserPage() {
  const [data, setData] = useState<ContentBrowseResult | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [pathHistory, setPathHistory] = useState<string[]>([])
  const [viewMode, setViewMode] = useState<'list' | 'grid'>('list')

  // Side panel state
  const [inspecting, setInspecting] = useState<AssetInspectResult | null>(null)
  const [verseContent, setVerseContent] = useState<VerseFileContent | null>(null)
  const [sideLoading, setSideLoading] = useState(false)

  useEffect(() => {
    loadContent()
  }, [])

  async function loadContent(path?: string) {
    try {
      setLoading(true)
      setError(null)
      setInspecting(null)
      setVerseContent(null)
      const result = await window.electronAPI.forgeBrowseContent(path)
      // Normalize entries — sidecar returns { type: 'folder'|'uasset'|... } not { isDirectory, extension }
      if (result?.entries) {
        result.entries = result.entries.map((e: any) => ({
          ...e,
          isDirectory: e.isDirectory ?? e.type === 'folder',
          extension: e.extension ?? (e.type === 'folder' ? '' : `.${e.type || 'unknown'}`),
        }))
      }
      setData(result)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to browse content')
    } finally {
      setLoading(false)
    }
  }

  async function navigateInto(entry: ContentEntry) {
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

  async function navigateToBreadcrumb(index: number) {
    // index 0 = root, 1 = first segment, etc.
    const segments = breadcrumbSegments
    if (index < 0 || index >= segments.length - 1) {
      // Navigate to root
      setPathHistory([])
      setSearch('')
      await loadContent()
      return
    }
    // Build path from segments
    const targetPath = segments.slice(0, index + 1).join('/')
    setPathHistory((h) => [...h.slice(0, index)])
    setSearch('')
    await loadContent(targetPath || undefined)
  }

  async function handleEntryClick(entry: ContentEntry) {
    if (entry.isDirectory || (entry as any).type === 'folder') {
      await navigateInto(entry)
      return
    }

    // Determine file type from extension or type field
    const fileType = (entry as any).type || entry.extension?.replace('.', '') || ''

    // The entry.path from sidecar is relative to content root.
    // For inspection, we need the absolute path. The sidecar's inspect-asset
    // resolves relative paths against the active project's content directory.
    // But some sidecar responses include absolute paths — use as-is if absolute.
    const filePath = entry.path.includes(':') || entry.path.startsWith('/') || entry.path.startsWith('\\')
      ? entry.path  // absolute
      : entry.path  // relative — sidecar's inspect-asset will try to resolve it

    if (fileType === 'uasset') {
      try {
        setSideLoading(true)
        setVerseContent(null)
        const result = await window.electronAPI.forgeInspectAsset(filePath)
        setInspecting(result)
      } catch (err) {
        console.error('Inspect failed:', err)
        setInspecting(null)
      } finally {
        setSideLoading(false)
      }
    } else if (fileType === 'verse') {
      try {
        setSideLoading(true)
        setInspecting(null)
        const result = await window.electronAPI.forgeReadVerse(filePath)
        // Normalize source field
        const r = result as any
        setVerseContent({
          filePath: filePath,
          name: r.name || entry.name,
          content: r.source || r.content || '',
          lineCount: r.lineCount || 0,
        })
      } catch (err) {
        console.error('Read verse failed:', err)
        setVerseContent(null)
      } finally {
        setSideLoading(false)
      }
    }
  }

  const filteredEntries = useMemo(() => {
    const entries = data?.entries ?? []
    if (!search.trim()) return entries
    const q = search.toLowerCase()
    return entries.filter((e) => e.name.toLowerCase().includes(q))
  }, [data, search])

  // Sort: directories first, then by name
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

  const hasSidePanel = inspecting || verseContent || sideLoading

  function getFileIcon(entry: ContentEntry, large = false) {
    const sz = large ? 'w-8 h-8' : 'w-4 h-4'
    if (entry.isDirectory) {
      const isExternal = entry.name.includes('__External')
      const color = isExternal ? 'text-purple-400/50' : 'text-yellow-500/70'
      return (
        <svg className={`${sz} ${color}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
          <path d="M3 7v10a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-6l-2-2H5a2 2 0 00-2 2z" />
        </svg>
      )
    }
    const ext = entry.extension?.toLowerCase()
    if (ext === '.uasset') {
      return (
        <svg className={`${sz} text-fn-rare/70`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
          <path d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4" />
        </svg>
      )
    }
    if (ext === '.verse') {
      return (
        <svg className={`${sz} text-green-400/70`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
          <path d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4" />
        </svg>
      )
    }
    if (ext === '.umap') {
      return (
        <svg className={`${sz} text-fn-epic/70`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
          <path d="M3.055 11H5a2 2 0 012 2v1a2 2 0 002 2 2 2 0 012 2v2.945M8 3.935V5.5A2.5 2.5 0 0010.5 8h.5a2 2 0 012 2 2 2 0 104 0 2 2 0 012-2h1.064M15 20.488V18a2 2 0 012-2h3.064M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
        </svg>
      )
    }
    if (ext === '.uexp') {
      return (
        <svg className={`${sz} text-gray-500/60`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
          <path d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
        </svg>
      )
    }
    return (
      <svg className={`${sz} text-gray-500`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
        <path d="M7 21h10a2 2 0 002-2V9.414a1 1 0 00-.293-.707l-5.414-5.414A1 1 0 0012.586 3H7a2 2 0 00-2 2v14a2 2 0 002 2z" />
      </svg>
    )
  }

  function getTypeBadge(entry: ContentEntry) {
    if (entry.isDirectory) return null
    const ext = entry.extension?.toLowerCase()
    if (ext === '.uasset') return <span className="px-1 py-0.5 rounded text-[8px] font-medium text-fn-rare bg-fn-rare/10">UASSET</span>
    if (ext === '.verse') return <span className="px-1 py-0.5 rounded text-[8px] font-medium text-green-400 bg-green-400/10">VERSE</span>
    if (ext === '.umap') return <span className="px-1 py-0.5 rounded text-[8px] font-medium text-fn-epic bg-fn-epic/10">UMAP</span>
    if (ext === '.uexp') return <span className="px-1 py-0.5 rounded text-[8px] font-medium text-gray-500/80 bg-gray-500/10">UEXP</span>
    return <span className="px-1 py-0.5 rounded text-[8px] font-medium text-gray-500 bg-gray-500/10">{ext?.toUpperCase()?.slice(1) ?? 'FILE'}</span>
  }

  function formatSize(bytes?: number): string {
    if (bytes == null) return ''
    if (bytes < 1024) return `${bytes} B`
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  }

  if (loading && !data) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-[11px] text-gray-400">Loading content...</div>
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
    <div className="flex-1 flex bg-fn-darker overflow-hidden min-h-0">
      {/* Main content area */}
      <div className="flex-1 flex flex-col overflow-hidden">
        {/* Breadcrumb + search bar + view toggle */}
        <div className="px-4 py-2 border-b border-fn-border bg-fn-dark shrink-0 space-y-2">
          {/* Breadcrumb navigation */}
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
            <button
              onClick={() => navigateToBreadcrumb(-1)}
              className="text-fn-rare hover:text-fn-rare/80 transition-colors font-medium"
            >
              Content
            </button>
            {breadcrumbSegments.map((seg, i) => (
              <span key={i} className="flex items-center gap-1">
                <span className="text-gray-600">/</span>
                {i < breadcrumbSegments.length - 1 ? (
                  <button
                    onClick={() => navigateToBreadcrumb(i)}
                    className="text-fn-rare hover:text-fn-rare/80 transition-colors"
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
              className="flex-1 bg-fn-darker border border-fn-border rounded px-2.5 py-1.5 text-[11px] text-white placeholder-gray-600 focus:outline-none focus:border-fn-rare/50"
            />
            <div className="flex items-center border border-fn-border rounded overflow-hidden shrink-0">
              <button
                onClick={() => setViewMode('list')}
                className={`p-1.5 transition-colors ${viewMode === 'list' ? 'text-fn-rare bg-fn-rare/10' : 'text-gray-500 hover:text-gray-300'}`}
                title="List view"
              >
                <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path d="M4 6h16M4 12h16M4 18h16" />
                </svg>
              </button>
              <button
                onClick={() => setViewMode('grid')}
                className={`p-1.5 transition-colors ${viewMode === 'grid' ? 'text-fn-rare bg-fn-rare/10' : 'text-gray-500 hover:text-gray-300'}`}
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
        <div className="flex-1 overflow-y-auto min-h-0">
          {loading ? (
            <div className="p-4 text-center text-[11px] text-gray-400">Loading...</div>
          ) : sortedEntries.length === 0 ? (
            <div className="p-8 text-center">
              <p className="text-[11px] text-gray-400 mb-1">
                {search ? 'No matching entries' : 'Empty directory'}
              </p>
              <p className="text-[10px] text-gray-600">
                {search ? 'Try a different filter.' : 'This directory has no files.'}
              </p>
            </div>
          ) : viewMode === 'list' ? (
            /* List View */
            <div>
              {/* Column Headers */}
              <div className="flex items-center gap-3 px-4 py-1.5 border-b border-fn-border/40 bg-fn-dark/50 sticky top-0 z-10">
                <span className="w-4 shrink-0" /> {/* icon spacer */}
                <span className="text-[9px] font-semibold text-gray-500 uppercase tracking-wider flex-1 min-w-0">Name</span>
                <span className="text-[9px] font-semibold text-gray-500 uppercase tracking-wider w-14 shrink-0 text-center">Type</span>
                <span className="text-[9px] font-semibold text-gray-500 uppercase tracking-wider w-16 shrink-0 text-right">Size</span>
                <span className="text-[9px] font-semibold text-gray-500 uppercase tracking-wider w-20 shrink-0 text-right">Modified</span>
                <span className="w-3 shrink-0" /> {/* arrow spacer */}
              </div>
              {sortedEntries.map((entry) => {
                const tooltip = getTooltipText(entry)
                const isAsset = !entry.isDirectory && (entry.extension === '.uasset' || entry.extension === '.umap')
                const displayName = isAsset ? prettifyAssetName(entry.name) : entry.name
                return (
                  <button
                    key={entry.path}
                    onClick={() => handleEntryClick(entry)}
                    className="w-full flex items-center gap-3 px-4 py-2 border-b border-fn-border/20 hover:bg-white/[0.02] transition-colors text-left"
                  >
                    {getFileIcon(entry)}
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center">
                        <span className={`text-[11px] text-white truncate ${isAsset ? 'font-medium' : ''}`}>{displayName}</span>
                        {tooltip && <InfoTooltip text={tooltip} />}
                      </div>
                      {isAsset && displayName !== entry.name && (
                        <div className="text-[9px] text-gray-600 truncate mt-0.5">{entry.name}</div>
                      )}
                    </div>
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
                )
              })}
            </div>
          ) : (
            /* Grid View */
            <div className="p-3 grid grid-cols-[repeat(auto-fill,minmax(120px,1fr))] gap-2">
              {sortedEntries.map((entry) => {
                const tooltip = getTooltipText(entry)
                const isFolder = entry.isDirectory
                const isAsset = !isFolder && (entry.extension === '.uasset' || entry.extension === '.umap')
                const gridDisplayName = isAsset ? prettifyAssetName(entry.name) : entry.name
                return (
                  <button
                    key={entry.path}
                    onClick={() => handleEntryClick(entry)}
                    className={`flex flex-col items-center gap-1.5 p-3 rounded border transition-colors text-center ${
                      isFolder
                        ? 'bg-fn-dark/50 border-dashed border-fn-border/40 hover:bg-fn-panel hover:border-fn-border'
                        : 'bg-fn-darker border-fn-border/20 hover:bg-fn-panel hover:border-fn-border'
                    }`}
                    title={entry.name}
                  >
                    {getFileIcon(entry, true)}
                    <div className="w-full flex items-center justify-center">
                      <span className={`text-[10px] text-white truncate max-w-full ${isAsset ? 'font-medium' : ''}`}>{gridDisplayName}</span>
                      {tooltip && <InfoTooltip text={tooltip} />}
                    </div>
                    {getTypeBadge(entry)}
                  </button>
                )
              })}
            </div>
          )}
        </div>

        {/* Status bar */}
        <div className="px-4 py-1.5 border-t border-fn-border bg-fn-dark shrink-0 text-[10px] text-gray-600">
          {sortedEntries.length} item{sortedEntries.length !== 1 ? 's' : ''}
          {search && ` (filtered from ${data?.entries?.length ?? 0})`}
        </div>
      </div>

      {/* Side panel for inspection */}
      {hasSidePanel && (
        <div className="w-96 border-l border-fn-border bg-fn-dark flex flex-col overflow-hidden shrink-0">
          {/* Side panel header */}
          <div className="flex items-center justify-between px-4 py-2 border-b border-fn-border shrink-0">
            <div className="flex items-center gap-2 min-w-0">
              <span className="text-[11px] font-semibold text-white truncate">
                {inspecting?.fileName ?? verseContent?.name ?? 'Loading...'}
              </span>
              {inspecting && (
                <span className="flex items-center gap-1 text-[9px] text-gray-500 shrink-0">
                  <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                    <rect x="5" y="11" width="14" height="10" rx="2" />
                    <path d="M12 3a4 4 0 00-4 4v4h8V7a4 4 0 00-4-4z" />
                  </svg>
                  Copy-on-read
                </span>
              )}
            </div>
            <button
              onClick={() => {
                setInspecting(null)
                setVerseContent(null)
              }}
              className="p-1 text-gray-500 hover:text-gray-300 transition-colors"
            >
              <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>

          {/* Side panel body */}
          <div className="flex-1 overflow-y-auto min-h-0">
            {sideLoading ? (
              <div className="p-4 text-center text-[11px] text-gray-400">Loading...</div>
            ) : inspecting ? (
              <div className="p-4 space-y-4">
                {inspecting.assetClass && (
                  <div>
                    <span className="text-[9px] font-semibold text-gray-600 uppercase tracking-wider block mb-0.5">Asset Class</span>
                    <span className="text-[11px] text-fn-rare">{inspecting.assetClass}</span>
                  </div>
                )}

                {(inspecting.exports ?? []).length > 0 && (
                  <div>
                    <span className="text-[9px] font-semibold text-gray-600 uppercase tracking-wider block mb-1">
                      Exports ({inspecting.exports.length})
                    </span>
                    {inspecting.exports.map((exp: any, i: number) => (
                      <div key={i} className="text-[10px] py-0.5">
                        <span className="text-fn-epic">{exp.className || exp.exportType || 'Unknown'}</span>
                        <span className="text-gray-500 ml-1.5">{exp.objectName || ''}</span>
                        {exp.propertyCount != null && (
                          <span className="text-[8px] text-gray-600 ml-1">({exp.propertyCount} props)</span>
                        )}
                      </div>
                    ))}
                  </div>
                )}

                {/* Flatten all properties from all exports */}
                {(() => {
                  const allProps = inspecting.properties ?? (inspecting.exports ?? []).flatMap((e: any) => e.properties ?? [])
                  if (!allProps || allProps.length === 0) return null
                  return (
                    <div>
                      <span className="text-[9px] font-semibold text-gray-600 uppercase tracking-wider block mb-1">
                        Properties ({allProps.length})
                      </span>
                      <div className="bg-fn-darker rounded border border-fn-border/50 max-h-[400px] overflow-y-auto">
                        {allProps.map((prop: any, i: number) => (
                          <div key={i} className="px-2 py-1 border-b border-fn-border/20 last:border-b-0">
                            <div className="flex items-center justify-between">
                              <span className="text-[10px] text-gray-300 truncate">{prop.name}</span>
                              <span className="text-[8px] text-gray-600 ml-2 shrink-0">{prop.type}</span>
                            </div>
                            <span className="text-[10px] text-white truncate block font-mono">{prop.value}</span>
                          </div>
                        ))}
                      </div>
                    </div>
                  )
                })()}
              </div>
            ) : verseContent ? (
              <div className="p-2">
                <div className="text-[9px] text-gray-600 px-2 mb-1">{verseContent.lineCount} lines</div>
                <pre className="text-[10px] leading-4 font-mono text-gray-300 whitespace-pre overflow-x-auto p-2">{verseContent.content}</pre>
              </div>
            ) : null}
          </div>
        </div>
      )}
    </div>
  )
}
