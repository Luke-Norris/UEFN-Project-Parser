import { useEffect } from 'react'
import { useForgeStore } from '../stores/forgeStore'
import type { PageId } from '../components/Sidebar/Sidebar'

interface DashboardPageProps {
  onNavigate: (page: PageId) => void
  onProjectChanged?: () => void
}

export function DashboardPage({ onNavigate }: DashboardPageProps) {
  const status = useForgeStore((s) => s.status)
  const statusLoading = useForgeStore((s) => s.statusLoading)
  const statusError = useForgeStore((s) => s.statusError)
  const fetchStatus = useForgeStore((s) => s.fetchStatus)
  const refreshAll = useForgeStore((s) => s.refreshAll)

  useEffect(() => {
    fetchStatus()
  }, [fetchStatus])

  const handleRefresh = () => {
    refreshAll()
  }

  if (statusLoading && !status) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-[11px] text-gray-400">Loading...</div>
      </div>
    )
  }

  if (statusError && !status) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="text-[11px] text-red-400 mb-3">{statusError}</div>
          <button
            onClick={handleRefresh}
            className="px-3 py-1.5 text-[10px] font-medium text-white bg-fn-panel border border-fn-border rounded hover:bg-white/[0.06] transition-colors"
          >
            Retry
          </button>
        </div>
      </div>
    )
  }

  // No project configured — show Get Started
  if (!status?.isConfigured) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center max-w-sm">
          <div className="w-12 h-12 mx-auto mb-4 rounded-xl bg-fn-rare/10 border border-fn-rare/20 flex items-center justify-center">
            <svg className="w-6 h-6 text-fn-rare" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path d="M13 10V3L4 14h7v7l9-11h-7z" />
            </svg>
          </div>
          <h2 className="text-lg font-semibold text-white mb-2">Get Started</h2>
          <p className="text-[11px] text-gray-400 mb-5 leading-relaxed">
            No UEFN project is configured yet. Add a project to start browsing assets, auditing, and managing your maps.
          </p>
          <button
            onClick={() => onNavigate('projects')}
            className="px-4 py-2 text-[11px] font-medium text-white bg-fn-rare/20 border border-fn-rare/30 rounded-lg hover:bg-fn-rare/30 transition-colors"
          >
            Add a Project
          </button>
        </div>
      </div>
    )
  }

  const modeColor =
    status.mode === 'ReadOnly'
      ? 'text-red-400'
      : status.mode === 'Staged'
        ? 'text-yellow-400'
        : status.mode === 'Direct'
          ? 'text-green-400'
          : 'text-gray-400'

  const modeBg =
    status.mode === 'ReadOnly'
      ? 'bg-red-400/10 border-red-400/20'
      : status.mode === 'Staged'
        ? 'bg-yellow-400/10 border-yellow-400/20'
        : status.mode === 'Direct'
          ? 'bg-green-400/10 border-green-400/20'
          : 'bg-fn-panel border-fn-border'

  return (
    <div className="flex-1 bg-fn-darker overflow-y-auto min-h-0">
      <div className="max-w-3xl mx-auto p-6 space-y-5">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-lg font-semibold text-white">Project Dashboard</h1>
            <p className="text-[11px] text-gray-500 mt-0.5">Project overview and status</p>
          </div>
          <button
            onClick={handleRefresh}
            disabled={statusLoading}
            className="flex items-center gap-1.5 px-3 py-1.5 text-[10px] font-medium text-gray-300 bg-fn-panel border border-fn-border rounded hover:bg-white/[0.06] transition-colors disabled:opacity-40"
          >
            <svg className={`w-3.5 h-3.5 ${statusLoading ? 'animate-spin' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
            </svg>
            {statusLoading ? 'Refreshing...' : 'Refresh Project Data'}
          </button>
        </div>

        {/* Status Card */}
        <div className="bg-fn-panel border border-fn-border rounded-lg p-4">
          <div className="flex items-start justify-between">
            <div className="min-w-0">
              <div className="flex items-center gap-2 mb-1">
                <h2 className="text-sm font-semibold text-white truncate">{status.projectName}</h2>
                {status.projectType && (
                  <span className={`inline-block px-1.5 py-0.5 rounded text-[9px] font-medium ${
                    status.projectType === 'Library'
                      ? 'text-blue-400 bg-blue-400/10 border border-blue-400/20'
                      : 'text-fn-rare bg-fn-rare/10 border border-fn-rare/20'
                  }`}>
                    {status.projectType === 'Library' ? 'Library' : 'My Project'}
                  </span>
                )}
              </div>
              <p className="text-[10px] text-gray-500 truncate">{status.projectPath}</p>
            </div>
          </div>

          {/* Mode + UEFN indicator row */}
          <div className="flex items-center gap-3 mt-3 pt-3 border-t border-fn-border">
            <div className={`flex items-center gap-1.5 px-2 py-1 rounded text-[10px] font-medium border ${modeBg}`}>
              <span className={modeColor}>{status.mode}</span>
            </div>

            <div className="flex items-center gap-1.5 text-[10px]">
              <span
                className={`w-1.5 h-1.5 rounded-full ${status.isUefnRunning ? 'bg-green-400' : 'bg-gray-600'}`}
              />
              <span className={status.isUefnRunning ? 'text-green-400' : 'text-gray-500'}>
                UEFN {status.isUefnRunning ? 'Running' : 'Not running'}
              </span>
              {status.isUefnRunning && status.uefnPid && (
                <span className="text-gray-600">PID {status.uefnPid}</span>
              )}
            </div>

            {status.hasUrc && (
              <div className="flex items-center gap-1.5 text-[10px]">
                <span
                  className={`w-1.5 h-1.5 rounded-full ${status.urcActive ? 'bg-yellow-400' : 'bg-gray-600'}`}
                />
                <span className={status.urcActive ? 'text-yellow-400' : 'text-gray-500'}>
                  URC {status.urcActive ? 'Active' : 'Present'}
                </span>
              </div>
            )}

            {(status.stagedFileCount ?? 0) > 0 && (
              <div className="flex items-center gap-1.5 text-[10px] text-yellow-400">
                <span className="w-1.5 h-1.5 rounded-full bg-yellow-400" />
                {status.stagedFileCount} staged
              </div>
            )}
          </div>
        </div>

        {/* Stats Grid */}
        <div className="grid grid-cols-3 gap-3">
          <StatCard label="Assets" value={status.assetCount} icon="assets" />
          <StatCard label="Levels" value={status.levelCount ?? 0} icon="levels" />
          <StatCard label="Verse Files" value={status.verseCount} icon="verse" />
        </div>

        {/* Health Report Link */}
        <button
          onClick={() => onNavigate('project-health')}
          className="w-full flex items-center gap-3 px-4 py-3 bg-fn-panel border border-fn-border rounded-lg hover:bg-white/[0.03] hover:border-fn-border/80 transition-colors group text-left"
        >
          <div className="w-8 h-8 rounded-lg bg-green-400/10 border border-green-400/20 flex items-center justify-center shrink-0 group-hover:bg-green-400/15 transition-colors">
            <svg className="w-4 h-4 text-green-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path d="M4.318 6.318a4.5 4.5 0 000 6.364L12 20.364l7.682-7.682a4.5 4.5 0 00-6.364-6.364L12 7.636l-1.318-1.318a4.5 4.5 0 00-6.364 0z" />
            </svg>
          </div>
          <div className="flex-1 min-w-0">
            <div className="text-[11px] font-medium text-white group-hover:text-green-400 transition-colors">
              View Health Report
            </div>
            <div className="text-[10px] text-gray-500">
              Comprehensive project metrics, actor density, and health score
            </div>
          </div>
          <svg className="w-4 h-4 text-gray-600 group-hover:text-gray-400 transition-colors" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path d="M9 5l7 7-7 7" />
          </svg>
        </button>

        {/* Quick Actions */}
        <div>
          <h3 className="text-[11px] font-semibold text-gray-500 uppercase tracking-wider mb-2">Quick Actions</h3>
          <div className="grid grid-cols-2 gap-2">
            <QuickAction
              label="Widget Editor"
              description="Design UMG widgets visually"
              onClick={() => onNavigate('widget-editor')}
            />
            <QuickAction
              label="Audit Project"
              description="Check for issues and warnings"
              onClick={() => onNavigate('audit')}
            />
            <QuickAction
              label="Browse Levels"
              description="View maps and external actors"
              onClick={() => onNavigate('levels')}
            />
            <QuickAction
              label="Manage Projects"
              description="Add, remove, or switch projects"
              onClick={() => onNavigate('projects')}
            />
          </div>
        </div>
      </div>
    </div>
  )
}

function StatCard({ label, value, icon }: { label: string; value: number; icon: string }) {
  const iconSvg =
    icon === 'assets' ? (
      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
        <path d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4" />
      </svg>
    ) : icon === 'levels' ? (
      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
        <path d="M3.055 11H5a2 2 0 012 2v1a2 2 0 002 2 2 2 0 012 2v2.945M8 3.935V5.5A2.5 2.5 0 0010.5 8h.5a2 2 0 012 2 2 2 0 104 0 2 2 0 012-2h1.064M15 20.488V18a2 2 0 012-2h3.064M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
    ) : (
      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
        <path d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4" />
      </svg>
    )

  return (
    <div className="bg-fn-panel border border-fn-border rounded-lg p-3">
      <div className="flex items-center gap-2 mb-1.5">
        <span className="text-gray-500">{iconSvg}</span>
        <span className="text-[10px] text-gray-500 uppercase tracking-wider">{label}</span>
      </div>
      <div className="text-xl font-semibold text-white">{value.toLocaleString()}</div>
    </div>
  )
}

function QuickAction({
  label,
  description,
  onClick
}: {
  label: string
  description: string
  onClick: () => void
}) {
  return (
    <button
      onClick={onClick}
      className="text-left bg-fn-panel border border-fn-border rounded-lg p-3 hover:bg-white/[0.03] hover:border-fn-border/80 transition-colors group"
    >
      <div className="text-[11px] font-medium text-white group-hover:text-fn-rare transition-colors">
        {label}
      </div>
      <div className="text-[10px] text-gray-500 mt-0.5">{description}</div>
    </button>
  )
}
