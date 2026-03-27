import { create } from 'zustand'
import {
  forgeBridgeConnect as apiBridgeConnect,
  forgeBridgeStatus as apiBridgeStatus,
  forgeBridgeCommand as apiBridgeCommand,
} from '../lib/api'

export interface BridgeStatus {
  connected: boolean
  deviceCatalogCount: number
  levelActorCount: number
  activeLevelPath: string | null
}

interface BridgeState {
  connected: boolean
  port: number
  lastPing: number | null
  deviceCatalogCount: number
  levelActorCount: number
  activeLevelPath: string | null
  connecting: boolean
  error: string | null

  connect: (port?: number) => Promise<boolean>
  disconnect: () => void
  ping: () => Promise<boolean>
  getStatus: () => Promise<BridgeStatus | null>
}

let _pingInterval: ReturnType<typeof setInterval> | null = null

export const useBridgeStore = create<BridgeState>((set, get) => ({
  connected: false,
  port: 9858,
  lastPing: null,
  deviceCatalogCount: 0,
  levelActorCount: 0,
  activeLevelPath: null,
  connecting: false,
  error: null,

  connect: async (port?: number) => {
    const targetPort = port ?? get().port
    set({ connecting: true, error: null, port: targetPort })

    try {
      const result = await apiBridgeConnect(targetPort)

      if (result.connected) {
        set({
          connected: true,
          connecting: false,
          lastPing: Date.now(),
        })

        // Start auto-ping every 10 seconds
        if (_pingInterval) clearInterval(_pingInterval)
        _pingInterval = setInterval(() => {
          get().ping()
        }, 10_000)

        // Fetch initial status
        get().getStatus()
        return true
      } else {
        set({
          connected: false,
          connecting: false,
          error: result.error ?? 'Connection refused',
        })
        return false
      }
    } catch (err) {
      set({
        connected: false,
        connecting: false,
        error: err instanceof Error ? err.message : 'Connection failed',
      })
      return false
    }
  },

  disconnect: () => {
    if (_pingInterval) {
      clearInterval(_pingInterval)
      _pingInterval = null
    }
    set({
      connected: false,
      lastPing: null,
      deviceCatalogCount: 0,
      levelActorCount: 0,
      activeLevelPath: null,
      error: null,
    })
  },

  ping: async () => {
    try {
      const result = await apiBridgeStatus()
      if (result.connected) {
        set({ connected: true, lastPing: Date.now(), error: null })
        return true
      }
      set({ connected: false, error: 'Bridge disconnected' })
      return false
    } catch {
      set({ connected: false, error: 'Bridge unreachable' })
      if (_pingInterval) {
        clearInterval(_pingInterval)
        _pingInterval = null
      }
      return false
    }
  },

  getStatus: async () => {
    try {
      const result = await apiBridgeStatus()
      const data = (result.data ?? {}) as Record<string, unknown>
      const status: BridgeStatus = {
        connected: result.connected,
        deviceCatalogCount: (data.deviceCatalogCount as number) ?? 0,
        levelActorCount: (data.levelActorCount as number) ?? 0,
        activeLevelPath: (data.activeLevelPath as string) ?? null,
      }
      set({
        connected: status.connected,
        deviceCatalogCount: status.deviceCatalogCount,
        levelActorCount: status.levelActorCount,
        activeLevelPath: status.activeLevelPath,
        lastPing: Date.now(),
      })
      return status
    } catch {
      return null
    }
  },
}))
