import { useState, useCallback, useRef, useEffect } from 'react'
import { previewSearch, previewTexture, previewMeshInfo, previewStatus, previewInit } from '../lib/api'

// ─── Asset type classification ──────────────────────────────────────────────

type AssetType = 'mesh' | 'texture' | 'material' | 'sound' | 'blueprint' | 'animation' | 'other'

function classifyAsset(path: string): AssetType {
  const lower = path.toLowerCase()
  if (lower.includes('/sm_') || lower.includes('staticmesh') || lower.includes('/s_')) return 'mesh'
  if (lower.includes('/t_') || lower.includes('texture') || lower.includes('/ti_')) return 'texture'
  if (lower.includes('/m_') || lower.includes('/mi_') || lower.includes('material')) return 'material'
  if (lower.includes('sound') || lower.includes('audio') || lower.includes('/a_') || lower.includes('.bnk')) return 'sound'
  if (lower.includes('/bp_') || lower.includes('blueprint') || lower.includes('/b_')) return 'blueprint'
  if (lower.includes('anim') || lower.includes('montage') || lower.includes('sequence')) return 'animation'
  return 'other'
}

const TYPE_COLORS: Record<AssetType, string> = {
  mesh: 'text-blue-400 bg-blue-400/10 border-blue-400/20',
  texture: 'text-green-400 bg-green-400/10 border-green-400/20',
  material: 'text-purple-400 bg-purple-400/10 border-purple-400/20',
  sound: 'text-yellow-400 bg-yellow-400/10 border-yellow-400/20',
  blueprint: 'text-orange-400 bg-orange-400/10 border-orange-400/20',
  animation: 'text-cyan-400 bg-cyan-400/10 border-cyan-400/20',
  other: 'text-gray-400 bg-gray-400/10 border-gray-400/20',
}

const TYPE_ICONS: Record<AssetType, string> = {
  mesh: '\u25B3',      // triangle
  texture: '\u25A0',   // square
  material: '\u25C6',  // diamond
  sound: '\u266B',     // music note
  blueprint: '\u2699', // gear
  animation: '\u25B6', // play
  other: '\u25CB',     // circle
}

interface SearchResult {
  path: string
  name: string
  type: AssetType
  folder: string
}

interface AssetDetail {
  path: string
  thumbnailUrl?: string
  meshInfo?: { vertexCount: number; triangleCount: number; lodCount: number; materialCount: number }
  loading: boolean
}

// ─── Main Component ─────────────────────────────────────────────────────────

export function AssetSearchPage() {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<SearchResult[]>([])
  const [searching, setSearching] = useState(false)
  const [totalCount, setTotalCount] = useState(0)
  const [selectedAsset, setSelectedAsset] = useState<SearchResult | null>(null)
  const [detail, setDetail] = useState<AssetDetail | null>(null)
  const [typeFilter, setTypeFilter] = useState<AssetType | 'all'>('all')
  const [initialized, setInitialized] = useState<boolean | null>(null)
  const [initPath, setInitPath] = useState('')
  const [initializing, setInitializing] = useState(false)
  const searchInputRef = useRef<HTMLInputElement>(null)
  const searchTimeoutRef = useRef<ReturnType<typeof setTimeout>>(undefined)

  // Check CUE4Parse status on mount
  useEffect(() => {
    previewStatus()
      .then((s) => setInitialized(s.initialized))
      .catch(() => setInitialized(false))
  }, [])

  // Focus search on mount
  useEffect(() => {
    searchInputRef.current?.focus()
  }, [])

  const doSearch = useCallback(async (q: string) => {
    if (!q.trim()) { setResults([]); setTotalCount(0); return }
    setSearching(true)
    try {
      const resp = await previewSearch(q.trim(), 200)
      const parsed: SearchResult[] = resp.results.map((path) => {
        const parts = path.split('/')
        const name = parts[parts.length - 1]
        const folder = parts.slice(0, -1).join('/')
        return { path, name, type: classifyAsset(path), folder }
      })
      setResults(parsed)
      setTotalCount(resp.count)
    } catch {
      setResults([])
      setTotalCount(0)
    } finally {
      setSearching(false)
    }
  }, [])

  // Debounced search
  const handleSearchChange = useCallback((value: string) => {
    setQuery(value)
    clearTimeout(searchTimeoutRef.current)
    searchTimeoutRef.current = setTimeout(() => doSearch(value), 300)
  }, [doSearch])

  // Load asset detail (thumbnail for textures, mesh info for meshes)
  useEffect(() => {
    if (!selectedAsset) { setDetail(null); return }
    let cancelled = false

    const loadDetail = async () => {
      setDetail({ path: selectedAsset.path, loading: true })

      try {
        if (selectedAsset.type === 'texture') {
          const tex = await previewTexture(selectedAsset.path)
          if (!cancelled) {
            setDetail({ path: selectedAsset.path, thumbnailUrl: tex.dataUrl, loading: false })
          }
        } else if (selectedAsset.type === 'mesh') {
          const info = await previewMeshInfo(selectedAsset.path)
          if (!cancelled) {
            setDetail({ path: selectedAsset.path, meshInfo: info, loading: false })
          }
        } else {
          if (!cancelled) {
            setDetail({ path: selectedAsset.path, loading: false })
          }
        }
      } catch {
        if (!cancelled) {
          setDetail({ path: selectedAsset.path, loading: false })
        }
      }
    }

    loadDetail()
    return () => { cancelled = true }
  }, [selectedAsset])

  const filteredResults = typeFilter === 'all'
    ? results
    : results.filter((r) => r.type === typeFilter)

  // Type counts for filter badges
  const typeCounts = results.reduce<Record<string, number>>((acc, r) => {
    acc[r.type] = (acc[r.type] || 0) + 1
    return acc
  }, {})

  const handleInit = async () => {
    if (!initPath.trim()) return
    setInitializing(true)
    try {
      const result = await previewInit(initPath.trim())
      setInitialized(result.initialized)
    } catch {
      setInitialized(false)
    } finally {
      setInitializing(false)
    }
  }

  // ─── Not initialized ───────────────────────────────────────────────────────

  if (initialized === false) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="max-w-md text-center space-y-4">
          <svg className="w-12 h-12 mx-auto text-gray-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
            <path d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
          </svg>
          <div className="text-[13px] text-gray-300 font-semibold">Asset Search Engine</div>
          <div className="text-[11px] text-gray-500">
            Search all Fortnite game assets — meshes, textures, materials, sounds, and more.
            Requires CUE4Parse initialization with your Fortnite installation path.
          </div>
          <div className="flex gap-2">
            <input
              type="text"
              value={initPath}
              onChange={(e) => setInitPath(e.target.value)}
              placeholder="C:\Program Files\Epic Games\Fortnite"
              className="flex-1 bg-fn-darker border border-fn-border rounded px-3 py-2 text-[11px] text-white placeholder-gray-600 focus:outline-none focus:border-blue-500/50"
            />
            <button
              onClick={handleInit}
              disabled={initializing || !initPath.trim()}
              className="px-4 py-2 text-[11px] font-medium text-white bg-blue-600 hover:bg-blue-500 rounded disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
            >
              {initializing ? 'Initializing...' : 'Initialize'}
            </button>
          </div>
          <div className="text-[9px] text-gray-600">
            This scans the PAK files in your Fortnite installation. Read-only — never modifies game files.
          </div>
        </div>
      </div>
    )
  }

  // ─── Loading state ─────────────────────────────────────────────────────────

  if (initialized === null) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="w-5 h-5 border-2 border-blue-400/30 border-t-blue-400 rounded-full animate-spin" />
      </div>
    )
  }

  // ─── Main search UI ────────────────────────────────────────────────────────

  return (
    <div className="flex-1 flex flex-col bg-fn-darker min-h-0 overflow-hidden">
      {/* Search bar */}
      <div className="px-4 py-3 border-b border-fn-border bg-fn-dark shrink-0">
        <div className="flex items-center gap-3">
          <div className="relative flex-1">
            <svg className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
            </svg>
            <input
              ref={searchInputRef}
              type="text"
              value={query}
              onChange={(e) => handleSearchChange(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') doSearch(query) }}
              placeholder="Search Fortnite assets... (e.g., vending machine, treasure chest, weapon rack)"
              className="w-full bg-fn-darker border border-fn-border rounded-lg pl-10 pr-4 py-2.5 text-[12px] text-white placeholder-gray-600 focus:outline-none focus:border-blue-500/50 transition-colors"
            />
            {searching && (
              <div className="absolute right-3 top-1/2 -translate-y-1/2 w-4 h-4 border-2 border-blue-400/30 border-t-blue-400 rounded-full animate-spin" />
            )}
          </div>
          <div className="text-[10px] text-gray-500 shrink-0 tabular-nums">
            {results.length > 0 && (
              <>
                {filteredResults.length}
                {typeFilter !== 'all' && ` / ${results.length}`}
                {totalCount > results.length && ` of ${totalCount.toLocaleString()}`}
              </>
            )}
          </div>
        </div>

        {/* Type filter pills */}
        {results.length > 0 && (
          <div className="flex gap-1.5 mt-2">
            <button
              onClick={() => setTypeFilter('all')}
              className={`px-2 py-0.5 rounded-full text-[9px] font-medium border transition-colors ${
                typeFilter === 'all' ? 'text-white bg-white/10 border-white/20' : 'text-gray-500 bg-transparent border-transparent hover:text-gray-300'
              }`}
            >
              All {results.length}
            </button>
            {(['mesh', 'texture', 'material', 'sound', 'blueprint', 'animation', 'other'] as AssetType[]).map((t) => {
              const count = typeCounts[t] || 0
              if (count === 0) return null
              return (
                <button
                  key={t}
                  onClick={() => setTypeFilter(typeFilter === t ? 'all' : t)}
                  className={`px-2 py-0.5 rounded-full text-[9px] font-medium border transition-colors ${
                    typeFilter === t ? TYPE_COLORS[t] : 'text-gray-500 bg-transparent border-transparent hover:text-gray-300'
                  }`}
                >
                  {TYPE_ICONS[t]} {t} {count}
                </button>
              )
            })}
          </div>
        )}
      </div>

      {/* Results + Detail split */}
      <div className="flex-1 flex min-h-0 overflow-hidden">
        {/* Results list */}
        <div className="flex-1 overflow-y-auto min-h-0">
          {results.length === 0 && !searching && query.trim() && (
            <div className="flex items-center justify-center h-full">
              <div className="text-center text-[11px] text-gray-600">No results found</div>
            </div>
          )}

          {results.length === 0 && !query.trim() && (
            <div className="flex items-center justify-center h-full">
              <div className="text-center space-y-3 max-w-sm">
                <svg className="w-10 h-10 mx-auto text-gray-700" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
                  <path d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                </svg>
                <div className="text-[11px] text-gray-500">Search across all Fortnite game assets</div>
                <div className="flex flex-wrap gap-1.5 justify-center">
                  {['vending machine', 'treasure chest', 'weapon rack', 'spawn pad', 'barrier', 'cube', 'tree', 'rock'].map((term) => (
                    <button
                      key={term}
                      onClick={() => { setQuery(term); doSearch(term) }}
                      className="px-2 py-1 text-[9px] text-gray-400 bg-white/[0.03] border border-fn-border rounded hover:bg-white/[0.06] hover:text-gray-200 transition-colors"
                    >
                      {term}
                    </button>
                  ))}
                </div>
              </div>
            </div>
          )}

          {filteredResults.map((r) => {
            const isSelected = selectedAsset?.path === r.path
            return (
              <button
                key={r.path}
                onClick={() => setSelectedAsset(r)}
                className={`w-full flex items-center gap-2.5 px-4 py-2 text-left transition-colors border-b border-fn-border/30 ${
                  isSelected ? 'bg-blue-500/10' : 'hover:bg-white/[0.02]'
                }`}
              >
                <span className={`text-[11px] shrink-0 w-5 text-center ${TYPE_COLORS[r.type].split(' ')[0]}`}>
                  {TYPE_ICONS[r.type]}
                </span>
                <div className="min-w-0 flex-1">
                  <div className={`text-[11px] truncate ${isSelected ? 'text-white font-medium' : 'text-gray-300'}`}>
                    {r.name}
                  </div>
                  <div className="text-[9px] text-gray-600 truncate font-mono">{r.folder}</div>
                </div>
                <span className={`text-[8px] px-1.5 py-0.5 rounded-full border shrink-0 ${TYPE_COLORS[r.type]}`}>
                  {r.type}
                </span>
              </button>
            )
          })}
        </div>

        {/* Detail panel */}
        {selectedAsset && (
          <div className="w-[360px] border-l border-fn-border bg-fn-dark flex flex-col shrink-0 min-h-0 overflow-hidden">
            <div className="px-4 py-3 border-b border-fn-border shrink-0">
              <div className="flex items-center gap-2">
                <span className={`text-[13px] ${TYPE_COLORS[selectedAsset.type].split(' ')[0]}`}>
                  {TYPE_ICONS[selectedAsset.type]}
                </span>
                <span className="text-[12px] font-semibold text-white truncate">{selectedAsset.name}</span>
              </div>
              <div className="text-[9px] text-gray-500 font-mono mt-1 break-all">{selectedAsset.path}</div>
            </div>

            <div className="flex-1 overflow-y-auto p-4 space-y-4">
              {/* Thumbnail for textures */}
              {detail?.loading && (
                <div className="flex items-center justify-center py-8">
                  <div className="w-5 h-5 border-2 border-blue-400/30 border-t-blue-400 rounded-full animate-spin" />
                </div>
              )}

              {detail?.thumbnailUrl && (
                <div className="rounded-lg overflow-hidden border border-fn-border bg-fn-darker">
                  <img
                    src={detail.thumbnailUrl}
                    alt={selectedAsset.name}
                    className="w-full h-auto"
                    style={{ imageRendering: 'auto' }}
                  />
                </div>
              )}

              {/* Mesh info */}
              {detail?.meshInfo && (
                <div className="space-y-2">
                  <div className="text-[10px] font-semibold text-gray-400 uppercase tracking-wider">Mesh Info</div>
                  <div className="grid grid-cols-2 gap-2">
                    <InfoCard label="Vertices" value={detail.meshInfo.vertexCount.toLocaleString()} color="text-blue-400" />
                    <InfoCard label="Triangles" value={detail.meshInfo.triangleCount.toLocaleString()} color="text-cyan-400" />
                    <InfoCard label="LODs" value={String(detail.meshInfo.lodCount)} color="text-green-400" />
                    <InfoCard label="Materials" value={String(detail.meshInfo.materialCount)} color="text-purple-400" />
                  </div>
                </div>
              )}

              {/* Asset metadata */}
              <div className="space-y-2">
                <div className="text-[10px] font-semibold text-gray-400 uppercase tracking-wider">Details</div>
                <div className="space-y-1.5">
                  <DetailRow label="Type" value={selectedAsset.type} />
                  <DetailRow label="Name" value={selectedAsset.name} />
                  <DetailRow label="Path" value={selectedAsset.folder} mono />
                </div>
              </div>

              {/* Copy path button */}
              <button
                onClick={() => navigator.clipboard.writeText(selectedAsset.path)}
                className="w-full px-3 py-2 text-[10px] font-medium text-gray-300 bg-white/[0.03] border border-fn-border rounded-lg hover:bg-white/[0.06] transition-colors"
              >
                Copy Asset Path
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

// ─── Sub-components ─────────────────────────────────────────────────────────

function InfoCard({ label, value, color }: { label: string; value: string; color: string }) {
  return (
    <div className="bg-fn-darker rounded-lg border border-fn-border/50 px-3 py-2">
      <div className="text-[9px] text-gray-500 uppercase tracking-wider">{label}</div>
      <div className={`text-[14px] font-semibold tabular-nums ${color}`}>{value}</div>
    </div>
  )
}

function DetailRow({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="flex items-start gap-2">
      <span className="text-[9px] text-gray-500 uppercase tracking-wider shrink-0 w-12 pt-0.5">{label}</span>
      <span className={`text-[10px] text-gray-300 break-all ${mono ? 'font-mono' : ''}`}>{value}</span>
    </div>
  )
}
