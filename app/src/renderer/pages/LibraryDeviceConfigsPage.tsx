import { useEffect, useMemo, useState } from 'react'
import { useLibraryStore } from '../stores/libraryStore'

export function LibraryDeviceConfigsPage() {
  const activeLibrary = useLibraryStore((s) => s.activeLibrary)
  const assetsByType = useLibraryStore((s) => s.assetsByType)
  const assetsByTypeLoading = useLibraryStore((s) => s.assetsByTypeLoading)
  const assetsByTypeError = useLibraryStore((s) => s.assetsByTypeError)
  const fetchAssetsByType = useLibraryStore((s) => s.fetchAssetsByType)

  const [search, setSearch] = useState('')

  useEffect(() => {
    if (activeLibrary) fetchAssetsByType()
  }, [activeLibrary, fetchAssetsByType])

  // Find device-related asset types
  const deviceTypes = useMemo(() => {
    if (!assetsByType) return []
    const devicePatterns = ['Device', 'Creative', 'Spawner', 'Trigger', 'Manager', 'Controller', 'Placer', 'Generator']
    return Object.entries(assetsByType)
      .filter(([type]) => {
        return devicePatterns.some((p) => type.includes(p))
      })
      .map(([type, entries]) => ({ type, entries, count: entries.length }))
      .filter((t) => {
        if (!search.trim()) return true
        const q = search.toLowerCase()
        return t.type.toLowerCase().includes(q) || t.entries.some((e) => e.name.toLowerCase().includes(q))
      })
      .sort((a, b) => b.count - a.count)
  }, [assetsByType, search])

  const totalDevices = useMemo(() => {
    return deviceTypes.reduce((sum, t) => sum + t.count, 0)
  }, [deviceTypes])

  if (!activeLibrary) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <svg className="w-10 h-10 mx-auto mb-2 text-gray-700" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
            <path d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.066 2.573c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.573 1.066c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.066-2.573c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.573-1.066z" />
            <path d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
          </svg>
          <p className="text-[11px] text-gray-400 mb-1">No active library</p>
          <p className="text-[10px] text-gray-600">Select a library to browse Device Configs.</p>
        </div>
      </div>
    )
  }

  if (assetsByTypeLoading && !assetsByType) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-[11px] text-gray-400">Loading device configs...</div>
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
            <h1 className="text-lg font-semibold text-white">Device Configs</h1>
            <span className="text-[8px] font-bold px-1.5 py-0.5 rounded border text-blue-400 bg-blue-400/10 border-blue-400/20">
              REF
            </span>
          </div>
          <p className="text-[11px] text-gray-500 mt-0.5">
            Device types from {activeLibrary.name} ({deviceTypes.length} types, {totalDevices} instances)
          </p>
        </div>

        {/* Search */}
        <input
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search device types..."
          className="w-full bg-fn-darker border border-fn-border rounded px-3 py-2 text-[11px] text-white placeholder-gray-600 focus:outline-none focus:border-blue-400/50"
        />

        {/* Device type list */}
        {deviceTypes.length === 0 ? (
          <div className="bg-fn-panel border border-fn-border rounded-lg p-6 text-center">
            <p className="text-[11px] text-gray-400 mb-1">
              {search ? 'No matching device types' : 'No device types found'}
            </p>
            <p className="text-[10px] text-gray-600">
              Build the library index to discover device configurations.
            </p>
          </div>
        ) : (
          <div className="space-y-2">
            {deviceTypes.map(({ type, entries, count }) => (
              <div key={type} className="bg-fn-dark border border-fn-border rounded-lg p-3">
                <div className="flex items-center gap-3">
                  <svg className="w-4 h-4 text-orange-400/60 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                    <path d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.066 2.573c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.573 1.066c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.066-2.573c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.573-1.066z" />
                    <path d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                  </svg>
                  <span className="text-[11px] font-medium text-white flex-1">{type}</span>
                  <span className="text-[10px] text-gray-500 px-1.5 py-0.5 rounded bg-fn-darker border border-fn-border/50">
                    {count} instance{count !== 1 ? 's' : ''}
                  </span>
                </div>
                {entries.length > 0 && (
                  <div className="mt-2 pl-7">
                    {entries.slice(0, 5).map((entry) => (
                      <div key={entry.filePath} className="text-[10px] text-gray-500 py-0.5 truncate">
                        {entry.relativePath}
                      </div>
                    ))}
                    {entries.length > 5 && (
                      <div className="text-[10px] text-gray-600 py-0.5">
                        + {entries.length - 5} more
                      </div>
                    )}
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
