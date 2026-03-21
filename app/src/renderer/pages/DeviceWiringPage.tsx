import { useEffect, useState, useRef, useMemo, useCallback } from 'react'
import { ErrorMessage } from '../components/ErrorMessage'
import type {
  ForgeLevel,
  DeviceEntry,
  DeviceListResult,
  DeviceInspectResult,
  AssetProperty
} from '../../shared/types'

// ─── Types ──────────────────────────────────────────────────────────────────

interface WiringNode {
  id: string
  label: string
  deviceType: string
  filePath: string
  x: number
  y: number
  vx: number
  vy: number
  channels: Map<string, string> // propertyName -> channelValue
  pinned: boolean
}

interface WiringEdge {
  source: string // node id
  target: string // node id
  channel: string
  sourceProperty: string
  targetProperty: string
}

interface ChannelGroup {
  channel: string
  devices: { nodeId: string; property: string }[]
}

interface DeviceWiringPageProps {
  selectedLevel?: string | null
  onNavigate?: (page: string) => void
}

// ─── Constants ──────────────────────────────────────────────────────────────

// UEFN Creative wiring: devices use *WhenReceived (input) and *Trigger (output) properties.
// The values are internal export indices, not shared channel strings.
// We detect connections by matching Trigger outputs to WhenReceived inputs
// that reference the same Verse function components.
const CHANNEL_OUTPUT_PATTERNS = [
  /Trigger$/,                    // "WhenItemGrantedTrigger", "OnClearedTrigger"
  /OnSuccess$/,
  /OnComplete$/,
  /OnFailure$/,
  /OnLoaded$/,
  /OnSaved$/,
  /OnCleared$/,
]

const CHANNEL_INPUT_PATTERNS = [
  /WhenReceived$/,               // "EnableWhenReceived", "DisableWhenReceived"
  /WhenReceivingFrom$/,
  /OnReceivedFrom$/,
]

// Combined
const CHANNEL_PROPERTY_PATTERNS = [
  ...CHANNEL_OUTPUT_PATTERNS,
  ...CHANNEL_INPUT_PATTERNS,
]

const TYPE_COLORS: Record<string, string> = {
  'Button': '#3d85e0',
  'Trigger': '#60aa3a',
  'Mutator Zone': '#c76b29',
  'Item Spawner': '#a34ee1',
  'Vending Machine': '#c4a23c',
  'Damage Volume': '#e05555',
  'Timer': '#76d6e3',
  'Scoreboard': '#e0a03d',
  'HUD Message': '#55b8e0',
  'Tracker': '#e07055',
  'Conditional Button': '#7055e0',
  'Teleporter': '#55e0a0',
  'Barrier': '#e05590',
  'Elimination Manager': '#e04040',
  'Team Settings': '#40a0e0',
  'Player Spawner': '#40e060',
  'Creature Spawner': '#e0a040',
  'Billboard': '#a0a040',
  'Radio': '#40a0a0',
  'Explosive': '#e06040',
}

function getTypeColor(deviceType: string): string {
  // Try exact match first
  if (TYPE_COLORS[deviceType]) return TYPE_COLORS[deviceType]
  // Try partial match
  for (const [key, color] of Object.entries(TYPE_COLORS)) {
    if (deviceType.toLowerCase().includes(key.toLowerCase())) return color
  }
  // Hash-based fallback
  let hash = 0
  for (let i = 0; i < deviceType.length; i++) {
    hash = deviceType.charCodeAt(i) + ((hash << 5) - hash)
  }
  const hue = Math.abs(hash) % 360
  return `hsl(${hue}, 55%, 55%)`
}

function isChannelProperty(name: string): boolean {
  return CHANNEL_PROPERTY_PATTERNS.some(p => p.test(name))
}

function isNonEmptyChannel(value: string): boolean {
  if (!value || value === '' || value === 'None' || value === '(none)' || value === 'null') return false
  if (value === '0' || value === 'false' || value === 'False' || value === 'true' || value === 'True') return false
  if (value.startsWith('(') && value.endsWith(')')) return false
  if (value.includes('/') && value.includes('.')) return false
  if (value.includes('UAssetAPI')) return false
  if (value.includes('System.Byte')) return false
  if (value.length < 1) return false
  // Export index references ARE meaningful — they indicate the device has this wiring configured
  return true
}

// ─── Grid Layout by Type ─────────────────────────────────────────────────────
// Groups devices by type and arranges them in a clean grid.
// Much faster and more readable than force-directed layout.

function runForceLayout(
  nodes: WiringNode[],
  edges: WiringEdge[],
  width: number,
  height: number,
  _iterations: number
): void {
  // Group nodes by device type
  const groups = new Map<string, WiringNode[]>()
  for (const node of nodes) {
    if (node.pinned) continue
    const type = node.deviceType || 'Unknown'
    if (!groups.has(type)) groups.set(type, [])
    groups.get(type)!.push(node)
  }

  // Sort groups by size (largest first)
  const sortedGroups = Array.from(groups.entries()).sort((a, b) => b[1].length - a[1].length)

  const NODE_GAP_X = 220
  const NODE_GAP_Y = 70
  const GROUP_GAP = 80
  const COLS_PER_GROUP = 4  // max items per row in a group

  let cursorY = 50

  for (const [_type, groupNodes] of sortedGroups) {
    const rows = Math.ceil(groupNodes.length / COLS_PER_GROUP)
    const cols = Math.min(groupNodes.length, COLS_PER_GROUP)
    const groupWidth = cols * NODE_GAP_X
    const startX = (width - groupWidth) / 2

    for (let i = 0; i < groupNodes.length; i++) {
      const col = i % COLS_PER_GROUP
      const row = Math.floor(i / COLS_PER_GROUP)
      groupNodes[i].x = startX + col * NODE_GAP_X + NODE_GAP_X / 2
      groupNodes[i].y = cursorY + row * NODE_GAP_Y + NODE_GAP_Y / 2
      groupNodes[i].vx = 0
      groupNodes[i].vy = 0
    }

    cursorY += rows * NODE_GAP_Y + GROUP_GAP
  }

  void edges; // used by signature
}

// ─── Component ──────────────────────────────────────────────────────────────

export function DeviceWiringPage({ selectedLevel: selectedLevelProp, onNavigate }: DeviceWiringPageProps) {
  const [levels, setLevels] = useState<ForgeLevel[]>([])
  const [activeLevelPath, setActiveLevelPath] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [scanning, setScanning] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Graph data
  const [nodes, setNodes] = useState<WiringNode[]>([])
  const [edges, setEdges] = useState<WiringEdge[]>([])
  const [channelGroups, setChannelGroups] = useState<ChannelGroup[]>([])

  // Interaction state
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)
  const [hoveredNodeId, setHoveredNodeId] = useState<string | null>(null)
  const [selectedNodeDetail, setSelectedNodeDetail] = useState<DeviceInspectResult | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)

  // SVG viewport
  const [viewBox, setViewBox] = useState({ x: 0, y: 0, w: 1200, h: 800 })
  const [isPanning, setIsPanning] = useState(false)
  const [panStart, setPanStart] = useState({ x: 0, y: 0 })
  const [dragNode, setDragNode] = useState<string | null>(null)
  const [dragOffset, setDragOffset] = useState({ x: 0, y: 0 })
  const svgRef = useRef<SVGSVGElement>(null)
  const containerRef = useRef<HTMLDivElement>(null)

  // Scan progress
  const [scanProgress, setScanProgress] = useState({ current: 0, total: 0 })

  // Load levels on mount
  useEffect(() => {
    loadLevels()
  }, [])

  useEffect(() => {
    if (selectedLevelProp) {
      setActiveLevelPath(selectedLevelProp)
    }
  }, [selectedLevelProp])

  async function loadLevels() {
    try {
      setLoading(true)
      setError(null)
      const result = await window.electronAPI.forgeListLevels()
      setLevels(result ?? [])
      if (selectedLevelProp) {
        setActiveLevelPath(selectedLevelProp)
      } else if (result && result.length > 0) {
        setActiveLevelPath(result[0].filePath)
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load levels')
    } finally {
      setLoading(false)
    }
  }

  async function scanConnections() {
    if (!activeLevelPath) return

    try {
      setScanning(true)
      setError(null)
      setNodes([])
      setEdges([])
      setChannelGroups([])
      setSelectedNodeId(null)
      setSelectedNodeDetail(null)

      // Step 1: Get all devices
      const deviceResult: DeviceListResult = await window.electronAPI.forgeListDevices(activeLevelPath)
      const devices = deviceResult?.devices ?? []
      if (devices.length === 0) {
        setError('No devices found in this level')
        return
      }

      // Step 2: Group by type, pick up to 1 sample per type for inspection
      // But we actually need to inspect ALL devices to find channel values
      // For large levels, we batch the inspections
      const inspectedDevices: { device: DeviceEntry; props: AssetProperty[] }[] = []
      setScanProgress({ current: 0, total: devices.length })

      // Inspect in batches of 5
      const BATCH_SIZE = 5
      for (let i = 0; i < devices.length; i += BATCH_SIZE) {
        const batch = devices.slice(i, i + BATCH_SIZE)
        const results = await Promise.all(
          batch.map(async (device) => {
            try {
              const result = await window.electronAPI.forgeInspectDevice(device.filePath)
              return { device, props: result?.properties ?? [] }
            } catch {
              return { device, props: [] }
            }
          })
        )
        inspectedDevices.push(...results)
        setScanProgress({ current: Math.min(i + BATCH_SIZE, devices.length), total: devices.length })
      }

      // Step 3: Build nodes with channel info
      const svgW = 1200
      const svgH = 800
      const newNodes: WiringNode[] = inspectedDevices.map((item, idx) => {
        const channels = new Map<string, string>()
        for (const prop of item.props) {
          if (isChannelProperty(prop.name) && isNonEmptyChannel(prop.value)) {
            channels.set(prop.name, prop.value)
          }
        }

        // Initial position: spread in a circle
        const angle = (2 * Math.PI * idx) / inspectedDevices.length
        const radius = Math.min(svgW, svgH) * 0.3
        return {
          id: item.device.filePath,
          label: item.device.name,
          deviceType: item.device.deviceType,
          filePath: item.device.filePath,
          x: svgW / 2 + Math.cos(angle) * radius,
          y: svgH / 2 + Math.sin(angle) * radius,
          vx: 0,
          vy: 0,
          channels,
          pinned: false
        }
      })

      // Step 4: Build wiring graph
      // UEFN stores wiring as internal export references — not shared channel strings.
      // We identify devices that have OUTPUTS (Trigger properties) and INPUTS (WhenReceived)
      // and show which devices are "wired" (have active signal properties configured).

      const newEdges: WiringEdge[] = []
      const newChannelGroups: ChannelGroup[] = []
      const edgeSet = new Set<string>()

      // Collect devices with outputs and inputs
      const outputDevices: { nodeId: string; property: string; value: string }[] = []
      const inputDevices: { nodeId: string; property: string; value: string }[] = []

      for (const node of newNodes) {
        for (const [propName, value] of node.channels) {
          if (CHANNEL_OUTPUT_PATTERNS.some(p => p.test(propName))) {
            outputDevices.push({ nodeId: node.id, property: propName, value })
          }
          if (CHANNEL_INPUT_PATTERNS.some(p => p.test(propName))) {
            inputDevices.push({ nodeId: node.id, property: propName, value })
          }
        }
      }

      // Group by the export index value — within a single actor file,
      // a Trigger and WhenReceived sharing the same export index are wired.
      // Across files, we match by property name patterns (e.g., Enable/Disable pairs).

      // Strategy: connect output devices to input devices of DIFFERENT types
      // (a Trigger device's output likely connects to another device's input)
      const outputsByNode = new Map<string, typeof outputDevices>()
      for (const o of outputDevices) {
        if (!outputsByNode.has(o.nodeId)) outputsByNode.set(o.nodeId, [])
        outputsByNode.get(o.nodeId)!.push(o)
      }
      const inputsByNode = new Map<string, typeof inputDevices>()
      for (const i of inputDevices) {
        if (!inputsByNode.has(i.nodeId)) inputsByNode.set(i.nodeId, [])
        inputsByNode.get(i.nodeId)!.push(i)
      }

      // Show all wired devices as a group — connect outputs to inputs
      // of the same action type (Enable→EnableWhenReceived, etc.)
      const actionPairs: [RegExp, RegExp, string][] = [
        [/Enable/, /EnableWhenReceiv/, 'Enable'],
        [/Disable/, /DisableWhenReceiv/, 'Disable'],
        [/Trigger/, /TriggerWhenReceiv/, 'Trigger'],
        [/Grant/, /GrantItemWhenReceiv/, 'GrantItem'],
        [/Reset/, /ResetWhenReceiv/, 'Reset'],
        [/Start/, /StartWhenReceiv/, 'Start'],
        [/Pause/, /PauseWhenReceiv/, 'Pause'],
        [/Resume/, /ResumeWhenReceiv/, 'Resume'],
        [/Show/, /ShowWhenReceiv/, 'Show'],
        [/Hide/, /HideWhenReceiv/, 'Hide'],
        [/Spawn/, /SpawnOnReceiv/, 'Spawn'],
        [/Despawn/, /DespawnOnReceiv/, 'Despawn'],
      ]

      // For each output device, find input devices that could be connected
      for (const [srcId, outs] of outputsByNode) {
        for (const [tgtId, ins] of inputsByNode) {
          if (srcId === tgtId) continue
          for (const out of outs) {
            for (const inp of ins) {
              // Check if this output→input pair makes sense
              for (const [_outPat, inPat, label] of actionPairs) {
                if (inPat.test(inp.property)) {
                  const key = `${srcId}|${tgtId}|${label}`
                  if (!edgeSet.has(key)) {
                    edgeSet.add(key)
                    newEdges.push({
                      source: srcId,
                      target: tgtId,
                      channel: label,
                      sourceProperty: out.property,
                      targetProperty: inp.property
                    })
                  }
                  break
                }
              }
            }
          }
        }
      }

      // Build channel groups for the sidebar
      const groupMap = new Map<string, { nodeId: string; property: string }[]>()
      for (const edge of newEdges) {
        if (!groupMap.has(edge.channel)) groupMap.set(edge.channel, [])
        const g = groupMap.get(edge.channel)!
        if (!g.some(m => m.nodeId === edge.source)) g.push({ nodeId: edge.source, property: edge.sourceProperty })
        if (!g.some(m => m.nodeId === edge.target)) g.push({ nodeId: edge.target, property: edge.targetProperty })
      }
      for (const [channel, devices] of groupMap) {
        newChannelGroups.push({ channel, devices })
      }

      // Step 5: Only show connected nodes (nodes with at least one edge)
      const connectedNodeIds = new Set<string>()
      for (const edge of newEdges) {
        connectedNodeIds.add(edge.source)
        connectedNodeIds.add(edge.target)
      }
      const connectedNodes = newNodes.filter(n => connectedNodeIds.has(n.id))

      // Re-layout only connected nodes
      if (connectedNodes.length > 0) {
        // Re-distribute in circle
        connectedNodes.forEach((node, idx) => {
          const angle = (2 * Math.PI * idx) / connectedNodes.length
          const radius = Math.min(svgW, svgH) * 0.3
          node.x = svgW / 2 + Math.cos(angle) * radius
          node.y = svgH / 2 + Math.sin(angle) * radius
          node.vx = 0
          node.vy = 0
        })
        runForceLayout(connectedNodes, newEdges, svgW, svgH, 150)
      }

      setNodes(connectedNodes)
      setEdges(newEdges)
      setChannelGroups(newChannelGroups)
      setViewBox({ x: 0, y: 0, w: svgW, h: svgH })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Scan failed')
    } finally {
      setScanning(false)
    }
  }

  // Inspect selected node for detail panel
  useEffect(() => {
    if (!selectedNodeId) {
      setSelectedNodeDetail(null)
      return
    }
    let cancelled = false
    ;(async () => {
      try {
        setDetailLoading(true)
        const result = await window.electronAPI.forgeInspectDevice(selectedNodeId)
        if (!cancelled) setSelectedNodeDetail(result)
      } catch {
        if (!cancelled) setSelectedNodeDetail(null)
      } finally {
        if (!cancelled) setDetailLoading(false)
      }
    })()
    return () => { cancelled = true }
  }, [selectedNodeId])

  // Connected nodes/edges for highlight
  const highlightTarget = hoveredNodeId ?? selectedNodeId
  const connectedEdges = useMemo(() => {
    if (!highlightTarget) return new Set<number>()
    const set = new Set<number>()
    edges.forEach((e, i) => {
      if (e.source === highlightTarget || e.target === highlightTarget) set.add(i)
    })
    return set
  }, [highlightTarget, edges])

  const connectedNodeIds = useMemo(() => {
    if (!highlightTarget) return new Set<string>()
    const set = new Set<string>([highlightTarget])
    for (const e of edges) {
      if (e.source === highlightTarget) set.add(e.target)
      if (e.target === highlightTarget) set.add(e.source)
    }
    return set
  }, [highlightTarget, edges])

  // SVG coordinate transform
  const svgPoint = useCallback((clientX: number, clientY: number) => {
    const svg = svgRef.current
    if (!svg) return { x: 0, y: 0 }
    const rect = svg.getBoundingClientRect()
    return {
      x: viewBox.x + ((clientX - rect.left) / rect.width) * viewBox.w,
      y: viewBox.y + ((clientY - rect.top) / rect.height) * viewBox.h
    }
  }, [viewBox])

  // Mouse handlers for pan and drag
  const handleMouseDown = useCallback((e: React.MouseEvent<SVGSVGElement>) => {
    if (dragNode) return
    // Only pan on middle click or left click on background
    if (e.button === 1 || (e.button === 0 && (e.target as SVGElement).tagName === 'svg')) {
      setIsPanning(true)
      setPanStart({ x: e.clientX, y: e.clientY })
      e.preventDefault()
    }
  }, [dragNode])

  const handleMouseMove = useCallback((e: React.MouseEvent<SVGSVGElement>) => {
    if (isPanning) {
      const svg = svgRef.current
      if (!svg) return
      const rect = svg.getBoundingClientRect()
      const dx = ((e.clientX - panStart.x) / rect.width) * viewBox.w
      const dy = ((e.clientY - panStart.y) / rect.height) * viewBox.h
      setViewBox(v => ({ ...v, x: v.x - dx, y: v.y - dy }))
      setPanStart({ x: e.clientX, y: e.clientY })
    } else if (dragNode) {
      const pt = svgPoint(e.clientX, e.clientY)
      setNodes(prev => prev.map(n =>
        n.id === dragNode
          ? { ...n, x: pt.x - dragOffset.x, y: pt.y - dragOffset.y, pinned: true }
          : n
      ))
    }
  }, [isPanning, panStart, viewBox, dragNode, dragOffset, svgPoint])

  const handleMouseUp = useCallback(() => {
    setIsPanning(false)
    setDragNode(null)
  }, [])

  const handleWheel = useCallback((e: React.WheelEvent<SVGSVGElement>) => {
    e.preventDefault()
    const scaleFactor = e.deltaY > 0 ? 1.1 : 0.9
    const svg = svgRef.current
    if (!svg) return
    const rect = svg.getBoundingClientRect()

    // Zoom toward cursor
    const mx = ((e.clientX - rect.left) / rect.width)
    const my = ((e.clientY - rect.top) / rect.height)

    setViewBox(v => {
      const newW = v.w * scaleFactor
      const newH = v.h * scaleFactor
      return {
        x: v.x + (v.w - newW) * mx,
        y: v.y + (v.h - newH) * my,
        w: newW,
        h: newH
      }
    })
  }, [])

  const handleNodeMouseDown = useCallback((nodeId: string, e: React.MouseEvent) => {
    e.stopPropagation()
    const pt = svgPoint(e.clientX, e.clientY)
    const node = nodes.find(n => n.id === nodeId)
    if (node) {
      setDragNode(nodeId)
      setDragOffset({ x: pt.x - node.x, y: pt.y - node.y })
    }
  }, [nodes, svgPoint])

  const handleNodeClick = useCallback((nodeId: string) => {
    setSelectedNodeId(prev => prev === nodeId ? null : nodeId)
  }, [])

  // Node size
  const NODE_W = 140
  const NODE_H = 44

  // Stats
  const stats = useMemo(() => ({
    devices: nodes.length,
    connections: edges.length,
    channels: channelGroups.length
  }), [nodes, edges, channelGroups])

  // Selected node's connections for detail panel
  const selectedNodeConnections = useMemo(() => {
    if (!selectedNodeId) return []
    return edges
      .filter(e => e.source === selectedNodeId || e.target === selectedNodeId)
      .map(e => {
        const otherId = e.source === selectedNodeId ? e.target : e.source
        const otherNode = nodes.find(n => n.id === otherId)
        return {
          deviceName: otherNode?.label ?? 'Unknown',
          deviceType: otherNode?.deviceType ?? 'Unknown',
          channel: e.channel,
          myProperty: e.source === selectedNodeId ? e.sourceProperty : e.targetProperty,
          theirProperty: e.source === selectedNodeId ? e.targetProperty : e.sourceProperty
        }
      })
  }, [selectedNodeId, edges, nodes])

  const selectedNodeData = useMemo(() => nodes.find(n => n.id === selectedNodeId), [selectedNodeId, nodes])

  // ─── Renders ──────────────────────────────────────────────────────────────

  if (loading) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-[11px] text-gray-400">Loading levels...</div>
      </div>
    )
  }

  if (error && levels.length === 0) {
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
        <div className="bg-fn-panel border border-fn-border rounded-lg p-8 text-center max-w-md">
          <svg className="w-10 h-10 mx-auto mb-3 text-gray-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path d="M3.055 11H5a2 2 0 012 2v1a2 2 0 002 2 2 2 0 012 2v2.945M8 3.935V5.5A2.5 2.5 0 0010.5 8h.5a2 2 0 012 2 2 2 0 104 0 2 2 0 012-2h1.064M15 20.488V18a2 2 0 012-2h3.064M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          <p className="text-[12px] text-gray-300 font-medium mb-1">Select a level first</p>
          <p className="text-[10px] text-gray-500 mb-3">
            Device wiring requires a level with placed devices.
          </p>
          {onNavigate && (
            <button
              onClick={() => onNavigate('levels')}
              className="px-3 py-1.5 text-[10px] font-medium text-fn-rare bg-fn-rare/10 border border-fn-rare/20 rounded hover:bg-fn-rare/20 transition-colors"
            >
              Go to Levels
            </button>
          )}
        </div>
      </div>
    )
  }

  return (
    <div className="flex-1 flex flex-col bg-fn-darker overflow-hidden min-h-0">
      {/* Top Bar */}
      <div className="px-4 py-3 border-b border-fn-border bg-fn-dark flex items-center gap-4 shrink-0">
        {/* Level selector */}
        <div className="flex items-center gap-2">
          <label className="text-[9px] font-semibold text-gray-600 uppercase tracking-wider">Level</label>
          <select
            value={activeLevelPath ?? ''}
            onChange={(e) => {
              setActiveLevelPath(e.target.value)
              setNodes([])
              setEdges([])
              setChannelGroups([])
              setSelectedNodeId(null)
              setSelectedNodeDetail(null)
            }}
            className="bg-fn-darker border border-fn-border rounded px-2 py-1.5 text-[11px] text-white focus:outline-none focus:border-fn-rare/50 min-w-[200px]"
          >
            {levels.map((l) => (
              <option key={l.filePath} value={l.filePath}>{l.name}</option>
            ))}
          </select>
        </div>

        {/* Scan button */}
        <button
          onClick={scanConnections}
          disabled={scanning || !activeLevelPath}
          className={`flex items-center gap-2 px-3 py-1.5 text-[11px] font-medium rounded transition-colors ${
            scanning
              ? 'bg-fn-rare/20 text-fn-rare/60 cursor-wait'
              : 'bg-fn-rare/10 text-fn-rare border border-fn-rare/20 hover:bg-fn-rare/20'
          }`}
        >
          {scanning ? (
            <>
              <svg className="w-3.5 h-3.5 animate-spin" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
              </svg>
              Scanning... ({scanProgress.current}/{scanProgress.total})
            </>
          ) : (
            <>
              <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path d="M13 10V3L4 14h7v7l9-11h-7z" />
              </svg>
              Scan Connections
            </>
          )}
        </button>

        {/* Stats */}
        {nodes.length > 0 && (
          <div className="flex items-center gap-4 ml-auto text-[10px]">
            <div className="flex items-center gap-1.5">
              <span className="w-2 h-2 rounded-full bg-fn-rare" />
              <span className="text-gray-400">{stats.devices} device{stats.devices !== 1 ? 's' : ''}</span>
            </div>
            <div className="flex items-center gap-1.5">
              <span className="w-2 h-2 rounded-full bg-emerald-400" />
              <span className="text-gray-400">{stats.connections} connection{stats.connections !== 1 ? 's' : ''}</span>
            </div>
            <div className="flex items-center gap-1.5">
              <span className="w-2 h-2 rounded-full bg-amber-400" />
              <span className="text-gray-400">{stats.channels} channel{stats.channels !== 1 ? 's' : ''}</span>
            </div>
          </div>
        )}
      </div>

      {/* Error banner */}
      {error && nodes.length === 0 && !scanning && (
        <div className="px-4 py-2">
          <ErrorMessage message={error} />
        </div>
      )}

      {/* Main area */}
      <div className="flex-1 flex overflow-hidden">
        {/* Graph area */}
        <div
          ref={containerRef}
          className="flex-1 relative overflow-hidden"
          style={{ cursor: isPanning ? 'grabbing' : dragNode ? 'grabbing' : 'default' }}
        >
          {nodes.length === 0 && !scanning ? (
            <div className="absolute inset-0 flex items-center justify-center">
              <div className="text-center">
                <svg className="w-16 h-16 mx-auto mb-4 text-gray-700" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
                  <circle cx="6" cy="6" r="2.5" />
                  <circle cx="18" cy="6" r="2.5" />
                  <circle cx="12" cy="18" r="2.5" />
                  <line x1="8" y1="7" x2="16" y2="7" strokeDasharray="3 3" />
                  <line x1="7" y1="8" x2="11" y2="16" strokeDasharray="3 3" />
                  <line x1="17" y1="8" x2="13" y2="16" strokeDasharray="3 3" />
                </svg>
                <p className="text-[12px] text-gray-400 font-medium mb-1">Device Wiring Visualizer</p>
                <p className="text-[10px] text-gray-600 max-w-xs mx-auto">
                  Select a level and click "Scan Connections" to discover how devices communicate via channels.
                </p>
              </div>
            </div>
          ) : (
            <svg
              ref={svgRef}
              className="w-full h-full"
              viewBox={`${viewBox.x} ${viewBox.y} ${viewBox.w} ${viewBox.h}`}
              onMouseDown={handleMouseDown}
              onMouseMove={handleMouseMove}
              onMouseUp={handleMouseUp}
              onMouseLeave={handleMouseUp}
              onWheel={handleWheel}
            >
              <defs>
                {/* Arrow marker for edges */}
                <marker
                  id="arrowhead"
                  markerWidth="8"
                  markerHeight="6"
                  refX="7"
                  refY="3"
                  orient="auto"
                >
                  <polygon points="0 0, 8 3, 0 6" fill="#555" />
                </marker>
              </defs>

              {/* Edges — only render edges connected to selected/hovered node for performance */}
              {edges.map((edge, idx) => {
                // PERFORMANCE: Only render edges that connect to the highlighted node
                // With 71K+ edges, rendering all of them crashes the browser
                if (!connectedEdges.has(idx)) return null

                const source = nodes.find(n => n.id === edge.source)
                const target = nodes.find(n => n.id === edge.target)
                if (!source || !target) return null

                const x1 = source.x
                const y1 = source.y
                const x2 = target.x
                const y2 = target.y

                // Curved path for better readability
                const dx = x2 - x1
                const dy = y2 - y1
                const cx1 = x1 + dx * 0.3
                const cy1 = y1
                const cx2 = x1 + dx * 0.7
                const cy2 = y2

                return (
                  <g key={idx}>
                    <path
                      d={`M ${x1} ${y1} C ${cx1} ${cy1}, ${cx2} ${cy2}, ${x2} ${y2}`}
                      stroke="#60aa3a"
                      strokeWidth={2}
                      fill="none"
                      strokeOpacity={0.8}
                    />
                    {/* Channel label at midpoint */}
                    <text
                      x={(x1 + x2) / 2} y={(y1 + y2) / 2 - 6}
                      textAnchor="middle"
                      fill="#8fdf6a"
                      fontSize={9}
                      fontFamily="system-ui"
                      fontWeight="500"
                    >
                      {edge.channel.length > 15 ? edge.channel.slice(0, 15) + '...' : edge.channel}
                    </text>
                  </g>
                )
              })}

              {/* Group labels */}
              {(() => {
                const groups = new Map<string, { minX: number; minY: number; maxX: number; count: number }>()
                for (const node of nodes) {
                  const type = node.deviceType || 'Unknown'
                  const g = groups.get(type)
                  if (!g) {
                    groups.set(type, { minX: node.x, minY: node.y, maxX: node.x, count: 1 })
                  } else {
                    g.minX = Math.min(g.minX, node.x)
                    g.minY = Math.min(g.minY, node.y)
                    g.maxX = Math.max(g.maxX, node.x)
                    g.count++
                  }
                }
                return Array.from(groups.entries()).map(([type, g]) => {
                  const prettyType = type.replace(/_/g, ' ').replace(/V\d+$/, '').replace(/Placed$/, '').trim()
                  return (
                    <text
                      key={`label-${type}`}
                      x={g.minX - NODE_W / 2}
                      y={g.minY - NODE_H / 2 - 10}
                      fill={getTypeColor(type)}
                      fontSize={12}
                      fontFamily="system-ui"
                      fontWeight="600"
                      opacity={0.7}
                    >
                      {prettyType} ({g.count})
                    </text>
                  )
                })
              })()}

              {/* Nodes */}
              {nodes.map((node) => {
                const isSelected = node.id === selectedNodeId
                const isHovered = node.id === hoveredNodeId
                const isConnected = connectedNodeIds.has(node.id)
                const isDimmed = highlightTarget && !isConnected
                const color = getTypeColor(node.deviceType)
                const channelCount = node.channels.size

                return (
                  <g
                    key={node.id}
                    transform={`translate(${node.x - NODE_W / 2}, ${node.y - NODE_H / 2})`}
                    style={{
                      cursor: 'pointer',
                      opacity: isDimmed ? 0.2 : 1,
                      transition: 'opacity 0.2s'
                    }}
                    onMouseDown={(e) => handleNodeMouseDown(node.id, e)}
                    onMouseUp={() => {
                      if (dragNode === node.id) {
                        // Only fire click if we didn't drag far
                        handleNodeClick(node.id)
                      }
                      setDragNode(null)
                    }}
                    onMouseEnter={() => setHoveredNodeId(node.id)}
                    onMouseLeave={() => setHoveredNodeId(null)}
                  >
                    {/* Node background */}
                    <rect
                      width={NODE_W}
                      height={NODE_H}
                      rx={6}
                      fill="#1e1e2e"
                      stroke={isSelected ? '#fff' : isHovered ? color : `${color}66`}
                      strokeWidth={isSelected ? 2 : isHovered ? 1.5 : 1}
                    />
                    {/* Color accent bar */}
                    <rect
                      x={0} y={0}
                      width={4} height={NODE_H}
                      rx={2}
                      fill={color}
                    />
                    {/* Device name — cleaned up */}
                    <text
                      x={12} y={17}
                      fill="#e0e0e0"
                      fontSize={10}
                      fontFamily="system-ui"
                      fontWeight="500"
                    >
                      {(() => {
                        let name = node.label
                        // Strip UAID hash
                        if (name.includes('_UAID_')) name = name.split('_UAID_')[0]
                        // Clean prefixes
                        name = name.replace(/^BP_|^PBWA_|^Device_/, '').replace(/_C$/, '').replace(/_/g, ' ').trim()
                        return name.length > 20 ? name.slice(0, 20) + '...' : name
                      })()}
                    </text>
                    {/* Device type badge */}
                    <text
                      x={12} y={33}
                      fill={color}
                      fontSize={8}
                      fontFamily="system-ui"
                    >
                      {node.deviceType.replace(/_/g, ' ').replace(/V\d+$/, '').trim().slice(0, 22)}
                    </text>
                    {/* Channel count badge */}
                    {channelCount > 0 && (
                      <>
                        <rect
                          x={NODE_W - 24} y={4}
                          width={18} height={14}
                          rx={7}
                          fill={`${color}33`}
                        />
                        <text
                          x={NODE_W - 15} y={14}
                          textAnchor="middle"
                          fill={color}
                          fontSize={8}
                          fontFamily="system-ui"
                          fontWeight="600"
                        >
                          {channelCount}
                        </text>
                      </>
                    )}
                    {/* Selection ring */}
                    {isSelected && (
                      <rect
                        x={-3} y={-3}
                        width={NODE_W + 6} height={NODE_H + 6}
                        rx={9}
                        fill="none"
                        stroke={color}
                        strokeWidth={1}
                        strokeDasharray="4 2"
                        opacity={0.6}
                      />
                    )}
                  </g>
                )
              })}
            </svg>
          )}

          {/* Zoom controls */}
          {nodes.length > 0 && (
            <div className="absolute bottom-4 left-4 flex flex-col gap-1">
              <button
                onClick={() => setViewBox(v => ({
                  x: v.x + v.w * 0.05,
                  y: v.y + v.h * 0.05,
                  w: v.w * 0.9,
                  h: v.h * 0.9
                }))}
                className="w-7 h-7 bg-fn-dark border border-fn-border rounded flex items-center justify-center text-gray-400 hover:text-white hover:bg-white/[0.05] transition-colors"
                title="Zoom in"
              >
                <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path d="M12 4v16m8-8H4" />
                </svg>
              </button>
              <button
                onClick={() => setViewBox(v => ({
                  x: v.x - v.w * 0.05,
                  y: v.y - v.h * 0.05,
                  w: v.w * 1.1,
                  h: v.h * 1.1
                }))}
                className="w-7 h-7 bg-fn-dark border border-fn-border rounded flex items-center justify-center text-gray-400 hover:text-white hover:bg-white/[0.05] transition-colors"
                title="Zoom out"
              >
                <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path d="M20 12H4" />
                </svg>
              </button>
              <button
                onClick={() => setViewBox({ x: 0, y: 0, w: 1200, h: 800 })}
                className="w-7 h-7 bg-fn-dark border border-fn-border rounded flex items-center justify-center text-gray-400 hover:text-white hover:bg-white/[0.05] transition-colors"
                title="Reset view"
              >
                <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path d="M4 8V4m0 0h4M4 4l5 5m11-1V4m0 0h-4m4 0l-5 5M4 16v4m0 0h4m-4 0l5-5m11 5l-5-5m5 5v-4m0 4h-4" />
                </svg>
              </button>
            </div>
          )}
        </div>

        {/* Right Panel — Detail */}
        {selectedNodeId && (
          <div className="w-80 border-l border-fn-border bg-fn-dark overflow-y-auto shrink-0">
            {detailLoading ? (
              <div className="p-4 text-center text-[11px] text-gray-400">Loading...</div>
            ) : selectedNodeData ? (
              <div className="p-4 space-y-4">
                {/* Header */}
                <div>
                  <div className="flex items-start justify-between gap-2">
                    <h3 className="text-[13px] font-semibold text-white leading-tight">
                      {selectedNodeData.label}
                    </h3>
                    <button
                      onClick={() => setSelectedNodeId(null)}
                      className="text-gray-600 hover:text-gray-300 transition-colors shrink-0"
                    >
                      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                        <path d="M6 18L18 6M6 6l12 12" />
                      </svg>
                    </button>
                  </div>
                  <span
                    className="inline-block mt-1.5 px-1.5 py-0.5 rounded text-[9px] font-medium border"
                    style={{
                      color: getTypeColor(selectedNodeData.deviceType),
                      backgroundColor: `${getTypeColor(selectedNodeData.deviceType)}15`,
                      borderColor: `${getTypeColor(selectedNodeData.deviceType)}30`
                    }}
                  >
                    {selectedNodeData.deviceType}
                  </span>
                </div>

                {/* Channel Assignments */}
                {selectedNodeData.channels.size > 0 && (
                  <div>
                    <h4 className="text-[9px] font-semibold text-gray-600 uppercase tracking-wider mb-2">
                      Channel Assignments
                    </h4>
                    <div className="space-y-1">
                      {Array.from(selectedNodeData.channels.entries()).map(([prop, value]) => (
                        <div key={prop} className="flex items-center gap-2 bg-fn-darker rounded px-2 py-1.5 border border-fn-border/50">
                          <span className="w-1.5 h-1.5 rounded-full bg-amber-400 shrink-0" />
                          <div className="flex-1 min-w-0">
                            <div className="text-[10px] text-gray-400 truncate">{prop}</div>
                            <div className="text-[11px] text-amber-300 font-medium truncate">{value}</div>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {/* Connected Devices */}
                {selectedNodeConnections.length > 0 && (
                  <div>
                    <h4 className="text-[9px] font-semibold text-gray-600 uppercase tracking-wider mb-2">
                      Connected Devices ({selectedNodeConnections.length})
                    </h4>
                    <div className="space-y-1">
                      {selectedNodeConnections.map((conn, idx) => (
                        <div key={idx} className="bg-fn-darker rounded px-2 py-1.5 border border-fn-border/50">
                          <div className="flex items-center gap-2">
                            <span
                              className="w-1.5 h-1.5 rounded-full shrink-0"
                              style={{ backgroundColor: getTypeColor(conn.deviceType) }}
                            />
                            <span className="text-[11px] text-gray-200 truncate flex-1">{conn.deviceName}</span>
                          </div>
                          <div className="mt-1 pl-3.5 text-[9px] text-gray-500">
                            via <span className="text-emerald-400">{conn.channel}</span>
                            <span className="text-gray-600 ml-1">
                              ({conn.myProperty} &harr; {conn.theirProperty})
                            </span>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {/* All Properties */}
                {selectedNodeDetail && selectedNodeDetail.properties.length > 0 && (
                  <div>
                    <h4 className="text-[9px] font-semibold text-gray-600 uppercase tracking-wider mb-2">
                      All Properties ({selectedNodeDetail.properties.length})
                    </h4>
                    <div className="bg-fn-darker rounded border border-fn-border/50 divide-y divide-fn-border/30">
                      {selectedNodeDetail.properties.map((prop, idx) => {
                        const isChannel = isChannelProperty(prop.name) && isNonEmptyChannel(prop.value)
                        return (
                          <div key={idx} className="px-2 py-1.5">
                            <div className="flex items-center gap-1">
                              {isChannel && <span className="w-1 h-1 rounded-full bg-amber-400 shrink-0" />}
                              <span className="text-[10px] text-gray-400 truncate">{prop.name}</span>
                              <span className="text-[8px] text-gray-700 ml-auto shrink-0">{prop.type}</span>
                            </div>
                            <div className={`text-[10px] truncate mt-0.5 ${isChannel ? 'text-amber-300' : 'text-gray-300'}`}>
                              {prop.value || '(empty)'}
                            </div>
                          </div>
                        )
                      })}
                    </div>
                  </div>
                )}
              </div>
            ) : null}
          </div>
        )}
      </div>
    </div>
  )
}
