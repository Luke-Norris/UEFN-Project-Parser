import { useEffect, useState, useMemo } from 'react'
import type {
  WellVersedLevel,
  DeviceEntry,
  DeviceListResult,
  DeviceInspectResult
} from '../../shared/types'

interface DevicesPageProps {
  selectedLevel?: string | null
}

export function DevicesPage({ selectedLevel: selectedLevelProp }: DevicesPageProps) {
  const [levels, setLevels] = useState<WellVersedLevel[]>([])
  const [activeLevelPath, setActiveLevelPath] = useState<string | null>(null)
  const [deviceData, setDeviceData] = useState<DeviceListResult | null>(null)
  const [selectedDevice, setSelectedDevice] = useState<DeviceInspectResult | null>(null)
  const [loading, setLoading] = useState(true)
  const [devicesLoading, setDevicesLoading] = useState(false)
  const [inspectLoading, setInspectLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    loadLevels()
  }, [])

  // When a level is passed via prop, load devices for it directly
  useEffect(() => {
    if (selectedLevelProp) {
      loadDevices(selectedLevelProp)
    }
  }, [selectedLevelProp])

  async function loadLevels() {
    try {
      setLoading(true)
      setError(null)
      const result = await window.electronAPI.forgeListLevels()
      setLevels(result ?? [])
      // If a level was passed as a prop, use that; otherwise auto-select first
      if (selectedLevelProp) {
        loadDevices(selectedLevelProp)
      } else if (result && result.length > 0) {
        loadDevices(result[0].filePath)
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load levels')
    } finally {
      setLoading(false)
    }
  }

  async function loadDevices(levelPath: string) {
    try {
      setDevicesLoading(true)
      setActiveLevelPath(levelPath)
      setSelectedDevice(null)
      const result = await window.electronAPI.forgeListDevices(levelPath)
      setDeviceData(result)
    } catch (err) {
      console.error('Failed to load devices:', err)
    } finally {
      setDevicesLoading(false)
    }
  }

  async function inspectDevice(device: DeviceEntry) {
    try {
      setInspectLoading(true)
      const result = await window.electronAPI.forgeInspectDevice(device.filePath)
      setSelectedDevice(result)
    } catch (err) {
      console.error('Inspect failed:', err)
    } finally {
      setInspectLoading(false)
    }
  }

  const groupedDevices = useMemo(() => {
    const devices = deviceData?.devices ?? []
    const groups = new Map<string, DeviceEntry[]>()
    for (const d of devices) {
      const type = d.deviceType || 'Unknown'
      if (!groups.has(type)) groups.set(type, [])
      groups.get(type)!.push(d)
    }
    return Array.from(groups.entries()).sort(([a], [b]) => a.localeCompare(b))
  }, [deviceData])

  if (loading) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-[11px] text-gray-400">Loading...</div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="text-[11px] text-red-400 mb-3">{error}</div>
          <button
            onClick={loadLevels}
            className="px-3 py-1.5 text-[10px] font-medium text-white bg-fn-panel border border-fn-border rounded hover:bg-white/[0.06] transition-colors"
          >
            Retry
          </button>
        </div>
      </div>
    )
  }

  if (levels.length === 0) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="bg-fn-panel border border-fn-border rounded-lg p-6 text-center">
          <p className="text-[11px] text-gray-400 mb-1">No levels found</p>
          <p className="text-[10px] text-gray-600">
            Add a project with .umap level files first.
          </p>
        </div>
      </div>
    )
  }

  return (
    <div className="flex-1 flex bg-fn-darker overflow-hidden min-h-0">
      {/* Left Panel — Device List */}
      <div className="w-80 flex flex-col border-r border-fn-border bg-fn-dark shrink-0">
        {/* Level selector */}
        <div className="px-3 py-2 border-b border-fn-border">
          <label className="text-[9px] font-semibold text-gray-600 uppercase tracking-wider block mb-1">Level</label>
          <select
            value={activeLevelPath ?? ''}
            onChange={(e) => loadDevices(e.target.value)}
            className="w-full bg-fn-darker border border-fn-border rounded px-2 py-1.5 text-[11px] text-white focus:outline-none focus:border-fn-rare/50"
          >
            {levels.map((l) => (
              <option key={l.filePath} value={l.filePath}>{l.name}</option>
            ))}
          </select>
        </div>

        {/* Device list */}
        <div className="flex-1 overflow-y-auto min-h-0">
          {devicesLoading ? (
            <div className="p-4 text-center text-[11px] text-gray-400">Loading devices...</div>
          ) : groupedDevices.length === 0 ? (
            <div className="p-4 text-center text-[11px] text-gray-500">No devices in this level</div>
          ) : (
            groupedDevices.map(([type, devices]) => (
              <div key={type}>
                {/* Group header */}
                <div className="px-3 py-1.5 bg-fn-panel border-b border-fn-border/50">
                  <div className="flex items-center justify-between">
                    <span className="text-[10px] font-semibold text-gray-400">{type}</span>
                    <span className="text-[9px] text-gray-600">{devices.length}</span>
                  </div>
                </div>
                {/* Device rows */}
                {devices.map((device) => (
                  <button
                    key={device.filePath}
                    onClick={() => inspectDevice(device)}
                    className={`w-full text-left px-3 py-2 border-b border-fn-border/20 hover:bg-white/[0.03] transition-colors ${
                      selectedDevice?.filePath === device.filePath ? 'bg-fn-rare/10 border-l-2 border-l-fn-rare' : ''
                    }`}
                  >
                    <div className="text-[11px] text-white truncate">{device.name}</div>
                    {device.position && (
                      <div className="text-[9px] text-gray-600 mt-0.5">
                        ({device.position.x.toFixed(0)}, {device.position.y.toFixed(0)}, {device.position.z.toFixed(0)})
                      </div>
                    )}
                  </button>
                ))}
              </div>
            ))
          )}
        </div>

        {/* Device count */}
        <div className="px-3 py-2 border-t border-fn-border text-[10px] text-gray-600">
          {deviceData?.devices.length ?? 0} device{(deviceData?.devices.length ?? 0) !== 1 ? 's' : ''}
        </div>
      </div>

      {/* Right Panel — Device Detail */}
      <div className="flex-1 overflow-y-auto min-h-0">
        {inspectLoading ? (
          <div className="flex items-center justify-center h-full">
            <div className="text-[11px] text-gray-400">Loading device properties...</div>
          </div>
        ) : selectedDevice ? (
          <div className="p-6 space-y-5">
            {/* Device header */}
            <div>
              <h2 className="text-lg font-semibold text-white">{selectedDevice.name}</h2>
              <div className="flex items-center gap-3 mt-1">
                <span className="inline-block px-1.5 py-0.5 rounded text-[9px] font-medium text-fn-epic bg-fn-epic/10 border border-fn-epic/20">
                  {selectedDevice.deviceType}
                </span>
                {selectedDevice.position && (
                  <span className="text-[10px] text-gray-500">
                    Position: ({selectedDevice.position.x.toFixed(1)}, {selectedDevice.position.y.toFixed(1)}, {selectedDevice.position.z.toFixed(1)})
                  </span>
                )}
              </div>
            </div>

            {/* Properties table */}
            {selectedDevice.properties.length > 0 ? (
              <div>
                <h3 className="text-[11px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
                  Properties ({selectedDevice.properties.length})
                </h3>
                <div className="bg-fn-dark border border-fn-border rounded-lg overflow-hidden">
                  {/* Table header */}
                  <div className="grid grid-cols-[1fr_1fr_auto_auto] gap-3 px-4 py-2 border-b border-fn-border bg-fn-panel">
                    <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Name</span>
                    <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Value</span>
                    <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Type</span>
                    <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider w-8"></span>
                  </div>
                  {/* Rows */}
                  {selectedDevice.properties.map((prop, i) => (
                    <div
                      key={i}
                      className="grid grid-cols-[1fr_1fr_auto_auto] gap-3 px-4 py-2 border-b border-fn-border/30 hover:bg-white/[0.02]"
                    >
                      <span className="text-[11px] text-gray-300 truncate">{prop.name}</span>
                      <span className="text-[11px] text-white truncate">{prop.value}</span>
                      <span className="text-[9px] text-gray-600 self-center">{prop.type}</span>
                      <span className="w-8 flex items-center justify-center">
                        {prop.isEditable && (
                          <svg className="w-3 h-3 text-fn-rare/50" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                            <path d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                          </svg>
                        )}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            ) : (
              <div className="bg-fn-panel border border-fn-border rounded-lg p-6 text-center">
                <p className="text-[11px] text-gray-400">No properties found</p>
              </div>
            )}
          </div>
        ) : (
          <div className="flex items-center justify-center h-full">
            <div className="text-center">
              <svg className="w-8 h-8 mx-auto mb-2 text-gray-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.066 2.573c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.573 1.066c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.066-2.573c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.573-1.066z" />
                <path d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
              </svg>
              <p className="text-[11px] text-gray-500">Select a device to inspect</p>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
