import { create } from 'zustand'
import { persist } from 'zustand/middleware'

const CACHE_TTL_MS = 30_000

interface LibraryEntry {
  id: string
  name: string
  path: string
  verseFileCount?: number
  assetCount?: number
  lastIndexed?: string | null
  indexed?: boolean
}

interface LibraryList {
  activeLibraryId: string | null
  libraries: LibraryEntry[]
}

interface VerseFileEntry {
  name: string
  filePath: string
  relativePath: string
  lineCount: number
  projectFolder?: string
}

interface AssetsByTypeEntry {
  name: string
  filePath: string
  relativePath: string
  assetClass: string
  size?: number
}

interface LibraryState {
  // Cached data
  libraryList: LibraryList | null
  verseFiles: VerseFileEntry[] | null
  assetsByType: Record<string, AssetsByTypeEntry[]> | null

  // Derived
  activeLibrary: LibraryEntry | null

  // Persisted favorites
  favoriteVerseFiles: string[]

  // Loading states
  libraryListLoading: boolean
  indexing: boolean
  verseFilesLoading: boolean
  assetsByTypeLoading: boolean

  // Error states
  libraryListError: string | null
  verseFilesError: string | null
  assetsByTypeError: string | null

  // Timestamps
  libraryListFetchedAt: number | null
  verseFilesFetchedAt: number | null
  assetsByTypeFetchedAt: number | null

  // Actions
  fetchLibraries: () => Promise<void>
  addLibrary: (path: string) => Promise<void>
  removeLibrary: (id: string) => Promise<void>
  activateLibrary: (id: string) => Promise<void>
  indexActiveLibrary: () => Promise<void>
  fetchVerseFiles: () => Promise<void>
  fetchAssetsByType: () => Promise<void>
  toggleFavorite: (filePath: string) => void
  invalidateCache: () => void
}

function isFresh(fetchedAt: number | null): boolean {
  if (fetchedAt === null) return false
  return Date.now() - fetchedAt < CACHE_TTL_MS
}

// We use persist middleware only for favoriteVerseFiles
export const useLibraryStore = create<LibraryState>()(
  persist(
    (set, get) => ({
      // Cached data
      libraryList: null,
      verseFiles: null,
      assetsByType: null,
      activeLibrary: null,
      favoriteVerseFiles: [],

      // Loading states
      libraryListLoading: false,
      indexing: false,
      verseFilesLoading: false,
      assetsByTypeLoading: false,

      // Error states
      libraryListError: null,
      verseFilesError: null,
      assetsByTypeError: null,

      // Timestamps
      libraryListFetchedAt: null,
      verseFilesFetchedAt: null,
      assetsByTypeFetchedAt: null,

      fetchLibraries: async () => {
        const state = get()
        if (isFresh(state.libraryListFetchedAt) && state.libraryList !== null) return
        if (state.libraryListLoading) return

        set({ libraryListLoading: true, libraryListError: null })
        try {
          const result = await (window.electronAPI as any).forgeListLibraries()
          const list = result as LibraryList
          const active = list.libraries.find((l) => l.id === list.activeLibraryId) ?? null
          set({
            libraryList: list,
            activeLibrary: active,
            libraryListLoading: false,
            libraryListFetchedAt: Date.now(),
            libraryListError: null,
          })
        } catch (err) {
          set({
            libraryListLoading: false,
            libraryListError: err instanceof Error ? err.message : 'Failed to fetch libraries',
          })
        }
      },

      addLibrary: async (path: string) => {
        try {
          await (window.electronAPI as any).forgeAddLibrary(path)
          // Force re-fetch
          set({ libraryListFetchedAt: null })
          await get().fetchLibraries()
        } catch (err) {
          throw err
        }
      },

      removeLibrary: async (id: string) => {
        try {
          await (window.electronAPI as any).forgeRemoveLibrary(id)
          set({ libraryListFetchedAt: null })
          await get().fetchLibraries()
        } catch (err) {
          throw err
        }
      },

      activateLibrary: async (id: string) => {
        try {
          await (window.electronAPI as any).forgeActivateLibrary(id)
          // Invalidate library-specific data
          set({
            libraryListFetchedAt: null,
            verseFiles: null,
            verseFilesFetchedAt: null,
            assetsByType: null,
            assetsByTypeFetchedAt: null,
          })
          await get().fetchLibraries()
        } catch (err) {
          throw err
        }
      },

      indexActiveLibrary: async () => {
        const state = get()
        if (state.indexing) return

        set({ indexing: true })
        try {
          await (window.electronAPI as any).forgeIndexLibrary()
          // Refresh everything after indexing
          set({
            libraryListFetchedAt: null,
            verseFilesFetchedAt: null,
            assetsByTypeFetchedAt: null,
          })
          await get().fetchLibraries()
        } catch (err) {
          console.error('Indexing failed:', err)
        } finally {
          set({ indexing: false })
        }
      },

      fetchVerseFiles: async () => {
        const state = get()
        if (isFresh(state.verseFilesFetchedAt) && state.verseFiles !== null) return
        if (state.verseFilesLoading) return

        set({ verseFilesLoading: true, verseFilesError: null })
        try {
          const result = await (window.electronAPI as any).forgeGetLibraryVerseFiles()
          // Normalize — sidecar may return array or {verseFiles: [...]}
          const files = Array.isArray(result) ? result : Array.isArray(result?.verseFiles) ? result.verseFiles : []
          set({
            verseFiles: files as VerseFileEntry[],
            verseFilesLoading: false,
            verseFilesFetchedAt: Date.now(),
            verseFilesError: null,
          })
        } catch (err) {
          set({
            verseFilesLoading: false,
            verseFilesError: err instanceof Error ? err.message : 'Failed to fetch verse files',
          })
        }
      },

      fetchAssetsByType: async () => {
        const state = get()
        if (isFresh(state.assetsByTypeFetchedAt) && state.assetsByType !== null) return
        if (state.assetsByTypeLoading) return

        set({ assetsByTypeLoading: true, assetsByTypeError: null })
        try {
          const result = await (window.electronAPI as any).forgeGetLibraryAssetsByType()
          // Normalize — sidecar may return {groups: [...]} or {error: ...}
          if (result?.error) {
            set({
              assetsByTypeLoading: false,
              assetsByTypeError: result.error.message || 'Library not indexed — click "Build Index" first',
            })
            return
          }
          // Convert groups array to record if needed
          let data: Record<string, AssetsByTypeEntry[]> = {}
          if (Array.isArray(result?.groups)) {
            for (const g of result.groups) {
              data[g.assetClass || 'Unknown'] = (g.assets || []).map((a: any) => ({
                name: a.name, filePath: a.filePath, assetClass: g.assetClass,
                fileSize: a.fileSize || 0, projectName: a.projectName || ''
              }))
            }
          } else if (result && typeof result === 'object' && !Array.isArray(result)) {
            data = result as Record<string, AssetsByTypeEntry[]>
          }
          set({
            assetsByType: data,
            assetsByTypeLoading: false,
            assetsByTypeFetchedAt: Date.now(),
            assetsByTypeError: null,
          })
        } catch (err) {
          set({
            assetsByTypeLoading: false,
            assetsByTypeError: err instanceof Error ? err.message : 'Failed to fetch assets. Build index first.',
          })
        }
      },

      toggleFavorite: (filePath: string) => {
        set((state) => {
          const favs = state.favoriteVerseFiles
          if (favs.includes(filePath)) {
            return { favoriteVerseFiles: favs.filter((f) => f !== filePath) }
          }
          return { favoriteVerseFiles: [...favs, filePath] }
        })
      },

      invalidateCache: () => {
        set({
          libraryList: null,
          activeLibrary: null,
          verseFiles: null,
          assetsByType: null,
          libraryListFetchedAt: null,
          verseFilesFetchedAt: null,
          assetsByTypeFetchedAt: null,
          libraryListError: null,
          verseFilesError: null,
          assetsByTypeError: null,
        })
      },
    }),
    {
      name: 'wellversed-library-favorites',
      partialize: (state) => ({
        favoriteVerseFiles: state.favoriteVerseFiles,
      }),
    }
  )
)
