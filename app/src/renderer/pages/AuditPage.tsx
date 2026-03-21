import { useEffect, useState } from 'react'
import type { AuditResult, AuditFinding } from '../../shared/types'

export function AuditPage() {
  const [result, setResult] = useState<AuditResult | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    runAudit()
  }, [])

  async function runAudit() {
    try {
      setLoading(true)
      setError(null)
      const data = await Promise.race([
        window.electronAPI.forgeAudit(),
        new Promise<AuditResult>((_, reject) =>
          setTimeout(() => reject(new Error('Audit timed out — is a project selected?')), 30000)
        )
      ])
      // Handle sidecar error responses
      if (!data || (data as any).error) {
        const msg = (data as any)?.error?.message || 'No active project — select a project first'
        setError(msg)
        return
      }
      setResult(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to run audit')
    } finally {
      setLoading(false)
    }
  }

  if (loading) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="w-5 h-5 mx-auto mb-2 border-2 border-fn-rare/30 border-t-fn-rare rounded-full animate-spin" />
          <div className="text-[11px] text-gray-400">Running audit...</div>
        </div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="text-[11px] text-red-400 mb-3">{error}</div>
          <button
            onClick={runAudit}
            className="px-3 py-1.5 text-[10px] font-medium text-white bg-fn-panel border border-fn-border rounded hover:bg-white/[0.06] transition-colors"
          >
            Retry
          </button>
        </div>
      </div>
    )
  }

  // Normalize findings — sidecar may return severity as number (0=Info,1=Warning,2=Error) or string
  const rawFindings = result?.findings ?? []
  const findings: AuditFinding[] = rawFindings.map((f: any) => ({
    ...f,
    severity: typeof f.severity === 'number'
      ? (['Info', 'Warning', 'Error'][f.severity] ?? 'Info') as AuditFinding['severity']
      : f.severity ?? 'Info'
  }))
  const errorCount = findings.filter((f) => f.severity === 'Error').length
  const warnCount = findings.filter((f) => f.severity === 'Warning').length
  const infoCount = findings.filter((f) => f.severity === 'Info').length

  const overallStatus =
    errorCount > 0 ? 'Fail' : warnCount > 0 ? 'Warning' : 'Pass'
  const statusColor =
    overallStatus === 'Fail'
      ? 'text-red-400 bg-red-400/10 border-red-400/20'
      : overallStatus === 'Warning'
        ? 'text-yellow-400 bg-yellow-400/10 border-yellow-400/20'
        : 'text-green-400 bg-green-400/10 border-green-400/20'

  return (
    <div className="flex-1 bg-fn-darker overflow-y-auto min-h-0">
      <div className="max-w-3xl mx-auto p-6 space-y-5">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-lg font-semibold text-white">Audit</h1>
            <p className="text-[11px] text-gray-500 mt-0.5">
              {result?.projectName ? `Project: ${result.projectName}` : 'Project audit results'}
            </p>
          </div>
          <button
            onClick={runAudit}
            className="px-3 py-1.5 text-[10px] font-medium text-white bg-fn-rare/20 border border-fn-rare/30 rounded hover:bg-fn-rare/30 transition-colors"
          >
            Re-run Audit
          </button>
        </div>

        {/* Status Badge */}
        <div className="bg-fn-panel border border-fn-border rounded-lg p-4">
          <div className="flex items-center gap-4">
            <span className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold border ${statusColor}`}>
              {overallStatus === 'Fail' && (
                <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path d="M6 18L18 6M6 6l12 12" />
                </svg>
              )}
              {overallStatus === 'Warning' && (
                <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4.5c-.77-.833-2.694-.833-3.464 0L3.34 16.5c-.77.833.192 2.5 1.732 2.5z" />
                </svg>
              )}
              {overallStatus === 'Pass' && (
                <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path d="M5 13l4 4L19 7" />
                </svg>
              )}
              {overallStatus}
            </span>

            <div className="flex items-center gap-4 text-[10px]">
              {errorCount > 0 && (
                <span className="text-red-400">{errorCount} error{errorCount !== 1 ? 's' : ''}</span>
              )}
              {warnCount > 0 && (
                <span className="text-yellow-400">{warnCount} warning{warnCount !== 1 ? 's' : ''}</span>
              )}
              {infoCount > 0 && (
                <span className="text-blue-400">{infoCount} info</span>
              )}
              {findings.length === 0 && (
                <span className="text-gray-500">No findings</span>
              )}
            </div>
          </div>
        </div>

        {/* Findings List */}
        {findings.length > 0 && (
          <div>
            <h3 className="text-[11px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
              Findings ({findings.length})
            </h3>
            <div className="space-y-2">
              {findings.map((finding, i) => (
                <FindingCard key={i} finding={finding} />
              ))}
            </div>
          </div>
        )}

        {/* Empty state */}
        {findings.length === 0 && (
          <div className="bg-fn-panel border border-fn-border rounded-lg p-6 text-center">
            <svg className="w-8 h-8 mx-auto mb-2 text-green-400/50" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
            </svg>
            <p className="text-[11px] text-gray-400">All clear! No issues found.</p>
          </div>
        )}
      </div>
    </div>
  )
}

function FindingCard({ finding }: { finding: AuditFinding }) {
  const severityConfig = {
    Error: {
      icon: (
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path d="M6 18L18 6M6 6l12 12" />
        </svg>
      ),
      color: 'text-red-400',
      bg: 'bg-red-400/10 border-red-400/20'
    },
    Warning: {
      icon: (
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4.5c-.77-.833-2.694-.833-3.464 0L3.34 16.5c-.77.833.192 2.5 1.732 2.5z" />
        </svg>
      ),
      color: 'text-yellow-400',
      bg: 'bg-yellow-400/10 border-yellow-400/20'
    },
    Info: {
      icon: (
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
        </svg>
      ),
      color: 'text-blue-400',
      bg: 'bg-blue-400/10 border-blue-400/20'
    }
  }

  const config = severityConfig[finding.severity] ?? severityConfig.Info

  return (
    <div className={`border rounded-lg p-3 ${config.bg}`}>
      <div className="flex items-start gap-2">
        <span className={`${config.color} shrink-0 mt-0.5`}>{config.icon}</span>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 mb-0.5">
            <span className={`text-[10px] font-semibold uppercase tracking-wider ${config.color}`}>
              {finding.category}
            </span>
          </div>
          <p className="text-[11px] text-white">{finding.message}</p>
          {finding.suggestion && (
            <p className="text-[10px] text-gray-400 mt-1">{finding.suggestion}</p>
          )}
          {finding.filePath && (
            <p className="text-[10px] text-gray-600 mt-1 truncate">{finding.filePath}</p>
          )}
        </div>
      </div>
    </div>
  )
}
