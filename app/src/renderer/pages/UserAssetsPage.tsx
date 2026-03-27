import { useEffect, useState, useMemo, useCallback } from 'react'
import { useForgeStore } from '../stores/forgeStore'
import { ErrorMessage } from '../components/ErrorMessage'
import { prettifyAssetName, categorizeAsset, formatFileSize } from '../lib/assetNames'
import type {
  UserAssetEntry,
  AssetInspectResult,
  AssetProperty,
} from '../../shared/types'

type SortField = 'name' | 'class' | 'size'

export function UserAssetsPage() {
  const data = useForgeStore((s) => s.userAssets)
  const loading = useForgeStore((s) => s.userAssetsLoading)
  const error = useForgeStore((s) => s.userAssetsError)
  const fetchUserAssets = useForgeStore((s) => s.fetchUserAssets)

  const [search, setSearch] = useState('')

  // Sorting
  const [sortField, setSortField] = useState<SortField>('name')
  const [sortReversed, setSortReversed] = useState(false)

  // Grouping collapse state
  const [collapsedGroups, setCollapsedGroups] = useState<Set<string>>(new Set())

  // Selection & inspection
  const [selectedAsset, setSelectedAsset] = useState<UserAssetEntry | null>(null)
  const [inspectedAsset, setInspectedAsset] = useState<AssetInspectResult | null>(null)
  const [inspectLoading, setInspectLoading] = useState(false)
  const [inspectError, setInspectError] = useState<string | null>(null)
  const [copiedProp, setCopiedProp] = useState<string | null>(null)

  useEffect(() => {
    fetchUserAssets()
  }, [fetchUserAssets])

  function handleSort(field: SortField) {
    if (sortField === field) {
      setSortReversed(!sortReversed)
    } else {
      setSortField(field)
      setSortReversed(false)
    }
  }

  // Handle both { assets: [...] } and raw array from sidecar
  const normalizedAssets = useMemo((): UserAssetEntry[] => {
    if (!data) return []
    if (Array.isArray(data)) return data as unknown as UserAssetEntry[]
    return data.assets ?? []
  }, [data])

  const totalCount = useMemo((): number => {
    if (!data) return 0
    if (Array.isArray(data)) return (data as unknown as UserAssetEntry[]).length
    return data.totalCount ?? data.assets?.length ?? 0
  }, [data])

  const filteredAssets = useMemo(() => {
    if (!search.trim()) return normalizedAssets
    const q = search.toLowerCase()
    return normalizedAssets.filter(
      (a) =>
        a.name.toLowerCase().includes(q) ||
        prettifyAssetName(a.name).toLowerCase().includes(q) ||
        a.assetClass.toLowerCase().includes(q)
    )
  }, [normalizedAssets, search])

  const groupedAndSorted = useMemo(() => {
    // Group by category (human-readable) instead of raw class
    const groups = new Map<string, UserAssetEntry[]>()
    for (const asset of filteredAssets) {
      const category = categorizeAsset(asset.assetClass, asset.name)
      if (!groups.has(category)) groups.set(category, [])
      groups.get(category)!.push(asset)
    }

    // Sort assets within each group
    for (const [, assets] of groups) {
      assets.sort((a, b) => {
        switch (sortField) {
          case 'name':
            return a.name.localeCompare(b.name)
          case 'class':
            return a.assetClass.localeCompare(b.assetClass)
          case 'size':
            return b.size - a.size
          default:
            return 0
        }
      })
      if (sortReversed) assets.reverse()
    }

    // Sort groups by name, then by sort field if applicable
    const entries = Array.from(groups.entries())
    if (sortField === 'size') {
      // Sort groups by total size
      entries.sort(([, a], [, b]) => {
        const totalA = a.reduce((sum, x) => sum + x.size, 0)
        const totalB = b.reduce((sum, x) => sum + x.size, 0)
        return sortReversed ? totalA - totalB : totalB - totalA
      })
    } else {
      entries.sort(([a], [b]) => {
        const cmp = a.localeCompare(b)
        return sortReversed ? -cmp : cmp
      })
    }

    return entries
  }, [filteredAssets, sortField, sortReversed])

  // Flat sorted list for the left panel (ungrouped mode not needed, but useful for counting)
  const totalFiltered = filteredAssets.length

  function toggleGroup(cls: string) {
    setCollapsedGroups((prev) => {
      const next = new Set(prev)
      if (next.has(cls)) next.delete(cls)
      else next.add(cls)
      return next
    })
  }

  function handleSelectAsset(asset: UserAssetEntry) {
    setSelectedAsset(asset)
    setInspectedAsset(null)
  }

  const inspectAsset = useCallback(async (path: string) => {
    try {
      setInspectLoading(true)
      setInspectError(null)
      const result = await window.electronAPI.forgeInspectAsset(path)
      setInspectedAsset(result)
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err)
      console.error('Inspect failed:', msg)
      setInspectError(msg)
    } finally {
      setInspectLoading(false)
    }
  }, [])

  function handleInspectSelected() {
    if (!selectedAsset) return
    inspectAsset(selectedAsset.filePath)
  }

  function copyPropertyValue(value: string, propName: string) {
    navigator.clipboard.writeText(value).then(() => {
      setCopiedProp(propName)
      setTimeout(() => setCopiedProp(null), 1500)
    })
  }

  // Loading state — only show spinner if no cached data
  if (loading && !data) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="w-5 h-5 border-2 border-fn-epic/30 border-t-fn-epic rounded-full animate-spin mx-auto mb-2" />
          <div className="text-[11px] text-gray-400">Scanning user assets...</div>
        </div>
      </div>
    )
  }

  // Error state
  if (error) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="text-[11px] text-red-400 mb-3">{error}</div>
          <button
            onClick={() => { useForgeStore.getState().userAssetsFetchedAt = null; fetchUserAssets() }}
            className="px-3 py-1.5 text-[10px] font-medium text-white bg-fn-panel border border-fn-border rounded hover:bg-white/[0.06] transition-colors"
          >
            Retry
          </button>
        </div>
      </div>
    )
  }

  // No data / no project
  if (!data || normalizedAssets.length === 0) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="bg-fn-panel border border-fn-border rounded-lg p-6 text-center max-w-sm">
          <svg
            className="w-8 h-8 mx-auto mb-2 text-gray-600"
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            strokeWidth={1.5}
          >
            <path d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4" />
          </svg>
          <p className="text-[11px] text-gray-400 mb-1">No user assets found</p>
          <p className="text-[10px] text-gray-600">
            User assets live in Plugins/&lt;Name&gt;/Content/.
            <br />
            Select a project with custom blueprints, materials, or props.
          </p>
        </div>
      </div>
    )
  }

  return (
    <div className="flex-1 flex bg-fn-darker overflow-hidden min-h-0">
      {/* ======= Left Panel — Asset List ======= */}
      <div className="w-[300px] flex flex-col border-r border-fn-border bg-fn-dark shrink-0">
        {/* Search */}
        <div className="px-3 py-2 border-b border-fn-border">
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search assets..."
            className="w-full bg-fn-darker border border-fn-border rounded px-2.5 py-1.5 text-[11px] text-white placeholder-gray-600 focus:outline-none focus:border-fn-rare/50"
          />
        </div>

        {/* Sort buttons */}
        <div className="flex gap-1 px-3 py-2 border-b border-fn-border">
          {(['name', 'class', 'size'] as SortField[]).map((field) => {
            const isActive = sortField === field
            const label = field === 'name' ? 'A-Z' : field === 'class' ? 'Class' : 'Size'
            return (
              <button
                key={field}
                onClick={() => handleSort(field)}
                className={`flex-1 flex items-center justify-center gap-0.5 px-1 py-1 text-[9px] font-medium rounded transition-colors ${
                  isActive
                    ? 'text-blue-400 bg-blue-400/10 border border-blue-400/30'
                    : 'text-gray-500 hover:text-gray-300 bg-fn-darker border border-fn-border/50 hover:bg-white/[0.03]'
                }`}
              >
                {label}
                {isActive && (
                  <svg
                    className={`w-2.5 h-2.5 transition-transform ${sortReversed ? 'rotate-180' : ''}`}
                    fill="none"
                    viewBox="0 0 24 24"
                    stroke="currentColor"
                    strokeWidth={2.5}
                  >
                    <path d="M19 14l-7-7-7 7" />
                  </svg>
                )}
              </button>
            )
          })}
        </div>

        {/* Asset list — grouped by class */}
        <div className="flex-1 overflow-y-auto min-h-0">
          {groupedAndSorted.length === 0 ? (
            <div className="p-4 text-center text-[11px] text-gray-500">
              {search ? 'No matching assets' : 'No assets found'}
            </div>
          ) : (
            groupedAndSorted.map(([cls, assets]) => {
              const isCollapsed = collapsedGroups.has(cls)
              return (
                <div key={cls}>
                  {/* Group header */}
                  <button
                    onClick={() => toggleGroup(cls)}
                    className="w-full flex items-center gap-2 px-3 py-1.5 bg-fn-panel border-b border-fn-border/30 hover:bg-white/[0.03] transition-colors"
                  >
                    <svg
                      className={`w-2.5 h-2.5 text-gray-500 transition-transform ${isCollapsed ? '' : 'rotate-90'}`}
                      fill="none"
                      viewBox="0 0 24 24"
                      stroke="currentColor"
                      strokeWidth={2}
                    >
                      <path d="M9 5l7 7-7 7" />
                    </svg>
                    <span className="text-[10px] font-medium text-gray-400">{cls}</span>
                    <span className="text-[9px] text-gray-600 ml-auto">{assets.length}</span>
                  </button>

                  {/* Group items */}
                  {!isCollapsed &&
                    assets.map((asset) => {
                      const isSelected = selectedAsset?.filePath === asset.filePath
                      return (
                        <button
                          key={asset.filePath}
                          onClick={() => handleSelectAsset(asset)}
                          className={`w-full text-left px-3 py-2 border-b border-fn-border/20 hover:bg-white/[0.03] transition-colors ${
                            isSelected
                              ? 'bg-fn-rare/10 border-l-2 border-l-fn-rare'
                              : ''
                          }`}
                        >
                          <div className="flex items-center justify-between gap-2">
                            <span className="text-[11px] text-white truncate font-medium">
                              {prettifyAssetName(asset.name)}
                            </span>
                            <div className="flex items-center gap-1.5 shrink-0">
                              <span className="text-[9px] text-gray-600 tabular-nums">
                                {formatFileSize(asset.size)}
                              </span>
                              <span className="inline-block px-1 py-0.5 rounded text-[8px] font-medium text-fn-rare/80 bg-fn-rare/10 border border-fn-rare/20">
                                .uasset
                              </span>
                            </div>
                          </div>
                          <div className="text-[9px] text-gray-600 mt-0.5 truncate">
                            {asset.name}
                          </div>
                        </button>
                      )
                    })}
                </div>
              )
            })
          )}
        </div>

        {/* Bottom stats */}
        <div className="px-3 py-2 border-t border-fn-border text-[10px] text-gray-600 flex justify-between">
          <span>
            {totalFiltered} asset{totalFiltered !== 1 ? 's' : ''}
          </span>
          <span>
            {totalCount.toLocaleString()} total
          </span>
        </div>
      </div>

      {/* ======= Right Panel — Asset Detail + Inspector ======= */}
      <div className="flex-1 overflow-y-auto min-h-0">
        {inspectLoading ? (
          <div className="flex items-center justify-center h-full">
            <div className="text-center">
              <div className="w-5 h-5 border-2 border-fn-rare/30 border-t-fn-rare rounded-full animate-spin mx-auto mb-2" />
              <div className="text-[11px] text-gray-400">Loading asset details...</div>
            </div>
          </div>
        ) : selectedAsset ? (
          <div className="p-6 space-y-5">
            {/* === Asset Header === */}
            <div>
              <h2 className="text-lg font-semibold text-white">
                {prettifyAssetName(selectedAsset.name)}
              </h2>
              <div className="text-[10px] text-gray-600 font-mono mt-0.5">
                {selectedAsset.name}
              </div>
              <div className="flex items-center gap-3 mt-1.5">
                <span className="inline-block px-1.5 py-0.5 rounded text-[9px] font-medium border text-fn-rare bg-fn-rare/10 border-fn-rare/20">
                  {selectedAsset.assetClass}
                </span>
                <span className="inline-block px-1.5 py-0.5 rounded text-[9px] font-medium border text-fn-rare/60 bg-fn-rare/5 border-fn-rare/15">
                  .uasset
                </span>
                <span className="text-[10px] text-gray-500">
                  {formatFileSize(selectedAsset.size)}
                </span>
                <span className="text-[9px] text-gray-600 font-mono truncate">
                  {selectedAsset.relativePath}
                </span>
              </div>

              {/* Inspect button */}
              <button
                onClick={handleInspectSelected}
                disabled={inspectLoading}
                className="mt-3 px-3 py-1.5 text-[10px] font-medium text-white bg-fn-panel border border-fn-border rounded hover:bg-white/[0.06] transition-colors disabled:opacity-50"
              >
                Inspect Asset
              </button>
              {inspectError && (
                <div className="mt-2">
                  <ErrorMessage message={inspectError} />
                </div>
              )}
            </div>

            {/* === Inspect Results === */}
            {inspectedAsset && (
              <>
                {/* Exports list */}
                {(inspectedAsset.exports?.length ?? 0) > 0 && (
                  <div>
                    <h3 className="text-[11px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
                      Exports ({inspectedAsset.exports.length})
                    </h3>
                    <div className="bg-fn-dark border border-fn-border rounded-lg overflow-hidden">
                      {inspectedAsset.exports.map((exp, i) => (
                        <div
                          key={i}
                          className="flex items-center gap-3 px-4 py-2 border-b border-fn-border/20 last:border-b-0"
                        >
                          <span className="text-[11px] text-fn-epic font-medium">{exp.exportType}</span>
                          <span className="text-[11px] text-gray-500">{exp.objectName}</span>
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {/* Properties table */}
                {(inspectedAsset.properties?.length ?? 0) > 0 && (
                  <PropertyInspector
                    properties={inspectedAsset.properties}
                    copiedProp={copiedProp}
                    onCopyValue={copyPropertyValue}
                  />
                )}

                {(inspectedAsset.exports?.length ?? 0) === 0 && (inspectedAsset.properties?.length ?? 0) === 0 && (
                  <div className="bg-fn-panel border border-fn-border rounded-lg p-6 text-center">
                    <p className="text-[11px] text-gray-400">No details available for this asset</p>
                  </div>
                )}
              </>
            )}
          </div>
        ) : (
          /* No asset selected */
          <div className="flex items-center justify-center h-full">
            <div className="text-center">
              <svg
                className="w-8 h-8 mx-auto mb-2 text-gray-600"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={1.5}
              >
                <path d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4" />
              </svg>
              <p className="text-[11px] text-gray-500">Select an asset to inspect</p>
              <p className="text-[10px] text-gray-600 mt-1">
                {groupedAndSorted.length} class{groupedAndSorted.length !== 1 ? 'es' : ''}, {totalCount.toLocaleString()} assets
              </p>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

// ============================================================
// Sub-components
// ============================================================

function PropertyInspector({
  properties,
  copiedProp,
  onCopyValue,
}: {
  properties: AssetProperty[]
  copiedProp: string | null
  onCopyValue: (value: string, name: string) => void
}) {
  return (
    <div>
      <h3 className="text-[11px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
        Properties ({properties.length})
      </h3>
      <div className="bg-fn-dark border border-fn-border rounded-lg overflow-hidden">
        {/* Table header */}
        <div className="grid grid-cols-[1fr_1fr_auto_auto] gap-3 px-4 py-2 border-b border-fn-border bg-fn-panel">
          <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">
            Name
          </span>
          <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">
            Value
          </span>
          <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">
            Type
          </span>
          <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider w-8"></span>
        </div>
        {/* Rows */}
        {properties.map((prop, i) => (
          <div
            key={`${prop.name}-${i}`}
            onClick={() => onCopyValue(prop.value, prop.name)}
            className="grid grid-cols-[1fr_1fr_auto_auto] gap-3 px-4 py-2 border-b border-fn-border/30 hover:bg-white/[0.02] cursor-pointer transition-colors"
            title="Click to copy value"
          >
            <span className="text-[11px] text-gray-300 truncate">{prop.name}</span>
            <span className="text-[11px] text-white truncate font-mono">
              {copiedProp === prop.name ? (
                <span className="text-green-400">Copied!</span>
              ) : (
                prop.value
              )}
            </span>
            <span className="text-[9px] text-gray-600 self-center">{prop.type}</span>
            <span className="w-8 flex items-center justify-center">
              <svg
                className="w-3 h-3 text-gray-600"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={1.5}
              >
                <path d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z" />
              </svg>
            </span>
          </div>
        ))}
      </div>
    </div>
  )
}
