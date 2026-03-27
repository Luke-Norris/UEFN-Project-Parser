import { useEffect, useState, useRef, useMemo, useCallback } from 'react'
import type { DeviceEntry, DeviceListResult, DeviceInspectResult, AssetProperty } from '../../shared/types'

// ─── Pin classification ─────────────────────────────────────────────────────

type PinDirection = 'input' | 'output'
type PinCategory = 'event' | 'data' | 'config'

interface BlueprintPin {
  name: string
  direction: PinDirection
  category: PinCategory
  value: string
  type: string
  connected: boolean
}

interface BlueprintNode {
  id: string
  label: string
  deviceType: string
  filePath: string
  x: number
  y: number
  pins: BlueprintPin[]
  inspected: boolean
  connectionCount: number
}

interface Connection {
  fromNode: string
  fromPin: string
  toNode: string
  toPin: string
  channel: string
}

// Patterns for classifying pins
const OUTPUT_PATTERNS = [/Trigger$/, /OnSuccess$/, /OnComplete$/, /OnFailure$/, /OnLoaded$/, /OnSaved$/, /OnCleared$/]
const INPUT_PATTERNS = [/WhenReceived$/, /WhenReceivingFrom$/, /OnReceivedFrom$/]

// ─── Filtering ───────────────────────────────────────────────────────────────

/** System/engine actors nobody cares about */
const FILTERED_NAME_PATTERNS = [
  /^Default__/i, /WorldSettings/i, /^HLOD/i, /NavigationData/i,
  /^AbstractNavData/i, /^NavMesh/i, /^RecastNavMesh/i,
  /^WorldDataLayers/i, /^LevelBounds/i, /^AtmosphericFog/i,
  /^SkyLight/i, /^DirectionalLight/i, /^ExponentialHeightFog/i,
  /^PostProcessVolume/i, /^SphereReflectionCapture/i,
  /^Note_/i, /^LevelSequence/i, /^GameMode/i, /^GameState/i,
  /^PlayerStart$/i, /^Brush/i, /^Volume$/i,
]

/** Name prefixes that indicate a user-placed creative device */
const USER_DEVICE_PREFIXES = ['BP_', 'PBWA_']

/** Maximum nodes to display */
const MAX_VISIBLE_NODES = 50

function classifyProperty(prop: AssetProperty): BlueprintPin | null {
  const { name, value, type } = prop

  // Skip rendering/transform noise
  if (['RelativeLocation', 'RelativeRotation', 'RelativeScale3D', 'RootComponent',
       'AttachParent', 'AttachSocketName', 'BlueprintCreatedComponents',
       'bNetAddressable', 'CreationMethod', 'UCSSerializationIndex',
       'bHidden', 'bIsEditorOnlyActor'].includes(name)) return null

  // Output events (Trigger)
  for (const pat of OUTPUT_PATTERNS) {
    if (pat.test(name)) {
      return { name, direction: 'output', category: 'event', value, type, connected: value !== '' && value !== 'None' }
    }
  }

  // Input events (WhenReceived)
  for (const pat of INPUT_PATTERNS) {
    if (pat.test(name)) {
      return { name, direction: 'input', category: 'event', value, type, connected: value !== '' && value !== 'None' }
    }
  }

  // ObjectPropertyData → likely a reference to another device/asset
  if (type === 'ObjectPropertyData' || type === 'SoftObjectPropertyData') {
    return { name, direction: 'input', category: 'data', value, type, connected: value !== '' && value !== 'None' && value !== 'null' }
  }

  // Config properties — all other meaningful properties
  if (value && value !== 'None' && value !== '' && value !== '0' && value !== 'false' && value !== 'False') {
    return { name, direction: 'input', category: 'config', value, type, connected: false }
  }

  return null
}

// ─── Colors ─────────────────────────────────────────────────────────────────

const DEVICE_COLORS: Record<string, string> = {
  'Button': '#3d85e0',
  'Trigger': '#60aa3a',
  'Item Spawner': '#a34ee1',
  'Vending Machine': '#c4a23c',
  'Damage Volume': '#e05555',
  'Timer': '#76d6e3',
  'Elimination Manager': '#e04040',
  'Score Manager': '#a34ee1',
  'Player Spawn': '#60aa3a',
  'HUD Message': '#44ff88',
  'Conditional': '#9966ff',
  'Class Selector': '#66ffcc',
  'Barrier': '#4a9eff',
}

function getNodeColor(deviceType: string): string {
  for (const [key, color] of Object.entries(DEVICE_COLORS)) {
    if (deviceType.includes(key)) return color
  }
  // Hash-based fallback
  let hash = 0
  for (let i = 0; i < deviceType.length; i++) hash = deviceType.charCodeAt(i) + ((hash << 5) - hash)
  const h = Math.abs(hash) % 360
  return `hsl(${h}, 50%, 45%)`
}

const PIN_COLORS = {
  event: { fill: '#ff4444', stroke: '#ff6666' },
  data: { fill: '#3d85e0', stroke: '#5a9de6' },
  config: { fill: '#888899', stroke: '#aaaabb' },
}

// ─── Layout ─────────────────────────────────────────────────────────────────

const NODE_WIDTH = 260
const NODE_HEADER_HEIGHT = 32
const PIN_HEIGHT = 22
const PIN_RADIUS = 5

function getNodeHeight(node: BlueprintNode): number {
  return NODE_HEADER_HEIGHT + Math.max(node.pins.length, 1) * PIN_HEIGHT + 8
}

function layoutNodes(nodes: BlueprintNode[]): BlueprintNode[] {
  // Group by device type, arrange in columns
  const types = [...new Set(nodes.map((n) => n.deviceType))]
  const COLS = Math.max(1, Math.ceil(Math.sqrt(types.length)))
  const COL_WIDTH = NODE_WIDTH + 120
  const ROW_GAP = 30

  let col = 0
  let row = 0
  let maxRowHeight = 0

  const typeIndex = new Map<string, { col: number; startY: number }>()
  for (const t of types) {
    typeIndex.set(t, { col, startY: row })
    const nodesOfType = nodes.filter((n) => n.deviceType === t)
    const totalHeight = nodesOfType.reduce((h, n) => h + getNodeHeight(n) + ROW_GAP, 0)
    maxRowHeight = Math.max(maxRowHeight, totalHeight)
    col++
    if (col >= COLS) {
      col = 0
      row += maxRowHeight + 60
      maxRowHeight = 0
    }
  }

  return nodes.map((n) => {
    const info = typeIndex.get(n.deviceType) || { col: 0, startY: 0 }
    const sameType = nodes.filter((o) => o.deviceType === n.deviceType)
    const idx = sameType.indexOf(n)
    let y = info.startY
    for (let i = 0; i < idx; i++) y += getNodeHeight(sameType[i]) + ROW_GAP
    return { ...n, x: info.col * COL_WIDTH + 40, y: y + 40 }
  })
}

// ─── Connection detection ───────────────────────────────────────────────────

function detectConnections(nodes: BlueprintNode[]): Connection[] {
  const connections: Connection[] = []

  // Build index of all output pins with their values
  const outputIndex = new Map<string, { nodeId: string; pinName: string }>()
  for (const node of nodes) {
    for (const pin of node.pins) {
      if (pin.direction === 'output' && pin.connected && pin.value) {
        outputIndex.set(`${node.id}:${pin.value}`, { nodeId: node.id, pinName: pin.name })
      }
    }
  }

  // Match inputs to outputs by shared references (object export indices, channel strings)
  for (const node of nodes) {
    for (const pin of node.pins) {
      if (pin.direction === 'input' && pin.connected && pin.value) {
        // Look for outputs that reference the same value
        for (const [key, out] of outputIndex) {
          if (out.nodeId === node.id) continue // no self-connections
          const outValue = key.split(':')[1]
          if (pin.value === outValue || pin.value.includes(outValue) || outValue.includes(pin.value)) {
            connections.push({
              fromNode: out.nodeId,
              fromPin: out.pinName,
              toNode: node.id,
              toPin: pin.name,
              channel: pin.value,
            })
          }
        }
      }
    }
  }

  return connections
}

// ─── Main Component ─────────────────────────────────────────────────────────

interface Props {
  selectedLevel?: string | null
}

/** Returns true if a device should be shown in the graph */
function isRelevantDevice(name: string, deviceType: string): boolean {
  // Reject known system/engine patterns
  for (const pat of FILTERED_NAME_PATTERNS) {
    if (pat.test(name) || pat.test(deviceType)) return false
  }

  // Accept user-created prefixes
  for (const prefix of USER_DEVICE_PREFIXES) {
    if (deviceType.startsWith(prefix) || name.startsWith(prefix)) return true
  }

  // Accept creative device keyword match
  const t = deviceType.toLowerCase()
  return t.includes('device') || t.includes('spawner') || t.includes('manager') ||
    t.includes('tracker') || t.includes('trigger') || t.includes('button') ||
    t.includes('switch') || t.includes('timer') || t.includes('barrier') ||
    t.includes('granter') || t.includes('volume') || t.includes('hud') ||
    t.includes('conditional') || t.includes('selector') || t.includes('portal') ||
    t.includes('scoreboard') || t.includes('billboard') || t.includes('teleporter')
}

export function BlueprintGraphPage({ selectedLevel }: Props) {
  const svgRef = useRef<SVGSVGElement>(null)
  const [nodes, setNodes] = useState<BlueprintNode[]>([])
  const [connections, setConnections] = useState<Connection[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [levels, setLevels] = useState<{ filePath: string; name: string }[]>([])
  const [levelPath, setLevelPath] = useState<string | null>(selectedLevel ?? null)
  const [selectedNode, setSelectedNode] = useState<BlueprintNode | null>(null)
  const [inspectProgress, setInspectProgress] = useState({ done: 0, total: 0 })
  const [viewBox, setViewBox] = useState({ x: -20, y: -20, w: 2000, h: 1400 })
  const [showConfig, setShowConfig] = useState(false)
  const [searchQuery, setSearchQuery] = useState('')
  const [totalFiltered, setTotalFiltered] = useState(0)
  const [truncatedCount, setTruncatedCount] = useState(0)

  // Drag state
  const dragRef = useRef<{ startX: number; startY: number; startVBX: number; startVBY: number } | null>(null)

  // Load levels
  useEffect(() => {
    window.electronAPI.forgeListLevels()
      .then((r: any) => {
        const lvls = Array.isArray(r) ? r : []
        setLevels(lvls)
        if (!levelPath && lvls.length > 0) setLevelPath(lvls[0].filePath)
      })
      .catch(() => {})
  }, []) // eslint-disable-line

  // Load devices when level changes
  useEffect(() => {
    if (!levelPath) return
    let cancelled = false
    setLoading(true)
    setError(null)
    setNodes([])
    setConnections([])
    setTotalFiltered(0)
    setTruncatedCount(0)

    ;(async () => {
      try {
        const result: DeviceListResult = await window.electronAPI.forgeListDevices(levelPath)
        if (cancelled) return

        // Filter to relevant creative devices only
        const filtered = result.devices.filter(
          (d) => d.deviceType && isRelevantDevice(d.name || d.deviceType, d.deviceType)
        )
        setTotalFiltered(filtered.length)

        // Build initial nodes (will be capped after inspection reveals connection counts)
        const rawNodes: BlueprintNode[] = filtered
          .map((d, i) => ({
            id: d.filePath || `device-${i}`,
            label: d.name || d.deviceType,
            deviceType: d.deviceType,
            filePath: d.filePath,
            x: 0,
            y: 0,
            pins: [],
            inspected: false,
            connectionCount: 0,
          }))

        // Cap to MAX_VISIBLE_NODES for initial display; will re-sort after inspection
        const cappedNodes = rawNodes.slice(0, MAX_VISIBLE_NODES)
        if (rawNodes.length > MAX_VISIBLE_NODES) {
          setTruncatedCount(rawNodes.length - MAX_VISIBLE_NODES)
        }

        // Layout initially
        const laid = layoutNodes(cappedNodes)
        setNodes(laid)
        setInspectProgress({ done: 0, total: laid.length })

        // Inspect each device for properties (batch in parallel groups of 8)
        const BATCH = 8
        for (let i = 0; i < laid.length; i += BATCH) {
          if (cancelled) break
          const batch = laid.slice(i, i + BATCH)
          const results = await Promise.allSettled(
            batch.map((n) => window.electronAPI.forgeInspectDevice(n.filePath))
          )

          if (cancelled) break

          setNodes((prev) => {
            const next = [...prev]
            for (let j = 0; j < batch.length; j++) {
              const r = results[j]
              if (r.status !== 'fulfilled') continue
              const inspect = r.value as DeviceInspectResult
              const idx = next.findIndex((n) => n.id === batch[j].id)
              if (idx < 0) continue

              const pins: BlueprintPin[] = []
              for (const prop of inspect.properties) {
                const pin = classifyProperty(prop)
                if (pin) pins.push(pin)
              }

              const connCount = pins.filter((p) => p.connected).length
              next[idx] = { ...next[idx], pins, inspected: true, connectionCount: connCount }
            }
            return layoutNodes(next)
          })

          setInspectProgress((p) => ({ ...p, done: Math.min(p.total, i + BATCH) }))
        }

        // Detect connections after all inspected
        setNodes((prev) => {
          const conns = detectConnections(prev)
          setConnections(conns)

          // Re-sort by connection count and re-cap if we had excess
          if (rawNodes.length > MAX_VISIBLE_NODES) {
            // Update connection counts from detected connections
            const connCountMap = new Map<string, number>()
            for (const c of conns) {
              connCountMap.set(c.fromNode, (connCountMap.get(c.fromNode) ?? 0) + 1)
              connCountMap.set(c.toNode, (connCountMap.get(c.toNode) ?? 0) + 1)
            }
            const sorted = [...prev].map((n) => ({
              ...n,
              connectionCount: (connCountMap.get(n.id) ?? 0) + n.pins.filter((p) => p.connected).length,
            }))
            sorted.sort((a, b) => b.connectionCount - a.connectionCount)
            return layoutNodes(sorted.slice(0, MAX_VISIBLE_NODES))
          }
          return prev
        })
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Failed to load devices')
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()

    return () => { cancelled = true }
  }, [levelPath])

  // Filter nodes by search
  const visibleNodes = useMemo(() => {
    if (!searchQuery.trim()) return nodes
    const q = searchQuery.toLowerCase()
    return nodes.filter((n) =>
      n.label.toLowerCase().includes(q) ||
      n.deviceType.toLowerCase().includes(q) ||
      n.pins.some((p) => p.name.toLowerCase().includes(q))
    )
  }, [nodes, searchQuery])

  const visibleNodeIds = useMemo(() => new Set(visibleNodes.map((n) => n.id)), [visibleNodes])

  const visibleConnections = useMemo(() =>
    connections.filter((c) => visibleNodeIds.has(c.fromNode) && visibleNodeIds.has(c.toNode)),
    [connections, visibleNodeIds]
  )

  // Pan handlers
  const handleMouseDown = useCallback((e: React.MouseEvent) => {
    if (e.button === 1 || (e.button === 0 && e.altKey)) { // middle or alt+click
      dragRef.current = { startX: e.clientX, startY: e.clientY, startVBX: viewBox.x, startVBY: viewBox.y }
    }
  }, [viewBox])

  const handleMouseMove = useCallback((e: React.MouseEvent) => {
    const drag = dragRef.current
    if (!drag) return
    const scale = viewBox.w / (svgRef.current?.clientWidth || 1)
    const dx = (e.clientX - drag.startX) * scale
    const dy = (e.clientY - drag.startY) * scale
    setViewBox((v) => ({ ...v, x: drag.startVBX - dx, y: drag.startVBY - dy }))
  }, [viewBox])

  const handleMouseUp = useCallback(() => { dragRef.current = null }, [])

  const handleWheel = useCallback((e: React.WheelEvent) => {
    const factor = e.deltaY > 0 ? 1.1 : 0.9
    const svg = svgRef.current
    if (!svg) return
    const rect = svg.getBoundingClientRect()
    const mx = (e.clientX - rect.left) / rect.width
    const my = (e.clientY - rect.top) / rect.height

    setViewBox((v) => {
      const nw = v.w * factor
      const nh = v.h * factor
      return { x: v.x + (v.w - nw) * mx, y: v.y + (v.h - nh) * my, w: nw, h: nh }
    })
  }, [])

  // Get pin position for connection drawing
  const getPinPos = useCallback((nodeId: string, pinName: string, direction: PinDirection) => {
    const node = nodes.find((n) => n.id === nodeId)
    if (!node) return { x: 0, y: 0 }
    const pinIdx = node.pins.filter((p) => showConfig || p.category !== 'config').findIndex((p) => p.name === pinName)
    if (pinIdx < 0) return { x: 0, y: 0 }
    const x = direction === 'output' ? node.x + NODE_WIDTH : node.x
    const y = node.y + NODE_HEADER_HEIGHT + pinIdx * PIN_HEIGHT + PIN_HEIGHT / 2
    return { x, y }
  }, [nodes, showConfig])

  // ─── Render ─────────────────────────────────────────────────────────────────

  if (loading && nodes.length === 0) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="w-5 h-5 border-2 border-blue-400/30 border-t-blue-400 rounded-full animate-spin mx-auto mb-2" />
          <div className="text-[11px] text-gray-400">Loading Blueprint graph...</div>
        </div>
      </div>
    )
  }

  return (
    <div className="flex-1 flex flex-col bg-fn-darker min-h-0 overflow-hidden">
      {/* Toolbar */}
      <div className="flex items-center gap-2 px-3 py-2 border-b border-fn-border bg-fn-dark shrink-0 text-[10px]">
        <span className="text-gray-400 font-semibold uppercase tracking-wider">Blueprint Graph</span>

        {/* Level selector */}
        <select
          value={levelPath || ''}
          onChange={(e) => setLevelPath(e.target.value || null)}
          className="bg-fn-darker border border-fn-border rounded px-2 py-1 text-[10px] text-white focus:outline-none focus:border-blue-500/50 max-w-[200px]"
        >
          {levels.map((l) => (
            <option key={l.filePath} value={l.filePath}>{l.name}</option>
          ))}
        </select>

        <div className="w-px h-4 bg-fn-border" />

        <input
          type="text"
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          placeholder="Filter nodes..."
          className="bg-fn-darker border border-fn-border rounded px-2 py-1 text-[10px] text-white placeholder-gray-600 focus:outline-none focus:border-blue-500/50 w-40"
        />

        <button
          onClick={() => setShowConfig(!showConfig)}
          className={`px-1.5 py-0.5 rounded transition-colors ${showConfig ? 'text-white bg-white/10' : 'text-gray-600 hover:text-gray-400'}`}
        >
          Config
        </button>

        <div className="ml-auto flex items-center gap-2 text-gray-500">
          {inspectProgress.total > 0 && inspectProgress.done < inspectProgress.total && (
            <span className="text-blue-400 animate-pulse">
              Inspecting {inspectProgress.done}/{inspectProgress.total}
            </span>
          )}
          {truncatedCount > 0 && (
            <span className="text-amber-400/70" title={`${totalFiltered} total devices, showing top ${MAX_VISIBLE_NODES} by connection count`}>
              Top {MAX_VISIBLE_NODES} of {totalFiltered}
            </span>
          )}
          <span>{visibleNodes.length} nodes</span>
          <span>{visibleConnections.length} connections</span>
        </div>
      </div>

      {error && (
        <div className="px-4 py-2 bg-red-500/10 border-b border-red-500/20 text-[10px] text-red-400">{error}</div>
      )}

      {/* SVG Graph */}
      <div className="flex-1 min-h-0 flex overflow-hidden">
        <svg
          ref={svgRef}
          className="flex-1 min-h-0"
          viewBox={`${viewBox.x} ${viewBox.y} ${viewBox.w} ${viewBox.h}`}
          onMouseDown={handleMouseDown}
          onMouseMove={handleMouseMove}
          onMouseUp={handleMouseUp}
          onMouseLeave={handleMouseUp}
          onWheel={handleWheel}
          style={{ cursor: dragRef.current ? 'grabbing' : 'default' }}
        >
          <defs>
            {/* Grid pattern */}
            <pattern id="grid-small" width="20" height="20" patternUnits="userSpaceOnUse">
              <path d="M 20 0 L 0 0 0 20" fill="none" stroke="rgba(255,255,255,0.02)" strokeWidth="0.5" />
            </pattern>
            <pattern id="grid-large" width="100" height="100" patternUnits="userSpaceOnUse">
              <rect width="100" height="100" fill="url(#grid-small)" />
              <path d="M 100 0 L 0 0 0 100" fill="none" stroke="rgba(255,255,255,0.04)" strokeWidth="0.5" />
            </pattern>
          </defs>

          {/* Background grid */}
          <rect x={viewBox.x} y={viewBox.y} width={viewBox.w} height={viewBox.h} fill="url(#grid-large)" />

          {/* Connection wires */}
          {visibleConnections.map((conn, i) => {
            const from = getPinPos(conn.fromNode, conn.fromPin, 'output')
            const to = getPinPos(conn.toNode, conn.toPin, 'input')
            const dx = Math.abs(to.x - from.x) * 0.5
            return (
              <path
                key={i}
                d={`M ${from.x} ${from.y} C ${from.x + dx} ${from.y}, ${to.x - dx} ${to.y}, ${to.x} ${to.y}`}
                fill="none"
                stroke="#ff6666"
                strokeWidth={2}
                opacity={0.6}
              />
            )
          })}

          {/* Nodes */}
          {visibleNodes.map((node) => {
            const color = getNodeColor(node.deviceType)
            const isSelected = selectedNode?.id === node.id
            const visiblePins = showConfig ? node.pins : node.pins.filter((p) => p.category !== 'config')
            const height = NODE_HEADER_HEIGHT + Math.max(visiblePins.length, 1) * PIN_HEIGHT + 8

            // Viewport culling: skip nodes entirely outside the viewBox
            const nodeRight = node.x + NODE_WIDTH
            const nodeBottom = node.y + height
            const inView = nodeRight >= viewBox.x && node.x <= viewBox.x + viewBox.w &&
              nodeBottom >= viewBox.y && node.y <= viewBox.y + viewBox.h
            if (!inView && !isSelected) return null

            // Label detail level: hide pin labels when zoomed out far
            const svgWidth = svgRef.current?.clientWidth ?? 1200
            const pixelsPerUnit = svgWidth / viewBox.w
            const showLabels = pixelsPerUnit > 0.25 // ~25% zoom or closer
            const showPinLabels = pixelsPerUnit > 0.4 // ~40% zoom or closer

            return (
              <g key={node.id} onClick={() => setSelectedNode(isSelected ? null : node)} style={{ cursor: 'pointer' }}>
                {/* Node background */}
                <rect
                  x={node.x}
                  y={node.y}
                  width={NODE_WIDTH}
                  height={height}
                  rx={4}
                  fill="#1a1a2e"
                  stroke={isSelected ? '#ffffff' : color}
                  strokeWidth={isSelected ? 2 : 1}
                  opacity={0.95}
                />

                {/* Header */}
                <rect
                  x={node.x}
                  y={node.y}
                  width={NODE_WIDTH}
                  height={NODE_HEADER_HEIGHT}
                  rx={4}
                  fill={color}
                  opacity={0.8}
                />
                {/* Square bottom corners on header */}
                <rect
                  x={node.x}
                  y={node.y + NODE_HEADER_HEIGHT - 4}
                  width={NODE_WIDTH}
                  height={4}
                  fill={color}
                  opacity={0.8}
                />

                {/* Node title — hidden when zoomed out too far */}
                {showLabels && (
                  <text
                    x={node.x + 10}
                    y={node.y + 20}
                    fill="white"
                    fontSize={11}
                    fontWeight={600}
                    fontFamily="system-ui"
                  >
                    {node.label.length > 30 ? node.label.slice(0, 28) + '...' : node.label}
                  </text>
                )}

                {/* Pins — labels hidden when zoomed out; circles always visible */}
                {visiblePins.map((pin, pi) => {
                  const py = node.y + NODE_HEADER_HEIGHT + pi * PIN_HEIGHT + PIN_HEIGHT / 2
                  const px = pin.direction === 'output' ? node.x + NODE_WIDTH : node.x
                  const pinColor = PIN_COLORS[pin.category]

                  return (
                    <g key={pin.name}>
                      {/* Pin circle */}
                      <circle
                        cx={px}
                        cy={py}
                        r={PIN_RADIUS}
                        fill={pin.connected ? pinColor.fill : '#1a1a2e'}
                        stroke={pinColor.stroke}
                        strokeWidth={1.5}
                      />

                      {/* Pin label — only when zoomed in enough */}
                      {showPinLabels && (
                        <text
                          x={pin.direction === 'output' ? px - 10 : px + 10}
                          y={py + 3.5}
                          fill={pin.connected ? '#e2e8f0' : '#6b7280'}
                          fontSize={9}
                          fontFamily="system-ui"
                          textAnchor={pin.direction === 'output' ? 'end' : 'start'}
                        >
                          {pin.name.length > 28 ? pin.name.slice(0, 26) + '..' : pin.name}
                        </text>
                      )}

                      {/* Pin value (for config pins) */}
                      {showPinLabels && pin.category === 'config' && pin.value && (
                        <text
                          x={pin.direction === 'output' ? node.x + NODE_WIDTH - 10 : node.x + NODE_WIDTH - 10}
                          y={py + 3.5}
                          fill="#4b5563"
                          fontSize={8}
                          fontFamily="monospace"
                          textAnchor="end"
                        >
                          {pin.value.length > 18 ? pin.value.slice(0, 16) + '..' : pin.value}
                        </text>
                      )}
                    </g>
                  )
                })}

                {/* No pins placeholder */}
                {showLabels && visiblePins.length === 0 && (
                  <text
                    x={node.x + NODE_WIDTH / 2}
                    y={node.y + NODE_HEADER_HEIGHT + 16}
                    fill="#4b5563"
                    fontSize={9}
                    fontFamily="system-ui"
                    textAnchor="middle"
                  >
                    {node.inspected ? 'No pins' : 'Loading...'}
                  </text>
                )}
              </g>
            )
          })}
        </svg>

        {/* Selected node detail panel */}
        {selectedNode && (
          <div className="w-[320px] border-l border-fn-border bg-fn-dark flex flex-col shrink-0 min-h-0 overflow-hidden">
            <div className="px-3 py-2 border-b border-fn-border shrink-0">
              <div className="text-[12px] font-semibold text-white truncate">{selectedNode.label}</div>
              <div className="text-[9px] text-gray-500">{selectedNode.deviceType}</div>
            </div>
            <div className="flex-1 overflow-y-auto min-h-0 p-3 space-y-3">
              {/* Event pins */}
              {selectedNode.pins.filter((p) => p.category === 'event').length > 0 && (
                <div>
                  <div className="text-[9px] font-semibold text-red-400/70 uppercase tracking-wider mb-1">Events</div>
                  {selectedNode.pins.filter((p) => p.category === 'event').map((pin) => (
                    <div key={pin.name} className="flex items-center gap-2 py-1">
                      <span className={`text-[8px] px-1 rounded ${pin.direction === 'output' ? 'bg-red-400/10 text-red-400' : 'bg-green-400/10 text-green-400'}`}>
                        {pin.direction === 'output' ? 'OUT' : 'IN'}
                      </span>
                      <span className="text-[10px] text-gray-300 flex-1 truncate">{pin.name}</span>
                      {pin.connected && <span className="w-1.5 h-1.5 rounded-full bg-green-400 shrink-0" />}
                    </div>
                  ))}
                </div>
              )}

              {/* Data pins */}
              {selectedNode.pins.filter((p) => p.category === 'data').length > 0 && (
                <div>
                  <div className="text-[9px] font-semibold text-blue-400/70 uppercase tracking-wider mb-1">References</div>
                  {selectedNode.pins.filter((p) => p.category === 'data').map((pin) => (
                    <div key={pin.name} className="py-1">
                      <div className="text-[10px] text-gray-300">{pin.name}</div>
                      <div className="text-[9px] text-gray-500 font-mono truncate">{pin.value}</div>
                    </div>
                  ))}
                </div>
              )}

              {/* Config pins */}
              {selectedNode.pins.filter((p) => p.category === 'config').length > 0 && (
                <div>
                  <div className="text-[9px] font-semibold text-gray-400/70 uppercase tracking-wider mb-1">Configuration</div>
                  {selectedNode.pins.filter((p) => p.category === 'config').map((pin) => (
                    <div key={pin.name} className="flex items-center gap-2 py-0.5">
                      <span className="text-[10px] text-gray-400 flex-1 truncate">{pin.name}</span>
                      <span className="text-[9px] text-gray-500 font-mono truncate max-w-[120px]">{pin.value}</span>
                    </div>
                  ))}
                </div>
              )}

              <div className="text-[9px] text-gray-600 font-mono break-all pt-2 border-t border-fn-border/30">
                {selectedNode.filePath}
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
