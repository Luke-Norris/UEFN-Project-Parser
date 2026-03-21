import { useEffect, useState, useMemo } from 'react'
import type { ContentBrowseResult, ContentEntry, VerseFileContent } from '../../shared/types'

export function VerseFilesPage() {
  const [data, setData] = useState<ContentBrowseResult | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [viewingFile, setViewingFile] = useState<VerseFileContent | null>(null)
  const [viewLoading, setViewLoading] = useState(false)

  useEffect(() => {
    loadVerseFiles()
  }, [])

  async function loadVerseFiles() {
    try {
      setLoading(true)
      setError(null)
      // Recursively find all .verse files by walking the content directory
      const allVerseFiles: ContentEntry[] = []

      async function scanDir(path?: string) {
        const result = await window.electronAPI.forgeBrowseContent(path)
        for (const entry of result?.entries ?? []) {
          // Sidecar returns type: "folder" for dirs, not isDirectory
          const isDir = entry.isDirectory || (entry as any).type === 'folder'
          if (isDir) {
            // Skip __External* directories (huge, no verse)
            if (entry.name?.startsWith('__')) continue
            await scanDir(entry.path)
          } else if (entry.name?.endsWith('.verse') || (entry as any).type === 'verse') {
            allVerseFiles.push(entry)
          }
        }
      }

      await scanDir()
      setData({ currentPath: '', relativePath: '', entries: allVerseFiles })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load verse files')
    } finally {
      setLoading(false)
    }
  }

  const verseFiles = useMemo(() => {
    return data?.entries ?? []
  }, [data])

  const filteredFiles = useMemo(() => {
    if (!search.trim()) return verseFiles
    const q = search.toLowerCase()
    return verseFiles.filter((f) => f.name.toLowerCase().includes(q))
  }, [verseFiles, search])

  async function handleViewFile(entry: ContentEntry) {
    try {
      setViewLoading(true)
      const result = await window.electronAPI.forgeReadVerse(entry.path)
      setViewingFile(result)
    } catch (err) {
      console.error('Failed to read verse file:', err)
    } finally {
      setViewLoading(false)
    }
  }

  function formatSize(bytes?: number): string {
    if (bytes == null) return ''
    if (bytes < 1024) return `${bytes} B`
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  }

  if (loading) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-[11px] text-gray-400">Loading verse files...</div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="text-[11px] text-red-400 mb-3">{error}</div>
          <button
            onClick={loadVerseFiles}
            className="px-3 py-1.5 text-[10px] font-medium text-white bg-fn-panel border border-fn-border rounded hover:bg-white/[0.06] transition-colors"
          >
            Retry
          </button>
        </div>
      </div>
    )
  }

  // If viewing a file, show the source panel
  if (viewingFile || viewLoading) {
    return (
      <div className="flex-1 bg-fn-darker overflow-hidden flex flex-col">
        {/* Header bar */}
        <div className="flex items-center gap-3 px-6 py-3 border-b border-fn-border bg-fn-dark shrink-0">
          <button
            onClick={() => setViewingFile(null)}
            className="flex items-center gap-1.5 px-2 py-1 text-[10px] font-medium text-gray-400 hover:text-white bg-fn-darker border border-fn-border rounded transition-colors"
          >
            <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M15 19l-7-7 7-7" />
            </svg>
            Back
          </button>
          {viewingFile && (
            <>
              <span className="text-[11px] font-medium text-white">{viewingFile.name}</span>
              <span className="text-[10px] text-gray-500">{viewingFile.lineCount} lines</span>
            </>
          )}
        </div>

        {viewLoading ? (
          <div className="flex-1 flex items-center justify-center">
            <div className="text-[11px] text-gray-400">Loading source...</div>
          </div>
        ) : viewingFile ? (
          <div className="flex-1 overflow-auto">
            <pre className="p-4 text-[11px] leading-5 font-mono text-gray-300 whitespace-pre">{viewingFile.content}</pre>
          </div>
        ) : null}
      </div>
    )
  }

  return (
    <div className="flex-1 bg-fn-darker overflow-y-auto">
      <div className="max-w-3xl mx-auto p-6 space-y-5">
        {/* Header */}
        <div>
          <h1 className="text-lg font-semibold text-white">Verse Files</h1>
          <p className="text-[11px] text-gray-500 mt-0.5">
            Browse and view Verse gameplay logic scripts ({verseFiles.length} files)
          </p>
        </div>

        {/* Search */}
        <input
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search verse files..."
          className="w-full bg-fn-darker border border-fn-border rounded px-3 py-2 text-[11px] text-white placeholder-gray-600 focus:outline-none focus:border-fn-rare/50"
        />

        {/* File list */}
        {filteredFiles.length === 0 ? (
          <div className="bg-fn-panel border border-fn-border rounded-lg p-6 text-center">
            <p className="text-[11px] text-gray-400 mb-1">
              {search ? 'No matching verse files' : 'No verse files found'}
            </p>
            <p className="text-[10px] text-gray-600">
              {search ? 'Try a different search term.' : 'Verse files live in the project Content directory.'}
            </p>
          </div>
        ) : (
          <div className="bg-fn-dark border border-fn-border rounded-lg overflow-hidden">
            {/* Table header */}
            <div className="grid grid-cols-[1fr_2fr_auto] gap-4 px-4 py-2 border-b border-fn-border bg-fn-panel">
              <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">File Name</span>
              <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Path</span>
              <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Size</span>
            </div>

            {/* Table rows */}
            {filteredFiles.map((file) => (
              <button
                key={file.path}
                onClick={() => handleViewFile(file)}
                className="w-full grid grid-cols-[1fr_2fr_auto] gap-4 px-4 py-2.5 border-b border-fn-border/30 hover:bg-white/[0.02] transition-colors text-left"
              >
                <div className="flex items-center gap-2 min-w-0">
                  <svg className="w-3.5 h-3.5 text-green-400/70 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                    <path d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4" />
                  </svg>
                  <span className="text-[11px] text-white truncate">{file.name}</span>
                </div>
                <span className="text-[11px] text-gray-500 truncate self-center">{file.relativePath}</span>
                <span className="text-[10px] text-gray-500 self-center shrink-0">{formatSize(file.size)}</span>
              </button>
            ))}
          </div>
        )}

        {/* Search count */}
        {search && filteredFiles.length > 0 && (
          <div className="text-[10px] text-gray-600">
            {filteredFiles.length} result{filteredFiles.length !== 1 ? 's' : ''} for &quot;{search}&quot;
          </div>
        )}
      </div>
    </div>
  )
}
