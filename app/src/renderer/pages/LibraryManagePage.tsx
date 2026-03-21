import { useEffect, useState } from 'react'
import { useLibraryStore } from '../stores/libraryStore'
import { ErrorMessage } from '../components/ErrorMessage'

export function LibraryManagePage({ onNavigate }: { onNavigate: (page: string) => void }) {
  const libraryList = useLibraryStore((s) => s.libraryList)
  const libraryListLoading = useLibraryStore((s) => s.libraryListLoading)
  const libraryListError = useLibraryStore((s) => s.libraryListError)
  const indexing = useLibraryStore((s) => s.indexing)
  const fetchLibraries = useLibraryStore((s) => s.fetchLibraries)
  const addLibrary = useLibraryStore((s) => s.addLibrary)
  const removeLibrary = useLibraryStore((s) => s.removeLibrary)
  const activateLibrary = useLibraryStore((s) => s.activateLibrary)
  const indexActiveLibrary = useLibraryStore((s) => s.indexActiveLibrary)

  const [error, setError] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)

  useEffect(() => {
    fetchLibraries()
  }, [fetchLibraries])

  async function handleBrowseAdd() {
    try {
      const dir = await window.electronAPI.selectDirectory()
      if (!dir) return
      setAdding(true)
      setError(null)
      await addLibrary(dir)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to add library')
    } finally {
      setAdding(false)
    }
  }

  async function handleRemove(id: string) {
    try {
      setError(null)
      await removeLibrary(id)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to remove library')
    }
  }

  async function handleActivate(id: string) {
    try {
      setError(null)
      await activateLibrary(id)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to activate library')
    }
  }

  async function handleIndex() {
    try {
      setError(null)
      await indexActiveLibrary()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to index library')
    }
  }

  function formatTimeAgo(iso: string | null | undefined): string {
    if (!iso) return 'Not indexed'
    const d = new Date(iso)
    const diffMs = Date.now() - d.getTime()
    const diffMin = Math.floor(diffMs / 60000)
    if (diffMin < 1) return 'Just now'
    if (diffMin < 60) return `${diffMin}m ago`
    const diffHours = Math.floor(diffMin / 60)
    if (diffHours < 24) return `${diffHours}h ago`
    const diffDays = Math.floor(diffHours / 24)
    return `${diffDays}d ago`
  }

  const libraries = libraryList?.libraries ?? []
  const activeId = libraryList?.activeLibraryId ?? null

  if (libraryListLoading && !libraryList) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-[11px] text-gray-400">Loading libraries...</div>
      </div>
    )
  }

  return (
    <div className="flex-1 bg-fn-darker overflow-y-auto min-h-0">
      <div className="max-w-3xl mx-auto p-6 space-y-5">
        {/* Header */}
        <div>
          <h1 className="text-lg font-semibold text-white">Reference Libraries</h1>
          <p className="text-[11px] text-gray-500 mt-0.5">
            Reference collection — read-only. Add paths to UEFN project collections for browsing.
          </p>
        </div>

        {/* Error display */}
        {(error || libraryListError) && (
          <ErrorMessage message={(error || libraryListError)!} />
        )}

        {/* Safety note */}
        <div className="flex items-center gap-2 bg-blue-400/5 border border-blue-400/15 rounded-lg px-3 py-2">
          <svg className="w-4 h-4 text-blue-400/70 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          <span className="text-[10px] text-blue-400/70">
            Libraries are read-only reference collections. Source files are never modified. Browse verse, widgets, materials, and device configs from other UEFN projects.
          </span>
        </div>

        {/* Library List */}
        <div>
          <h3 className="text-[11px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
            Libraries ({libraries.length})
          </h3>

          {libraries.length === 0 ? (
            <div className="bg-fn-panel border border-fn-border rounded-lg p-6 text-center">
              <svg className="w-10 h-10 mx-auto mb-2 text-gray-700" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
                <path d="M8 14v3m4-3v3m4-3v3M3 21h18M3 10h18M3 7l9-4 9 4M4 10h16v11H4V10z" />
              </svg>
              <p className="text-[11px] text-gray-400 mb-1">No libraries added yet</p>
              <p className="text-[10px] text-gray-600">
                Add a path to a directory containing UEFN projects
              </p>
            </div>
          ) : (
            <div className="space-y-2">
              {libraries.map((lib) => {
                const isActive = lib.id === activeId
                return (
                  <div
                    key={lib.id}
                    className={`bg-fn-panel border rounded-lg p-3 transition-colors ${
                      isActive ? 'border-blue-400/40 bg-blue-400/[0.03]' : 'border-fn-border'
                    }`}
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-2 mb-0.5">
                          {isActive && (
                            <span className="w-1.5 h-1.5 rounded-full bg-blue-400 shrink-0" />
                          )}
                          <span className="text-[11px] font-medium text-white truncate">{lib.name}</span>
                          <span className="inline-block px-1.5 py-0.5 rounded text-[9px] font-medium border shrink-0 text-blue-400 bg-blue-400/10 border-blue-400/20">
                            REF
                          </span>
                        </div>
                        <p className="text-[10px] text-gray-500 truncate">{lib.path}</p>

                        {/* Stats row */}
                        <div className="flex items-center gap-3 mt-1.5">
                          <span className="text-[10px] text-gray-500">
                            {lib.verseFileCount ?? '?'} verse files
                          </span>
                          <span className="text-[10px] text-gray-600">|</span>
                          <span className="text-[10px] text-gray-500">
                            {lib.assetCount ?? '?'} assets
                          </span>
                          <span className="text-[10px] text-gray-600">|</span>
                          <span className={`text-[10px] ${lib.indexedAt ? 'text-green-400/70' : 'text-amber-400/70'}`}>
                            {lib.indexedAt ? `Indexed ${formatTimeAgo(lib.indexedAt)}` : 'Not indexed'}
                          </span>
                        </div>
                      </div>

                      {/* Action buttons */}
                      <div className="flex items-center gap-1.5 shrink-0">
                        {!isActive && (
                          <button
                            onClick={() => handleActivate(lib.id)}
                            className="px-2 py-1 text-[10px] font-medium text-blue-400 bg-blue-400/10 border border-blue-400/20 rounded hover:bg-blue-400/20 transition-colors"
                          >
                            Activate
                          </button>
                        )}
                        {isActive && (
                          <button
                            onClick={handleIndex}
                            disabled={indexing}
                            className={`px-2 py-1 text-[10px] font-medium rounded transition-colors disabled:opacity-40 ${
                              lib.indexedAt
                                ? 'text-blue-400 bg-blue-400/10 border border-blue-400/20 hover:bg-blue-400/20'
                                : 'text-amber-400 bg-amber-400/10 border border-amber-400/20 hover:bg-amber-400/20 animate-pulse'
                            }`}
                          >
                            {indexing ? 'Indexing...' : lib.indexedAt ? 'Re-index' : 'Build Index'}
                          </button>
                        )}
                        <button
                          onClick={() => handleRemove(lib.id)}
                          className="px-2 py-1 text-[10px] font-medium text-gray-500 bg-fn-darker border border-fn-border rounded hover:text-red-400 hover:border-red-400/30 transition-colors"
                        >
                          Remove
                        </button>
                      </div>
                    </div>
                  </div>
                )
              })}
            </div>
          )}
        </div>

        {/* Add Library */}
        <div>
          <h3 className="text-[11px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
            Add Library
          </h3>
          <div className="bg-fn-panel border border-fn-border rounded-lg p-4 space-y-3">
            <p className="text-[10px] text-gray-500">
              Select a directory containing UEFN projects. The entire directory tree will be browsable.
            </p>

            <button
              onClick={handleBrowseAdd}
              disabled={adding}
              className="px-4 py-1.5 text-[10px] font-medium text-white bg-blue-400/20 border border-blue-400/30 rounded hover:bg-blue-400/30 transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
            >
              {adding ? 'Adding...' : 'Browse & Add Library Path'}
            </button>

            <div className="flex items-center gap-2 text-[10px] text-blue-400/60">
              <svg className="w-3.5 h-3.5 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
              </svg>
              <span>Read-only — source files are never modified or locked.</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
