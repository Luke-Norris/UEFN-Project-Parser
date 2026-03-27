import { useEffect, useRef, useCallback, useState } from 'react'
import { listen, type UnlistenFn } from '@tauri-apps/api/event'
import { invoke } from '@tauri-apps/api/core'
import { useForgeStore } from '../stores/forgeStore'

export interface FileChangeEvent {
  changedFiles: string[]
  changeType: 'modified' | 'created' | 'deleted'
  timestamp: number
}

export interface WatcherStatus {
  watching: boolean
  projectPath: string | null
}

/**
 * Starts / stops the Rust file watcher when the active project changes,
 * and provides a callback for each debounced change batch.
 *
 * Usage:
 *   const { watching, lastEvent } = useFileWatcher()
 */
export function useFileWatcher() {
  const status = useForgeStore((s) => s.status)
  const invalidateCache = useForgeStore((s) => s.invalidateCache)
  const fetchStatus = useForgeStore((s) => s.fetchStatus)

  const [watching, setWatching] = useState(false)
  const [lastEvent, setLastEvent] = useState<FileChangeEvent | null>(null)
  const [notification, setNotification] = useState<string | null>(null)

  // Track which project path the watcher is currently watching so we can
  // detect project switches without depending on React re-render timing.
  const currentWatchPath = useRef<string | null>(null)

  // Notification auto-dismiss timer
  const dismissTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  const showNotification = useCallback((msg: string) => {
    setNotification(msg)
    if (dismissTimer.current) clearTimeout(dismissTimer.current)
    dismissTimer.current = setTimeout(() => setNotification(null), 6000)
  }, [])

  // Start watcher for a project path
  const startWatcher = useCallback(async (projectPath: string) => {
    try {
      await invoke('start_file_watcher', { projectPath })
      currentWatchPath.current = projectPath
      setWatching(true)
    } catch (err) {
      console.warn('[useFileWatcher] Failed to start:', err)
      setWatching(false)
    }
  }, [])

  // Stop watcher
  const stopWatcher = useCallback(async () => {
    try {
      await invoke('stop_file_watcher')
    } catch {
      // Ignore — watcher may already be stopped.
    }
    currentWatchPath.current = null
    setWatching(false)
  }, [])

  // React to active-project changes: stop old watcher, start new one.
  useEffect(() => {
    const projectPath = status?.projectPath ?? null

    if (!projectPath || !status?.isConfigured) {
      // No active project — make sure watcher is stopped.
      if (currentWatchPath.current) {
        stopWatcher()
      }
      return
    }

    // If already watching this exact path, nothing to do.
    if (currentWatchPath.current === projectPath) return

    // Switch watchers.
    startWatcher(projectPath)

    return () => {
      // Cleanup on unmount — stop watcher.
      stopWatcher()
    }
  }, [status?.projectPath, status?.isConfigured, startWatcher, stopWatcher])

  // Listen for Tauri events from the Rust file watcher.
  useEffect(() => {
    let unlisten: UnlistenFn | null = null

    const setup = async () => {
      unlisten = await listen<FileChangeEvent>('project:files-changed', (event) => {
        const payload = event.payload
        setLastEvent(payload)

        // Build a human-readable notification.
        const count = payload.changedFiles.length
        const verb =
          payload.changeType === 'created'
            ? 'added'
            : payload.changeType === 'deleted'
              ? 'removed'
              : 'changed'
        showNotification(`${count} file${count !== 1 ? 's' : ''} ${verb} in UEFN`)

        // Invalidate caches so the next page render fetches fresh data.
        invalidateCache()
        // Re-fetch status immediately so sidebar badge counts update.
        // Use a small delay so the sidecar has time to see the new files.
        setTimeout(() => {
          useForgeStore.getState().statusFetchedAt = null
          fetchStatus()
        }, 1000)
      })
    }

    setup()

    return () => {
      if (unlisten) unlisten()
    }
  }, [invalidateCache, fetchStatus, showNotification])

  // Cleanup dismiss timer on unmount
  useEffect(() => {
    return () => {
      if (dismissTimer.current) clearTimeout(dismissTimer.current)
    }
  }, [])

  return { watching, lastEvent, notification, dismissNotification: () => setNotification(null) }
}
