import { create } from 'zustand'
import type { WellVersedStatus, WellVersedProjectList, WellVersedLevel, EpicAssetListResult, UserAssetListResult } from '../../shared/types'

const CACHE_TTL_MS = 60_000 // 60 seconds — longer TTL since project data doesn't change often

interface ForgeState {
  // Cached data
  status: WellVersedStatus | null
  projectList: WellVersedProjectList | null
  levels: WellVersedLevel[] | null
  userAssets: UserAssetListResult | null
  epicAssets: EpicAssetListResult | null

  // Loading states
  statusLoading: boolean
  projectListLoading: boolean
  levelsLoading: boolean
  userAssetsLoading: boolean
  epicAssetsLoading: boolean

  // Error states
  statusError: string | null
  projectListError: string | null
  levelsError: string | null
  userAssetsError: string | null
  epicAssetsError: string | null

  // Last fetch timestamps (for staleness checking)
  statusFetchedAt: number | null
  projectListFetchedAt: number | null
  levelsFetchedAt: number | null
  userAssetsFetchedAt: number | null
  epicAssetsFetchedAt: number | null

  // Actions
  fetchStatus: () => Promise<void>
  fetchProjects: () => Promise<void>
  fetchLevels: () => Promise<void>
  fetchUserAssets: () => Promise<void>
  fetchEpicAssets: () => Promise<void>
  refreshAll: () => Promise<void>
  invalidateCache: () => void
}

function isFresh(fetchedAt: number | null): boolean {
  if (fetchedAt === null) return false
  return Date.now() - fetchedAt < CACHE_TTL_MS
}

export const useForgeStore = create<ForgeState>((set, get) => ({
  // Cached data
  status: null,
  projectList: null,
  levels: null,
  userAssets: null,
  epicAssets: null,

  // Loading states
  statusLoading: false,
  projectListLoading: false,
  levelsLoading: false,
  userAssetsLoading: false,
  epicAssetsLoading: false,

  // Error states
  statusError: null,
  projectListError: null,
  levelsError: null,
  userAssetsError: null,
  epicAssetsError: null,

  // Timestamps
  statusFetchedAt: null,
  projectListFetchedAt: null,
  levelsFetchedAt: null,
  userAssetsFetchedAt: null,
  epicAssetsFetchedAt: null,

  fetchStatus: async () => {
    const state = get()
    if (isFresh(state.statusFetchedAt) && state.status !== null) return
    if (state.statusLoading) return

    set({ statusLoading: true, statusError: null })
    try {
      const result = await Promise.race([
        window.electronAPI.wellVersedStatus(),
        new Promise<WellVersedStatus>((_, reject) =>
          setTimeout(() => reject(new Error('Sidecar connection timeout')), 20_000)
        ),
      ])
      set({
        status: result,
        statusLoading: false,
        statusFetchedAt: Date.now(),
        statusError: null,
      })
    } catch (err) {
      set({
        status: {
          isConfigured: false,
          projectName: 'No Project',
          mode: 'None',
          assetCount: 0,
          verseCount: 0,
        } as WellVersedStatus,
        statusLoading: false,
        statusFetchedAt: Date.now(),
        statusError: err instanceof Error ? err.message : 'Failed to fetch status',
      })
    }
  },

  fetchProjects: async () => {
    const state = get()
    if (isFresh(state.projectListFetchedAt) && state.projectList !== null) return
    if (state.projectListLoading) return

    set({ projectListLoading: true, projectListError: null })
    try {
      const result = await window.electronAPI.wellVersedListProjects()
      set({
        projectList: result,
        projectListLoading: false,
        projectListFetchedAt: Date.now(),
        projectListError: null,
      })
    } catch (err) {
      set({
        projectListLoading: false,
        projectListError: err instanceof Error ? err.message : 'Failed to fetch projects',
      })
    }
  },

  fetchLevels: async () => {
    const state = get()
    if (isFresh(state.levelsFetchedAt) && state.levels !== null) return
    if (state.levelsLoading) return

    set({ levelsLoading: true, levelsError: null })
    try {
      const result = await window.electronAPI.forgeListLevels()
      set({
        levels: result ?? [],
        levelsLoading: false,
        levelsFetchedAt: Date.now(),
        levelsError: null,
      })
    } catch (err) {
      set({
        levelsLoading: false,
        levelsError: err instanceof Error ? err.message : 'Failed to fetch levels',
      })
    }
  },

  fetchUserAssets: async () => {
    const state = get()
    if (isFresh(state.userAssetsFetchedAt) && state.userAssets !== null) return
    if (state.userAssetsLoading) return

    set({ userAssetsLoading: true, userAssetsError: null })
    try {
      const raw = await window.electronAPI.forgeListUserAssets()
      // Normalize: sidecar field names → interface field names
      const result = raw as any
      if (result?.assets) {
        result.assets = result.assets.map((a: any) => ({
          ...a,
          filePath: a.filePath ?? a.path ?? '',
          size: a.size ?? a.fileSize ?? 0,
        }))
      }
      set({
        userAssets: result,
        userAssetsLoading: false,
        userAssetsFetchedAt: Date.now(),
        userAssetsError: null,
      })
    } catch (err) {
      set({
        userAssetsLoading: false,
        userAssetsError: err instanceof Error ? err.message : 'Failed to fetch user assets',
      })
    }
  },

  fetchEpicAssets: async () => {
    const state = get()
    if (isFresh(state.epicAssetsFetchedAt) && state.epicAssets !== null) return
    if (state.epicAssetsLoading) return

    set({ epicAssetsLoading: true, epicAssetsError: null })
    try {
      const raw = await window.electronAPI.forgeListEpicAssets()
      const rawTypes = (raw?.types ?? []) as Array<any>
      const types = rawTypes.map((t: any) => ({
        typeName: t.displayName || t.className || t.typeName || 'Unknown',
        className: t.className,
        displayName: t.displayName,
        count: t.count ?? 0,
        isDevice: t.isDevice ?? false,
        samplePaths: t.samplePaths ?? []
      }))
      const totalPlaced = types.reduce((sum: number, t: any) => sum + t.count, 0)
      set({
        epicAssets: {
          types,
          totalPlaced,
          uniqueTypes: types.length,
          deviceCount: types.filter((t: any) => t.isDevice).reduce((sum: number, t: any) => sum + t.count, 0),
          propCount: types.filter((t: any) => !t.isDevice).reduce((sum: number, t: any) => sum + t.count, 0),
        },
        epicAssetsLoading: false,
        epicAssetsFetchedAt: Date.now(),
        epicAssetsError: null,
      })
    } catch (err) {
      set({
        epicAssetsLoading: false,
        epicAssetsError: err instanceof Error ? err.message : 'Failed to fetch epic assets',
      })
    }
  },

  refreshAll: async () => {
    set({
      statusFetchedAt: null,
      projectListFetchedAt: null,
      levelsFetchedAt: null,
      userAssetsFetchedAt: null,
      epicAssetsFetchedAt: null,
    })
    const { fetchStatus, fetchProjects, fetchLevels } = get()
    await Promise.all([fetchStatus(), fetchProjects(), fetchLevels()])
  },

  invalidateCache: () => {
    set({
      status: null,
      projectList: null,
      levels: null,
      userAssets: null,
      epicAssets: null,
      statusFetchedAt: null,
      projectListFetchedAt: null,
      levelsFetchedAt: null,
      userAssetsFetchedAt: null,
      epicAssetsFetchedAt: null,
      statusError: null,
      projectListError: null,
      levelsError: null,
      userAssetsError: null,
      epicAssetsError: null,
    })
  },
}))
