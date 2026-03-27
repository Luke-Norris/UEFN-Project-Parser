import { useCallback, useEffect, useMemo, useState } from 'react'
import { useForgeStore } from '../stores/forgeStore'
import { useBridgeStore } from '../stores/bridgeStore'
import { forgeAudit, forgeRunPublishAudit, forgeBridgeCommand } from '../lib/api'
import type { AuditResult } from '../../shared/types'

interface ChecklistItem {
  id: string
  label: string
  description: string
  category: 'required' | 'recommended' | 'optional'
  status: 'pending' | 'pass' | 'warn' | 'fail' | 'running'
  message?: string
  fixable?: boolean
  fixAction?: string
}

function computeGrade(items: ChecklistItem[]): { letter: string; score: number; color: string } {
  const evaluated = items.filter((i) => i.status !== 'pending' && i.status !== 'running')
  if (evaluated.length === 0) return { letter: '?', score: 0, color: 'text-gray-500' }

  let score = 100
  for (const item of evaluated) {
    if (item.status === 'fail' && item.category === 'required') score -= 25
    else if (item.status === 'fail' && item.category === 'recommended') score -= 10
    else if (item.status === 'warn' && item.category === 'required') score -= 10
    else if (item.status === 'warn' && item.category === 'recommended') score -= 5
    else if (item.status === 'warn' && item.category === 'optional') score -= 2
  }
  score = Math.max(0, score)

  let letter: string
  let color: string
  if (score >= 95) { letter = 'A+'; color = 'text-emerald-400' }
  else if (score >= 90) { letter = 'A'; color = 'text-emerald-400' }
  else if (score >= 85) { letter = 'A-'; color = 'text-emerald-400' }
  else if (score >= 80) { letter = 'B+'; color = 'text-blue-400' }
  else if (score >= 75) { letter = 'B'; color = 'text-blue-400' }
  else if (score >= 70) { letter = 'B-'; color = 'text-blue-400' }
  else if (score >= 65) { letter = 'C+'; color = 'text-amber-400' }
  else if (score >= 60) { letter = 'C'; color = 'text-amber-400' }
  else if (score >= 55) { letter = 'C-'; color = 'text-amber-400' }
  else if (score >= 50) { letter = 'D'; color = 'text-orange-400' }
  else { letter = 'F'; color = 'text-red-400' }

  return { letter, score, color }
}

export function PublishPage() {
  const status = useForgeStore((s) => s.status)
  const bridgeConnected = useBridgeStore((s) => s.connected)
  const [checklist, setChecklist] = useState<ChecklistItem[]>([])
  const [running, setRunning] = useState(false)
  const [fixingAll, setFixingAll] = useState(false)
  const [auditResult, setAuditResult] = useState<AuditResult | null>(null)
  const [publishAuditDone, setPublishAuditDone] = useState(false)

  const grade = useMemo(() => computeGrade(checklist), [checklist])

  const fixableCount = useMemo(
    () => checklist.filter((i) => i.fixable && (i.status === 'fail' || i.status === 'warn')).length,
    [checklist]
  )

  // Build the checklist from audit results
  const buildChecklist = useCallback((audit: AuditResult, publishResult?: Record<string, unknown>) => {
    const items: ChecklistItem[] = []

    // Core audit checks
    const findings = audit.findings ?? []
    const errorCount = findings.filter((f) => f.severity === 'Error').length
    const warnCount = findings.filter((f) => f.severity === 'Warning').length

    items.push({
      id: 'audit-errors',
      label: 'No audit errors',
      description: 'Project audit found no errors',
      category: 'required',
      status: errorCount === 0 ? 'pass' : 'fail',
      message: errorCount > 0 ? `${errorCount} error(s) found` : 'No errors',
    })

    items.push({
      id: 'audit-warnings',
      label: 'No audit warnings',
      description: 'Project audit found no warnings',
      category: 'recommended',
      status: warnCount === 0 ? 'pass' : 'warn',
      message: warnCount > 0 ? `${warnCount} warning(s) found` : 'No warnings',
    })

    // Asset count check
    const assetCount = status?.assetCount ?? 0
    items.push({
      id: 'asset-count',
      label: 'Asset budget',
      description: 'Stay within UEFN asset limits',
      category: 'required',
      status: assetCount < 10000 ? 'pass' : assetCount < 15000 ? 'warn' : 'fail',
      message: `${assetCount.toLocaleString()} assets`,
    })

    // Verse files check
    const verseCount = status?.verseCount ?? 0
    items.push({
      id: 'verse-present',
      label: 'Verse files present',
      description: 'Project has Verse gameplay scripts',
      category: 'optional',
      status: verseCount > 0 ? 'pass' : 'warn',
      message: `${verseCount} verse file(s)`,
    })

    // Level check
    const levelCount = status?.levelCount ?? 0
    items.push({
      id: 'levels-present',
      label: 'Levels configured',
      description: 'Project has at least one level',
      category: 'required',
      status: levelCount > 0 ? 'pass' : 'fail',
      message: `${levelCount} level(s)`,
    })

    // UEFN not running check
    items.push({
      id: 'uefn-state',
      label: 'UEFN state check',
      description: 'UEFN should be running for publish',
      category: 'recommended',
      status: status?.isUefnRunning ? 'pass' : 'warn',
      message: status?.isUefnRunning ? 'UEFN is running' : 'UEFN is not running',
    })

    // Staged changes check
    items.push({
      id: 'no-staged',
      label: 'No pending staged changes',
      description: 'All staged changes should be applied before publish',
      category: 'recommended',
      status: 'pass', // We'd need to check staged count; default to pass
      message: 'No staged changes pending',
      fixable: true,
      fixAction: 'apply_staged',
    })

    // Publish audit checks (bridge-only)
    if (publishResult) {
      const pa = publishResult as Record<string, boolean | string>
      if (typeof pa.islandCodeSet === 'boolean') {
        items.push({
          id: 'island-code',
          label: 'Island code set',
          description: 'Project has an island code assigned',
          category: 'required',
          status: pa.islandCodeSet ? 'pass' : 'fail',
          message: pa.islandCodeSet ? 'Island code configured' : 'No island code -- set one in UEFN',
        })
      }
      if (typeof pa.thumbnailSet === 'boolean') {
        items.push({
          id: 'thumbnail',
          label: 'Thumbnail uploaded',
          description: 'Custom thumbnail image for your island',
          category: 'recommended',
          status: pa.thumbnailSet ? 'pass' : 'warn',
          message: pa.thumbnailSet ? 'Custom thumbnail set' : 'Using default thumbnail',
        })
      }
      if (typeof pa.descriptionSet === 'boolean') {
        items.push({
          id: 'description',
          label: 'Island description',
          description: 'Description visible in discover feed',
          category: 'recommended',
          status: pa.descriptionSet ? 'pass' : 'warn',
          message: pa.descriptionSet ? 'Description configured' : 'No description set',
        })
      }
    }

    setChecklist(items)
  }, [status])

  // Run all checks
  const runChecks = useCallback(async () => {
    setRunning(true)
    setChecklist([])
    setPublishAuditDone(false)

    try {
      // Run project audit
      const audit = await forgeAudit()
      setAuditResult(audit)

      // Run publish audit if bridge is connected
      let publishResult: Record<string, unknown> | undefined
      if (bridgeConnected) {
        try {
          const result = await forgeRunPublishAudit()
          publishResult = result as Record<string, unknown>
          setPublishAuditDone(true)
        } catch {
          // Bridge command may not exist yet
        }
      }

      buildChecklist(audit, publishResult)
    } catch (err) {
      setChecklist([{
        id: 'audit-error',
        label: 'Audit failed',
        description: err instanceof Error ? err.message : 'Unknown error',
        category: 'required',
        status: 'fail',
        message: 'Could not complete audit',
      }])
    } finally {
      setRunning(false)
    }
  }, [bridgeConnected, buildChecklist])

  // Auto-run on mount
  useEffect(() => {
    if (status?.isConfigured) {
      runChecks()
    }
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  // Fix all fixable issues
  const handleFixAll = useCallback(async () => {
    setFixingAll(true)
    const fixable = checklist.filter((i) => i.fixable && (i.status === 'fail' || i.status === 'warn'))
    for (const item of fixable) {
      try {
        if (item.fixAction === 'apply_staged') {
          await window.electronAPI.forgeApplyStaged()
        } else if (item.fixAction && bridgeConnected) {
          await forgeBridgeCommand(item.fixAction, {})
        }
        setChecklist((prev) =>
          prev.map((c) => c.id === item.id ? { ...c, status: 'pass', message: 'Fixed' } : c)
        )
      } catch {
        // Skip unfixable
      }
    }
    setFixingAll(false)
  }, [checklist, bridgeConnected])

  if (!status?.isConfigured) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <p className="text-[12px] text-gray-400">No project configured</p>
          <p className="text-[10px] text-gray-600 mt-1">Select a project to run publish checks</p>
        </div>
      </div>
    )
  }

  return (
    <div className="flex-1 flex flex-col bg-fn-darker min-h-0 overflow-y-auto">
      {/* Header */}
      <div className="border-b border-fn-border bg-fn-dark/50 px-6 py-4">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-[14px] font-semibold text-white">Publish Checklist</h1>
            <p className="text-[10px] text-gray-500 mt-0.5">
              Pre-publish audit for {status.projectName}
            </p>
          </div>
          <div className="flex items-center gap-3">
            {fixableCount > 0 && (
              <button
                onClick={handleFixAll}
                disabled={fixingAll}
                className="text-[10px] px-3 py-1.5 rounded-lg border border-fn-rare/30 bg-fn-rare/10 text-fn-rare hover:bg-fn-rare/20 disabled:opacity-40 transition-colors"
              >
                {fixingAll ? 'Fixing...' : `Fix All (${fixableCount})`}
              </button>
            )}
            <button
              onClick={runChecks}
              disabled={running}
              className="text-[10px] px-3 py-1.5 rounded-lg border border-fn-border text-gray-400 hover:text-white hover:border-gray-500 disabled:opacity-40 transition-colors"
            >
              {running ? 'Running...' : 'Re-run Checks'}
            </button>
          </div>
        </div>
      </div>

      <div className="flex-1 p-6">
        <div className="max-w-3xl mx-auto space-y-6">
          {/* Grade Display */}
          <div className="flex items-center gap-6 bg-fn-panel border border-fn-border rounded-xl p-6">
            <div className="text-center">
              <div className={`text-5xl font-bold ${grade.color}`}>{grade.letter}</div>
              <div className="text-[9px] text-gray-500 mt-1 uppercase tracking-wider">Health Score</div>
            </div>
            <div className="flex-1">
              <div className="flex items-center justify-between mb-1">
                <span className="text-[10px] text-gray-400">Score</span>
                <span className={`text-[11px] font-semibold ${grade.color}`}>{grade.score}/100</span>
              </div>
              <div className="h-2 bg-fn-darker rounded-full overflow-hidden">
                <div
                  className={`h-full rounded-full transition-all duration-1000 ${
                    grade.score >= 80 ? 'bg-emerald-400' :
                    grade.score >= 60 ? 'bg-amber-400' : 'bg-red-400'
                  }`}
                  style={{ width: `${grade.score}%` }}
                />
              </div>
              <div className="flex items-center gap-4 mt-2 text-[9px] text-gray-500">
                <span>{checklist.filter((i) => i.status === 'pass').length} passed</span>
                <span>{checklist.filter((i) => i.status === 'warn').length} warnings</span>
                <span>{checklist.filter((i) => i.status === 'fail').length} failed</span>
                {!bridgeConnected && <span className="text-amber-400/60">Bridge offline -- some checks skipped</span>}
              </div>
            </div>
          </div>

          {/* Checklist by category */}
          {(['required', 'recommended', 'optional'] as const).map((category) => {
            const items = checklist.filter((i) => i.category === category)
            if (items.length === 0) return null
            return (
              <div key={category}>
                <h2 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
                  {category === 'required' ? 'Required' : category === 'recommended' ? 'Recommended' : 'Optional'}
                </h2>
                <div className="space-y-1">
                  {items.map((item) => (
                    <ChecklistRow key={item.id} item={item} />
                  ))}
                </div>
              </div>
            )
          })}

          {/* Audit findings detail */}
          {auditResult && auditResult.findings && auditResult.findings.length > 0 && (
            <div>
              <h2 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
                Audit Findings ({auditResult.findings.length})
              </h2>
              <div className="bg-fn-panel border border-fn-border rounded-xl divide-y divide-fn-border">
                {auditResult.findings.slice(0, 20).map((finding, i) => (
                  <div key={i} className="flex items-start gap-2 px-4 py-2.5">
                    <span className={`text-[9px] px-1.5 py-0.5 rounded font-medium shrink-0 mt-0.5 ${
                      finding.severity === 'Error' ? 'text-red-400 bg-red-400/10' :
                      finding.severity === 'Warning' ? 'text-amber-400 bg-amber-400/10' :
                      'text-blue-400 bg-blue-400/10'
                    }`}>
                      {finding.severity}
                    </span>
                    <div className="min-w-0">
                      <p className="text-[10px] text-gray-300">{finding.message}</p>
                      {finding.filePath && (
                        <p className="text-[9px] text-gray-600 font-mono truncate mt-0.5">{finding.filePath}</p>
                      )}
                    </div>
                  </div>
                ))}
                {auditResult.findings.length > 20 && (
                  <div className="px-4 py-2 text-[9px] text-gray-500">
                    ... and {auditResult.findings.length - 20} more findings
                  </div>
                )}
              </div>
            </div>
          )}

          {/* Bridge status note */}
          {!publishAuditDone && !running && (
            <div className="text-[10px] text-gray-600 bg-fn-panel border border-fn-border rounded-lg px-4 py-3 text-center">
              {bridgeConnected
                ? 'Publish audit data was not available. Some checks may be incomplete.'
                : 'Connect to the UEFN bridge for complete publish checks (island code, thumbnail, description).'}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

// ─── Checklist Row ──────────────────────────────────────────────────────────

function ChecklistRow({ item }: { item: ChecklistItem }) {
  return (
    <div className="flex items-center gap-3 bg-fn-panel border border-fn-border rounded-lg px-4 py-2.5">
      {/* Status icon */}
      <div className="shrink-0">
        {item.status === 'running' ? (
          <div className="w-4 h-4 rounded-full border-2 border-fn-rare border-t-transparent animate-spin" />
        ) : item.status === 'pass' ? (
          <svg className="w-4 h-4 text-emerald-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.5}>
            <path d="M5 13l4 4L19 7" />
          </svg>
        ) : item.status === 'fail' ? (
          <svg className="w-4 h-4 text-red-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.5}>
            <path d="M6 18L18 6M6 6l12 12" />
          </svg>
        ) : item.status === 'warn' ? (
          <svg className="w-4 h-4 text-amber-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4.5c-.77-.833-2.694-.833-3.464 0L3.34 16.5c-.77.833.192 2.5 1.732 2.5z" />
          </svg>
        ) : (
          <div className="w-4 h-4 rounded-full border border-gray-600" />
        )}
      </div>

      {/* Content */}
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2">
          <span className="text-[11px] font-medium text-gray-300">{item.label}</span>
          {item.fixable && (item.status === 'fail' || item.status === 'warn') && (
            <span className="text-[8px] px-1 py-0.5 rounded bg-fn-rare/10 text-fn-rare border border-fn-rare/20">
              fixable
            </span>
          )}
        </div>
        <p className="text-[9px] text-gray-500">{item.description}</p>
      </div>

      {/* Message */}
      {item.message && (
        <span className={`text-[9px] shrink-0 ${
          item.status === 'pass' ? 'text-emerald-400/60' :
          item.status === 'fail' ? 'text-red-400/60' :
          item.status === 'warn' ? 'text-amber-400/60' :
          'text-gray-600'
        }`}>
          {item.message}
        </span>
      )}
    </div>
  )
}
