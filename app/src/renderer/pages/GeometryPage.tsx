import { useCallback, useMemo, useState } from 'react'
import { useBridgeStore } from '../stores/bridgeStore'
import { forgeBridgeCommand, forgeDesignGame } from '../lib/api'

interface GeometryPreset {
  id: string
  name: string
  width: number
  height: number
  depth: number
  description: string
}

const PRESETS: GeometryPreset[] = [
  { id: 'small-box', name: 'Small Box', width: 20, height: 20, depth: 10, description: '20x20x10m -- 1v1 fights, testing' },
  { id: 'medium-box', name: 'Medium Box', width: 50, height: 50, depth: 15, description: '50x50x15m -- small team modes' },
  { id: 'large-arena', name: 'Large Arena', width: 100, height: 100, depth: 20, description: '100x100x20m -- full game modes' },
  { id: 'corridor', name: 'Corridor', width: 10, height: 80, depth: 8, description: '10x80x8m -- linear gameplay' },
  { id: 'tower', name: 'Tower', width: 30, height: 30, depth: 50, description: '30x30x50m -- vertical gameplay' },
]

const TEAM_OPTIONS = [2, 4, 8]

export function GeometryPage() {
  const bridgeConnected = useBridgeStore((s) => s.connected)

  const [width, setWidth] = useState(50)
  const [height, setHeight] = useState(50)
  const [depth, setDepth] = useState(15)
  const [teamCount, setTeamCount] = useState(2)
  const [selectedPreset, setSelectedPreset] = useState<string | null>('medium-box')
  const [building, setBuilding] = useState(false)
  const [generating, setGenerating] = useState(false)
  const [result, setResult] = useState<{ success: boolean; message: string } | null>(null)
  const [generatedPlan, setGeneratedPlan] = useState<Record<string, unknown> | null>(null)

  const handlePresetSelect = useCallback((preset: GeometryPreset) => {
    setSelectedPreset(preset.id)
    setWidth(preset.width)
    setHeight(preset.height)
    setDepth(preset.depth)
    setResult(null)
    setGeneratedPlan(null)
  }, [])

  const handleDimensionChange = useCallback(() => {
    setSelectedPreset(null)
    setResult(null)
    setGeneratedPlan(null)
  }, [])

  // Computed volume and metrics
  const metrics = useMemo(() => {
    const volume = width * height * depth
    const floorArea = width * height
    const wallPerimeter = 2 * (width + height)
    // Approximate building piece count (each piece is ~5m)
    const wallPieces = Math.ceil(wallPerimeter / 5) * Math.ceil(depth / 5)
    const floorPieces = Math.ceil(width / 5) * Math.ceil(height / 5)
    return { volume, floorArea, wallPerimeter, wallPieces, floorPieces, totalPieces: wallPieces + floorPieces * 2 }
  }, [width, height, depth])

  // Build in UEFN via bridge
  const handleBuild = useCallback(async () => {
    if (!bridgeConnected) return
    setBuilding(true)
    setResult(null)
    try {
      await forgeBridgeCommand('build_geometry', {
        width, height, depth, teamCount,
      })
      setResult({ success: true, message: `Geometry built: ${width}x${height}x${depth}m with ${teamCount} team spawns` })
    } catch (err) {
      setResult({ success: false, message: err instanceof Error ? err.message : 'Build failed' })
    } finally {
      setBuilding(false)
    }
  }, [bridgeConnected, width, height, depth, teamCount])

  // Generate plan offline
  const handleGeneratePlan = useCallback(async () => {
    setGenerating(true)
    setResult(null)
    setGeneratedPlan(null)
    try {
      const design = await forgeDesignGame(
        `Build a ${width}x${height}x${depth}m box arena for ${teamCount} teams with walls, floor, ceiling, and team spawn areas.`
      )
      setGeneratedPlan(design as Record<string, unknown>)
      setResult({ success: true, message: 'Plan generated -- save it or execute when bridge connects.' })
    } catch (err) {
      // Fallback: generate a basic plan locally
      const plan = {
        name: `Box Arena ${width}x${height}x${depth}`,
        description: `${width}x${height}x${depth}m arena for ${teamCount} teams`,
        devices: [
          ...Array.from({ length: teamCount }, (_, i) => ({
            role: `team_${i + 1}_spawn`,
            type: 'Player Spawner',
            class: 'BP_PlayerSpawnerDevice_C',
          })),
          { role: 'game_manager', type: 'Game Manager', class: 'BP_GameManagerDevice_C' },
        ],
        geometry: { width, height, depth, teamCount },
        estimatedPieces: metrics.totalPieces,
      }
      setGeneratedPlan(plan)
      setResult({ success: true, message: 'Basic plan generated offline.' })
    } finally {
      setGenerating(false)
    }
  }, [width, height, depth, teamCount, metrics.totalPieces])

  // Save plan as JSON
  const handleSavePlan = useCallback(async () => {
    if (!generatedPlan) return
    try {
      const { save } = await import('@tauri-apps/plugin-dialog')
      const filePath = await save({
        defaultPath: `geometry_${width}x${height}x${depth}.json`,
        filters: [{ name: 'JSON', extensions: ['json'] }],
      })
      if (!filePath) return

      const json = JSON.stringify(generatedPlan, null, 2)
      const { invoke } = await import('@tauri-apps/api/core')
      const encoder = new TextEncoder()
      const bytes = encoder.encode(json)
      const binary = Array.from(bytes).map((b) => String.fromCharCode(b)).join('')
      const b64 = btoa(binary)
      await invoke('export_png', {
        dataUrl: `data:application/json;base64,${b64}`,
        filePath,
      })
      setResult({ success: true, message: `Plan saved to ${filePath}` })
    } catch (err) {
      setResult({ success: false, message: err instanceof Error ? err.message : 'Save failed' })
    }
  }, [generatedPlan, width, height, depth])

  return (
    <div className="flex-1 flex flex-col bg-fn-darker min-h-0 overflow-y-auto">
      {/* Header */}
      <div className="border-b border-fn-border bg-fn-dark/50 px-6 py-4">
        <h1 className="text-[14px] font-semibold text-white">Geometry Builder</h1>
        <p className="text-[10px] text-gray-500 mt-0.5">
          Define level geometry and spawn layout for your UEFN map
        </p>
      </div>

      <div className="flex-1 p-6">
        <div className="max-w-4xl mx-auto grid grid-cols-2 gap-6">
          {/* Left: Controls */}
          <div className="space-y-6">
            {/* Presets */}
            <div>
              <h2 className="text-[11px] font-semibold text-gray-400 uppercase tracking-wider mb-3">Presets</h2>
              <div className="space-y-1.5">
                {PRESETS.map((preset) => (
                  <button
                    key={preset.id}
                    onClick={() => handlePresetSelect(preset)}
                    className={`w-full text-left px-3 py-2.5 rounded-lg border transition-all ${
                      selectedPreset === preset.id
                        ? 'border-fn-rare/30 bg-fn-rare/10 text-white'
                        : 'border-fn-border bg-fn-panel text-gray-300 hover:bg-white/[0.03] hover:border-gray-600'
                    }`}
                  >
                    <div className="flex items-center justify-between">
                      <span className="text-[11px] font-medium">{preset.name}</span>
                      <span className="text-[9px] text-gray-500 font-mono">
                        {preset.width}x{preset.height}x{preset.depth}m
                      </span>
                    </div>
                    <p className="text-[9px] text-gray-500 mt-0.5">{preset.description}</p>
                  </button>
                ))}
              </div>
            </div>

            {/* Dimensions */}
            <div>
              <h2 className="text-[11px] font-semibold text-gray-400 uppercase tracking-wider mb-3">Dimensions</h2>
              <div className="space-y-3">
                <DimensionInput label="Width (m)" value={width} onChange={(v) => { setWidth(v); handleDimensionChange() }} min={5} max={500} />
                <DimensionInput label="Height (m)" value={height} onChange={(v) => { setHeight(v); handleDimensionChange() }} min={5} max={500} />
                <DimensionInput label="Depth (m)" value={depth} onChange={(v) => { setDepth(v); handleDimensionChange() }} min={3} max={200} />
              </div>
            </div>

            {/* Team Count */}
            <div>
              <h2 className="text-[11px] font-semibold text-gray-400 uppercase tracking-wider mb-3">Teams</h2>
              <div className="flex gap-2">
                {TEAM_OPTIONS.map((count) => (
                  <button
                    key={count}
                    onClick={() => setTeamCount(count)}
                    className={`flex-1 text-[11px] py-2 rounded-lg border transition-all ${
                      teamCount === count
                        ? 'border-fn-rare/30 bg-fn-rare/10 text-fn-rare font-semibold'
                        : 'border-fn-border bg-fn-panel text-gray-400 hover:border-gray-500'
                    }`}
                  >
                    {count} Teams
                  </button>
                ))}
              </div>
            </div>

            {/* Actions */}
            <div className="space-y-2 pt-2">
              {bridgeConnected ? (
                <button
                  onClick={handleBuild}
                  disabled={building}
                  className="w-full text-[11px] py-2.5 rounded-lg bg-emerald-500/20 border border-emerald-500/30 text-emerald-400 font-semibold hover:bg-emerald-500/30 disabled:opacity-40 transition-all"
                >
                  {building ? 'Building...' : 'Build in UEFN'}
                </button>
              ) : (
                <div className="text-[9px] text-amber-400/70 bg-amber-400/5 border border-amber-400/10 rounded-lg px-3 py-2 text-center">
                  Connect to UEFN bridge to build directly
                </div>
              )}
              <button
                onClick={handleGeneratePlan}
                disabled={generating}
                className="w-full text-[11px] py-2.5 rounded-lg bg-fn-rare/10 border border-fn-rare/20 text-fn-rare font-medium hover:bg-fn-rare/20 disabled:opacity-40 transition-all"
              >
                {generating ? 'Generating...' : 'Generate Plan'}
              </button>
            </div>

            {/* Result */}
            {result && (
              <div className={`text-[10px] px-3 py-2 rounded-lg border ${
                result.success
                  ? 'text-emerald-400 bg-emerald-400/5 border-emerald-400/10'
                  : 'text-red-400 bg-red-400/5 border-red-400/10'
              }`}>
                {result.message}
                {generatedPlan && (
                  <button
                    onClick={handleSavePlan}
                    className="mt-2 block text-fn-rare hover:underline"
                  >
                    Save plan as JSON
                  </button>
                )}
              </div>
            )}
          </div>

          {/* Right: Preview */}
          <div className="space-y-4">
            {/* 3D Schematic Preview */}
            <div className="bg-fn-panel border border-fn-border rounded-xl overflow-hidden">
              <div className="px-4 py-2 border-b border-fn-border bg-fn-darker/50">
                <span className="text-[10px] font-semibold text-gray-400">Preview</span>
              </div>
              <div className="p-6 flex items-center justify-center" style={{ minHeight: 280 }}>
                <GeometrySchematic width={width} height={height} depth={depth} teamCount={teamCount} />
              </div>
            </div>

            {/* Metrics */}
            <div className="bg-fn-panel border border-fn-border rounded-xl p-4">
              <h3 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider mb-3">Metrics</h3>
              <div className="grid grid-cols-2 gap-y-2 gap-x-4">
                <MetricRow label="Floor Area" value={`${metrics.floorArea.toLocaleString()} m2`} />
                <MetricRow label="Volume" value={`${metrics.volume.toLocaleString()} m3`} />
                <MetricRow label="Wall Perimeter" value={`${metrics.wallPerimeter}m`} />
                <MetricRow label="Est. Building Pieces" value={metrics.totalPieces.toLocaleString()} />
                <MetricRow label="Team Spawns" value={`${teamCount} zones`} />
                <MetricRow
                  label="Complexity"
                  value={metrics.totalPieces < 200 ? 'Low' : metrics.totalPieces < 800 ? 'Medium' : 'High'}
                  warn={metrics.totalPieces > 800}
                />
              </div>
            </div>

            {/* Generated Plan Preview */}
            {generatedPlan && (
              <div className="bg-[#1a1a2e] border border-fn-border rounded-xl overflow-hidden">
                <div className="flex items-center justify-between px-3 py-1.5 bg-black/20 border-b border-fn-border">
                  <span className="text-[9px] text-gray-500 font-mono">plan.json</span>
                  <button
                    onClick={() => navigator.clipboard.writeText(JSON.stringify(generatedPlan, null, 2))}
                    className="text-[9px] text-gray-600 hover:text-gray-400 transition-colors"
                  >
                    Copy
                  </button>
                </div>
                <pre className="px-3 py-2 text-[9px] font-mono text-gray-400 overflow-x-auto max-h-48 overflow-y-auto leading-relaxed">
                  {JSON.stringify(generatedPlan, null, 2)}
                </pre>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}

// ─── Sub-components ─────────────────────────────────────────────────────────

function DimensionInput({
  label, value, onChange, min, max,
}: {
  label: string; value: number; onChange: (v: number) => void; min: number; max: number
}) {
  return (
    <div className="flex items-center gap-3">
      <label className="text-[10px] text-gray-400 w-20 shrink-0">{label}</label>
      <input
        type="range"
        min={min} max={max} value={value}
        onChange={(e) => onChange(parseInt(e.target.value))}
        className="flex-1 accent-[var(--fn-rare,#a855f7)]"
      />
      <input
        type="number"
        min={min} max={max} value={value}
        onChange={(e) => onChange(Math.max(min, Math.min(max, parseInt(e.target.value) || min)))}
        className="w-16 bg-fn-darker border border-fn-border rounded px-2 py-1 text-[10px] text-white text-center focus:border-fn-rare/40 focus:outline-none"
      />
    </div>
  )
}

function MetricRow({ label, value, warn }: { label: string; value: string; warn?: boolean }) {
  return (
    <div className="flex items-center justify-between">
      <span className="text-[9px] text-gray-500">{label}</span>
      <span className={`text-[10px] font-medium ${warn ? 'text-amber-400' : 'text-gray-300'}`}>{value}</span>
    </div>
  )
}

function GeometrySchematic({
  width, height, depth, teamCount,
}: {
  width: number; height: number; depth: number; teamCount: number
}) {
  // Simple isometric wireframe representation
  const maxDim = Math.max(width, height, depth)
  const scale = 180 / maxDim
  const w = width * scale
  const h = height * scale
  const d = depth * scale * 0.5 // Compressed for isometric view

  // Isometric projection offsets
  const isoX = (x: number, y: number) => 200 + (x - y) * 0.5
  const isoY = (x: number, y: number, z: number) => 140 + (x + y) * 0.3 - z

  // Box corners
  const corners = {
    fbl: [isoX(0, 0), isoY(0, 0, 0)],
    fbr: [isoX(w, 0), isoY(w, 0, 0)],
    bbl: [isoX(0, h), isoY(0, h, 0)],
    bbr: [isoX(w, h), isoY(w, h, 0)],
    ftl: [isoX(0, 0), isoY(0, 0, d)],
    ftr: [isoX(w, 0), isoY(w, 0, d)],
    btl: [isoX(0, h), isoY(0, h, d)],
    btr: [isoX(w, h), isoY(w, h, d)],
  }

  // Team spawn positions along the floor
  const spawnPositions = Array.from({ length: teamCount }, (_, i) => {
    const t = (i + 0.5) / teamCount
    const sx = w * t
    const sy = h * 0.5
    return [isoX(sx, sy), isoY(sx, sy, 0)]
  })

  const teamColors = ['#f87171', '#60a5fa', '#4ade80', '#facc15', '#c084fc', '#fb923c', '#2dd4bf', '#f472b6']

  return (
    <svg viewBox="0 0 400 280" className="w-full h-full" style={{ maxWidth: 400 }}>
      {/* Floor */}
      <polygon
        points={`${corners.fbl[0]},${corners.fbl[1]} ${corners.fbr[0]},${corners.fbr[1]} ${corners.bbr[0]},${corners.bbr[1]} ${corners.bbl[0]},${corners.bbl[1]}`}
        fill="rgba(168, 85, 247, 0.05)"
        stroke="rgba(168, 85, 247, 0.3)"
        strokeWidth={1}
      />
      {/* Back wall */}
      <polygon
        points={`${corners.bbl[0]},${corners.bbl[1]} ${corners.bbr[0]},${corners.bbr[1]} ${corners.btr[0]},${corners.btr[1]} ${corners.btl[0]},${corners.btl[1]}`}
        fill="rgba(168, 85, 247, 0.03)"
        stroke="rgba(168, 85, 247, 0.2)"
        strokeWidth={0.5}
      />
      {/* Right wall */}
      <polygon
        points={`${corners.fbr[0]},${corners.fbr[1]} ${corners.bbr[0]},${corners.bbr[1]} ${corners.btr[0]},${corners.btr[1]} ${corners.ftr[0]},${corners.ftr[1]}`}
        fill="rgba(168, 85, 247, 0.03)"
        stroke="rgba(168, 85, 247, 0.2)"
        strokeWidth={0.5}
      />
      {/* Top edges */}
      <line x1={corners.ftl[0]} y1={corners.ftl[1]} x2={corners.ftr[0]} y2={corners.ftr[1]} stroke="rgba(168, 85, 247, 0.15)" strokeWidth={0.5} strokeDasharray="3,3" />
      <line x1={corners.ftl[0]} y1={corners.ftl[1]} x2={corners.btl[0]} y2={corners.btl[1]} stroke="rgba(168, 85, 247, 0.15)" strokeWidth={0.5} strokeDasharray="3,3" />
      {/* Front edges */}
      <line x1={corners.fbl[0]} y1={corners.fbl[1]} x2={corners.ftl[0]} y2={corners.ftl[1]} stroke="rgba(168, 85, 247, 0.3)" strokeWidth={1} />
      <line x1={corners.fbr[0]} y1={corners.fbr[1]} x2={corners.ftr[0]} y2={corners.ftr[1]} stroke="rgba(168, 85, 247, 0.3)" strokeWidth={1} />

      {/* Team spawns */}
      {spawnPositions.map((pos, i) => (
        <g key={i}>
          <circle cx={pos[0]} cy={pos[1]} r={6} fill={teamColors[i % teamColors.length]} opacity={0.3} />
          <circle cx={pos[0]} cy={pos[1]} r={3} fill={teamColors[i % teamColors.length]} opacity={0.8} />
          <text x={pos[0]} y={pos[1] + 14} textAnchor="middle" fill={teamColors[i % teamColors.length]} fontSize={8} opacity={0.8}>
            T{i + 1}
          </text>
        </g>
      ))}

      {/* Dimension labels */}
      <text x={isoX(w / 2, 0)} y={isoY(w / 2, 0, 0) + 16} textAnchor="middle" fill="#6b7280" fontSize={9}>
        {width}m
      </text>
      <text x={isoX(w, h / 2) + 14} y={isoY(w, h / 2, 0)} textAnchor="start" fill="#6b7280" fontSize={9}>
        {height}m
      </text>
      <text x={corners.fbl[0] - 10} y={(corners.fbl[1] + corners.ftl[1]) / 2} textAnchor="end" fill="#6b7280" fontSize={9}>
        {depth}m
      </text>
    </svg>
  )
}
