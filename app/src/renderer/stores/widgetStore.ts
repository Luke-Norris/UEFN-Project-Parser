import { create } from 'zustand'
import type { WidgetSummary } from '../lib/api'
import { forgeListProjectWidgets, forgeParseWidget, forgeListLibraryWidgets } from '../lib/api'
import type { WidgetSpecJson } from '../../shared/widget-spec'

interface WidgetState {
  // Widget lists
  projectWidgets: WidgetSummary[]
  libraryWidgets: WidgetSummary[]

  // Currently loaded widget
  loadedWidgetPath: string | null
  loadedSpec: WidgetSpecJson | null

  // Loading/error states
  listLoading: boolean
  parseLoading: boolean
  listError: string | null
  parseError: string | null

  // Search
  searchQuery: string

  // Actions
  fetchProjectWidgets: () => Promise<void>
  fetchLibraryWidgets: () => Promise<void>
  loadWidget: (path: string) => Promise<WidgetSpecJson | null>
  clearLoadedWidget: () => void
  setSearchQuery: (query: string) => void
}

export const useWidgetStore = create<WidgetState>((set, get) => ({
  projectWidgets: [],
  libraryWidgets: [],
  loadedWidgetPath: null,
  loadedSpec: null,
  listLoading: false,
  parseLoading: false,
  listError: null,
  parseError: null,
  searchQuery: '',

  fetchProjectWidgets: async () => {
    set({ listLoading: true, listError: null })
    try {
      const result = await forgeListProjectWidgets()
      set({ projectWidgets: result.widgets, listLoading: false })
    } catch (e: any) {
      set({ listError: e.message ?? String(e), listLoading: false })
    }
  },

  fetchLibraryWidgets: async () => {
    try {
      const result = await forgeListLibraryWidgets()
      set({ libraryWidgets: result.widgets })
    } catch {
      // Library may not be configured — silently ignore
      set({ libraryWidgets: [] })
    }
  },

  loadWidget: async (path: string) => {
    set({ parseLoading: true, parseError: null })
    try {
      const result = await forgeParseWidget(path)
      const spec = result.spec as WidgetSpecJson
      set({ loadedWidgetPath: path, loadedSpec: spec, parseLoading: false })
      return spec
    } catch (e: any) {
      set({ parseError: e.message ?? String(e), parseLoading: false })
      return null
    }
  },

  clearLoadedWidget: () => {
    set({ loadedWidgetPath: null, loadedSpec: null, parseError: null })
  },

  setSearchQuery: (query: string) => {
    set({ searchQuery: query })
  },
}))
