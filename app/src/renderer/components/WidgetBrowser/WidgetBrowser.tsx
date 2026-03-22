import { useEffect, useState } from 'react'
import { useWidgetStore } from '../../stores/widgetStore'
import type { WidgetSummary } from '../../lib/api'

interface WidgetBrowserProps {
  onWidgetSelect: (path: string) => void
}

export function WidgetBrowser({ onWidgetSelect }: WidgetBrowserProps) {
  const {
    projectWidgets,
    libraryWidgets,
    loadedWidgetPath,
    listLoading,
    listError,
    searchQuery,
    fetchProjectWidgets,
    fetchLibraryWidgets,
    setSearchQuery,
  } = useWidgetStore()

  useEffect(() => {
    fetchProjectWidgets()
    fetchLibraryWidgets()
  }, [])

  const filterWidgets = (widgets: WidgetSummary[]) => {
    if (!searchQuery) return widgets
    const q = searchQuery.toLowerCase()
    return widgets.filter((w) => w.name.toLowerCase().includes(q))
  }

  const filteredProject = filterWidgets(projectWidgets)
  const filteredLibrary = filterWidgets(libraryWidgets)

  return (
    <div className="flex flex-col h-full text-sm">
      {/* Search */}
      <div className="p-2 border-b border-fn-border">
        <input
          type="text"
          placeholder="Search widgets..."
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          className="w-full px-2 py-1 bg-fn-darker border border-fn-border rounded text-fn-text text-xs focus:outline-none focus:border-fn-accent"
        />
      </div>

      <div className="flex-1 overflow-y-auto">
        {listLoading && (
          <div className="p-3 text-fn-muted text-xs">Scanning for widgets...</div>
        )}

        {listError && (
          <div className="p-3 text-red-400 text-xs">{listError}</div>
        )}

        {/* Project Widgets */}
        {filteredProject.length > 0 && (
          <WidgetSection
            title="Project Widgets"
            widgets={filteredProject}
            activeWidget={loadedWidgetPath}
            onSelect={onWidgetSelect}
          />
        )}

        {/* Library Widgets */}
        {filteredLibrary.length > 0 && (
          <WidgetSection
            title="Library Widgets"
            widgets={filteredLibrary}
            activeWidget={loadedWidgetPath}
            onSelect={onWidgetSelect}
            defaultCollapsed
          />
        )}

        {!listLoading && filteredProject.length === 0 && filteredLibrary.length === 0 && (
          <div className="p-3 text-fn-muted text-xs">
            {searchQuery ? 'No widgets match your search.' : 'No widget blueprints found.'}
          </div>
        )}
      </div>

      {/* Refresh button */}
      <div className="p-2 border-t border-fn-border">
        <button
          onClick={() => { fetchProjectWidgets(); fetchLibraryWidgets() }}
          className="w-full px-2 py-1 bg-fn-darker border border-fn-border rounded text-fn-muted text-xs hover:text-fn-text hover:border-fn-accent transition-colors"
        >
          Refresh
        </button>
      </div>
    </div>
  )
}

function WidgetSection({
  title,
  widgets,
  activeWidget,
  onSelect,
  defaultCollapsed = false,
}: {
  title: string
  widgets: WidgetSummary[]
  activeWidget: string | null
  onSelect: (path: string) => void
  defaultCollapsed?: boolean
}) {
  const [collapsed, setCollapsed] = useState(defaultCollapsed)

  return (
    <div>
      <button
        onClick={() => setCollapsed(!collapsed)}
        className="w-full flex items-center justify-between px-3 py-1.5 text-xs font-medium text-fn-muted hover:text-fn-text bg-fn-darker/50"
      >
        <span>{title} ({widgets.length})</span>
        <span className="text-[10px]">{collapsed ? '\u25B6' : '\u25BC'}</span>
      </button>

      {!collapsed && (
        <div>
          {widgets.map((widget) => (
            <button
              key={widget.path}
              onClick={() => onSelect(widget.path)}
              className={`w-full flex items-center gap-2 px-3 py-1.5 text-left text-xs transition-colors ${
                activeWidget === widget.path
                  ? 'bg-fn-accent/20 text-fn-accent border-l-2 border-fn-accent'
                  : 'text-fn-text hover:bg-fn-darker/80 border-l-2 border-transparent'
              }`}
            >
              <span className="text-fn-muted text-[10px]">W</span>
              <span className="flex-1 truncate">{widget.name}</span>
              <span className="text-fn-muted text-[10px]">{widget.widgetCount}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
