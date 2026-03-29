import { useCallback, useEffect, useState } from 'react'
import { ContextMenuProvider } from './components/ContextMenu/ContextMenu'
import { Sidebar, type PageId } from './components/Sidebar/Sidebar'
import { ResizeHandle } from './components/ResizeHandle'
import { useForgeStore } from './stores/forgeStore'
import { HomePage } from './pages/HomePage'
import { DashboardPage } from './pages/DashboardPage'
import { WidgetEditorPage } from './pages/WidgetEditorPage'
import { AuditPage } from './pages/AuditPage'
import { LevelsPage } from './pages/LevelsPage'
import { UserAssetsPage } from './pages/UserAssetsPage'
import { EpicAssetsPage } from './pages/EpicAssetsPage'
import { DevicesPage } from './pages/DevicesPage'
import { VerseFilesPage } from './pages/VerseFilesPage'
import { ProjectsPage } from './pages/ProjectsPage'
import { StagedPage } from './pages/StagedPage'
import { ContentBrowserPage } from './pages/ContentBrowserPage'
import { SettingsPage } from './pages/SettingsPage'
import { LibraryManagePage } from './pages/LibraryManagePage'
import { LibraryBrowsePage } from './pages/LibraryBrowsePage'
import { LibraryVersePage } from './pages/LibraryVersePage'
import { LibraryWidgetsPage } from './pages/LibraryWidgetsPage'
import { LibraryMaterialsPage } from './pages/LibraryMaterialsPage'
import { LibraryAssetsByTypePage } from './pages/LibraryAssetsByTypePage'
import { LibraryDeviceConfigsPage } from './pages/LibraryDeviceConfigsPage'
import { DeviceWiringPage } from './pages/DeviceWiringPage'
import { ScenePreviewPage } from './pages/ScenePreviewPage'
import { RecipesPage } from './pages/RecipesPage'
import { AssetSearchPage } from './pages/AssetSearchPage'
import { BlueprintGraphPage } from './pages/BlueprintGraphPage'
import { PersistentDataPage } from './pages/PersistentDataPage'
import { VerseReferencePage } from './pages/VerseReferencePage'
import { VerseErrorExplainerPage } from './pages/VerseErrorExplainerPage'
import { ProjectHealthPage } from './pages/ProjectHealthPage'
import { ProjectDiffPage } from './pages/ProjectDiffPage'
import { DeviceEncyclopediaPage } from './pages/DeviceEncyclopediaPage'
import { ChatPage } from './pages/ChatPage'
import { StampsPage } from './pages/StampsPage'
import { GeometryPage } from './pages/GeometryPage'
import { PublishPage } from './pages/PublishPage'
import { GameLoopPage } from './pages/GameLoopPage'
import { GuidePage } from './pages/GuidePage'
import { useTheme } from './hooks/useTheme'
import { useFileWatcher } from './hooks/useFileWatcher'
import { useSettingsStore } from './stores/settingsStore'
import { useBridgeStore } from './stores/bridgeStore'

import { applyZoom } from './lib/zoom'

export default function App() {
  useTheme()

  // File watcher — auto-starts when a project is active, shows notification bar
  const { notification: watcherNotification, dismissNotification } = useFileWatcher()

  // Restore UI zoom via transform (not body.zoom which breaks layout)
  const savedZoom = useSettingsStore((s) => s.uiZoom)
  useEffect(() => {
    applyZoom(savedZoom)
  }, []) // eslint-disable-line react-hooks/exhaustive-deps -- only on mount

  const [activePage, setActivePage] = useState<PageId>('home')
  const [fontsLoaded, setFontsLoaded] = useState(false)
  const [fontsError, setFontsError] = useState<string | null>(null)
  const [selectedLevel, setSelectedLevel] = useState<string | null>(null)
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false)
  const [sidebarWidth, setSidebarWidth] = useState(220)

  const status = useForgeStore((s) => s.status)
  const fetchStatus = useForgeStore((s) => s.fetchStatus)
  const invalidateCache = useForgeStore((s) => s.invalidateCache)

  // Bridge auto-connect on mount (attempt silently, no error display)
  const bridgeConnected = useBridgeStore((s) => s.connected)
  const bridgeConnect = useBridgeStore((s) => s.connect)
  const bridgeError = useBridgeStore((s) => s.error)
  useEffect(() => {
    // Try to connect to bridge silently on startup
    bridgeConnect().catch(() => { /* silent — bridge may not be running */ })
  }, []) // eslint-disable-line react-hooks/exhaustive-deps -- only on mount

  // Derive activeProject from the store
  const activeProject = status?.isConfigured
    ? { name: status.projectName, type: status.projectType ?? 'MyProject' }
    : null

  const handleNavigate = useCallback((page: PageId) => {
    setActivePage(page)
    if (page === 'widget-editor') {
      setSidebarCollapsed(true)
    }
  }, [])

  // Load custom fonts on startup
  useEffect(() => {
    loadFonts()
      .then(() => setFontsLoaded(true))
      .catch((err) => {
        console.warn('Font loading failed:', err)
        setFontsError(String(err))
        setFontsLoaded(true)
      })
  }, [])

  // Fetch status on mount — retry after delay to catch slow sidecar startup
  useEffect(() => {
    fetchStatus()
    // Sidecar may still be building on first call — retry after 5s if not configured
    const retryTimer = setTimeout(() => {
      const s = useForgeStore.getState()
      if (!s.status?.isConfigured) {
        s.statusFetchedAt = null // force re-fetch
        fetchStatus()
      }
    }, 5000)
    return () => clearTimeout(retryTimer)
  }, [fetchStatus])

  const refreshProject = useCallback(() => {
    invalidateCache()
    fetchStatus()
  }, [invalidateCache, fetchStatus])

  function renderPage() {
    switch (activePage) {
      case 'home':
        return <HomePage onNavigate={handleNavigate} />
      case 'dashboard':
        return <DashboardPage onNavigate={handleNavigate} onProjectChanged={refreshProject} />
      case 'project-health':
        return <ProjectHealthPage />
      case 'widget-editor':
        return <WidgetEditorPage fontsLoaded={fontsLoaded} fontsError={fontsError} />
      case 'audit':
        return <AuditPage />
      case 'verse-errors':
        return <VerseErrorExplainerPage />
      case 'device-wiring':
        return <DeviceWiringPage selectedLevel={selectedLevel} onNavigate={handleNavigate} />
      case 'scene-preview':
        return <ScenePreviewPage selectedLevel={selectedLevel} />
      case 'asset-search':
        return <AssetSearchPage />
      case 'blueprint-graph':
        return <BlueprintGraphPage selectedLevel={selectedLevel} />
      case 'recipes':
        return <RecipesPage />
      case 'encyclopedia':
        return <DeviceEncyclopediaPage />
      case 'levels':
        return <LevelsPage onNavigate={handleNavigate} onSelectLevel={(path) => { setSelectedLevel(path); handleNavigate('devices') }} />
      case 'content-browser':
        return <ContentBrowserPage />
      case 'user-assets':
        return <UserAssetsPage />
      case 'epic-assets':
        return <EpicAssetsPage />
      case 'devices':
        return <DevicesPage selectedLevel={selectedLevel} />
      case 'project-verse-files':
        return <VerseFilesPage />
      case 'persistent-data':
        return <PersistentDataPage onNavigate={handleNavigate} />
      case 'library-manage':
        return <LibraryManagePage onNavigate={handleNavigate} />
      case 'library-browse':
        return <LibraryBrowsePage />
      case 'library-verse':
        return <LibraryVersePage />
      case 'library-widgets':
        return <LibraryWidgetsPage />
      case 'library-materials':
        return <LibraryMaterialsPage />
      case 'library-assets-by-type':
        return <LibraryAssetsByTypePage />
      case 'library-device-configs':
        return <LibraryDeviceConfigsPage />
      case 'verse-reference':
        return <VerseReferencePage />
      case 'projects':
        return <ProjectsPage onNavigate={handleNavigate} onProjectChanged={refreshProject} />
      case 'staged':
        return <StagedPage />
      case 'project-diff':
        return <ProjectDiffPage />
      case 'chat':
        return <ChatPage selectedLevel={selectedLevel} onNavigate={handleNavigate} />
      case 'stamps':
        return <StampsPage />
      case 'geometry':
        return <GeometryPage />
      case 'game-loop':
        return <GameLoopPage selectedLevel={selectedLevel} onNavigate={handleNavigate} />
      case 'publish':
        return <PublishPage />
      case 'guide':
        return <GuidePage />
      case 'settings':
        return <SettingsPage />
      default:
        return <HomePage onNavigate={handleNavigate} />
    }
  }

  return (
    <ContextMenuProvider>
      <div
        className="h-full flex flex-col bg-fn-darker text-gray-200 select-none overflow-hidden"
        onContextMenu={(e) => {
          e.preventDefault()
        }}
      >
        {/* Bridge connection indicator bar */}
        {bridgeConnected && (
          <div className="flex items-center px-3 py-1 bg-emerald-900/30 border-b border-emerald-700/20 text-[10px] text-emerald-300/70 shrink-0">
            <span className="inline-block w-1.5 h-1.5 rounded-full bg-emerald-400 mr-2" />
            <span>Bridge connected</span>
          </div>
        )}
        {/* File watcher notification bar */}
        {watcherNotification && (
          <div className="flex items-center justify-between px-3 py-1.5 bg-blue-900/60 border-b border-blue-700/40 text-xs text-blue-200 shrink-0">
            <div className="flex items-center gap-2">
              <span className="inline-block w-1.5 h-1.5 rounded-full bg-blue-400 animate-pulse" />
              <span>{watcherNotification}</span>
            </div>
            <button
              onClick={dismissNotification}
              className="text-blue-400 hover:text-blue-200 ml-4"
            >
              Dismiss
            </button>
          </div>
        )}
        <div className="flex-1 min-h-0 min-w-0 flex">
          <Sidebar
            activePage={activePage}
            onNavigate={handleNavigate}
            activeProject={activeProject}
            selectedLevel={selectedLevel}
            collapsed={sidebarCollapsed}
            onToggleCollapse={() => setSidebarCollapsed(c => !c)}
            width={sidebarWidth}
          />
          {!sidebarCollapsed && (
            <ResizeHandle
              direction="horizontal"
              onResize={(delta) => setSidebarWidth((w) => Math.max(160, Math.min(400, w + delta)))}
            />
          )}
          <div className="flex-1 min-h-0 min-w-0 flex">
            {renderPage()}
          </div>
        </div>
      </div>
    </ContextMenuProvider>
  )
}

// ─── Font Loader ────────────────────────────────────────────────────────────

async function loadFonts(): Promise<void> {
  const fontFiles = await window.electronAPI.getFonts().catch(() => undefined)
  if (!fontFiles || !Array.isArray(fontFiles) || fontFiles.length === 0) {
    console.warn('No font files found in fonts/ directory')
    return
  }

  const loadPromises: Promise<void>[] = []

  for (const file of fontFiles) {
    const fontData = await window.electronAPI.getFontData(file).catch(() => undefined)
    if (!fontData) continue

    const familyName = file.replace(/\.(ttf|otf|woff|woff2)$/i, '')

    const font = new FontFace(familyName, `url(${fontData.data})`)
    const p = font.load().then((loaded) => {
      document.fonts.add(loaded)
      console.log(`Loaded font: ${familyName}`)
    })
    loadPromises.push(p)
  }

  await Promise.all(loadPromises)
  await document.fonts.ready
}
