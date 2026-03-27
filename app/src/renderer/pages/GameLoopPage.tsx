import { useEffect, useState, useCallback, useRef, useMemo } from 'react'
import { ErrorMessage } from '../components/ErrorMessage'
import {
  forgeSimulateGameLoop,
  forgeSimulateEvent,
  forgeListLevels,
  forgeListDevices,
  type GameLoopResult,
  type SimulateEventResult,
  type DFANode,
  type DFAEdge,
  type DFASnapshot,
} from '../lib/api'
import type { WellVersedLevel, DeviceListResult } from '../../shared/types'
import type { PageId } from '../components/Sidebar/Sidebar'

// ─── Types ──────────────────────────────────────────────────────────────────

interface GameLoopPageProps {
  selectedLevel?: string | null
  onNavigate?: (page: PageId) => void
}

// ─── Constants ──────────────────────────────────────────────────────────────

const PHASE_COLORS: Record<string, string> = {
  Idle: '#6e7681',
  Active: '#3fb950',
  Running: '#58a6ff',
  Triggered: '#d29922',
  Completed: '#bc8cff',
  Disabled: '#f85149',
  Cooldown: '#f0883e',
}

const NODE_RADIUS = 24
const ARROW_ID = 'dfa-arrow'
const ARROW_ACTIVE_ID = 'dfa-arrow-active'
const ARROW_PULSE_ID = 'dfa-arrow-pulse'

function phaseColor(phase: string): string {
  return PHASE_COLORS[phase] ?? '#6e7681'
}

function deviceAbbr(node: DFANode): string {
  // Use first 3 characters of the device type, or name if type is empty
  const src = node.deviceType || node.deviceName
  return src.slice(0, 3).toUpperCase()
}

// Compute quadratic bezier midpoint for edge label placement
function quadMid(
  x1: number,
  y1: number,
  cx: number,
  cy: number,
  x2: number,
  y2: number,
): { x: number; y: number } {
  const t = 0.5
  const x = (1 - t) * (1 - t) * x1 + 2 * (1 - t) * t * cx + t * t * x2
  const y = (1 - t) * (1 - t) * y1 + 2 * (1 - t) * t * cy + t * t * y2
  return { x, y }
}

// Compute control point for a quadratic bezier curve between two nodes,
// offset perpendicular to the line so edges don't overlap
function computeEdgePath(
  x1: number,
  y1: number,
  x2: number,
  y2: number,
  _edgeIndex: number,
  _totalBetweenPair: number,
): { path: string; labelX: number; labelY: number; cx: number; cy: number } {
  const mx = (x1 + x2) / 2
  const my = (y1 + y2) / 2
  const dx = x2 - x1
  const dy = y2 - y1
  const len = Math.sqrt(dx * dx + dy * dy) || 1
  // Perpendicular offset
  const nx = -dy / len
  const ny = dx / len
  const offset = 30 + _edgeIndex * 20
  const cx = mx + nx * offset
  const cy = my + ny * offset

  // Shorten the line so the arrow starts/ends at the node edge, not center
  const r = NODE_RADIUS + 4
  const angleStart = Math.atan2(cy - y1, cx - x1)
  const angleEnd = Math.atan2(cy - y2, cx - x2)
  const sx = x1 + Math.cos(angleStart) * r
  const sy = y1 + Math.sin(angleStart) * r
  const ex = x2 + Math.cos(angleEnd) * r
  const ey = y2 + Math.sin(angleEnd) * r

  const mid = quadMid(sx, sy, cx, cy, ex, ey)
  return {
    path: `M ${sx} ${sy} Q ${cx} ${cy} ${ex} ${ey}`,
    labelX: mid.x,
    labelY: mid.y,
    cx,
    cy,
  }
}

// ─── Component ──────────────────────────────────────────────────────────────

export function GameLoopPage({ selectedLevel, onNavigate }: GameLoopPageProps) {
  const [levels, setLevels] = useState<WellVersedLevel[]>([])
  const [activeLevelPath, setActiveLevelPath] = useState<string | null>(selectedLevel ?? null)
  const [simResult, setSimResult] = useState<GameLoopResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Interactive state
  const [selectedNode, setSelectedNode] = useState<string | null>(null)
  const [hoveredNode, setHoveredNode] = useState<string | null>(null)

  // Playback state
  const [playing, setPlaying] = useState(false)
  const [stepIndex, setStepIndex] = useState(0)
  const [playbackSpeed, setPlaybackSpeed] = useState(1)
  const playIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null)

  // Custom simulation
  const [devices, setDevices] = useState<DeviceListResult | null>(null)
  const [customDevice, setCustomDevice] = useState('')
  const [customEvent, setCustomEvent] = useState('')
  const [customResult, setCustomResult] = useState<SimulateEventResult | null>(null)

  // SVG viewport
  const svgContainerRef = useRef<HTMLDivElement>(null)

  // ─── Data loading ─────────────────────────────────────────────────────────

  useEffect(() => {
    forgeListLevels()
      .then(setLevels)
      .catch(() => setLevels([]))
  }, [])

  useEffect(() => {
    if (!activeLevelPath) return
    forgeListDevices(activeLevelPath)
      .then(setDevices)
      .catch(() => setDevices(null))
  }, [activeLevelPath])

  const runSimulation = useCallback(async () => {
    if (!activeLevelPath) return
    setLoading(true)
    setError(null)
    setSimResult(null)
    setSelectedNode(null)
    setCustomResult(null)
    setStepIndex(0)
    setPlaying(false)
    try {
      const result = await forgeSimulateGameLoop(activeLevelPath)
      setSimResult(result)
    } catch (err) {
      setError(`Simulation failed: ${err}`)
    } finally {
      setLoading(false)
    }
  }, [activeLevelPath])

  useEffect(() => {
    if (activeLevelPath) runSimulation()
  }, [activeLevelPath]) // eslint-disable-line react-hooks/exhaustive-deps

  // ─── Resolved data (merge custom result if present) ───────────────────────

  const nodes: DFANode[] = customResult?.nodes ?? simResult?.nodes ?? []
  const edges: DFAEdge[] = customResult?.edges ?? simResult?.edges ?? []
  const history: DFASnapshot[] = customResult?.history ?? simResult?.history ?? []
  const warnings: string[] = customResult?.warnings ?? simResult?.warnings ?? []
  const totalSteps = history.length
  const currentSnapshot: DFASnapshot | null = history[stepIndex] ?? null

  // Build device phases from current snapshot (fallback to node default)
  const devicePhases = useMemo((): Record<string, string> => {
    if (currentSnapshot) return currentSnapshot.devicePhases
    const phases: Record<string, string> = {}
    for (const n of nodes) {
      phases[n.deviceName] = n.phase
    }
    return phases
  }, [currentSnapshot, nodes])

  // Build a lookup from deviceName -> DFANode
  const nodeMap = useMemo((): Map<string, DFANode> => {
    const m = new Map<string, DFANode>()
    for (const n of nodes) m.set(n.deviceName, n)
    return m
  }, [nodes])

  // Edge rendering info: group edges between same source-target pair
  const edgePaths = useMemo(() => {
    // Count edges per pair
    const pairCounts = new Map<string, number>()
    const pairIndex = new Map<string, number>()
    for (const e of edges) {
      const key = `${e.sourceDevice}|${e.targetDevice}`
      pairCounts.set(key, (pairCounts.get(key) ?? 0) + 1)
    }
    return edges.map((e, i) => {
      const key = `${e.sourceDevice}|${e.targetDevice}`
      const idx = pairIndex.get(key) ?? 0
      pairIndex.set(key, idx + 1)
      const total = pairCounts.get(key) ?? 1

      const src = nodeMap.get(e.sourceDevice)
      const tgt = nodeMap.get(e.targetDevice)
      if (!src || !tgt) return null

      const computed = computeEdgePath(src.x, src.y, tgt.x, tgt.y, idx, total)
      return { edge: e, index: i, ...computed }
    })
  }, [edges, nodeMap])

  // Determine which edge is "active" (just fired) in the current snapshot
  const activeEdgeIndex = useMemo((): number | null => {
    if (!currentSnapshot) return null
    const { firedEdgeSource, firedEdgeEvent } = currentSnapshot
    if (!firedEdgeSource || !firedEdgeEvent) return null
    const idx = edges.findIndex(
      (e) => e.sourceDevice === firedEdgeSource && e.event === firedEdgeEvent,
    )
    return idx >= 0 ? idx : null
  }, [currentSnapshot, edges])

  // ─── Playback controls ────────────────────────────────────────────────────

  useEffect(() => {
    if (playing && totalSteps > 0) {
      const interval = 1000 / playbackSpeed
      playIntervalRef.current = setInterval(() => {
        setStepIndex((prev) => {
          if (prev >= totalSteps - 1) {
            setPlaying(false)
            return prev
          }
          return prev + 1
        })
      }, interval)
    }
    return () => {
      if (playIntervalRef.current) clearInterval(playIntervalRef.current)
    }
  }, [playing, playbackSpeed, totalSteps])

  const handlePlay = () => {
    if (totalSteps === 0) return
    if (stepIndex >= totalSteps - 1) setStepIndex(0)
    setPlaying(true)
  }
  const handlePause = () => setPlaying(false)
  const handleStepForward = () => setStepIndex((prev) => Math.min(prev + 1, totalSteps - 1))
  const handleStepBack = () => setStepIndex((prev) => Math.max(prev - 1, 0))

  // ─── Custom simulation ───────────────────────────────────────────────────

  const runCustomSimulation = async () => {
    if (!activeLevelPath || !customDevice || !customEvent) return
    setPlaying(false)
    setStepIndex(0)
    try {
      const result = await forgeSimulateEvent(activeLevelPath, customDevice, customEvent)
      setCustomResult(result)
    } catch (err) {
      setError(`Custom simulation failed: ${err}`)
    }
  }

  // ─── Device names for custom simulation dropdown ──────────────────────────

  const deviceNames = useMemo(() => {
    const names: string[] = []
    if (devices?.devices) {
      for (const d of devices.devices) {
        if (d.name && !names.includes(d.name)) names.push(d.name)
      }
    }
    for (const n of nodes) {
      if (!names.includes(n.deviceName)) names.push(n.deviceName)
    }
    return names.sort()
  }, [devices, nodes])

  // Events available for the selected custom device
  const customDeviceEvents = useMemo((): string[] => {
    if (!customDevice) return []
    const node = nodeMap.get(customDevice)
    if (node) return node.events
    // Fallback: gather from edges
    const evts = new Set<string>()
    for (const e of edges) {
      if (e.sourceDevice === customDevice) evts.add(e.event)
    }
    return Array.from(evts).sort()
  }, [customDevice, nodeMap, edges])

  // ─── Selected node detail ─────────────────────────────────────────────────

  const selectedNodeData = selectedNode ? nodeMap.get(selectedNode) ?? null : null

  // ─── SVG bounds ───────────────────────────────────────────────────────────

  const svgBounds = useMemo(() => {
    if (nodes.length === 0) return { width: 800, height: 600 }
    let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity
    for (const n of nodes) {
      if (n.x < minX) minX = n.x
      if (n.x > maxX) maxX = n.x
      if (n.y < minY) minY = n.y
      if (n.y > maxY) maxY = n.y
    }
    const pad = 80
    return {
      width: Math.max(800, maxX - minX + pad * 2),
      height: Math.max(600, maxY - minY + pad * 2),
      offsetX: minX - pad,
      offsetY: minY - pad,
    }
  }, [nodes])

  // ─── Render ───────────────────────────────────────────────────────────────

  return (
    <div className="h-full flex flex-col bg-fn-darker text-gray-300 overflow-hidden">
      {/* ── Top bar ────────────────────────────────────────────────────────── */}
      <div className="shrink-0 px-4 py-2.5 border-b border-fn-border flex items-center gap-3">
        <h1 className="text-sm font-semibold text-white">DFA Simulator</h1>

        {/* Level selector */}
        <select
          className="text-[11px] bg-fn-dark border border-fn-border rounded px-2 py-1 text-gray-300 focus:border-fn-rare/50 focus:outline-none"
          value={activeLevelPath ?? ''}
          onChange={(e) => {
            setActiveLevelPath(e.target.value || null)
            setCustomResult(null)
          }}
        >
          <option value="">Select a level...</option>
          {levels.map((l) => (
            <option key={l.filePath} value={l.filePath}>
              {l.name}
            </option>
          ))}
        </select>

        <button
          className="text-[10px] px-2.5 py-1 rounded bg-fn-rare/20 text-fn-rare hover:bg-fn-rare/30 transition-colors disabled:opacity-40"
          onClick={runSimulation}
          disabled={!activeLevelPath || loading}
        >
          {loading ? 'Simulating...' : 'Run Simulation'}
        </button>

        {/* Playback controls */}
        {totalSteps > 0 && (
          <div className="flex items-center gap-1.5 ml-2">
            {playing ? (
              <button
                className="text-[10px] px-2 py-1 rounded bg-fn-panel border border-fn-border text-gray-400 hover:text-white hover:border-fn-rare/40 transition-colors"
                onClick={handlePause}
              >
                Pause
              </button>
            ) : (
              <button
                className="text-[10px] px-2 py-1 rounded bg-fn-rare/20 text-fn-rare hover:bg-fn-rare/30 transition-colors"
                onClick={handlePlay}
              >
                Play
              </button>
            )}
            <button
              className="text-[10px] px-1.5 py-1 rounded bg-fn-panel border border-fn-border text-gray-400 hover:text-white transition-colors disabled:opacity-30"
              onClick={handleStepBack}
              disabled={stepIndex <= 0}
            >
              &lt;
            </button>
            <button
              className="text-[10px] px-1.5 py-1 rounded bg-fn-panel border border-fn-border text-gray-400 hover:text-white transition-colors disabled:opacity-30"
              onClick={handleStepForward}
              disabled={stepIndex >= totalSteps - 1}
            >
              &gt;
            </button>
            <select
              className="text-[10px] bg-fn-dark border border-fn-border rounded px-1 py-0.5 text-gray-400"
              value={playbackSpeed}
              onChange={(e) => setPlaybackSpeed(Number(e.target.value))}
            >
              <option value={0.5}>0.5x</option>
              <option value={1}>1x</option>
              <option value={2}>2x</option>
              <option value={4}>4x</option>
            </select>
          </div>
        )}

        {/* Stats */}
        {(simResult || customResult) && (
          <span className="text-[10px] text-gray-500 ml-auto tabular-nums">
            {nodes.length} nodes | {edges.length} edges | {totalSteps} steps
            {simResult?.reachesEndGame && (
              <span className="ml-2 text-green-400">Reaches EndGame</span>
            )}
          </span>
        )}
      </div>

      {error && (
        <div className="shrink-0 px-4 py-2">
          <ErrorMessage message={error} />
        </div>
      )}

      {/* ── Main content ───────────────────────────────────────────────────── */}
      <div className="flex-1 flex min-h-0 overflow-hidden">
        {/* ── SVG Graph ──────────────────────────────────────────────────── */}
        <div ref={svgContainerRef} className="flex-1 min-w-0 overflow-auto">
          {nodes.length > 0 ? (
            <svg
              width={svgBounds.width}
              height={svgBounds.height}
              viewBox={`${svgBounds.offsetX ?? 0} ${svgBounds.offsetY ?? 0} ${svgBounds.width} ${svgBounds.height}`}
              className="mx-auto"
            >
              <defs>
                {/* Default arrow marker */}
                <marker
                  id={ARROW_ID}
                  markerWidth="10"
                  markerHeight="7"
                  refX="9"
                  refY="3.5"
                  orient="auto"
                >
                  <polygon points="0 0, 10 3.5, 0 7" fill="#4d5561" />
                </marker>
                {/* Active arrow marker */}
                <marker
                  id={ARROW_ACTIVE_ID}
                  markerWidth="10"
                  markerHeight="7"
                  refX="9"
                  refY="3.5"
                  orient="auto"
                >
                  <polygon points="0 0, 10 3.5, 0 7" fill="#58a6ff" />
                </marker>
                {/* Pulse (just fired) arrow marker */}
                <marker
                  id={ARROW_PULSE_ID}
                  markerWidth="10"
                  markerHeight="7"
                  refX="9"
                  refY="3.5"
                  orient="auto"
                >
                  <polygon points="0 0, 10 3.5, 0 7" fill="#3fb950" />
                </marker>
                {/* Glow filter for hovered nodes */}
                <filter id="node-glow" x="-50%" y="-50%" width="200%" height="200%">
                  <feGaussianBlur stdDeviation="4" result="blur" />
                  <feMerge>
                    <feMergeNode in="blur" />
                    <feMergeNode in="SourceGraphic" />
                  </feMerge>
                </filter>
              </defs>

              {/* ── Edges ─────────────────────────────────────────────────── */}
              {edgePaths.map((ep) => {
                if (!ep) return null
                const { edge, index, path, labelX, labelY } = ep
                const isActive = activeEdgeIndex === index
                const isSelected =
                  selectedNode !== null &&
                  (edge.sourceDevice === selectedNode || edge.targetDevice === selectedNode)

                let stroke = '#3d444d'
                let strokeWidth = 1.2
                let marker = `url(#${ARROW_ID})`
                let opacity = 0.6

                if (isActive) {
                  stroke = '#3fb950'
                  strokeWidth = 2.5
                  marker = `url(#${ARROW_PULSE_ID})`
                  opacity = 1
                } else if (isSelected) {
                  stroke = '#58a6ff'
                  strokeWidth = 2
                  marker = `url(#${ARROW_ACTIVE_ID})`
                  opacity = 0.9
                }

                return (
                  <g key={`edge-${index}`}>
                    <path
                      d={path}
                      fill="none"
                      stroke={stroke}
                      strokeWidth={strokeWidth}
                      strokeDasharray={edge.isConditional ? '6,4' : undefined}
                      markerEnd={marker}
                      opacity={opacity}
                      className={isActive ? 'animate-pulse' : ''}
                    />
                    {/* Edge event label */}
                    <text
                      x={labelX}
                      y={labelY - 4}
                      textAnchor="middle"
                      fill="#6e7681"
                      fontSize="8"
                      fontFamily="monospace"
                      className="select-none"
                    >
                      {edge.event}
                    </text>
                    {/* Edge action label (smaller, below) */}
                    {edge.action && (
                      <text
                        x={labelX}
                        y={labelY + 7}
                        textAnchor="middle"
                        fill="#484f58"
                        fontSize="7"
                        fontFamily="monospace"
                        className="select-none"
                      >
                        {edge.action}
                      </text>
                    )}
                  </g>
                )
              })}

              {/* ── Nodes ─────────────────────────────────────────────────── */}
              {nodes.map((node) => {
                const phase = devicePhases[node.deviceName] ?? node.phase
                const color = phaseColor(phase)
                const isSelected = selectedNode === node.deviceName
                const isHovered = hoveredNode === node.deviceName
                const scale = isSelected ? 1.15 : 1

                return (
                  <g
                    key={node.deviceName}
                    className="cursor-pointer"
                    onClick={() =>
                      setSelectedNode(
                        selectedNode === node.deviceName ? null : node.deviceName,
                      )
                    }
                    onMouseEnter={() => setHoveredNode(node.deviceName)}
                    onMouseLeave={() => setHoveredNode(null)}
                    filter={isHovered ? 'url(#node-glow)' : undefined}
                  >
                    {/* Selection ring */}
                    {isSelected && (
                      <circle
                        cx={node.x}
                        cy={node.y}
                        r={NODE_RADIUS + 5}
                        fill="none"
                        stroke={color}
                        strokeWidth={2}
                        opacity={0.5}
                      />
                    )}
                    {/* Node circle */}
                    <circle
                      cx={node.x}
                      cy={node.y}
                      r={NODE_RADIUS * scale}
                      fill={color}
                      opacity={isSelected || isHovered ? 1 : 0.85}
                      stroke={isSelected ? '#e6edf3' : 'none'}
                      strokeWidth={isSelected ? 1.5 : 0}
                    />
                    {/* Abbreviation inside */}
                    <text
                      x={node.x}
                      y={node.y + 1}
                      textAnchor="middle"
                      dominantBaseline="middle"
                      fill="#0d1117"
                      fontSize="10"
                      fontWeight="bold"
                      fontFamily="monospace"
                      className="select-none"
                    >
                      {deviceAbbr(node)}
                    </text>
                    {/* Device name below */}
                    <text
                      x={node.x}
                      y={node.y + NODE_RADIUS + 12}
                      textAnchor="middle"
                      fill="#8b949e"
                      fontSize="9"
                      fontFamily="monospace"
                      className="select-none"
                    >
                      {node.deviceName}
                    </text>
                    {/* Phase label below name */}
                    <text
                      x={node.x}
                      y={node.y + NODE_RADIUS + 22}
                      textAnchor="middle"
                      fill={color}
                      fontSize="8"
                      fontFamily="monospace"
                      className="select-none"
                      opacity={0.7}
                    >
                      {phase}
                    </text>
                  </g>
                )
              })}
            </svg>
          ) : !loading ? (
            <div className="flex items-center justify-center h-full text-gray-600 text-[11px]">
              {activeLevelPath
                ? 'No simulation data. Click "Run Simulation" to generate the DFA graph.'
                : 'Select a level to simulate device wiring as a DFA graph.'}
            </div>
          ) : (
            <div className="flex items-center justify-center h-full text-gray-500 text-[11px] animate-pulse">
              Running simulation...
            </div>
          )}
        </div>

        {/* ── Right panel: Device Detail ─────────────────────────────────── */}
        <div className="w-[280px] shrink-0 border-l border-fn-border overflow-y-auto p-3">
          {selectedNodeData ? (
            <>
              <div className="text-[9px] font-semibold text-gray-600 tracking-widest uppercase mb-2">
                Device Detail
              </div>
              <div className="space-y-2">
                <DetailRow label="Name" value={selectedNodeData.deviceName} mono />
                <DetailRow label="Class" value={selectedNodeData.deviceClass} mono />
                <DetailRow label="Type" value={selectedNodeData.deviceType} />
                <DetailRow
                  label="State"
                  value={devicePhases[selectedNodeData.deviceName] ?? selectedNodeData.phase}
                  color={phaseColor(devicePhases[selectedNodeData.deviceName] ?? selectedNodeData.phase)}
                />
                <DetailRow
                  label="Events"
                  value={selectedNodeData.events.length > 0 ? selectedNodeData.events.join(', ') : 'none'}
                  mono
                />
                <DetailRow
                  label="Actions"
                  value={selectedNodeData.actions.length > 0 ? selectedNodeData.actions.join(', ') : 'none'}
                  mono
                />
                <DetailRow
                  label="Position"
                  value={`(${selectedNodeData.x.toFixed(0)}, ${selectedNodeData.y.toFixed(0)})`}
                />
              </div>

              {/* Connections from/to this device */}
              <div className="text-[9px] font-semibold text-gray-600 tracking-widest uppercase mt-4 mb-2">
                Connections
              </div>
              <div className="space-y-1">
                {edges
                  .filter(
                    (e) =>
                      e.sourceDevice === selectedNodeData.deviceName ||
                      e.targetDevice === selectedNodeData.deviceName,
                  )
                  .map((e, i) => {
                    const isOutgoing = e.sourceDevice === selectedNodeData.deviceName
                    return (
                      <div
                        key={i}
                        className="text-[9px] bg-fn-panel/50 rounded px-2 py-1.5 border border-fn-border/50"
                      >
                        <span className="text-gray-500">{isOutgoing ? 'OUT' : 'IN'}</span>
                        <span className="mx-1 text-gray-600">|</span>
                        <span className="font-mono text-fn-rare/80">{e.event}</span>
                        <span className="mx-1 text-gray-600">&rarr;</span>
                        <span className="font-mono text-gray-400">
                          {isOutgoing ? e.targetDevice : e.sourceDevice}
                        </span>
                        <span className="text-gray-600">.{e.action}</span>
                        {e.isConditional && (
                          <span className="ml-1 text-yellow-600 italic">
                            {e.condition ? `if ${e.condition}` : 'conditional'}
                          </span>
                        )}
                      </div>
                    )
                  })}
                {edges.filter(
                  (e) =>
                    e.sourceDevice === selectedNodeData.deviceName ||
                    e.targetDevice === selectedNodeData.deviceName,
                ).length === 0 && (
                  <div className="text-[9px] text-gray-600">No connections</div>
                )}
              </div>
            </>
          ) : (
            <div className="text-[10px] text-gray-600 py-4">
              Click a node to view device details.
            </div>
          )}

          {/* ── Step Detail ────────────────────────────────────────────── */}
          {totalSteps > 0 && (
            <>
              <div className="text-[9px] font-semibold text-gray-600 tracking-widest uppercase mt-5 mb-2">
                Step Detail
              </div>
              {currentSnapshot ? (
                <div className="space-y-1.5">
                  <div className="text-[10px] text-gray-400 tabular-nums">
                    Step {currentSnapshot.stepNumber}/{totalSteps}
                    <span className="ml-2 text-gray-600">
                      t={currentSnapshot.simulatedTime.toFixed(1)}s
                    </span>
                  </div>
                  {currentSnapshot.firedEdgeSource && (
                    <div className="text-[9px] bg-fn-panel/50 rounded px-2 py-1.5 border border-fn-border/50">
                      <div className="text-green-400/80 font-mono">
                        {currentSnapshot.firedEdgeSource}.{currentSnapshot.firedEdgeEvent}
                      </div>
                      {(() => {
                        const firedEdge = edges.find(
                          (e) =>
                            e.sourceDevice === currentSnapshot.firedEdgeSource &&
                            e.event === currentSnapshot.firedEdgeEvent,
                        )
                        if (!firedEdge) return null
                        return (
                          <div className="text-gray-500 mt-0.5">
                            &rarr; {firedEdge.targetDevice}.{firedEdge.action}
                          </div>
                        )
                      })()}
                    </div>
                  )}
                  {/* Show changed states in this step */}
                  {stepIndex > 0 && history[stepIndex - 1] && (
                    <div className="space-y-0.5">
                      {Object.entries(currentSnapshot.devicePhases)
                        .filter(
                          ([name, phase]) =>
                            history[stepIndex - 1].devicePhases[name] !== phase,
                        )
                        .map(([name, phase]) => (
                          <div key={name} className="text-[9px] text-gray-500">
                            <span className="font-mono text-gray-400">{name}</span>:{' '}
                            <span style={{ color: phaseColor(history[stepIndex - 1].devicePhases[name]) }}>
                              {history[stepIndex - 1].devicePhases[name]}
                            </span>
                            {' '}&rarr;{' '}
                            <span style={{ color: phaseColor(phase) }}>{phase}</span>
                          </div>
                        ))}
                    </div>
                  )}
                </div>
              ) : (
                <div className="text-[9px] text-gray-600">No step selected</div>
              )}
            </>
          )}

          {/* Warnings */}
          {warnings.length > 0 && (
            <>
              <div className="text-[9px] font-semibold text-gray-600 tracking-widest uppercase mt-5 mb-2">
                Warnings
              </div>
              {warnings.map((w, i) => (
                <div
                  key={i}
                  className="text-[9px] text-yellow-500/70 bg-yellow-500/5 rounded px-2 py-1.5 mb-1 border border-yellow-500/10"
                >
                  {w}
                </div>
              ))}
            </>
          )}
        </div>
      </div>

      {/* ── Bottom bar: Fire Event ─────────────────────────────────────────── */}
      <div className="shrink-0 px-4 py-2 border-t border-fn-border flex items-center gap-3">
        <span className="text-[9px] font-semibold text-gray-600 tracking-widest uppercase">
          Fire Event
        </span>

        <select
          className="text-[10px] bg-fn-dark border border-fn-border rounded px-2 py-1 text-gray-300 focus:border-fn-rare/50 focus:outline-none min-w-[140px]"
          value={customDevice}
          onChange={(e) => {
            setCustomDevice(e.target.value)
            setCustomEvent('')
          }}
        >
          <option value="">Device...</option>
          {deviceNames.map((name) => (
            <option key={name} value={name}>
              {name}
            </option>
          ))}
        </select>

        <select
          className="text-[10px] bg-fn-dark border border-fn-border rounded px-2 py-1 text-gray-300 focus:border-fn-rare/50 focus:outline-none min-w-[120px]"
          value={customEvent}
          onChange={(e) => setCustomEvent(e.target.value)}
          disabled={!customDevice}
        >
          <option value="">Event...</option>
          {customDeviceEvents.map((evt) => (
            <option key={evt} value={evt}>
              {evt}
            </option>
          ))}
        </select>

        <button
          className="text-[10px] px-2.5 py-1 rounded bg-fn-rare/20 text-fn-rare hover:bg-fn-rare/30 transition-colors disabled:opacity-40"
          onClick={runCustomSimulation}
          disabled={!activeLevelPath || !customDevice || !customEvent}
        >
          Fire
        </button>

        {customResult && (
          <button
            className="text-[10px] px-2 py-1 rounded bg-fn-panel border border-fn-border text-gray-500 hover:text-gray-300 transition-colors"
            onClick={() => {
              setCustomResult(null)
              setStepIndex(0)
            }}
          >
            Clear Custom
          </button>
        )}

        <span className="text-[10px] text-gray-600 ml-auto tabular-nums">
          {nodes.length} nodes
        </span>
      </div>
    </div>
  )
}

// ─── Helper component ───────────────────────────────────────────────────────

function DetailRow({
  label,
  value,
  mono,
  color,
}: {
  label: string
  value: string
  mono?: boolean
  color?: string
}) {
  return (
    <div className="flex items-start gap-2">
      <span className="text-[9px] text-gray-600 w-[50px] shrink-0 text-right">{label}</span>
      <span
        className={`text-[10px] ${mono ? 'font-mono' : ''} break-all`}
        style={color ? { color } : undefined}
      >
        {value}
      </span>
    </div>
  )
}
