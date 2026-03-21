import { useEffect, useMemo, useState } from 'react'
import { useLibraryStore } from '../stores/libraryStore'

export function LibraryMaterialsPage() {
  const activeLibrary = useLibraryStore((s) => s.activeLibrary)
  const assetsByType = useLibraryStore((s) => s.assetsByType)
  const assetsByTypeLoading = useLibraryStore((s) => s.assetsByTypeLoading)
  const assetsByTypeError = useLibraryStore((s) => s.assetsByTypeError)
  const fetchAssetsByType = useLibraryStore((s) => s.fetchAssetsByType)

  const [search, setSearch] = useState('')

  useEffect(() => {
    if (activeLibrary) fetchAssetsByType()
  }, [activeLibrary, fetchAssetsByType])

  const materials = useMemo(() => {
    if (!assetsByType) return []
    const materialTypes = ['Material', 'MaterialInstanceConstant', 'MaterialInstanceDynamic', 'MaterialFunction', 'MaterialParameterCollection']
    const all: { name: string; filePath: string; relativePath: string; assetClass: string }[] = []
    for (const type of materialTypes) {
      const entries = assetsByType[type]
      if (entries) all.push(...entries)
    }
    if (!search.trim()) return all
    const q = search.toLowerCase()
    return all.filter((m) => m.name.toLowerCase().includes(q) || m.relativePath.toLowerCase().includes(q))
  }, [assetsByType, search])

  if (!activeLibrary) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <svg className="w-10 h-10 mx-auto mb-2 text-gray-700" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
            <circle cx="12" cy="12" r="10" />
            <path d="M12 2a10 10 0 00-3 19.5M12 2a10 10 0 013 19.5" />
          </svg>
          <p className="text-[11px] text-gray-400 mb-1">No active library</p>
          <p className="text-[10px] text-gray-600">Select a library to browse Materials.</p>
        </div>
      </div>
    )
  }

  if (assetsByTypeLoading && !assetsByType) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-[11px] text-gray-400">Loading materials...</div>
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
    <div className="flex-1 bg-fn-darker overflow-y-auto">
      <div className="max-w-3xl mx-auto p-6 space-y-5">
        {/* Header */}
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-lg font-semibold text-white">Materials</h1>
            <span className="text-[8px] font-bold px-1.5 py-0.5 rounded border text-blue-400 bg-blue-400/10 border-blue-400/20">
              REF
            </span>
          </div>
          <p className="text-[11px] text-gray-500 mt-0.5">
            Materials and Material Instances from {activeLibrary.name} ({materials.length} found)
          </p>
        </div>

        {/* Search */}
        <input
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search materials..."
          className="w-full bg-fn-darker border border-fn-border rounded px-3 py-2 text-[11px] text-white placeholder-gray-600 focus:outline-none focus:border-blue-400/50"
        />

        {/* Material list */}
        {materials.length === 0 ? (
          <div className="bg-fn-panel border border-fn-border rounded-lg p-6 text-center">
            <p className="text-[11px] text-gray-400 mb-1">
              {search ? 'No matching materials' : 'No Materials found'}
            </p>
            <p className="text-[10px] text-gray-600">
              {search ? 'Try a different search term.' : 'Build the library index to discover materials.'}
            </p>
          </div>
        ) : (
          <div className="bg-fn-dark border border-fn-border rounded-lg overflow-hidden">
            <div className="grid grid-cols-[1fr_auto_auto] gap-4 px-4 py-2 border-b border-fn-border bg-fn-panel">
              <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Material Name</span>
              <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Type</span>
              <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Path</span>
            </div>
            {materials.map((m) => (
              <div
                key={m.filePath}
                className="grid grid-cols-[1fr_auto_auto] gap-4 px-4 py-2.5 border-b border-fn-border/30 hover:bg-white/[0.02] transition-colors"
              >
                <div className="flex items-center gap-2 min-w-0">
                  <svg className="w-3.5 h-3.5 text-purple-400/60 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                    <circle cx="12" cy="12" r="10" />
                    <path d="M12 2a10 10 0 00-3 19.5M12 2a10 10 0 013 19.5" />
                  </svg>
                  <span className="text-[11px] text-white truncate">{m.name}</span>
                </div>
                <span className="text-[9px] text-gray-500 shrink-0 px-1.5 py-0.5 rounded bg-purple-400/5 text-purple-400/70">{m.assetClass}</span>
                <span className="text-[10px] text-gray-600 truncate max-w-[200px]">{m.relativePath}</span>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
