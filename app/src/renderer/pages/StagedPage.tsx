import { useEffect, useState } from 'react'
import type { StagedListResult } from '../../shared/types'

export function StagedPage() {
  const [data, setData] = useState<StagedListResult | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionLoading, setActionLoading] = useState<string | null>(null)
  const [confirmAction, setConfirmAction] = useState<'apply' | 'discard' | null>(null)

  useEffect(() => {
    loadStaged()
  }, [])

  async function loadStaged() {
    try {
      setLoading(true)
      setError(null)
      const result = await window.electronAPI.forgeListStaged()
      setData(result)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load staged files')
    } finally {
      setLoading(false)
    }
  }

  async function handleApply() {
    try {
      setActionLoading('apply')
      setConfirmAction(null)
      await window.electronAPI.forgeApplyStaged()
      await loadStaged()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to apply staged changes')
    } finally {
      setActionLoading(null)
    }
  }

  async function handleDiscard() {
    try {
      setActionLoading('discard')
      setConfirmAction(null)
      await window.electronAPI.forgeDiscardStaged()
      await loadStaged()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to discard staged changes')
    } finally {
      setActionLoading(null)
    }
  }

  function formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  }

  if (loading) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-[11px] text-gray-400">Loading staged files...</div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="text-[11px] text-red-400 mb-3">{error}</div>
          <button
            onClick={loadStaged}
            className="px-3 py-1.5 text-[10px] font-medium text-white bg-fn-panel border border-fn-border rounded hover:bg-white/[0.06] transition-colors"
          >
            Retry
          </button>
        </div>
      </div>
    )
  }

  const files = data?.files ?? []

  return (
    <div className="flex-1 bg-fn-darker overflow-y-auto">
      <div className="max-w-3xl mx-auto p-6 space-y-5">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-lg font-semibold text-white">Staged Changes</h1>
            <p className="text-[11px] text-gray-500 mt-0.5">
              Pending modifications from staged writes
            </p>
          </div>
          <button
            onClick={loadStaged}
            className="p-1.5 text-gray-500 hover:text-gray-300 transition-colors rounded hover:bg-white/[0.04]"
            title="Refresh"
          >
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
            </svg>
          </button>
        </div>

        {/* Empty state */}
        {files.length === 0 ? (
          <div className="bg-fn-panel border border-fn-border rounded-lg p-8 text-center">
            <svg className="w-10 h-10 mx-auto mb-3 text-gray-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path d="M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4m0 5c0 2.21-3.582 4-8 4s-8-1.79-8-4" />
            </svg>
            <p className="text-[12px] text-gray-400 mb-1">No staged modifications</p>
            <p className="text-[10px] text-gray-600">
              When UEFN is running, file modifications are staged instead of written directly. They will appear here for review.
            </p>
          </div>
        ) : (
          <>
            {/* Action buttons */}
            <div className="flex items-center gap-3">
              <button
                onClick={() => setConfirmAction('apply')}
                disabled={actionLoading !== null}
                className="px-4 py-2 text-[10px] font-medium text-white bg-green-500/20 border border-green-500/30 rounded-lg hover:bg-green-500/30 transition-colors disabled:opacity-40"
              >
                {actionLoading === 'apply' ? 'Applying...' : 'Apply All'}
              </button>
              <button
                onClick={() => setConfirmAction('discard')}
                disabled={actionLoading !== null}
                className="px-4 py-2 text-[10px] font-medium text-white bg-red-400/20 border border-red-400/30 rounded-lg hover:bg-red-400/30 transition-colors disabled:opacity-40"
              >
                {actionLoading === 'discard' ? 'Discarding...' : 'Discard All'}
              </button>
              <span className="text-[10px] text-gray-500 ml-auto">
                {files.length} file{files.length !== 1 ? 's' : ''} ({formatSize(data?.totalSize ?? 0)})
              </span>
            </div>

            {/* File table */}
            <div className="bg-fn-dark border border-fn-border rounded-lg overflow-hidden">
              {/* Table header */}
              <div className="grid grid-cols-[1fr_auto] gap-4 px-4 py-2 border-b border-fn-border bg-fn-panel">
                <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">File Path</span>
                <span className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">Size</span>
              </div>

              {/* Rows */}
              {files.map((file) => (
                <div
                  key={file.filePath}
                  className="grid grid-cols-[1fr_auto] gap-4 px-4 py-2.5 border-b border-fn-border/30 hover:bg-white/[0.02]"
                >
                  <div className="min-w-0">
                    <span className="text-[11px] text-white truncate block">{file.relativePath}</span>
                    <span className="text-[9px] text-gray-600 truncate block">{file.filePath}</span>
                  </div>
                  <span className="text-[10px] text-gray-500 self-center shrink-0">{formatSize(file.size)}</span>
                </div>
              ))}
            </div>
          </>
        )}
      </div>

      {/* Confirmation Modal */}
      {confirmAction && (
        <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-50">
          <div className="bg-fn-dark border border-fn-border rounded-lg p-6 max-w-sm mx-4">
            <h3 className="text-sm font-semibold text-white mb-2">
              {confirmAction === 'apply' ? 'Apply all staged changes?' : 'Discard all staged changes?'}
            </h3>
            <p className="text-[11px] text-gray-400 mb-4">
              {confirmAction === 'apply'
                ? `This will write ${files.length} staged file${files.length !== 1 ? 's' : ''} to their target locations. Backups will be created automatically.`
                : `This will permanently delete ${files.length} staged file${files.length !== 1 ? 's' : ''}. This cannot be undone.`}
            </p>
            <div className="flex items-center gap-2 justify-end">
              <button
                onClick={() => setConfirmAction(null)}
                className="px-3 py-1.5 text-[10px] font-medium text-gray-400 bg-fn-darker border border-fn-border rounded hover:bg-white/[0.06] transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={confirmAction === 'apply' ? handleApply : handleDiscard}
                className={`px-3 py-1.5 text-[10px] font-medium text-white rounded transition-colors ${
                  confirmAction === 'apply'
                    ? 'bg-green-500/20 border border-green-500/30 hover:bg-green-500/30'
                    : 'bg-red-400/20 border border-red-400/30 hover:bg-red-400/30'
                }`}
              >
                {confirmAction === 'apply' ? 'Apply' : 'Discard'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
