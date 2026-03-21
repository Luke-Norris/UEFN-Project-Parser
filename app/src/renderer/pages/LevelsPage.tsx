import { useEffect } from 'react'
import { useForgeStore } from '../stores/forgeStore'
import type { PageId } from '../components/Sidebar/Sidebar'

interface LevelsPageProps {
  onNavigate?: (page: PageId) => void
  onSelectLevel?: (levelPath: string) => void
}

export function LevelsPage({ onNavigate, onSelectLevel }: LevelsPageProps) {
  const levels = useForgeStore((s) => s.levels)
  const levelsLoading = useForgeStore((s) => s.levelsLoading)
  const levelsError = useForgeStore((s) => s.levelsError)
  const fetchLevels = useForgeStore((s) => s.fetchLevels)

  useEffect(() => {
    fetchLevels()
  }, [fetchLevels])

  if (levelsLoading && !levels) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-[11px] text-gray-400">Loading levels...</div>
      </div>
    )
  }

  if (levelsError && !levels) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="text-[11px] text-red-400 mb-3">{levelsError}</div>
          <button
            onClick={() => { useForgeStore.setState({ levelsFetchedAt: null }); fetchLevels() }}
            className="px-3 py-1.5 text-[10px] font-medium text-white bg-fn-panel border border-fn-border rounded hover:bg-white/[0.06] transition-colors"
          >
            Retry
          </button>
        </div>
      </div>
    )
  }

  const levelsList = levels ?? []

  return (
    <div className="flex-1 bg-fn-darker overflow-y-auto">
      <div className="max-w-3xl mx-auto p-6 space-y-5">
        {/* Header */}
        <div>
          <h1 className="text-lg font-semibold text-white">Levels</h1>
          <p className="text-[11px] text-gray-500 mt-0.5">
            Browse and inspect levels in your UEFN project
          </p>
        </div>

        {levelsList.length === 0 ? (
          <div className="bg-fn-panel border border-fn-border rounded-lg p-6 text-center">
            <p className="text-[11px] text-gray-400 mb-1">No levels found</p>
            <p className="text-[10px] text-gray-600">
              Make sure a project is active with .umap files in the Content directory.
            </p>
          </div>
        ) : (
          <div className="bg-fn-dark border border-fn-border rounded-lg overflow-hidden">
            {/* Table header */}
            <div className="grid grid-cols-[1fr_2fr_auto] gap-4 px-4 py-2 border-b border-fn-border bg-fn-panel">
              <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Level Name</span>
              <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Relative Path</span>
              <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Actions</span>
            </div>

            {/* Table rows */}
            {levelsList.map((level) => (
              <div
                key={level.filePath}
                className="grid grid-cols-[1fr_2fr_auto] gap-4 px-4 py-2.5 border-b border-fn-border/30 hover:bg-white/[0.02] transition-colors"
              >
                <div className="flex items-center gap-2 min-w-0">
                  <svg className="w-3.5 h-3.5 text-fn-rare shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                    <path d="M3.055 11H5a2 2 0 012 2v1a2 2 0 002 2 2 2 0 012 2v2.945M8 3.935V5.5A2.5 2.5 0 0010.5 8h.5a2 2 0 012 2 2 2 0 104 0 2 2 0 012-2h1.064M15 20.488V18a2 2 0 012-2h3.064M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                  </svg>
                  <span className="text-[11px] text-white truncate">{level.name}</span>
                </div>
                <span className="text-[11px] text-gray-500 truncate self-center">{level.relativePath}</span>
                <button
                  onClick={() => onSelectLevel?.(level.filePath)}
                  className="px-2 py-1 text-[10px] font-medium text-fn-rare bg-fn-rare/10 border border-fn-rare/20 rounded hover:bg-fn-rare/20 transition-colors shrink-0"
                >
                  View Devices
                </button>
              </div>
            ))}
          </div>
        )}

        {/* Summary */}
        {levelsList.length > 0 && (
          <div className="text-[10px] text-gray-600">
            {levelsList.length} level{levelsList.length !== 1 ? 's' : ''} found
          </div>
        )}
      </div>
    </div>
  )
}
