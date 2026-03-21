import { useState, useEffect, useRef } from 'react'
import { useForgeStore } from '../../stores/forgeStore'
import { useLibraryStore } from '../../stores/libraryStore'
import type { ForgeProject } from '../../../shared/types'

export type PageId =
  | 'home'
  | 'dashboard'
  | 'project-health'
  | 'widget-editor'
  | 'audit'
  | 'verse-errors'
  | 'device-wiring'
  | 'scene-preview'
  | 'recipes'
  | 'levels'
  | 'content-browser'
  | 'user-assets'
  | 'epic-assets'
  | 'devices'
  | 'library-browse'
  | 'library-verse'
  | 'library-widgets'
  | 'library-materials'
  | 'library-assets-by-type'
  | 'library-device-configs'
  | 'library-manage'
  | 'verse-reference'
  | 'project-verse-files'
  | 'projects'
  | 'staged'
  | 'settings'

interface NavItem {
  id: PageId
  label: string
  icon: JSX.Element
  indent?: boolean
}

interface SidebarProps {
  activePage: PageId
  onNavigate: (page: PageId) => void
  activeProject: { name: string; type: string } | null
  selectedLevel: string | null
  collapsed: boolean
  onToggleCollapse: () => void
  width?: number
}

// ─── Icons ──────────────────────────────────────────────────────────────────

const icons = {
  home: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path d="M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-4 0a1 1 0 01-1-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 01-1 1h-2z" />
    </svg>
  ),
  folder: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path d="M3 7v10a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-6l-2-2H5a2 2 0 00-2 2z" />
    </svg>
  ),
  contentBrowser: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path d="M5 19a2 2 0 01-2-2V7a2 2 0 012-2h4l2 2h4a2 2 0 012 2v1M5 19h14a2 2 0 002-2v-5a2 2 0 00-2-2H9a2 2 0 00-2 2v5a2 2 0 01-2 2z" />
    </svg>
  ),
  code: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4" />
    </svg>
  ),
  levels: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path d="M3.055 11H5a2 2 0 012 2v1a2 2 0 002 2 2 2 0 012 2v2.945M8 3.935V5.5A2.5 2.5 0 0010.5 8h.5a2 2 0 012 2 2 2 0 104 0 2 2 0 012-2h1.064M15 20.488V18a2 2 0 012-2h3.064M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
    </svg>
  ),
  devices: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.066 2.573c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.573 1.066c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.066-2.573c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.573-1.066z" />
      <path d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
    </svg>
  ),
  userAssets: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4" />
    </svg>
  ),
  epicAssets: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
    </svg>
  ),
  widget: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path d="M4 5a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1H5a1 1 0 01-1-1V5zm10 0a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1h-4a1 1 0 01-1-1V5zM4 15a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1H5a1 1 0 01-1-1v-4zm10 0a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1h-4a1 1 0 01-1-1v-4z" />
    </svg>
  ),
  audit: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
    </svg>
  ),
  wiring: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <circle cx="5" cy="6" r="2" />
      <circle cx="19" cy="6" r="2" />
      <circle cx="12" cy="18" r="2" />
      <path d="M7 7l3 9M17 7l-3 9M7 6h10" />
    </svg>
  ),
  staged: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path d="M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4m0 5c0 2.21-3.582 4-8 4s-8-1.79-8-4" />
    </svg>
  ),
  search: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
    </svg>
  ),
  dashboard: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path d="M4 6a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2H6a2 2 0 01-2-2V6zm10 0a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2h-2a2 2 0 01-2-2V6zM4 16a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2H6a2 2 0 01-2-2v-2zm10 0a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2h-2a2 2 0 01-2-2v-2z" />
    </svg>
  ),
  settings: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path d="M12 6V4m0 2a2 2 0 100 4m0-4a2 2 0 110 4m-6 8a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4m6 6v10m6-2a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4" />
    </svg>
  ),
  book: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path d="M12 6.253v13m0-13C10.832 5.477 9.246 5 7.5 5S4.168 5.477 3 6.253v13C4.168 18.477 5.754 18 7.5 18s3.332.477 4.5 1.253m0-13C13.168 5.477 14.754 5 16.5 5c1.747 0 3.332.477 4.5 1.253v13C19.832 18.477 18.247 18 16.5 18c-1.746 0-3.332.477-4.5 1.253" />
    </svg>
  ),
  verseErrors: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4.5c-.77-.833-2.694-.833-3.464 0L3.34 16.5c-.77.833.192 2.5 1.732 2.5z" />
    </svg>
  ),
  health: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path d="M4.318 6.318a4.5 4.5 0 000 6.364L12 20.364l7.682-7.682a4.5 4.5 0 00-6.364-6.364L12 7.636l-1.318-1.318a4.5 4.5 0 00-6.364 0z" />
    </svg>
  ),
  chevronDown: (
    <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
      <path d="M19 9l-7 7-7-7" />
    </svg>
  ),
  plus: (
    <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
      <path d="M12 4v16m8-8H4" />
    </svg>
  ),
}

// Green dot for active project
const projectDot = (
  <span className="w-2 h-2 rounded-full bg-emerald-400 shrink-0" />
)

export function Sidebar({ activePage, onNavigate, activeProject, selectedLevel, collapsed, onToggleCollapse, width = 220 }: SidebarProps) {
  const [dropdownOpen, setDropdownOpen] = useState(false)
  const [dropdownSearch, setDropdownSearch] = useState('')
  const dropdownRef = useRef<HTMLDivElement>(null)

  // Library dropdown state
  const [libDropdownOpen, setLibDropdownOpen] = useState(false)
  const [libDropdownSearch, setLibDropdownSearch] = useState('')
  const libDropdownRef = useRef<HTMLDivElement>(null)

  // Section collapse state
  const [projectSectionCollapsed, setProjectSectionCollapsed] = useState(false)
  const [librarySectionCollapsed, setLibrarySectionCollapsed] = useState(false)

  const projectList = useForgeStore((s) => s.projectList)
  const fetchProjects = useForgeStore((s) => s.fetchProjects)
  const invalidateCache = useForgeStore((s) => s.invalidateCache)
  const fetchStatus = useForgeStore((s) => s.fetchStatus)

  const libraryList = useLibraryStore((s) => s.libraryList)
  const activeLibrary = useLibraryStore((s) => s.activeLibrary)
  const fetchLibraries = useLibraryStore((s) => s.fetchLibraries)
  const activateLibrary = useLibraryStore((s) => s.activateLibrary)

  // Fetch projects and libraries for dropdowns
  useEffect(() => {
    fetchProjects()
  }, [fetchProjects])

  useEffect(() => {
    fetchLibraries()
  }, [fetchLibraries])

  // Close dropdowns on outside click
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) {
        setDropdownOpen(false)
        setDropdownSearch('')
      }
      if (libDropdownRef.current && !libDropdownRef.current.contains(e.target as Node)) {
        setLibDropdownOpen(false)
        setLibDropdownSearch('')
      }
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [])

  async function handleSwitchProject(project: ForgeProject) {
    try {
      await window.electronAPI.forgeActivateProject(project.id)
      invalidateCache()
      await fetchStatus()
      await fetchProjects()
    } catch (err) {
      console.error('Failed to switch project:', err)
    }
    setDropdownOpen(false)
    setDropdownSearch('')
  }

  async function handleSwitchLibrary(libId: string) {
    try {
      await activateLibrary(libId)
    } catch (err) {
      console.error('Failed to switch library:', err)
    }
    setLibDropdownOpen(false)
    setLibDropdownSearch('')
  }

  const projects = projectList?.projects ?? []
  const activeProjectId = projectList?.activeProjectId
  const filteredProjects = dropdownSearch
    ? projects.filter((p) => p.name.toLowerCase().includes(dropdownSearch.toLowerCase()))
    : projects

  const libraries = libraryList?.libraries ?? []
  const activeLibraryId = libraryList?.activeLibraryId
  const filteredLibraries = libDropdownSearch
    ? libraries.filter((l) => l.name.toLowerCase().includes(libDropdownSearch.toLowerCase()))
    : libraries

  // Library nav items (only when a library is active)
  const isLibraryIndexed = activeLibrary?.indexedAt != null
  const libraryBrowseItems: NavItem[] = activeLibrary
    ? [{ id: 'library-browse', label: 'Browse All', icon: icons.contentBrowser, indent: true }]
    : []
  // Index-dependent items only show when indexed
  const libraryIndexedItems: NavItem[] = activeLibrary && isLibraryIndexed
    ? [
        { id: 'library-verse', label: 'Verse Files', icon: icons.code, indent: true },
        { id: 'library-widgets', label: 'Widgets', icon: icons.widget, indent: true },
        { id: 'library-materials', label: 'Materials', icon: icons.epicAssets, indent: true },
        { id: 'library-assets-by-type', label: 'Assets by Type', icon: icons.userAssets, indent: true },
        { id: 'library-device-configs', label: 'Device Configs', icon: icons.devices, indent: true },
      ]
    : []
  const libraryItems = [...libraryBrowseItems, ...libraryIndexedItems]

  // Blue dot for active library
  const libraryDot = (
    <span className="w-2 h-2 rounded-full bg-blue-400 shrink-0" />
  )

  // Nav items
  const topItems: NavItem[] = [
    { id: 'home', label: 'Home', icon: icons.home },
    { id: 'projects', label: 'Projects', icon: icons.folder },
  ]

  const browseItems: NavItem[] = activeProject
    ? [
        { id: 'content-browser', label: 'Content', icon: icons.contentBrowser, indent: true },
        { id: 'project-verse-files', label: 'Verse Files', icon: icons.code, indent: true },
        { id: 'levels', label: 'Levels', icon: icons.levels, indent: true },
        ...(selectedLevel !== null
          ? [{ id: 'devices' as PageId, label: 'Devices', icon: icons.devices, indent: true }]
          : []),
        { id: 'user-assets', label: 'User Assets', icon: icons.userAssets, indent: true },
        { id: 'epic-assets', label: 'Epic Assets', icon: icons.epicAssets, indent: true },
      ]
    : []

  const toolsItems: NavItem[] = activeProject
    ? [
        { id: 'dashboard', label: 'Dashboard', icon: icons.dashboard, indent: true },
        { id: 'project-health', label: 'Health Report', icon: icons.health, indent: true },
        { id: 'widget-editor', label: 'Widget Editor', icon: icons.widget, indent: true },
        { id: 'audit', label: 'Audit', icon: icons.audit, indent: true },
        { id: 'verse-errors', label: 'Error Explainer', icon: icons.verseErrors, indent: true },
        { id: 'device-wiring', label: 'Device Wiring', icon: icons.wiring, indent: true },
        { id: 'scene-preview', label: 'Scene Preview', icon: icons.levels, indent: true },
        { id: 'recipes', label: 'Device Recipes', icon: icons.devices, indent: true },
        { id: 'staged', label: 'Staged Changes', icon: icons.staged, indent: true },
      ]
    : []

  function renderNavButton(item: NavItem) {
    const isActive = activePage === item.id
    return (
      <button
        key={item.id}
        className={`w-full flex items-center gap-2.5 transition-colors ${
          collapsed ? 'justify-center px-0 py-2' : item.indent ? 'pl-5 pr-3 py-1.5' : 'px-3 py-1.5'
        } ${
          isActive
            ? 'text-fn-rare bg-fn-rare/10'
            : 'text-gray-400 hover:text-gray-200 hover:bg-white/[0.03]'
        }`}
        onClick={() => onNavigate(item.id)}
        title={collapsed ? item.label : undefined}
      >
        <span className="shrink-0">{item.icon}</span>
        {!collapsed && (
          <span className="text-[11px] font-medium whitespace-nowrap truncate">
            {item.label}
          </span>
        )}
        {isActive && !collapsed && (
          <div className="ml-auto w-1 h-4 rounded-full bg-fn-rare shrink-0" />
        )}
      </button>
    )
  }

  function renderSectionHeader(title: string) {
    if (collapsed) return <div className="h-px bg-fn-border mx-2 my-1" />
    return (
      <div className="px-3 pt-2 pb-1 text-[9px] font-semibold text-gray-600 tracking-widest uppercase">
        {title}
      </div>
    )
  }

  // All user projects are protected (copy-on-read)
  const typeBadgeLabel = 'SAFE'
  const typeBadgeColor = 'text-emerald-400 bg-emerald-400/10 border-emerald-400/20'

  return (
    <div
      className="h-full bg-fn-dark border-r border-fn-border flex flex-col shrink-0 transition-[width] duration-200 ease-in-out min-h-0"
      style={{ width: collapsed ? 48 : width }}
    >
      {/* Logo + collapse toggle */}
      <div className="h-10 flex items-center gap-2 px-3 border-b border-fn-border shrink-0 overflow-hidden">
        <img
          src={new URL('../../assets/logo.png', import.meta.url).href}
          alt="WellVersed"
          className={`w-6 h-6 shrink-0 ${collapsed ? 'cursor-pointer' : ''}`}
          onClick={collapsed ? onToggleCollapse : undefined}
          title={collapsed ? 'Expand sidebar' : undefined}
        />
        {!collapsed && (
          <>
            <span className="text-[11px] font-semibold text-white tracking-wide whitespace-nowrap flex-1">
              WellVersed
            </span>
            <button
              onClick={onToggleCollapse}
              className="shrink-0 p-1 rounded text-gray-600 hover:text-gray-300 hover:bg-white/[0.05] transition-colors"
              title="Collapse sidebar"
            >
              <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path d="M11 19l-7-7 7-7" />
              </svg>
            </button>
          </>
        )}
      </div>

      {/* Navigation */}
      <div className="flex-1 overflow-y-auto py-2 min-h-0">
        {/* Top items */}
        <div className="mb-1">
          {topItems.map(renderNavButton)}
        </div>

        {/* ─── Project Section ─── */}
        {!collapsed ? (
          <div className="mx-2 mt-2 mb-1 rounded-lg border border-fn-border bg-fn-darker/50">
            {/* Project Selector Dropdown */}
            <div className="relative" ref={dropdownRef}>
              <div className="flex items-center">
                <button
                  className="flex-1 flex items-center gap-2 px-2.5 py-2 hover:bg-white/[0.03] transition-colors rounded-tl-lg min-w-0"
                  onClick={() => setDropdownOpen(!dropdownOpen)}
                >
                  {activeProject ? projectDot : (
                    <span className="w-2 h-2 rounded-full bg-gray-600 shrink-0" />
                  )}
                  <span className="text-[11px] font-semibold text-gray-200 truncate flex-1 text-left">
                    {activeProject?.name ?? 'No Project'}
                  </span>
                  <span className="text-gray-500 shrink-0">{icons.chevronDown}</span>
                </button>
                {activeProject && (
                  <button
                    className="px-1.5 py-2 text-gray-600 hover:text-gray-300 transition-colors shrink-0"
                    onClick={() => setProjectSectionCollapsed(!projectSectionCollapsed)}
                    title={projectSectionCollapsed ? 'Expand' : 'Collapse'}
                  >
                    <svg className={`w-3 h-3 transition-transform ${projectSectionCollapsed ? '-rotate-90' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path d="M19 9l-7 7-7-7" />
                    </svg>
                  </button>
                )}
              </div>

              {/* Dropdown */}
              {dropdownOpen && (
                <div className="absolute left-0 right-0 top-full z-50 bg-fn-dark border border-fn-border rounded-b-lg shadow-xl max-h-64 overflow-hidden">
                  {/* Search */}
                  <div className="p-1.5 border-b border-fn-border">
                    <input
                      type="text"
                      className="w-full text-[10px] bg-fn-darker border border-fn-border rounded px-2 py-1 text-gray-300 focus:border-fn-rare/50 focus:outline-none placeholder-gray-600"
                      placeholder="Search projects..."
                      value={dropdownSearch}
                      onChange={(e) => setDropdownSearch(e.target.value)}
                      autoFocus
                    />
                  </div>

                  {/* Project list */}
                  <div className="max-h-44 overflow-y-auto">
                    {filteredProjects.map((p) => (
                      <button
                        key={p.id}
                        className={`w-full flex items-center gap-2 px-2.5 py-1.5 text-left transition-colors ${
                          p.id === activeProjectId
                            ? 'bg-emerald-400/10 text-white'
                            : 'text-gray-400 hover:text-white hover:bg-white/[0.03]'
                        }`}
                        onClick={() => handleSwitchProject(p)}
                      >
                        {p.id === activeProjectId ? projectDot : (
                          <span className="w-2 h-2 rounded-full bg-gray-700 shrink-0" />
                        )}
                        <span className="text-[10px] truncate flex-1">{p.name}</span>
                        <span className="text-[8px] text-gray-600 shrink-0">{p.name?.length > 15 ? '...' : ''}</span>
                      </button>
                    ))}
                    {filteredProjects.length === 0 && (
                      <div className="px-2.5 py-2 text-[10px] text-gray-600">No projects found</div>
                    )}
                  </div>

                  {/* Add project */}
                  <button
                    className="w-full flex items-center gap-2 px-2.5 py-1.5 text-fn-rare hover:bg-fn-rare/10 transition-colors border-t border-fn-border"
                    onClick={() => {
                      setDropdownOpen(false)
                      onNavigate('projects')
                    }}
                  >
                    {icons.plus}
                    <span className="text-[10px] font-medium">Add Project</span>
                  </button>
                </div>
              )}
            </div>

            {/* Browse + Tools (collapsible) */}
            {activeProject && !projectSectionCollapsed && (
              <div className="border-t border-fn-border/50">
                <div className="py-1">
                  {renderSectionHeader('BROWSE')}
                  {browseItems.map(renderNavButton)}
                </div>
                <div className="py-1 border-t border-fn-border/30">
                  {renderSectionHeader('TOOLS')}
                  {toolsItems.map(renderNavButton)}
                </div>
              </div>
            )}
          </div>
        ) : (
          /* Collapsed: project section */
          activeProject && (
            <div className="py-1 mx-1 my-1 rounded border border-emerald-400/20 bg-emerald-400/[0.03]">
              <div className="flex justify-center py-1" title={`${activeProject.name} (Protected)`}>
                <span className="w-2 h-2 rounded-full bg-emerald-400" />
              </div>
              {browseItems.map(renderNavButton)}
              <div className="h-px bg-fn-border mx-2 my-0.5" />
              {toolsItems.map(renderNavButton)}
            </div>
          )
        )}

        {/* ─── Library Section ─── */}
        {!collapsed ? (
          <div className="mx-2 mt-2 mb-1 rounded-lg border border-fn-border bg-fn-darker/50">
            {/* Library Selector Dropdown */}
            <div className="relative" ref={libDropdownRef}>
              <div className="flex items-center">
                <button
                  className="flex-1 flex items-center gap-2 px-2.5 py-2 hover:bg-white/[0.03] transition-colors rounded-tl-lg min-w-0"
                  onClick={() => setLibDropdownOpen(!libDropdownOpen)}
                >
                  {activeLibrary ? libraryDot : (
                    <span className="w-2 h-2 rounded-full bg-gray-600 shrink-0" />
                  )}
                  <span className="text-[11px] font-semibold text-gray-200 truncate flex-1 text-left">
                    {activeLibrary?.name ?? 'No Library'}
                  </span>
                  <span className="text-gray-500 shrink-0">{icons.chevronDown}</span>
                </button>
                {activeLibrary && (
                  <button
                    className="px-1.5 py-2 text-gray-600 hover:text-gray-300 transition-colors shrink-0"
                    onClick={() => setLibrarySectionCollapsed(!librarySectionCollapsed)}
                    title={librarySectionCollapsed ? 'Expand' : 'Collapse'}
                  >
                    <svg className={`w-3 h-3 transition-transform ${librarySectionCollapsed ? '-rotate-90' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path d="M19 9l-7 7-7-7" />
                    </svg>
                  </button>
                )}
              </div>

              {/* Dropdown */}
              {libDropdownOpen && (
                <div className="absolute left-0 right-0 top-full z-50 bg-fn-dark border border-fn-border rounded-b-lg shadow-xl max-h-64 overflow-hidden">
                  {/* Search */}
                  <div className="p-1.5 border-b border-fn-border">
                    <input
                      type="text"
                      className="w-full text-[10px] bg-fn-darker border border-fn-border rounded px-2 py-1 text-gray-300 focus:border-blue-400/50 focus:outline-none placeholder-gray-600"
                      placeholder="Search libraries..."
                      value={libDropdownSearch}
                      onChange={(e) => setLibDropdownSearch(e.target.value)}
                      autoFocus
                    />
                  </div>

                  {/* Library list */}
                  <div className="max-h-44 overflow-y-auto">
                    {filteredLibraries.map((lib) => (
                      <button
                        key={lib.id}
                        className={`w-full flex items-center gap-2 px-2.5 py-1.5 text-left transition-colors ${
                          lib.id === activeLibraryId
                            ? 'bg-blue-400/10 text-white'
                            : 'text-gray-400 hover:text-white hover:bg-white/[0.03]'
                        }`}
                        onClick={() => handleSwitchLibrary(lib.id)}
                      >
                        {lib.id === activeLibraryId ? libraryDot : (
                          <span className="w-2 h-2 rounded-full bg-gray-700 shrink-0" />
                        )}
                        <span className="text-[10px] truncate flex-1">{lib.name}</span>
                        <span className="text-[8px] text-gray-600 shrink-0">{lib.name?.length > 15 ? '...' : ''}</span>
                      </button>
                    ))}
                    {filteredLibraries.length === 0 && (
                      <div className="px-2.5 py-2 text-[10px] text-gray-600">No libraries found</div>
                    )}
                  </div>

                  {/* Add library */}
                  <button
                    className="w-full flex items-center gap-2 px-2.5 py-1.5 text-blue-400 hover:bg-blue-400/10 transition-colors border-t border-fn-border"
                    onClick={() => {
                      setLibDropdownOpen(false)
                      onNavigate('library-manage')
                    }}
                  >
                    {icons.plus}
                    <span className="text-[10px] font-medium">Add Library</span>
                  </button>
                </div>
              )}
            </div>

            {/* Index required prompt */}
            {activeLibrary && !isLibraryIndexed && !librarySectionCollapsed && (
              <div className="border-t border-fn-border/50 px-3 py-2">
                <div className="flex items-center gap-1.5 text-[10px] text-amber-400/80 mb-1.5">
                  <svg className="w-3 h-3 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4.5c-.77-.833-2.694-.833-3.464 0L3.34 16.5c-.77.833.192 2.5 1.732 2.5z" />
                  </svg>
                  <span>Index required</span>
                </div>
                <button
                  onClick={() => onNavigate('library-manage')}
                  className="w-full px-2 py-1 text-[9px] font-medium text-amber-400 bg-amber-400/10 border border-amber-400/20 rounded hover:bg-amber-400/20 transition-colors text-center"
                >
                  Go to Library Settings to Build Index
                </button>
              </div>
            )}

            {/* Library nav items (collapsible) */}
            {activeLibrary && libraryItems.length > 0 && !librarySectionCollapsed && (
              <div className="border-t border-fn-border/50 py-1">
                {libraryItems.map((item) => {
                  const isActive = activePage === item.id
                  return (
                    <button
                      key={item.id}
                      className={`w-full flex items-center gap-2.5 transition-colors pl-5 pr-3 py-1.5 ${
                        isActive
                          ? 'text-blue-400 bg-blue-400/10'
                          : 'text-gray-400 hover:text-gray-200 hover:bg-white/[0.03]'
                      }`}
                      onClick={() => onNavigate(item.id)}
                    >
                      <span className="shrink-0">{item.icon}</span>
                      <span className="text-[11px] font-medium whitespace-nowrap truncate">
                        {item.label}
                      </span>
                      {isActive && (
                        <div className="ml-auto w-1 h-4 rounded-full bg-blue-400 shrink-0" />
                      )}
                    </button>
                  )
                })}
              </div>
            )}

            {/* Verse Reference — always visible (language docs, not library content) */}
            <div className="border-t border-fn-border/50">
              <button
                className={`w-full flex items-center gap-2.5 transition-colors pl-5 pr-3 py-1.5 ${
                  activePage === 'verse-reference'
                    ? 'text-purple-400 bg-purple-400/10'
                    : 'text-gray-400 hover:text-gray-200 hover:bg-white/[0.03]'
                }`}
                onClick={() => onNavigate('verse-reference')}
              >
                <span className="shrink-0">{icons.book}</span>
                <span className="text-[11px] font-medium whitespace-nowrap truncate">
                  Verse Reference
                </span>
                {activePage === 'verse-reference' && (
                  <div className="ml-auto w-1 h-4 rounded-full bg-purple-400 shrink-0" />
                )}
              </button>
            </div>
          </div>
        ) : (
          /* Collapsed: library section */
          <div className="mx-1 my-1 rounded border border-blue-400/20 bg-blue-400/[0.03] py-1">
            {activeLibrary ? (
              <>
                <div className="flex justify-center py-1" title={`${activeLibrary.name} (Reference)`}>
                  <span className="w-2 h-2 rounded-full bg-blue-400" />
                </div>
                {libraryItems.map((item) => {
                  const isActive = activePage === item.id
                  return (
                    <button
                      key={item.id}
                      className={`w-full flex items-center justify-center px-0 py-2 transition-colors ${
                        isActive
                          ? 'text-blue-400 bg-blue-400/10'
                          : 'text-gray-400 hover:text-gray-200 hover:bg-white/[0.03]'
                      }`}
                      onClick={() => onNavigate(item.id)}
                      title={item.label}
                    >
                      <span className="shrink-0">{item.icon}</span>
                    </button>
                  )
                })}
              </>
            ) : (
              <button
                className="w-full flex items-center justify-center px-0 py-2 text-gray-600 hover:text-blue-400 transition-colors"
                onClick={() => onNavigate('library-manage')}
                title="Add Library"
              >
                {icons.plus}
              </button>
            )}
            {/* Verse Reference — always visible */}
            <div className="h-px bg-fn-border mx-2 my-0.5" />
            <button
              className={`w-full flex items-center justify-center px-0 py-2 transition-colors ${
                activePage === 'verse-reference'
                  ? 'text-purple-400 bg-purple-400/10'
                  : 'text-gray-400 hover:text-gray-200 hover:bg-white/[0.03]'
              }`}
              onClick={() => onNavigate('verse-reference')}
              title="Verse Reference"
            >
              <span className="shrink-0">{icons.book}</span>
            </button>
          </div>
        )}
      </div>

      {/* Footer: Settings */}
      <div className="border-t border-fn-border p-2 shrink-0">
        <button
          className={`w-full flex items-center gap-2.5 transition-colors rounded ${
            collapsed ? 'justify-center px-0 py-1.5' : 'px-3 py-1.5'
          } ${
            activePage === 'settings'
              ? 'text-fn-rare bg-fn-rare/10'
              : 'text-gray-500 hover:text-gray-300 hover:bg-white/[0.03]'
          }`}
          onClick={() => onNavigate('settings')}
          title={collapsed ? 'Settings' : undefined}
        >
          <span className="shrink-0">{icons.settings}</span>
          {!collapsed && (
            <span className="text-[11px] font-medium whitespace-nowrap">Settings</span>
          )}
          {activePage === 'settings' && !collapsed && (
            <div className="ml-auto w-1 h-4 rounded-full bg-fn-rare shrink-0" />
          )}
        </button>
      </div>
    </div>
  )
}
