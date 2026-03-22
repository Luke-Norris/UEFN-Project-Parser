/**
 * Hook to manage the Verse LSP connection lifecycle.
 * Starts the LSP when a workspace is set, tracks status.
 */

import { useState, useEffect, useCallback, useRef } from 'react'
import { lspStatus, lspStart, lspStop, lspDidOpen } from '../lib/api'
import { filePathToUri } from '../verse-language/verse-lsp-extensions'

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
    }).catch(() => {
      // API not available (not running in Tauri)
    })
  }, [])

  // Start LSP when workspace is provided and binary is available
  const start = useCallback(async (path?: string) => {
    const wsPath = path ?? workspacePath
    if (!wsPath) return

    setState(prev => ({ ...prev, starting: true, error: null }))
    try {
      const result = await lspStart(wsPath)
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
  }, [workspacePath])

  // Stop LSP
  const stop = useCallback(async () => {
    try {
      await lspStop()
    } catch { /* ignore */ }
    setState(prev => ({ ...prev, ready: false }))
  }, [])

  // Notify LSP about an opened file
  const openFile = useCallback(async (filePath: string, content: string) => {
    if (!state.ready) return
    try {
      await lspDidOpen(filePathToUri(filePath), content)
    } catch { /* ignore */ }
  }, [state.ready])

  return { ...state, start, stop, openFile }
}
