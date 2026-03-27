import { useState, useMemo, useCallback, useEffect } from 'react'
import {
  forgeEncyclopediaSearch,
  forgeEncyclopediaDeviceReference,
  forgeEncyclopediaListDevices,
} from '../lib/api'
import type {
  EncyclopediaSearchResult,
  DeviceReferenceResponse,
  PropertyReference,
  CommonConfiguration,
  DeviceSummary,
} from '../../shared/types'

// ─── Device Card ─────────────────────────────────────────────────────────────

function DeviceCard({
  device,
  isActive,
  onSelect,
}: {
  device: EncyclopediaSearchResult | DeviceSummary
  isActive: boolean
  onSelect: () => void
}) {
  const name = device.displayName
  const deviceName = 'deviceName' in device ? device.deviceName : (device as DeviceSummary).name

  return (
    <div
      onClick={onSelect}
      className={`p-3 rounded-lg border cursor-pointer transition-all ${
        isActive
          ? 'border-blue-500/50 bg-blue-500/10'
          : 'border-fn-border bg-fn-dark hover:bg-white/[0.03] hover:border-fn-border/80'
      }`}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="text-[12px] font-semibold text-white truncate">{name}</div>
          <div className="text-[9px] text-gray-600 font-mono mt-0.5 truncate">{deviceName}</div>
        </div>
        {device.hasCommonConfigs && (
          <span className="shrink-0 text-[8px] font-bold px-1.5 py-0.5 rounded border text-amber-400 bg-amber-400/10 border-amber-400/20">
            RECIPES
          </span>
        )}
      </div>
      <div className="flex items-center gap-2 mt-2 flex-wrap">
        {device.propertyCount > 0 && (
          <span className="text-[9px] px-1.5 py-0.5 rounded bg-fn-darker border border-fn-border text-gray-400">
            {device.propertyCount} props
          </span>
        )}
        {device.eventCount > 0 && (
          <span className="text-[9px] px-1.5 py-0.5 rounded bg-fn-darker border border-fn-border text-yellow-400/70">
            {device.eventCount} events
          </span>
        )}
        {device.functionCount > 0 && (
          <span className="text-[9px] px-1.5 py-0.5 rounded bg-fn-darker border border-fn-border text-purple-400/70">
            {device.functionCount} funcs
          </span>
        )}
        {device.usageCount > 0 && (
          <span className="text-[9px] text-gray-600">
            {device.usageCount} uses
          </span>
        )}
      </div>
      {'matchedProperties' in device && device.matchedProperties && device.matchedProperties.length > 0 && (
        <div className="flex flex-wrap gap-1 mt-1.5">
          {device.matchedProperties.slice(0, 3).map((prop) => (
            <span key={prop} className="text-[8px] px-1 py-0.5 rounded bg-blue-400/10 text-blue-400 border border-blue-400/20">
              {prop}
            </span>
          ))}
        </div>
      )}
    </div>
  )
}

// ─── Property Row ────────────────────────────────────────────────────────────

function PropertyRow({
  prop,
  isExpanded,
  onToggle,
}: {
  prop: PropertyReference
  isExpanded: boolean
  onToggle: () => void
}) {
  return (
    <div className="border-b border-fn-border/50 last:border-0">
      <div
        className="flex items-center gap-2 px-3 py-2 cursor-pointer hover:bg-white/[0.02] transition-colors"
        onClick={onToggle}
      >
        <svg
          className={`w-3 h-3 text-gray-600 shrink-0 transition-transform ${isExpanded ? 'rotate-90' : ''}`}
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
          strokeWidth={2}
        >
          <path d="M9 5l7 7-7 7" />
        </svg>
        <span className="text-[11px] font-medium text-white min-w-0 truncate flex-1">
          {prop.name}
        </span>
        <span className="text-[9px] text-purple-400 font-mono shrink-0">{prop.type}</span>
        {prop.defaultValue && (
          <span className="text-[9px] text-gray-600 font-mono shrink-0 max-w-[100px] truncate">
            = {prop.defaultValue}
          </span>
        )}
        {!prop.isEditable && (
          <span className="text-[7px] px-1 py-0.5 rounded bg-red-400/10 text-red-400 border border-red-400/20 shrink-0">
            READ-ONLY
          </span>
        )}
      </div>
      {isExpanded && (
        <div className="px-3 pb-2 pl-8 space-y-1.5">
          <div className="text-[10px] text-gray-400">{prop.description}</div>
          {prop.usagePercent != null && prop.usagePercent > 0 && (
            <div className="flex items-center gap-2">
              <span className="text-[9px] text-gray-600">Usage:</span>
              <div className="flex-1 h-1.5 bg-fn-darker rounded-full overflow-hidden max-w-[120px]">
                <div
                  className="h-full bg-blue-500 rounded-full"
                  style={{ width: `${Math.min(100, prop.usagePercent)}%` }}
                />
              </div>
              <span className="text-[9px] text-gray-500">{prop.usagePercent.toFixed(0)}%</span>
            </div>
          )}
          {prop.commonValues && prop.commonValues.length > 0 && (
            <div>
              <span className="text-[9px] text-gray-600 block mb-0.5">Common values:</span>
              <div className="flex flex-wrap gap-1">
                {prop.commonValues.map((v) => (
                  <span
                    key={v.value}
                    className="text-[9px] px-1.5 py-0.5 rounded bg-fn-darker text-gray-300 border border-fn-border"
                  >
                    {v.value} <span className="text-gray-600">({v.count}x)</span>
                  </span>
                ))}
              </div>
            </div>
          )}
          {prop.relatedProperties && prop.relatedProperties.length > 0 && (
            <div>
              <span className="text-[9px] text-gray-600 block mb-0.5">Related properties:</span>
              <div className="flex flex-wrap gap-1">
                {prop.relatedProperties.map((r) => (
                  <span
                    key={r}
                    className="text-[9px] px-1.5 py-0.5 rounded bg-green-400/10 text-green-400 border border-green-400/20"
                  >
                    {r}
                  </span>
                ))}
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  )
}

// ─── Configuration Card ──────────────────────────────────────────────────────

function ConfigCard({ config }: { config: CommonConfiguration }) {
  const [copied, setCopied] = useState(false)

  function handleCopy() {
    const text = Object.entries(config.properties)
      .map(([k, v]) => `${k} = ${v}`)
      .join('\n')
    navigator.clipboard.writeText(text).then(() => {
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    })
  }

  return (
    <div className="bg-fn-darker rounded-lg border border-fn-border p-3">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="text-[11px] font-semibold text-white">{config.name}</div>
          <div className="text-[10px] text-gray-500 mt-0.5">{config.description}</div>
        </div>
        <button
          onClick={handleCopy}
          className="shrink-0 px-2 py-1 text-[9px] font-medium rounded transition-colors border border-fn-border text-gray-400 hover:text-white hover:bg-white/[0.05]"
          title="Copy configuration"
        >
          {copied ? 'Copied' : 'Copy'}
        </button>
      </div>
      <div className="mt-2 space-y-0.5">
        {Object.entries(config.properties).map(([key, value]) => (
          <div key={key} className="flex items-center gap-2 text-[10px] bg-fn-dark rounded px-2 py-1">
            <span className="text-gray-500 font-mono">{key}</span>
            <span className="text-gray-300 ml-auto font-mono">{value}</span>
          </div>
        ))}
      </div>
      {config.tags.length > 0 && (
        <div className="flex flex-wrap gap-1 mt-2">
          {config.tags.map((tag) => (
            <span key={tag} className="text-[8px] px-1 py-0.5 rounded bg-fn-dark text-gray-600">
              {tag}
            </span>
          ))}
        </div>
      )}
    </div>
  )
}

// ─── Device Detail Panel ─────────────────────────────────────────────────────

function DeviceDetail({ reference }: { reference: DeviceReferenceResponse }) {
  const [expandedProps, setExpandedProps] = useState<Set<string>>(new Set())
  const [showAllProps, setShowAllProps] = useState(false)

  const toggleProp = useCallback(
    (name: string) => {
      setExpandedProps((prev) => {
        const next = new Set(prev)
        if (next.has(name)) next.delete(name)
        else next.add(name)
        return next
      })
    },
    []
  )

  const displayedProps = showAllProps
    ? reference.properties
    : reference.properties.slice(0, 20)

  return (
    <div className="p-4 space-y-5">
      {/* Header */}
      <div>
        <h2 className="text-[14px] font-semibold text-white">{reference.displayName}</h2>
        <div className="text-[10px] text-gray-600 font-mono mt-0.5">{reference.deviceName}</div>
        <p className="text-[11px] text-gray-400 mt-1">{reference.description}</p>
        <div className="flex items-center gap-3 mt-2 text-[9px] text-gray-600">
          {reference.parentClass && (
            <span>extends <span className="text-gray-400">{reference.parentClass}</span></span>
          )}
          {reference.sourceFile && (
            <span>from <span className="text-gray-400">{reference.sourceFile}</span></span>
          )}
        </div>
        {(reference.totalUsageCount > 0 || reference.projectsUsedIn > 0) && (
          <div className="flex items-center gap-3 mt-1 text-[9px]">
            {reference.totalUsageCount > 0 && (
              <span className="text-blue-400">
                {reference.totalUsageCount} instances in library
              </span>
            )}
            {reference.projectsUsedIn > 0 && (
              <span className="text-gray-600">
                across {reference.projectsUsedIn} projects
              </span>
            )}
          </div>
        )}
      </div>

      {/* Properties */}
      <div>
        <h3 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
          Properties ({reference.properties.length})
        </h3>
        <div className="bg-fn-dark rounded-lg border border-fn-border overflow-hidden">
          {displayedProps.map((prop) => (
            <PropertyRow
              key={prop.name}
              prop={prop}
              isExpanded={expandedProps.has(prop.name)}
              onToggle={() => toggleProp(prop.name)}
            />
          ))}
        </div>
        {reference.properties.length > 20 && !showAllProps && (
          <button
            onClick={() => setShowAllProps(true)}
            className="w-full mt-1 py-1 text-[9px] text-blue-400 hover:text-blue-300 transition-colors"
          >
            Show all {reference.properties.length} properties
          </button>
        )}
      </div>

      {/* Events */}
      {reference.events.length > 0 && (
        <div>
          <h3 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
            Events ({reference.events.length})
          </h3>
          <div className="flex flex-wrap gap-1">
            {reference.events.map((evt) => (
              <span
                key={evt}
                className="text-[10px] px-2 py-1 rounded bg-yellow-400/10 text-yellow-400 border border-yellow-400/20 font-mono"
              >
                {evt}
              </span>
            ))}
          </div>
        </div>
      )}

      {/* Functions */}
      {reference.functions.length > 0 && (
        <div>
          <h3 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
            Functions ({reference.functions.length})
          </h3>
          <div className="flex flex-wrap gap-1">
            {reference.functions.map((fn) => (
              <span
                key={fn}
                className="text-[10px] px-2 py-1 rounded bg-purple-400/10 text-purple-400 border border-purple-400/20 font-mono"
              >
                {fn}
              </span>
            ))}
          </div>
        </div>
      )}

      {/* Common Configurations */}
      {reference.commonConfigurations && reference.commonConfigurations.length > 0 && (
        <div>
          <h3 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
            Common Configurations ({reference.commonConfigurations.length})
          </h3>
          <div className="space-y-2">
            {reference.commonConfigurations.map((config) => (
              <ConfigCard key={config.name} config={config} />
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

// ─── Main Page ───────────────────────────────────────────────────────────────

export function DeviceEncyclopediaPage() {
  const [search, setSearch] = useState('')
  const [allDevices, setAllDevices] = useState<DeviceSummary[]>([])
  const [searchResults, setSearchResults] = useState<EncyclopediaSearchResult[] | null>(null)
  const [selectedDevice, setSelectedDevice] = useState<string | null>(null)
  const [reference, setReference] = useState<DeviceReferenceResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [refLoading, setRefLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [listLoading, setListLoading] = useState(true)

  // Load all devices on mount
  useEffect(() => {
    setListLoading(true)
    forgeEncyclopediaListDevices()
      .then((result) => {
        setAllDevices(result?.devices ?? [])
      })
      .catch((err) => {
        setError(err instanceof Error ? err.message : String(err))
      })
      .finally(() => setListLoading(false))
  }, [])

  // Search when query changes (debounced)
  useEffect(() => {
    if (!search.trim()) {
      setSearchResults(null)
      return
    }

    const timer = setTimeout(() => {
      setLoading(true)
      setError(null)
      forgeEncyclopediaSearch(search.trim())
        .then((result) => {
          setSearchResults(result?.results ?? [])
        })
        .catch((err) => {
          setError(err instanceof Error ? err.message : String(err))
        })
        .finally(() => setLoading(false))
    }, 300)

    return () => clearTimeout(timer)
  }, [search])

  // Load reference when device selected
  useEffect(() => {
    if (!selectedDevice) {
      setReference(null)
      return
    }

    setRefLoading(true)
    forgeEncyclopediaDeviceReference(selectedDevice)
      .then((result) => {
        if (result?.error) {
          setReference(null)
        } else {
          setReference(result)
        }
      })
      .catch(() => setReference(null))
      .finally(() => setRefLoading(false))
  }, [selectedDevice])

  // Filter all devices locally when no search query
  const displayedDevices = useMemo(() => {
    if (searchResults !== null) return null // using search results instead
    if (!search.trim()) return allDevices
    const q = search.toLowerCase()
    return allDevices.filter(
      (d) =>
        d.displayName.toLowerCase().includes(q) ||
        d.name.toLowerCase().includes(q) ||
        d.parentClass.toLowerCase().includes(q)
    )
  }, [allDevices, search, searchResults])

  return (
    <div className="flex-1 flex bg-fn-darker overflow-hidden min-h-0">
      {/* Left: Device List */}
      <div className="w-[320px] flex flex-col border-r border-fn-border bg-fn-dark shrink-0">
        {/* Header */}
        <div className="px-3 py-2 border-b border-fn-border">
          <div className="flex items-center gap-2 mb-2">
            <svg className="w-4 h-4 text-amber-400/70 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path d="M12 6.253v13m0-13C10.832 5.477 9.246 5 7.5 5S4.168 5.477 3 6.253v13C4.168 18.477 5.754 18 7.5 18s3.332.477 4.5 1.253m0-13C13.168 5.477 14.754 5 16.5 5c1.747 0 3.332.477 4.5 1.253v13C19.832 18.477 18.247 18 16.5 18c-1.746 0-3.332.477-4.5 1.253" />
            </svg>
            <span className="text-[12px] font-semibold text-white">Device Encyclopedia</span>
          </div>
          <div className="relative">
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search devices, properties, events..."
              className="w-full bg-fn-darker border border-fn-border rounded px-2.5 py-1.5 text-[10px] text-white placeholder-gray-600 focus:outline-none focus:border-blue-500/50 pr-7"
            />
            {loading && (
              <div className="absolute right-2 top-1/2 -translate-y-1/2">
                <div className="w-3 h-3 border-2 border-blue-400/30 border-t-blue-400 rounded-full animate-spin" />
              </div>
            )}
          </div>
          <div className="text-[9px] text-gray-600 mt-1.5">
            {searchResults !== null
              ? `${searchResults.length} results for "${search}"`
              : `${displayedDevices?.length ?? 0} device types`}
          </div>
        </div>

        {/* Device list */}
        <div className="flex-1 overflow-y-auto p-2 space-y-2 min-h-0">
          {listLoading && !searchResults && (
            <div className="flex items-center justify-center h-32">
              <div className="w-4 h-4 border-2 border-blue-400/30 border-t-blue-400 rounded-full animate-spin" />
            </div>
          )}
          {error && (
            <div className="p-3 text-[10px] text-red-400 bg-red-400/10 rounded border border-red-400/20">
              {error}
            </div>
          )}

          {/* Search results */}
          {searchResults !== null &&
            (searchResults.length === 0 ? (
              <div className="text-center py-8">
                <div className="text-[11px] text-gray-600 mb-1">No matches</div>
                <div className="text-[9px] text-gray-700">
                  Try broader terms like "trigger", "spawner", or "damage"
                </div>
              </div>
            ) : (
              searchResults.map((device) => (
                <DeviceCard
                  key={device.deviceName}
                  device={device}
                  isActive={selectedDevice === device.deviceName}
                  onSelect={() => setSelectedDevice(device.deviceName)}
                />
              ))
            ))}

          {/* All devices (when not searching) */}
          {searchResults === null &&
            displayedDevices &&
            (displayedDevices.length === 0 ? (
              <div className="text-center py-8">
                <div className="text-[11px] text-gray-600 mb-1">No devices found</div>
                <div className="text-[9px] text-gray-700">
                  Load a project with digest files to populate the encyclopedia.
                </div>
              </div>
            ) : (
              displayedDevices.map((device) => (
                <DeviceCard
                  key={device.name}
                  device={device}
                  isActive={selectedDevice === device.name}
                  onSelect={() => setSelectedDevice(device.name)}
                />
              ))
            ))}
        </div>
      </div>

      {/* Right: Device Detail */}
      <div className="flex-1 overflow-y-auto min-h-0">
        {refLoading && (
          <div className="flex items-center justify-center h-full">
            <div className="w-5 h-5 border-2 border-blue-400/30 border-t-blue-400 rounded-full animate-spin" />
          </div>
        )}
        {!refLoading && reference && <DeviceDetail reference={reference} />}
        {!refLoading && !reference && (
          <div className="flex items-center justify-center h-full">
            <div className="text-center">
              <svg
                className="w-12 h-12 mx-auto mb-3 text-gray-700"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={1}
              >
                <path d="M12 6.253v13m0-13C10.832 5.477 9.246 5 7.5 5S4.168 5.477 3 6.253v13C4.168 18.477 5.754 18 7.5 18s3.332.477 4.5 1.253m0-13C13.168 5.477 14.754 5 16.5 5c1.747 0 3.332.477 4.5 1.253v13C19.832 18.477 18.247 18 16.5 18c-1.746 0-3.332.477-4.5 1.253" />
              </svg>
              <div className="text-[12px] text-gray-500 font-medium">Device Encyclopedia</div>
              <div className="text-[10px] text-gray-700 mt-1 max-w-xs">
                Search or browse UEFN Creative Devices. Select a device to view its properties,
                events, functions, and common configurations.
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
