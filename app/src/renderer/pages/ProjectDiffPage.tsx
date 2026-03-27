import { useEffect, useState, useCallback } from 'react'
import type {
  SnapshotSummary,
  SnapshotListResult,
  DiffResult,
  FileDiffEntry,
} from '../../shared/types'

export function ProjectDiffPage() {
  const [snapshots, setSnapshots] = useState<SnapshotSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [snapshotLoading, setSnapshotLoading] = useState(false)
  const [selectedSnapshot, setSelectedSnapshot] = useState<string | null>(null)
  const [diff, setDiff] = useState<DiffResult | null>(null)
  const [diffLoading, setDiffLoading] = useState(false)
  const [description, setDescription] = useState('')
  const [expandedFiles, setExpandedFiles] = useState<Set<string>>(new Set())

  const loadSnapshots = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const result: SnapshotListResult = await window.electronAPI.forgeListSnapshots()
      if ((result as any)?.error) {
        setError((result as any).error.message || 'Failed to load snapshots')
        return
      }
      setSnapshots(result.snapshots ?? [])
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load snapshots')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    loadSnapshots()
  }, [loadSnapshots])

  async function takeSnapshot() {
    try {
      setSnapshotLoading(true)
      setError(null)
      await window.electronAPI.forgeTakeSnapshot(description || undefined)
      setDescription('')
      await loadSnapshots()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to take snapshot')
    } finally {
      setSnapshotLoading(false)
    }
  }

  async function compareSnapshot(snapshotId: string) {
    try {
      setDiffLoading(true)
      setSelectedSnapshot(snapshotId)
      setDiff(null)
      setExpandedFiles(new Set())
      const result = await window.electronAPI.forgeCompareSnapshot(snapshotId)
      if ((result as any)?.error) {
        setError((result as any).error.message || 'Failed to compare snapshot')
        return
      }
      setDiff(result)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to compare snapshot')
    } finally {
      setDiffLoading(false)
    }
  }

  function toggleFile(filePath: string) {
    setExpandedFiles((prev) => {
      const next = new Set(prev)
      if (next.has(filePath)) next.delete(filePath)
      else next.add(filePath)
      return next
    })
  }

  function formatTimestamp(ts: string) {
    try {
      const d = new Date(ts)
      return d.toLocaleString(undefined, {
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
      })
    } catch {
      return ts
    }
  }

  function formatSize(bytes: number | null) {
    if (bytes == null) return '-'
    if (bytes < 1024) return `${bytes} B`
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  }

  function timeAgo(ts: string) {
    try {
      const d = new Date(ts)
      const now = new Date()
      const diffMs = now.getTime() - d.getTime()
      const mins = Math.floor(diffMs / 60000)
      if (mins < 1) return 'just now'
      if (mins < 60) return `${mins}m ago`
      const hrs = Math.floor(mins / 60)
      if (hrs < 24) return `${hrs}h ago`
      const days = Math.floor(hrs / 24)
      return `${days}d ago`
    } catch {
      return ''
    }
  }

  const typeColor: Record<string, string> = {
    Added: 'text-emerald-400',
    Modified: 'text-amber-400',
    Deleted: 'text-red-400',
    Unchanged: 'text-gray-500',
  }

  const typeBg: Record<string, string> = {
    Added: 'bg-emerald-400/10 border-emerald-400/20',
    Modified: 'bg-amber-400/10 border-amber-400/20',
    Deleted: 'bg-red-400/10 border-red-400/20',
  }

  const typeIcon: Record<string, string> = {
    Added: '+',
    Modified: '~',
    Deleted: '-',
  }

  function getFileName(filePath: string) {
    return filePath.split(/[/\\]/).pop() ?? filePath
  }

  function getFileDir(filePath: string) {
    const parts = filePath.split(/[/\\]/)
    return parts.length > 1 ? parts.slice(0, -1).join('/') : ''
  }

  // ─── Loading state ─────────────────────────────────────────────────────────

  if (loading) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="w-5 h-5 mx-auto mb-2 border-2 border-fn-rare/30 border-t-fn-rare rounded-full animate-spin" />
          <div className="text-[11px] text-gray-400">Loading snapshots...</div>
        </div>
      </div>
    )
  }

  // ─── Main layout ───────────────────────────────────────────────────────────

  return (
    <div className="flex-1 flex bg-fn-darker overflow-hidden">
      {/* Left sidebar — snapshot timeline */}
      <div className="w-72 border-r border-fn-border flex flex-col bg-fn-dark">
        {/* Header + create snapshot */}
        <div className="p-3 border-b border-fn-border">
          <h2 className="text-[12px] font-semibold text-white mb-2">Project Diff</h2>
          <p className="text-[10px] text-gray-500 mb-3">
            Snapshot your project before editing in UEFN, then compare to see what changed.
          </p>
          <div className="flex gap-1.5">
            <input
              type="text"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Snapshot description..."
              className="flex-1 px-2 py-1.5 text-[10px] bg-fn-panel border border-fn-border rounded text-white placeholder-gray-600 focus:outline-none focus:border-fn-rare/40"
              onKeyDown={(e) => e.key === 'Enter' && takeSnapshot()}
            />
            <button
              onClick={takeSnapshot}
              disabled={snapshotLoading}
              className="px-2.5 py-1.5 text-[10px] font-medium text-white bg-fn-rare/20 border border-fn-rare/30 rounded hover:bg-fn-rare/30 transition-colors disabled:opacity-50"
            >
              {snapshotLoading ? '...' : 'Snap'}
            </button>
          </div>
        </div>

        {/* Snapshot list */}
        <div className="flex-1 overflow-y-auto min-h-0">
          {snapshots.length === 0 ? (
            <div className="p-4 text-center">
              <div className="text-[10px] text-gray-600 mb-1">No snapshots yet</div>
              <div className="text-[9px] text-gray-700">
                Take your first snapshot to start tracking changes
              </div>
            </div>
          ) : (
            <div className="py-1">
              {snapshots.map((snap) => (
                <button
                  key={snap.id}
                  onClick={() => compareSnapshot(snap.id)}
                  className={`w-full text-left px-3 py-2.5 border-b border-fn-border/50 transition-colors ${
                    selectedSnapshot === snap.id
                      ? 'bg-fn-rare/10 border-l-2 border-l-fn-rare'
                      : 'hover:bg-white/[0.03] border-l-2 border-l-transparent'
                  }`}
                >
                  <div className="flex items-baseline justify-between mb-0.5">
                    <span className="text-[10px] font-medium text-white truncate pr-2">
                      {snap.description || snap.id}
                    </span>
                    <span className="text-[9px] text-gray-600 shrink-0">
                      {timeAgo(snap.timestamp)}
                    </span>
                  </div>
                  <div className="text-[9px] text-gray-500">
                    {formatTimestamp(snap.timestamp)}
                  </div>
                  <div className="flex gap-3 mt-1">
                    <span className="text-[9px] text-gray-600">
                      {snap.fileCount} files
                    </span>
                    <span className="text-[9px] text-gray-600">
                      {snap.uassetCount} assets
                    </span>
                    {snap.verseCount > 0 && (
                      <span className="text-[9px] text-gray-600">
                        {snap.verseCount} verse
                      </span>
                    )}
                  </div>
                </button>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Right side — diff viewer */}
      <div className="flex-1 flex flex-col min-w-0 overflow-hidden">
        {error && (
          <div className="px-4 py-2 bg-red-400/10 border-b border-red-400/20">
            <span className="text-[10px] text-red-400">{error}</span>
          </div>
        )}

        {diffLoading ? (
          <div className="flex-1 flex items-center justify-center">
            <div className="text-center">
              <div className="w-5 h-5 mx-auto mb-2 border-2 border-fn-rare/30 border-t-fn-rare rounded-full animate-spin" />
              <div className="text-[11px] text-gray-400">Comparing to current state...</div>
            </div>
          </div>
        ) : diff ? (
          <>
            {/* Summary bar */}
            <div className="px-4 py-3 border-b border-fn-border bg-fn-dark">
              <div className="flex items-center gap-3 mb-1.5">
                <span className="text-[12px] font-semibold text-white">
                  {diff.description}
                </span>
              </div>
              <div className="flex items-center gap-4 text-[10px]">
                {diff.summary.added > 0 && (
                  <span className="text-emerald-400">
                    +{diff.summary.added} added
                  </span>
                )}
                {diff.summary.modified > 0 && (
                  <span className="text-amber-400">
                    ~{diff.summary.modified} modified
                  </span>
                )}
                {diff.summary.deleted > 0 && (
                  <span className="text-red-400">
                    -{diff.summary.deleted} deleted
                  </span>
                )}
                {diff.summary.totalPropertyChanges > 0 && (
                  <span className="text-gray-400">
                    {diff.summary.totalPropertyChanges} property changes
                  </span>
                )}
              </div>

              {/* Stats summary */}
              {diff.changes.length > 0 && (
                <div className="mt-2 flex flex-wrap gap-2">
                  {(() => {
                    const devicesMoved = diff.changes.filter(
                      (c) =>
                        c.type === 'Modified' &&
                        c.propertyChanges?.some(
                          (p) => p.propertyName === 'RelativeLocation'
                        )
                    ).length
                    const actorsAdded = diff.changes.filter(
                      (c) => c.type === 'Added' && c.actorClass
                    ).length
                    const verseChanged = diff.changes.filter(
                      (c) => c.filePath.endsWith('.verse')
                    ).length

                    return (
                      <>
                        {devicesMoved > 0 && (
                          <span className="px-2 py-0.5 text-[9px] rounded bg-amber-400/10 text-amber-400 border border-amber-400/20">
                            {devicesMoved} devices moved
                          </span>
                        )}
                        {actorsAdded > 0 && (
                          <span className="px-2 py-0.5 text-[9px] rounded bg-emerald-400/10 text-emerald-400 border border-emerald-400/20">
                            {actorsAdded} actors added
                          </span>
                        )}
                        {verseChanged > 0 && (
                          <span className="px-2 py-0.5 text-[9px] rounded bg-blue-400/10 text-blue-400 border border-blue-400/20">
                            {verseChanged} verse files changed
                          </span>
                        )}
                      </>
                    )
                  })()}
                </div>
              )}
            </div>

            {/* File list */}
            <div className="flex-1 overflow-y-auto min-h-0">
              {diff.changes.length === 0 ? (
                <div className="flex items-center justify-center h-full">
                  <div className="text-center">
                    <div className="text-[11px] text-gray-500 mb-1">No changes detected</div>
                    <div className="text-[9px] text-gray-600">
                      Project state matches the selected snapshot
                    </div>
                  </div>
                </div>
              ) : (
                <div className="divide-y divide-fn-border/50">
                  {diff.changes.map((change) => (
                    <FileChangeRow
                      key={change.filePath}
                      change={change}
                      expanded={expandedFiles.has(change.filePath)}
                      onToggle={() => toggleFile(change.filePath)}
                      typeColor={typeColor}
                      typeBg={typeBg}
                      typeIcon={typeIcon}
                      getFileName={getFileName}
                      getFileDir={getFileDir}
                      formatSize={formatSize}
                    />
                  ))}
                </div>
              )}
            </div>
          </>
        ) : (
          <div className="flex-1 flex items-center justify-center">
            <div className="text-center max-w-xs">
              <div className="text-[13px] text-gray-500 mb-2">Select a snapshot to diff</div>
              <div className="text-[10px] text-gray-600 leading-relaxed">
                Take a snapshot before editing in UEFN, then select it here to
                see exactly what changed in your project.
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

// ─── File Change Row ────────────────────────────────────────────────────────

function FileChangeRow({
  change,
  expanded,
  onToggle,
  typeColor,
  typeBg,
  typeIcon,
  getFileName,
  getFileDir,
  formatSize,
}: {
  change: FileDiffEntry
  expanded: boolean
  onToggle: () => void
  typeColor: Record<string, string>
  typeBg: Record<string, string>
  typeIcon: Record<string, string>
  getFileName: (p: string) => string
  getFileDir: (p: string) => string
  formatSize: (b: number | null) => string
}) {
  const hasDetails =
    (change.propertyChanges && change.propertyChanges.length > 0) ||
    change.linesAdded != null ||
    change.linesRemoved != null

  return (
    <div>
      <button
        onClick={hasDetails ? onToggle : undefined}
        className={`w-full text-left px-4 py-2.5 flex items-center gap-3 transition-colors ${
          hasDetails ? 'hover:bg-white/[0.02] cursor-pointer' : 'cursor-default'
        } ${expanded ? 'bg-white/[0.02]' : ''}`}
      >
        {/* Type indicator */}
        <span
          className={`w-5 h-5 flex items-center justify-center text-[11px] font-mono font-bold rounded border ${
            typeBg[change.type] ?? 'bg-gray-400/10 border-gray-400/20'
          } ${typeColor[change.type] ?? 'text-gray-400'}`}
        >
          {typeIcon[change.type] ?? '?'}
        </span>

        {/* File info */}
        <div className="flex-1 min-w-0">
          <div className="flex items-baseline gap-2">
            <span className={`text-[11px] font-medium ${typeColor[change.type] ?? 'text-gray-400'}`}>
              {getFileName(change.filePath)}
            </span>
            {change.actorClass && (
              <span className="text-[9px] text-gray-600 truncate">
                {change.actorClass}
              </span>
            )}
          </div>
          <div className="text-[9px] text-gray-600 truncate">
            {getFileDir(change.filePath)}
          </div>
        </div>

        {/* Size change */}
        <div className="text-[9px] text-gray-600 shrink-0 text-right">
          {change.type === 'Modified' && change.oldSize != null && change.newSize != null ? (
            <>
              <span>{formatSize(change.oldSize)}</span>
              <span className="text-gray-700 mx-1">&rarr;</span>
              <span>{formatSize(change.newSize)}</span>
            </>
          ) : change.type === 'Added' ? (
            <span className="text-emerald-400">{formatSize(change.newSize)}</span>
          ) : change.type === 'Deleted' ? (
            <span className="text-red-400">{formatSize(change.oldSize)}</span>
          ) : null}
        </div>

        {/* Expand arrow */}
        {hasDetails && (
          <svg
            className={`w-3 h-3 text-gray-600 transition-transform ${expanded ? 'rotate-90' : ''}`}
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            strokeWidth={2}
          >
            <path d="M9 5l7 7-7 7" />
          </svg>
        )}
      </button>

      {/* Expanded details */}
      {expanded && hasDetails && (
        <div className="px-4 pb-3 pl-12">
          {/* Property changes */}
          {change.propertyChanges && change.propertyChanges.length > 0 && (
            <div className="space-y-1">
              {change.propertyChanges.map((prop, idx) => (
                <div
                  key={idx}
                  className="flex items-center gap-2 py-1 px-2 rounded bg-fn-panel/50 border border-fn-border/30"
                >
                  <span className="text-[9px] text-gray-500 w-20 shrink-0 truncate" title={prop.actorName}>
                    {prop.actorName}
                  </span>
                  <span className="text-[10px] text-white font-medium shrink-0">
                    {prop.propertyName}
                  </span>
                  <span className="text-[9px] text-red-400/80 line-through truncate">
                    {prop.oldValue ?? 'null'}
                  </span>
                  <svg className="w-3 h-3 text-gray-600 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path d="M14 5l7 7-7 7" />
                  </svg>
                  <span className="text-[9px] text-emerald-400 truncate">
                    {prop.newValue ?? 'null'}
                  </span>
                </div>
              ))}
            </div>
          )}

          {/* Verse line changes */}
          {(change.linesAdded != null || change.linesRemoved != null) && (
            <div className="flex gap-3 py-1 px-2 rounded bg-fn-panel/50 border border-fn-border/30">
              {change.linesAdded != null && change.linesAdded > 0 && (
                <span className="text-[9px] text-emerald-400">
                  +{change.linesAdded} lines
                </span>
              )}
              {change.linesRemoved != null && change.linesRemoved > 0 && (
                <span className="text-[9px] text-red-400">
                  -{change.linesRemoved} lines
                </span>
              )}
              {change.linesAdded === 0 && change.linesRemoved === 0 && (
                <span className="text-[9px] text-gray-500">
                  Content changed (same line count)
                </span>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  )
}
