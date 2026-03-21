import { create } from 'zustand'
import { persist } from 'zustand/middleware'

export interface SettingsState {
  // Appearance
  theme: 'midnight' | 'obsidian' | 'arctic' | 'cyberpunk' | 'forest' | 'sunset'
  accentColor: 'blue' | 'purple' | 'green' | 'orange' | 'red' | 'pink' | 'cyan'
  fontSize: 'small' | 'medium' | 'large'
  sidebarWidth: number
  animationsEnabled: boolean

  // Accessibility
  highContrast: boolean
  reducedMotion: boolean
  largeClickTargets: boolean

  // UEFN Developer Settings
  autoRefreshOnFocus: boolean
  showHiddenFiles: boolean
  copyOnReadWarnings: boolean
  maxPropertiesPerExport: number
  defaultScanPath: string
  verseEditorFontSize: number

  // Library — reference collection path(s)
  libraryPaths: string[] // e.g. ['Z:\\UEFN_Resources\\mapContent']

  // Widget Editor
  defaultCanvasWidth: number
  defaultCanvasHeight: number
  snapToGrid: boolean
  gridSize: number

  // UI Zoom (persisted between sessions)
  uiZoom: number

  // Fortnite installation path (for CUE4Parse asset previews)
  fortnitePath: string

  // Actions
  setSetting: <K extends keyof Omit<SettingsState, 'setSetting' | 'resetToDefaults'>>(
    key: K,
    value: Omit<SettingsState, 'setSetting' | 'resetToDefaults'>[K]
  ) => void
  resetToDefaults: () => void
}

const defaults: Omit<SettingsState, 'setSetting' | 'resetToDefaults'> = {
  theme: 'midnight',
  accentColor: 'blue',
  fontSize: 'medium',
  sidebarWidth: 220,
  animationsEnabled: true,

  highContrast: false,
  reducedMotion: false,
  largeClickTargets: false,

  autoRefreshOnFocus: true,
  showHiddenFiles: false,
  copyOnReadWarnings: true,
  maxPropertiesPerExport: 50,
  defaultScanPath: '',
  verseEditorFontSize: 13,

  libraryPaths: [],

  defaultCanvasWidth: 1024,
  defaultCanvasHeight: 1024,
  snapToGrid: true,
  gridSize: 8,

  uiZoom: 1,

  fortnitePath: '',
}

export const useSettingsStore = create<SettingsState>()(
  persist(
    (set) => ({
      ...defaults,

      setSetting: (key, value) => set({ [key]: value } as Partial<SettingsState>),

      resetToDefaults: () => set(defaults),
    }),
    {
      name: 'wellversed-settings',
      partialize: (state) => {
        // Persist everything except actions
        const { setSetting: _a, resetToDefaults: _b, ...rest } = state
        return rest
      },
    }
  )
)

export { defaults as settingsDefaults }
