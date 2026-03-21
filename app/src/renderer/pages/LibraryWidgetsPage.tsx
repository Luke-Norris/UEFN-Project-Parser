import { useEffect, useMemo, useState } from 'react'
import { useLibraryStore } from '../stores/libraryStore'

export function LibraryWidgetsPage() {
  const activeLibrary = useLibraryStore((s) => s.activeLibrary)
  const assetsByType = useLibraryStore((s) => s.assetsByType)
  const assetsByTypeLoading = useLibraryStore((s) => s.assetsByTypeLoading)
  const assetsByTypeError = useLibraryStore((s) => s.assetsByTypeError)
  const fetchAssetsByType = useLibraryStore((s) => s.fetchAssetsByType)

  const [search, setSearch] = useState('')

  useEffect(() => {
    if (activeLibrary) fetchAssetsByType()
  }, [activeLibrary, fetchAssetsByType])

  const widgets = useMemo(() => {
    if (!assetsByType) return []
    const widgetTypes = ['WidgetBlueprint', 'UserWidget', 'WidgetBlueprintGeneratedClass']
    const all: { name: string; filePath: string; relativePath: string; assetClass: string }[] = []
    for (const type of widgetTypes) {
      const entries = assetsByType[type]
      if (entries) all.push(...entries)
    }
    if (!search.trim()) return all
    const q = search.toLowerCase()
    return all.filter((w) => w.name.toLowerCase().includes(q) || w.relativePath.toLowerCase().includes(q))
  }, [assetsByType, search])

  if (!activeLibrary) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <svg className="w-10 h-10 mx-auto mb-2 text-gray-700" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
            <path d="M4 5a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1H5a1 1 0 01-1-1V5zm10 0a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1h-4a1 1 0 01-1-1V5zM4 15a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1H5a1 1 0 01-1-1v-4zm10 0a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1h-4a1 1 0 01-1-1v-4z" />
          </svg>
          <p className="text-[11px] text-gray-400 mb-1">No active library</p>
          <p className="text-[10px] text-gray-600">Select a library to browse Widget Blueprints.</p>
        </div>
      </div>
    )
  }

  if (assetsByTypeLoading && !assetsByType) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-[11px] text-gray-400">Loading widgets...</div>
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
            <h1 className="text-lg font-semibold text-white">Widget Blueprints</h1>
            <span className="text-[8px] font-bold px-1.5 py-0.5 rounded border text-blue-400 bg-blue-400/10 border-blue-400/20">
              REF
            </span>
          </div>
          <p className="text-[11px] text-gray-500 mt-0.5">
            Widget Blueprints from {activeLibrary.name} ({widgets.length} found)
          </p>
        </div>

        {/* Search */}
        <input
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search widgets..."
          className="w-full bg-fn-darker border border-fn-border rounded px-3 py-2 text-[11px] text-white placeholder-gray-600 focus:outline-none focus:border-blue-400/50"
        />

        {/* Widget list */}
        {widgets.length === 0 ? (
          <div className="bg-fn-panel border border-fn-border rounded-lg p-6 text-center">
            <p className="text-[11px] text-gray-400 mb-1">
              {search ? 'No matching widgets' : 'No Widget Blueprints found'}
            </p>
            <p className="text-[10px] text-gray-600">
              {search ? 'Try a different search term.' : 'Build the library index to discover widgets.'}
            </p>
          </div>
        ) : (
          <div className="bg-fn-dark border border-fn-border rounded-lg overflow-hidden">
            <div className="grid grid-cols-[1fr_auto_auto] gap-4 px-4 py-2 border-b border-fn-border bg-fn-panel">
              <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Widget Name</span>
              <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Type</span>
              <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Path</span>
            </div>
            {widgets.map((w) => (
              <div
                key={w.filePath}
                className="grid grid-cols-[1fr_auto_auto] gap-4 px-4 py-2.5 border-b border-fn-border/30 hover:bg-white/[0.02] transition-colors"
              >
                <div className="flex items-center gap-2 min-w-0">
                  <svg className="w-3.5 h-3.5 text-fn-rare/60 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                    <path d="M4 5a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1H5a1 1 0 01-1-1V5zm10 0a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1h-4a1 1 0 01-1-1V5zM4 15a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1H5a1 1 0 01-1-1v-4zm10 0a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1h-4a1 1 0 01-1-1v-4z" />
                  </svg>
                  <span className="text-[11px] text-white truncate">{w.name}</span>
                </div>
                <span className="text-[9px] text-gray-500 shrink-0">{w.assetClass}</span>
                <span className="text-[10px] text-gray-600 truncate max-w-[200px]">{w.relativePath}</span>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
