import { useEffect, useState, useMemo } from 'react'
import { useForgeStore } from '../stores/forgeStore'
import type { AuditResult, AuditFinding } from '../../shared/types'

// ─── Constants ──────────────────────────────────────────────────────────────

const ACTOR_LIMIT_SOFT = 10_000
const ACTOR_LIMIT_HARD = 15_000
const ACTOR_LIMIT_MAX = 20_000 // for progress bar scale

// ─── Component ──────────────────────────────────────────────────────────────

export function ProjectHealthPage() {
  const status = useForgeStore((s) => s.status)
  const statusLoading = useForgeStore((s) => s.statusLoading)
  const fetchStatus = useForgeStore((s) => s.fetchStatus)
  const levels = useForgeStore((s) => s.levels)
  const fetchLevels = useForgeStore((s) => s.fetchLevels)
  const epicAssets = useForgeStore((s) => s.epicAssets)
  const epicAssetsLoading = useForgeStore((s) => s.epicAssetsLoading)
  const fetchEpicAssets = useForgeStore((s) => s.fetchEpicAssets)
  const userAssets = useForgeStore((s) => s.userAssets)
  const userAssetsLoading = useForgeStore((s) => s.userAssetsLoading)
  const fetchUserAssets = useForgeStore((s) => s.fetchUserAssets)

  // Audit data
  const [auditResult, setAuditResult] = useState<AuditResult | null>(null)
  const [auditLoading, setAuditLoading] = useState(false)
  const [auditError, setAuditError] = useState<string | null>(null)

  // Fetch all data
  useEffect(() => {
    fetchStatus()
    fetchLevels()
    fetchEpicAssets()
    fetchUserAssets()
  }, [fetchStatus, fetchLevels, fetchEpicAssets, fetchUserAssets])

  // Run audit
  useEffect(() => {
    let cancelled = false
    async function runAudit() {
      try {
        setAuditLoading(true)
        setAuditError(null)
        const data = await Promise.race([
          window.electronAPI.forgeAudit(),
          new Promise<AuditResult>((_, reject) =>
            setTimeout(() => reject(new Error('Audit timed out')), 30_000)
          ),
        ])
        if (cancelled) return
        if (!data || (data as any).error) {
          setAuditError((data as any)?.error?.message || 'Audit failed')
          return
        }
        setAuditResult(data)
      } catch (err) {
        if (!cancelled) {
          setAuditError(err instanceof Error ? err.message : 'Audit failed')
        }
      } finally {
        if (!cancelled) setAuditLoading(false)
      }
    }
    runAudit()
    return () => { cancelled = true }
  }, [])

  // ─── Derived metrics ────────────────────────────────────────────────────

  const totalActors = epicAssets?.totalPlaced ?? 0
  const deviceCount = epicAssets?.deviceCount ?? 0
  const propCount = epicAssets?.propCount ?? 0
  const uniqueTypes = epicAssets?.uniqueTypes ?? 0
  const userAssetCount = userAssets?.totalCount ?? 0
  const levelCount = status?.levelCount ?? levels?.length ?? 0
  const verseCount = status?.verseCount ?? 0
  const assetCount = status?.assetCount ?? 0

  // Top device types
  const topDeviceTypes = useMemo(() => {
    if (!epicAssets?.types) return []
    return [...epicAssets.types]
      .filter((t) => t.isDevice)
      .sort((a, b) => b.count - a.count)
      .slice(0, 5)
  }, [epicAssets])

  // Largest user assets
  const largestAssets = useMemo(() => {
    if (!userAssets?.assets) return []
    return [...userAssets.assets]
      .sort((a, b) => b.size - a.size)
      .slice(0, 5)
  }, [userAssets])

  // Audit findings
  const findings: AuditFinding[] = useMemo(() => {
    const raw = auditResult?.findings ?? []
    return raw.map((f: any) => ({
      ...f,
      severity:
        typeof f.severity === 'number'
          ? (['Info', 'Warning', 'Error'][f.severity] ?? 'Info') as AuditFinding['severity']
          : f.severity ?? 'Info',
    }))
  }, [auditResult])

  const errorCount = findings.filter((f) => f.severity === 'Error').length
  const warnCount = findings.filter((f) => f.severity === 'Warning').length

  // ─── Health Score Calculation ───────────────────────────────────────────

  const healthScore = useMemo(() => {
    let score = 100

    // Actor count penalty
    if (totalActors > ACTOR_LIMIT_HARD) {
      score -= 30
    } else if (totalActors > ACTOR_LIMIT_SOFT) {
      score -= 15
    }

    // Audit error penalty
    score -= Math.min(errorCount * 10, 30)

    // Audit warning penalty
    score -= Math.min(warnCount * 3, 15)

    // No verse files = slight penalty (no scripting)
    if (verseCount === 0 && assetCount > 0) {
      score -= 5
    }

    // Device/prop balance — extreme ratios penalized
    if (totalActors > 0) {
      const deviceRatio = deviceCount / totalActors
      if (deviceRatio > 0.8 || deviceRatio < 0.02) {
        score -= 5
      }
    }

    return Math.max(0, Math.min(100, score))
  }, [totalActors, errorCount, warnCount, verseCount, assetCount, deviceCount])

  const healthColor =
    healthScore >= 80 ? 'text-green-400' :
    healthScore >= 60 ? 'text-yellow-400' :
    healthScore >= 40 ? 'text-orange-400' : 'text-red-400'

  const healthBg =
    healthScore >= 80 ? 'bg-green-400' :
    healthScore >= 60 ? 'bg-yellow-400' :
    healthScore >= 40 ? 'bg-orange-400' : 'bg-red-400'

  const healthLabel =
    healthScore >= 80 ? 'Healthy' :
    healthScore >= 60 ? 'Fair' :
    healthScore >= 40 ? 'Needs Attention' : 'Critical'

  // ─── Operation mode ─────────────────────────────────────────────────────

  const modeColor =
    status?.mode === 'ReadOnly' ? 'text-red-400 bg-red-400/10 border-red-400/20' :
    status?.mode === 'Staged' ? 'text-yellow-400 bg-yellow-400/10 border-yellow-400/20' :
    status?.mode === 'Direct' ? 'text-green-400 bg-green-400/10 border-green-400/20' :
    'text-gray-400 bg-fn-panel border-fn-border'

  // Actor density color
  const actorDensityColor =
    totalActors > ACTOR_LIMIT_HARD ? 'text-red-400' :
    totalActors > ACTOR_LIMIT_SOFT ? 'text-yellow-400' : 'text-green-400'

  const actorDensityBg =
    totalActors > ACTOR_LIMIT_HARD ? 'bg-red-400' :
    totalActors > ACTOR_LIMIT_SOFT ? 'bg-yellow-400' : 'bg-green-400'

  // Loading state
  const isLoading = statusLoading && !status

  if (isLoading) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="w-5 h-5 mx-auto mb-2 border-2 border-fn-rare/30 border-t-fn-rare rounded-full animate-spin" />
          <div className="text-[11px] text-gray-400">Loading health data...</div>
        </div>
      </div>
    )
  }

  if (!status?.isConfigured) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <p className="text-[11px] text-gray-500">No project configured</p>
        </div>
      </div>
    )
  }

  // ─── Device / Prop ratio for pie chart ──────────────────────────────────

  const devicePercent = totalActors > 0 ? Math.round((deviceCount / totalActors) * 100) : 0
  const propPercent = totalActors > 0 ? 100 - devicePercent : 0

  return (
    <div className="flex-1 bg-fn-darker overflow-y-auto">
      <div className="max-w-4xl mx-auto p-6 space-y-5">
        {/* Header */}
        <div>
          <h1 className="text-lg font-semibold text-white">Project Health</h1>
          <p className="text-[11px] text-gray-500 mt-0.5">{status.projectName}</p>
        </div>

        {/* Top Row: Health Score + Mode + Actor Density */}
        <div className="grid grid-cols-3 gap-3">
          {/* Health Score */}
          <div className="bg-fn-panel border border-fn-border rounded-lg p-4">
            <div className="flex items-center gap-2 mb-3">
              <svg className="w-4 h-4 text-gray-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path d="M4.318 6.318a4.5 4.5 0 000 6.364L12 20.364l7.682-7.682a4.5 4.5 0 00-6.364-6.364L12 7.636l-1.318-1.318a4.5 4.5 0 00-6.364 0z" />
              </svg>
              <span className="text-[10px] text-gray-500 uppercase tracking-wider">Health Score</span>
            </div>
            <div className="flex items-end gap-2">
              <span className={`text-3xl font-bold tabular-nums ${healthColor}`}>{healthScore}</span>
              <span className="text-[11px] text-gray-600 mb-1">/100</span>
            </div>
            <div className="mt-2">
              <div className="w-full h-1.5 rounded-full bg-fn-darker overflow-hidden">
                <div
                  className={`h-full rounded-full transition-all duration-500 ${healthBg}`}
                  style={{ width: `${healthScore}%` }}
                />
              </div>
              <p className={`text-[10px] mt-1 ${healthColor}`}>{healthLabel}</p>
            </div>
          </div>

          {/* Operation Mode */}
          <div className="bg-fn-panel border border-fn-border rounded-lg p-4">
            <div className="flex items-center gap-2 mb-3">
              <svg className="w-4 h-4 text-gray-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
              </svg>
              <span className="text-[10px] text-gray-500 uppercase tracking-wider">Operation Mode</span>
            </div>
            <div className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-xs font-semibold border ${modeColor}`}>
              {status.mode}
            </div>
            <div className="mt-2 space-y-1">
              <div className="flex items-center gap-1.5 text-[10px]">
                <span className={`w-1.5 h-1.5 rounded-full ${status.isUefnRunning ? 'bg-green-400' : 'bg-gray-600'}`} />
                <span className={status.isUefnRunning ? 'text-green-400' : 'text-gray-500'}>
                  UEFN {status.isUefnRunning ? 'Running' : 'Not running'}
                </span>
              </div>
              {(status.stagedFileCount ?? 0) > 0 && (
                <div className="flex items-center gap-1.5 text-[10px] text-yellow-400">
                  <span className="w-1.5 h-1.5 rounded-full bg-yellow-400" />
                  {status.stagedFileCount} staged changes
                </div>
              )}
            </div>
          </div>

          {/* Actor Density */}
          <div className="bg-fn-panel border border-fn-border rounded-lg p-4">
            <div className="flex items-center gap-2 mb-3">
              <svg className="w-4 h-4 text-gray-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path d="M3.055 11H5a2 2 0 012 2v1a2 2 0 002 2 2 2 0 012 2v2.945M8 3.935V5.5A2.5 2.5 0 0010.5 8h.5a2 2 0 012 2 2 2 0 104 0 2 2 0 012-2h1.064M15 20.488V18a2 2 0 012-2h3.064M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              <span className="text-[10px] text-gray-500 uppercase tracking-wider">Actor Density</span>
            </div>
            <div className="flex items-end gap-2">
              <span className={`text-2xl font-bold tabular-nums ${actorDensityColor}`}>
                {totalActors.toLocaleString()}
              </span>
              <span className="text-[10px] text-gray-600 mb-1">actors</span>
            </div>
            <div className="mt-2">
              <div className="w-full h-1.5 rounded-full bg-fn-darker overflow-hidden relative">
                <div
                  className={`h-full rounded-full transition-all duration-500 ${actorDensityBg}`}
                  style={{ width: `${Math.min(100, (totalActors / ACTOR_LIMIT_MAX) * 100)}%` }}
                />
                {/* Threshold markers */}
                <div
                  className="absolute top-0 h-full w-px bg-yellow-400/40"
                  style={{ left: `${(ACTOR_LIMIT_SOFT / ACTOR_LIMIT_MAX) * 100}%` }}
                />
                <div
                  className="absolute top-0 h-full w-px bg-red-400/40"
                  style={{ left: `${(ACTOR_LIMIT_HARD / ACTOR_LIMIT_MAX) * 100}%` }}
                />
              </div>
              <div className="flex justify-between mt-1 text-[9px] text-gray-600">
                <span>0</span>
                <span className="text-yellow-400/60">10K</span>
                <span className="text-red-400/60">15K</span>
                <span>20K</span>
              </div>
            </div>
          </div>
        </div>

        {/* Stats Row */}
        <div className="grid grid-cols-5 gap-3">
          <MiniStatCard label="Total Assets" value={assetCount} icon="assets" />
          <MiniStatCard label="User Created" value={userAssetCount} icon="user" loading={userAssetsLoading} />
          <MiniStatCard label="Levels" value={levelCount} icon="levels" />
          <MiniStatCard label="Verse Files" value={verseCount} icon="verse" />
          <MiniStatCard label="Unique Types" value={uniqueTypes} icon="types" loading={epicAssetsLoading} />
        </div>

        {/* Middle Row: Device/Prop Ratio + Top Devices + Audit Summary */}
        <div className="grid grid-cols-3 gap-3">
          {/* Device/Prop Pie */}
          <div className="bg-fn-panel border border-fn-border rounded-lg p-4">
            <h3 className="text-[10px] text-gray-500 uppercase tracking-wider mb-3">Device / Prop Ratio</h3>
            {totalActors > 0 ? (
              <>
                <div className="flex items-center justify-center mb-3">
                  <div className="relative w-20 h-20">
                    <svg viewBox="0 0 36 36" className="w-20 h-20 -rotate-90">
                      <circle
                        cx="18" cy="18" r="15.915"
                        fill="none" stroke="currentColor"
                        className="text-fn-darker"
                        strokeWidth="3"
                      />
                      <circle
                        cx="18" cy="18" r="15.915"
                        fill="none" stroke="currentColor"
                        className="text-blue-400"
                        strokeWidth="3"
                        strokeDasharray={`${devicePercent} ${100 - devicePercent}`}
                        strokeDashoffset="0"
                        strokeLinecap="round"
                      />
                      <circle
                        cx="18" cy="18" r="15.915"
                        fill="none" stroke="currentColor"
                        className="text-emerald-400"
                        strokeWidth="3"
                        strokeDasharray={`${propPercent} ${100 - propPercent}`}
                        strokeDashoffset={`${-devicePercent}`}
                        strokeLinecap="round"
                      />
                    </svg>
                    <div className="absolute inset-0 flex items-center justify-center">
                      <span className="text-[10px] font-semibold text-white">{totalActors.toLocaleString()}</span>
                    </div>
                  </div>
                </div>
                <div className="space-y-1.5">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-1.5">
                      <span className="w-2 h-2 rounded-full bg-blue-400" />
                      <span className="text-[10px] text-gray-400">Devices</span>
                    </div>
                    <span className="text-[10px] font-medium text-white tabular-nums">
                      {deviceCount.toLocaleString()} ({devicePercent}%)
                    </span>
                  </div>
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-1.5">
                      <span className="w-2 h-2 rounded-full bg-emerald-400" />
                      <span className="text-[10px] text-gray-400">Props</span>
                    </div>
                    <span className="text-[10px] font-medium text-white tabular-nums">
                      {propCount.toLocaleString()} ({propPercent}%)
                    </span>
                  </div>
                </div>
              </>
            ) : (
              <div className="text-center py-6">
                <p className="text-[10px] text-gray-600">No actor data available</p>
              </div>
            )}
          </div>

          {/* Top Device Types */}
          <div className="bg-fn-panel border border-fn-border rounded-lg p-4">
            <h3 className="text-[10px] text-gray-500 uppercase tracking-wider mb-3">Top Device Types</h3>
            {epicAssetsLoading ? (
              <div className="flex items-center justify-center py-6">
                <div className="w-4 h-4 border-2 border-fn-rare/30 border-t-fn-rare rounded-full animate-spin" />
              </div>
            ) : topDeviceTypes.length > 0 ? (
              <div className="space-y-2">
                {topDeviceTypes.map((dt, i) => {
                  const maxCount = topDeviceTypes[0]?.count ?? 1
                  const pct = (dt.count / maxCount) * 100
                  return (
                    <div key={i}>
                      <div className="flex items-center justify-between mb-0.5">
                        <span className="text-[10px] text-gray-300 truncate flex-1 mr-2">
                          {dt.displayName || dt.typeName}
                        </span>
                        <span className="text-[10px] text-gray-500 tabular-nums shrink-0">
                          {dt.count}
                        </span>
                      </div>
                      <div className="w-full h-1 rounded-full bg-fn-darker overflow-hidden">
                        <div
                          className="h-full rounded-full bg-blue-400/60"
                          style={{ width: `${pct}%` }}
                        />
                      </div>
                    </div>
                  )
                })}
              </div>
            ) : (
              <div className="text-center py-6">
                <p className="text-[10px] text-gray-600">No device data</p>
              </div>
            )}
          </div>

          {/* Audit Summary */}
          <div className="bg-fn-panel border border-fn-border rounded-lg p-4">
            <h3 className="text-[10px] text-gray-500 uppercase tracking-wider mb-3">Audit Summary</h3>
            {auditLoading ? (
              <div className="flex items-center justify-center py-6">
                <div className="w-4 h-4 border-2 border-fn-rare/30 border-t-fn-rare rounded-full animate-spin" />
                <span className="text-[10px] text-gray-500 ml-2">Running audit...</span>
              </div>
            ) : auditError ? (
              <div className="text-center py-6">
                <p className="text-[10px] text-gray-600">{auditError}</p>
              </div>
            ) : (
              <div className="space-y-3">
                <div className="flex items-center gap-3">
                  <span className={`inline-flex items-center gap-1 px-2 py-1 rounded text-[10px] font-semibold border ${
                    errorCount > 0
                      ? 'text-red-400 bg-red-400/10 border-red-400/20'
                      : warnCount > 0
                        ? 'text-yellow-400 bg-yellow-400/10 border-yellow-400/20'
                        : 'text-green-400 bg-green-400/10 border-green-400/20'
                  }`}>
                    {errorCount > 0 ? 'Issues Found' : warnCount > 0 ? 'Warnings' : 'All Clear'}
                  </span>
                </div>

                <div className="space-y-1.5">
                  <AuditRow label="Errors" count={errorCount} color="text-red-400" bg="bg-red-400" />
                  <AuditRow label="Warnings" count={warnCount} color="text-yellow-400" bg="bg-yellow-400" />
                  <AuditRow
                    label="Info"
                    count={findings.filter((f) => f.severity === 'Info').length}
                    color="text-blue-400"
                    bg="bg-blue-400"
                  />
                </div>

                <div className="text-[10px] text-gray-600 pt-1 border-t border-fn-border/30">
                  {findings.length} total finding{findings.length !== 1 ? 's' : ''}
                </div>
              </div>
            )}
          </div>
        </div>

        {/* Bottom Row: Largest Assets */}
        <div className="bg-fn-panel border border-fn-border rounded-lg p-4">
          <h3 className="text-[10px] text-gray-500 uppercase tracking-wider mb-3">Largest User Assets</h3>
          {userAssetsLoading ? (
            <div className="flex items-center justify-center py-4">
              <div className="w-4 h-4 border-2 border-fn-rare/30 border-t-fn-rare rounded-full animate-spin" />
            </div>
          ) : largestAssets.length > 0 ? (
            <div className="grid grid-cols-5 gap-3">
              {largestAssets.map((asset, i) => {
                const maxSize = largestAssets[0]?.size ?? 1
                const pct = (asset.size / maxSize) * 100
                return (
                  <div key={i} className="bg-fn-darker rounded-lg p-2.5">
                    <p className="text-[10px] text-gray-300 truncate mb-1" title={asset.name}>
                      {asset.name}
                    </p>
                    <p className="text-[9px] text-gray-600 truncate mb-2">{asset.assetClass}</p>
                    <div className="w-full h-1 rounded-full bg-fn-dark overflow-hidden mb-1">
                      <div
                        className="h-full rounded-full bg-purple-400/60"
                        style={{ width: `${pct}%` }}
                      />
                    </div>
                    <p className="text-[10px] font-medium text-gray-400 tabular-nums">
                      {formatFileSize(asset.size)}
                    </p>
                  </div>
                )
              })}
            </div>
          ) : (
            <p className="text-[10px] text-gray-600 text-center py-4">No user asset data available</p>
          )}
        </div>

        {/* Health Breakdown */}
        <div className="bg-fn-panel border border-fn-border rounded-lg p-4">
          <h3 className="text-[10px] text-gray-500 uppercase tracking-wider mb-3">Health Score Breakdown</h3>
          <div className="grid grid-cols-2 gap-x-6 gap-y-2">
            <HealthCheckRow
              label="Actor count within limits"
              passed={totalActors <= ACTOR_LIMIT_SOFT}
              warning={totalActors > ACTOR_LIMIT_SOFT && totalActors <= ACTOR_LIMIT_HARD}
              detail={
                totalActors > ACTOR_LIMIT_HARD
                  ? `${totalActors.toLocaleString()} actors (over 15K limit)`
                  : totalActors > ACTOR_LIMIT_SOFT
                    ? `${totalActors.toLocaleString()} actors (approaching limit)`
                    : `${totalActors.toLocaleString()} actors`
              }
            />
            <HealthCheckRow
              label="No audit errors"
              passed={errorCount === 0}
              detail={errorCount > 0 ? `${errorCount} error${errorCount !== 1 ? 's' : ''} found` : 'Clean'}
            />
            <HealthCheckRow
              label="No audit warnings"
              passed={warnCount === 0}
              warning={warnCount > 0 && warnCount <= 3}
              detail={warnCount > 0 ? `${warnCount} warning${warnCount !== 1 ? 's' : ''}` : 'Clean'}
            />
            <HealthCheckRow
              label="Verse files present"
              passed={verseCount > 0}
              detail={verseCount > 0 ? `${verseCount} file${verseCount !== 1 ? 's' : ''}` : 'No Verse files'}
            />
            <HealthCheckRow
              label="Balanced device/prop ratio"
              passed={totalActors === 0 || (devicePercent >= 2 && devicePercent <= 80)}
              detail={totalActors > 0 ? `${devicePercent}% devices / ${propPercent}% props` : 'N/A'}
            />
            <HealthCheckRow
              label="User assets defined"
              passed={userAssetCount > 0}
              detail={`${userAssetCount} user-created asset${userAssetCount !== 1 ? 's' : ''}`}
            />
          </div>
        </div>
      </div>
    </div>
  )
}

// ─── Sub-components ─────────────────────────────────────────────────────────

function MiniStatCard({
  label,
  value,
  icon,
  loading,
}: {
  label: string
  value: number
  icon: string
  loading?: boolean
}) {
  const iconSvg =
    icon === 'assets' ? (
      <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
        <path d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4" />
      </svg>
    ) : icon === 'levels' ? (
      <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
        <path d="M3.055 11H5a2 2 0 012 2v1a2 2 0 002 2 2 2 0 012 2v2.945M8 3.935V5.5A2.5 2.5 0 0010.5 8h.5a2 2 0 012 2 2 2 0 104 0 2 2 0 012-2h1.064M15 20.488V18a2 2 0 012-2h3.064M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
    ) : icon === 'verse' ? (
      <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
        <path d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4" />
      </svg>
    ) : icon === 'user' ? (
      <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
        <path d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4" />
      </svg>
    ) : (
      <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
        <path d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
      </svg>
    )

  return (
    <div className="bg-fn-panel border border-fn-border rounded-lg p-3">
      <div className="flex items-center gap-1.5 mb-1">
        <span className="text-gray-600">{iconSvg}</span>
        <span className="text-[9px] text-gray-600 uppercase tracking-wider">{label}</span>
      </div>
      {loading ? (
        <div className="w-4 h-4 border-2 border-fn-rare/20 border-t-fn-rare rounded-full animate-spin mt-1" />
      ) : (
        <div className="text-lg font-semibold text-white tabular-nums">{value.toLocaleString()}</div>
      )}
    </div>
  )
}

function AuditRow({ label, count, color, bg }: { label: string; count: number; color: string; bg: string }) {
  return (
    <div className="flex items-center justify-between">
      <div className="flex items-center gap-1.5">
        <span className={`w-2 h-2 rounded-full ${bg}`} />
        <span className="text-[10px] text-gray-400">{label}</span>
      </div>
      <span className={`text-[11px] font-medium tabular-nums ${count > 0 ? color : 'text-gray-600'}`}>
        {count}
      </span>
    </div>
  )
}

function HealthCheckRow({
  label,
  passed,
  warning,
  detail,
}: {
  label: string
  passed: boolean
  warning?: boolean
  detail: string
}) {
  return (
    <div className="flex items-center gap-2 py-1">
      <span className="shrink-0">
        {passed ? (
          <svg className="w-3.5 h-3.5 text-green-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path d="M5 13l4 4L19 7" />
          </svg>
        ) : warning ? (
          <svg className="w-3.5 h-3.5 text-yellow-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4.5c-.77-.833-2.694-.833-3.464 0L3.34 16.5c-.77.833.192 2.5 1.732 2.5z" />
          </svg>
        ) : (
          <svg className="w-3.5 h-3.5 text-red-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path d="M6 18L18 6M6 6l12 12" />
          </svg>
        )}
      </span>
      <div className="flex-1 min-w-0">
        <span className="text-[10px] text-gray-300">{label}</span>
        <span className="text-[9px] text-gray-600 ml-2">{detail}</span>
      </div>
    </div>
  )
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}
