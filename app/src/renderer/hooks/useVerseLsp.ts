/**
 * Hook to manage the Verse LSP connection lifecycle.
 * Auto-starts when the Verse files page mounts if the binary is available.
 */

import { useState, useEffect, useCallback, useRef } from 'react'
import { lspStatus, lspStart, lspStop, lspDidOpen } from '../lib/api'
import { filePathToUri } from '../verse-language/verse-lsp-extensions'
import { startDiagnosticListener, stopDiagnosticListener } from '../verse-language/verse-lsp-diagnostics'

export interface VerseLspState {
  available: boolean       // Is verse-lsp.exe found?
  ready: boolean           // Is the LSP initialized and ready?
  starting: boolean        // Currently starting up?
  error: string | null
  capabilities: any | null
}

export function useVerseLsp(workspacePath?: string) {
  const [state, setState] = useState<VerseLspState>({
    available: false,
    ready: false,
    starting: false,
    error: null,
    capabilities: null,
  })
  const checkedRef = useRef(false)
  const autoStartAttempted = useRef(false)
  const openedFiles = useRef(new Set<string>())

  // Check if LSP binary is available on mount
  useEffect(() => {
    if (checkedRef.current) return
    checkedRef.current = true

    lspStatus().then((status) => {
      setState(prev => ({
        ...prev,
        available: status.available,
        ready: status.ready,
        capabilities: status.capabilities,
      }))

      // Auto-start if available, not already running, and we have a workspace
      if (status.available && !status.ready && !autoStartAttempted.current) {
        autoStartAttempted.current = true
        // Get workspace path from sidecar status
        const wsPath = workspacePath
        if (wsPath) {
          startLsp(wsPath)
        } else {
          // Try to get project path from sidecar
          window.electronAPI?.forgeStatus?.().then((forgeStatus: any) => {
            if (forgeStatus?.projectPath) {
              startLsp(forgeStatus.projectPath)
            }
          }).catch(() => {})
        }
      }
    }).catch(() => {
      // API not available (not running in Tauri)
    })
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Internal start function
  async function startLsp(wsPath: string) {
    setState(prev => ({ ...prev, starting: true, error: null }))
    try {
      const result = await lspStart(wsPath)
      await startDiagnosticListener()
      setState(prev => ({
        ...prev,
        ready: true,
        starting: false,
        capabilities: result.capabilities,
      }))
    } catch (err) {
      setState(prev => ({
        ...prev,
        starting: false,
        error: err instanceof Error ? err.message : String(err),
      }))
    }
  }

  // Start LSP (public, for manual start button)
  const start = useCallback(async (path?: string) => {
    const wsPath = path ?? workspacePath
    if (!wsPath) {
      // Try from sidecar
      try {
        const forgeStatus = await window.electronAPI?.forgeStatus?.() as any
        if (forgeStatus?.projectPath) {
          await startLsp(forgeStatus.projectPath)
          return
        }
      } catch { /* ignore */ }
      setState(prev => ({ ...prev, error: 'No workspace path available' }))
      return
    }
    await startLsp(wsPath)
  }, [workspacePath])

  // Stop LSP
  const stop = useCallback(async () => {
    stopDiagnosticListener()
    openedFiles.current.clear()
    try {
      await lspStop()
    } catch { /* ignore */ }
    setState(prev => ({ ...prev, ready: false }))
  }, [])

  // Notify LSP about an opened file (deduplicates)
  const openFile = useCallback(async (filePath: string, content: string) => {
    if (!state.ready) return
    const uri = filePathToUri(filePath)
    if (openedFiles.current.has(uri)) return // already opened
    openedFiles.current.add(uri)
    try {
      await lspDidOpen(uri, content)
    } catch { /* ignore */ }
  }, [state.ready])

  return { ...state, start, stop, openFile }
}
