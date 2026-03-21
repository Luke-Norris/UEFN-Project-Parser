import { useEffect, useState, useMemo, useCallback } from 'react'
import { useForgeStore } from '../stores/forgeStore'
import type {
  EpicAssetTypeEntry,
  EpicAssetListResult,
  DeviceInspectResult,
  AssetProperty
} from '../../shared/types'

type SortField = 'name' | 'count' | 'kind'

function cleanDisplayName(raw: string): string {
  // Remove _C suffix, replace underscores with spaces
  let name = raw.replace(/_C$/, '').replace(/_/g, ' ')
  // Insert spaces before capital letters in PascalCase
  name = name.replace(/([a-z])([A-Z])/g, '$1 $2')
  return name.trim()
}

function getKindLabel(entry: EpicAssetTypeEntry): string {
  if (entry.isDevice) return 'Device'
  const cls = (entry.className ?? entry.typeName ?? '').toLowerCase()
  if (cls.includes('actor') || cls.includes('spawner') || cls.includes('volume') || cls.includes('trigger'))
    return 'Actor'
  return 'Prop'
}

export function EpicAssetsPage() {
  const data = useForgeStore((s) => s.epicAssets)
  const loading = useForgeStore((s) => s.epicAssetsLoading)
  const error = useForgeStore((s) => s.epicAssetsError)
  const fetchEpicAssets = useForgeStore((s) => s.fetchEpicAssets)

  const [search, setSearch] = useState('')

  // Sorting
  const [sortField, setSortField] = useState<SortField>('count')
  const [sortReversed, setSortReversed] = useState(false)

  // Selection & inspection
  const [selectedType, setSelectedType] = useState<EpicAssetTypeEntry | null>(null)
  const [inspectedDevice, setInspectedDevice] = useState<DeviceInspectResult | null>(null)
  const [inspectLoading, setInspectLoading] = useState(false)
  const [copiedProp, setCopiedProp] = useState<string | null>(null)

  useEffect(() => {
    fetchEpicAssets()
  }, [fetchEpicAssets])

  function handleSort(field: SortField) {
    if (sortField === field) {
      setSortReversed(!sortReversed)
    } else {
      setSortField(field)
      setSortReversed(false)
    }
  }

  const filteredAndSorted = useMemo(() => {
    let types = data?.types ?? []

    // Filter by search
    if (search.trim()) {
      const q = search.toLowerCase()
      types = types.filter(
        (t) =>
          (t.typeName || '').toLowerCase().includes(q) ||
          (t.className || '').toLowerCase().includes(q)
      )
    }

    // Sort
    const sorted = [...types].sort((a, b) => {
      switch (sortField) {
        case 'name':
          return (a.typeName || '').localeCompare(b.typeName || '')
        case 'count':
          return b.count - a.count
        case 'kind': {
          // Devices first, then by count within kind
          if (a.isDevice !== b.isDevice) return a.isDevice ? -1 : 1
          return b.count - a.count
        }
        default:
          return 0
      }
    })

    return sortReversed ? sorted.reverse() : sorted
  }, [data, search, sortField, sortReversed])

  const [expandedType, setExpandedType] = useState<string | null>(null)

  function handleSelectType(type: EpicAssetTypeEntry) {
    // Toggle expand in the left panel
    const key = type.className ?? type.typeName
    setExpandedType(expandedType === key ? null : key)
    setSelectedType(type)
    setInspectedDevice(null)
  }

  const inspectInstance = useCallback(async (path: string) => {
    try {
      setInspectLoading(true)
      const result = await window.electronAPI.forgeInspectDevice(path)
      setInspectedDevice(result)
    } catch (err) {
      console.error('Inspect failed:', err)
    } finally {
      setInspectLoading(false)
    }
  }, [])

  async function handleInspectSample() {
    if (!selectedType?.samplePaths?.length) return
    await inspectInstance(selectedType.samplePaths[0])
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
          <div className="text-[11px] text-gray-400">Scanning placed instances...</div>
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
            onClick={() => { useForgeStore.getState().epicAssetsFetchedAt = null; fetchEpicAssets() }}
            className="px-3 py-1.5 text-[10px] font-medium text-white bg-fn-panel border border-fn-border rounded hover:bg-white/[0.06] transition-colors"
          >
            Retry
          </button>
        </div>
      </div>
    )
  }

  // No data / no project
  if (!data || data.types.length === 0) {
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
          <p className="text-[11px] text-gray-400 mb-1">No placed instances found</p>
          <p className="text-[10px] text-gray-600">
            External actors are in Content/__ExternalActors__/.
            <br />
            Select a project with placed actors first.
          </p>
        </div>
      </div>
    )
  }

  return (
    <div className="flex-1 flex bg-fn-darker overflow-hidden">
      {/* ======= Left Panel — Type List ======= */}
      <div className="w-[300px] flex flex-col border-r border-fn-border bg-fn-dark shrink-0">
        {/* Search */}
        <div className="px-3 py-2 border-b border-fn-border">
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search types..."
            className="w-full bg-fn-darker border border-fn-border rounded px-2.5 py-1.5 text-[11px] text-white placeholder-gray-600 focus:outline-none focus:border-fn-rare/50"
          />
        </div>

        {/* Sort buttons */}
        <div className="flex gap-1 px-3 py-2 border-b border-fn-border">
          {(['name', 'count', 'kind'] as SortField[]).map((field) => {
            const isActive = sortField === field
            const label = field === 'name' ? 'A-Z' : field === 'count' ? 'Count' : 'Kind'
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

        {/* Type list */}
        <div className="flex-1 overflow-y-auto">
          {filteredAndSorted.length === 0 ? (
            <div className="p-4 text-center text-[11px] text-gray-500">
              {search ? 'No matching types' : 'No types found'}
            </div>
          ) : (
            filteredAndSorted.map((type) => {
              const typeKey = type.className ?? type.typeName
              const isSelected = selectedType?.typeName === type.typeName
              const isExpanded = expandedType === typeKey
              const kind = getKindLabel(type)
              const friendlyName = cleanDisplayName(type.displayName || type.typeName)
              return (
                <div key={typeKey}>
                  <button
                    onClick={() => handleSelectType(type)}
                    className={`w-full text-left px-3 py-2 border-b border-fn-border/20 hover:bg-white/[0.03] transition-colors ${
                      isSelected
                        ? 'bg-fn-rare/10 border-l-2 border-l-fn-rare'
                        : ''
                    }`}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <div className="flex items-center gap-1.5 min-w-0">
                        <svg className={`w-2.5 h-2.5 shrink-0 text-gray-600 transition-transform ${isExpanded ? 'rotate-90' : ''}`} fill="currentColor" viewBox="0 0 20 20">
                          <path d="M6 4l8 6-8 6V4z" />
                        </svg>
                        <span className={`text-[11px] font-medium truncate ${type.isDevice ? 'text-fn-epic' : 'text-white'}`}>
                          {friendlyName}
                        </span>
                      </div>
                      <div className="flex items-center gap-1.5 shrink-0">
                        <span className="text-[10px] text-gray-500 tabular-nums">
                          {type.count}
                        </span>
                        <span className={`inline-block px-1 py-0.5 rounded text-[8px] font-medium ${
                          kind === 'Device'
                            ? 'text-fn-epic bg-fn-epic/10 border border-fn-epic/20'
                            : kind === 'Actor'
                              ? 'text-amber-400 bg-amber-400/10 border border-amber-400/20'
                              : 'text-gray-500 bg-gray-500/10 border border-gray-500/20'
                        }`}>
                          {kind}
                        </span>
                      </div>
                    </div>
                    {type.className && (
                      <div className="text-[9px] text-gray-600 mt-0.5 truncate font-mono pl-4">
                        {type.className}
                      </div>
                    )}
                  </button>
                  {/* Expanded: show sample instances inline */}
                  {isExpanded && type.samplePaths && type.samplePaths.length > 0 && (
                    <div className="bg-fn-darker/50 border-b border-fn-border/20">
                      {type.samplePaths.map((path, idx) => {
                        // Show as "Instance 1", "Instance 2" etc. since filenames are hashes
                        // The actual actor label comes from inspecting the file
                        const friendlyLabel = `${cleanDisplayName(type.displayName || type.typeName)} #${idx + 1}`
                        return (
                          <button
                            key={path}
                            onClick={(e) => { e.stopPropagation(); inspectInstance(path) }}
                            className="w-full text-left pl-7 pr-3 py-1.5 text-[10px] hover:bg-white/[0.03] transition-colors border-b border-fn-border/10 flex items-center gap-2"
                          >
                            <svg className="w-3 h-3 text-gray-600 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                              <path d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                            </svg>
                            <span className="text-gray-300 truncate">{friendlyLabel}</span>
                            <span className="text-[8px] text-gray-600 ml-auto shrink-0">Inspect</span>
                          </button>
                        )
                      })}
                      {type.count > type.samplePaths.length && (
                        <div className="pl-7 pr-3 py-1 text-[9px] text-gray-600 italic">
                          +{type.count - type.samplePaths.length} more instances
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )
            })
          )}
        </div>

        {/* Bottom stats */}
        <div className="px-3 py-2 border-t border-fn-border text-[10px] text-gray-600 flex justify-between">
          <span>
            {filteredAndSorted.length} type{filteredAndSorted.length !== 1 ? 's' : ''}
          </span>
          <span>
            {data.totalPlaced.toLocaleString()} total
          </span>
        </div>
      </div>

      {/* ======= Right Panel — Type Detail + Inspector ======= */}
      <div className="flex-1 overflow-y-auto">
        {inspectLoading ? (
          <div className="flex items-center justify-center h-full">
            <div className="text-center">
              <div className="w-5 h-5 border-2 border-fn-rare/30 border-t-fn-rare rounded-full animate-spin mx-auto mb-2" />
              <div className="text-[11px] text-gray-400">Loading properties...</div>
            </div>
          </div>
        ) : selectedType ? (
          <div className="p-5 space-y-4">
            {/* === Type Header === */}
            <div className="flex items-center justify-between">
              <div>
                <h2 className="text-base font-semibold text-white">
                  {cleanDisplayName(selectedType.typeName)}
                </h2>
                <div className="flex items-center gap-3 mt-0.5">
                  <KindBadge kind={getKindLabel(selectedType)} />
                  <span className="text-[10px] text-gray-500">
                    {selectedType.count} instance{selectedType.count !== 1 ? 's' : ''}
                  </span>
                  {selectedType.className && (
                    <span className="text-[9px] text-gray-600 font-mono">
                      {selectedType.className}
                    </span>
                  )}
                </div>
              </div>
              {!inspectedDevice && selectedType.samplePaths && selectedType.samplePaths.length > 0 && (
                <button
                  onClick={handleInspectSample}
                  disabled={inspectLoading}
                  className="px-3 py-1.5 text-[10px] font-medium text-white bg-fn-panel border border-fn-border rounded hover:bg-white/[0.06] transition-colors disabled:opacity-50 shrink-0"
                >
                  Inspect Sample
                </button>
              )}
            </div>

            {/* === Property Inspector === */}
            {inspectedDevice ? (
              <PropertyInspector
                device={inspectedDevice}
                copiedProp={copiedProp}
                onCopyValue={copyPropertyValue}
              />
            ) : (
              <div className="bg-fn-panel border border-fn-border rounded-lg p-4 text-center">
                <p className="text-[11px] text-gray-400">
                  Select an instance from the left panel to inspect its properties
                </p>
              </div>
            )}
          </div>
        ) : (
          /* No type selected */
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
              <p className="text-[11px] text-gray-500">Select a type to inspect</p>
              <p className="text-[10px] text-gray-600 mt-1">
                {data.uniqueTypes} types, {data.totalPlaced.toLocaleString()} placed instances
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

function KindBadge({ kind }: { kind: string }) {
  const styles =
    kind === 'Device'
      ? 'text-fn-epic bg-fn-epic/10 border-fn-epic/20'
      : kind === 'Actor'
        ? 'text-amber-400 bg-amber-400/10 border-amber-400/20'
        : 'text-gray-500 bg-gray-500/10 border-gray-500/20'

  return (
    <span
      className={`inline-block px-1.5 py-0.5 rounded text-[9px] font-medium border ${styles}`}
    >
      {kind}
    </span>
  )
}

function PropertyInspector({
  device,
  copiedProp,
  onCopyValue
}: {
  device: DeviceInspectResult
  copiedProp: string | null
  onCopyValue: (value: string, name: string) => void
}) {
  // Group properties by type category for multi-column layout
  const categorized = useMemo(() => {
    const cats: Record<string, AssetProperty[]> = {
      'Configuration': [],
      'Transform': [],
      'References': [],
      'Rendering': [],
      'Other': [],
    }
    for (const prop of device.properties) {
      const name = (prop.name || '').toLowerCase()
      const type = (prop.type || '').toLowerCase()
      if (name.includes('location') || name.includes('rotation') || name.includes('scale') || name.includes('transform'))
        cats['Transform'].push(prop)
      else if (type.includes('object') || type.includes('softobject') || name.includes('parent') || name.includes('attach'))
        cats['References'].push(prop)
      else if (name.includes('material') || name.includes('draw') || name.includes('render') || name.includes('shadow') || name.includes('lod') || name.includes('collision') || name.includes('physics') || name.includes('cached'))
        cats['Rendering'].push(prop)
      else if (prop.isEditable || type.includes('bool') || type.includes('int') || type.includes('float') || type.includes('str') || type.includes('enum') || type.includes('byte') || type.includes('name') || type.includes('text'))
        cats['Configuration'].push(prop)
      else
        cats['Other'].push(prop)
    }
    // Remove empty categories
    return Object.entries(cats).filter(([, props]) => props.length > 0)
  }, [device.properties])

  return (
    <div>
      {/* Header */}
      <div className="flex items-center gap-3 mb-3">
        <h3 className="text-[11px] font-semibold text-gray-500 uppercase tracking-wider">
          {device.displayName || device.className}
        </h3>
        <span className="text-[10px] text-gray-600">{device.properties.length} properties</span>
      </div>

      {/* Position & Rotation — compact inline */}
      {device.position && (device.position.x !== 0 || device.position.y !== 0 || device.position.z !== 0) && (
        <div className="flex gap-2 mb-3">
          <div className="bg-fn-panel border border-fn-border rounded px-2.5 py-1.5">
            <span className="text-[8px] text-gray-600 uppercase">Pos</span>
            <span className="text-[10px] text-white font-mono ml-1.5">
              {device.position.x.toFixed(0)}, {device.position.y.toFixed(0)}, {device.position.z.toFixed(0)}
            </span>
          </div>
          {device.rotationYaw != null && device.rotationYaw !== 0 && (
            <div className="bg-fn-panel border border-fn-border rounded px-2.5 py-1.5">
              <span className="text-[8px] text-gray-600 uppercase">Rot</span>
              <span className="text-[10px] text-white font-mono ml-1.5">{device.rotationYaw.toFixed(1)}</span>
            </div>
          )}
        </div>
      )}

      {/* Multi-column property cards */}
      <div className="grid grid-cols-2 gap-3">
        {categorized.map(([category, props]) => (
          <div key={category} className="bg-fn-dark border border-fn-border rounded-lg overflow-hidden">
            <div className="px-3 py-1.5 border-b border-fn-border bg-fn-panel flex items-center justify-between">
              <span className="text-[9px] font-semibold text-gray-500 uppercase tracking-wider">{category}</span>
              <span className="text-[8px] text-gray-600">{props.length}</span>
            </div>
            <div className="max-h-[300px] overflow-y-auto">
              {props.map((prop, i) => (
                <div
                  key={`${prop.name}-${i}`}
                  onClick={() => onCopyValue(prop.value, prop.name)}
                  className={`flex items-center gap-2 px-3 py-1.5 border-b border-fn-border/20 hover:bg-white/[0.03] cursor-pointer transition-colors ${
                    prop.isEditable ? 'border-l-2 border-l-fn-rare/30' : ''
                  }`}
                  title={`${prop.type} — click to copy`}
                >
                  <span className="text-[10px] text-gray-400 truncate w-[45%] shrink-0">{prop.name}</span>
                  <span className="text-[10px] text-white truncate font-mono flex-1">
                    {copiedProp === prop.name ? (
                      <span className="text-green-400 text-[9px]">Copied!</span>
                    ) : (
                      prop.value
                    )}
                  </span>
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
