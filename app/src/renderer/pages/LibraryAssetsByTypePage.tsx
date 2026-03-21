import { useEffect, useMemo, useState } from 'react'
import { useLibraryStore } from '../stores/libraryStore'

export function LibraryAssetsByTypePage() {
  const activeLibrary = useLibraryStore((s) => s.activeLibrary)
  const assetsByType = useLibraryStore((s) => s.assetsByType)
  const assetsByTypeLoading = useLibraryStore((s) => s.assetsByTypeLoading)
  const assetsByTypeError = useLibraryStore((s) => s.assetsByTypeError)
  const fetchAssetsByType = useLibraryStore((s) => s.fetchAssetsByType)

  const [search, setSearch] = useState('')
  const [expandedTypes, setExpandedTypes] = useState<Set<string>>(new Set())

  useEffect(() => {
    if (activeLibrary) fetchAssetsByType()
  }, [activeLibrary, fetchAssetsByType])

  const sortedTypes = useMemo(() => {
    if (!assetsByType) return []
    return Object.entries(assetsByType)
      .map(([type, entries]) => ({ type, entries, count: entries.length }))
      .filter((t) => {
        if (!search.trim()) return true
        const q = search.toLowerCase()
        return t.type.toLowerCase().includes(q) || t.entries.some((e) => e.name.toLowerCase().includes(q))
      })
      .sort((a, b) => b.count - a.count)
  }, [assetsByType, search])

  const totalAssets = useMemo(() => {
    return sortedTypes.reduce((sum, t) => sum + t.count, 0)
  }, [sortedTypes])

  function toggleType(type: string) {
    setExpandedTypes((prev) => {
      const next = new Set(prev)
      if (next.has(type)) {
        next.delete(type)
      } else {
        next.add(type)
      }
      return next
    })
  }

  if (!activeLibrary) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <svg className="w-10 h-10 mx-auto mb-2 text-gray-700" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
            <path d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
          </svg>
          <p className="text-[11px] text-gray-400 mb-1">No active library</p>
          <p className="text-[10px] text-gray-600">Select a library to browse assets by type.</p>
        </div>
      </div>
    )
  }

  if (assetsByTypeLoading && !assetsByType) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-[11px] text-gray-400">Loading assets...</div>
      </div>
    )
  }

  if (assetsByTypeError) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="text-[11px] text-red-400 mb-3">{assetsByTypeError}</div>
          <button
            onClick={() => {
              useLibraryStore.setState({ assetsByTypeFetchedAt: null })
              fetchAssetsByType()
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
    <div className="flex-1 bg-fn-darker overflow-y-auto min-h-0">
      <div className="max-w-3xl mx-auto p-6 space-y-5">
        {/* Header */}
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-lg font-semibold text-white">Assets by Type</h1>
            <span className="text-[8px] font-bold px-1.5 py-0.5 rounded border text-blue-400 bg-blue-400/10 border-blue-400/20">
              REF
            </span>
          </div>
          <p className="text-[11px] text-gray-500 mt-0.5">
            {sortedTypes.length} asset types, {totalAssets} total assets in {activeLibrary.name}
          </p>
        </div>

        {/* Search */}
        <input
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search asset types or names..."
          className="w-full bg-fn-darker border border-fn-border rounded px-3 py-2 text-[11px] text-white placeholder-gray-600 focus:outline-none focus:border-blue-400/50"
        />

        {/* Type list */}
        {sortedTypes.length === 0 ? (
          <div className="bg-fn-panel border border-fn-border rounded-lg p-6 text-center">
            <p className="text-[11px] text-gray-400 mb-1">
              {search ? 'No matching asset types' : 'No assets found'}
            </p>
            <p className="text-[10px] text-gray-600">
              Build the library index to discover assets.
            </p>
          </div>
        ) : (
          <div className="space-y-1">
            {sortedTypes.map(({ type, entries, count }) => {
              const isExpanded = expandedTypes.has(type)
              return (
                <div key={type} className="bg-fn-dark border border-fn-border rounded-lg overflow-hidden">
                  <button
                    onClick={() => toggleType(type)}
                    className="w-full flex items-center gap-3 px-4 py-2.5 hover:bg-white/[0.02] transition-colors text-left"
                  >
                    <svg
                      className={`w-3 h-3 text-gray-500 transition-transform shrink-0 ${isExpanded ? 'rotate-90' : ''}`}
                      fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}
                    >
                      <path d="M9 5l7 7-7 7" />
                    </svg>
                    <span className="text-[11px] font-medium text-white flex-1">{type}</span>
                    <span className="text-[10px] text-gray-500 shrink-0 px-1.5 py-0.5 rounded bg-fn-darker border border-fn-border/50">
                      {count}
                    </span>
                  </button>

                  {isExpanded && (
                    <div className="border-t border-fn-border/50">
                      {entries.slice(0, 50).map((entry) => (
                        <div
                          key={entry.filePath}
                          className="flex items-center gap-3 px-4 pl-9 py-1.5 border-b border-fn-border/20 hover:bg-white/[0.02] transition-colors"
                        >
                          <span className="text-[10px] text-white truncate flex-1">{entry.name}</span>
                          <span className="text-[9px] text-gray-600 truncate max-w-[200px]">{entry.relativePath}</span>
                        </div>
                      ))}
                      {entries.length > 50 && (
                        <div className="px-4 pl-9 py-2 text-[10px] text-gray-600">
                          ... and {entries.length - 50} more
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}
