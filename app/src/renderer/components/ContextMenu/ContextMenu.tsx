import { useEffect, useRef, useState, useCallback, createContext, useContext } from 'react'

// ─── Types ──────────────────────────────────────────────────────────────────

export interface MenuItem {
  label: string
  icon?: React.ReactNode
  shortcut?: string
  onClick: () => void
  disabled?: boolean
  danger?: boolean
}

export interface MenuDivider {
  type: 'divider'
}

export type MenuEntry = MenuItem | MenuDivider

function isDivider(entry: MenuEntry): entry is MenuDivider {
  return 'type' in entry && entry.type === 'divider'
}

// ─── Context ────────────────────────────────────────────────────────────────

interface ContextMenuState {
  show: (x: number, y: number, items: MenuEntry[]) => void
  hide: () => void
}

const ContextMenuContext = createContext<ContextMenuState | null>(null)

export function useContextMenu() {
  const ctx = useContext(ContextMenuContext)
  if (!ctx) throw new Error('useContextMenu must be used within ContextMenuProvider')
  return ctx
}

// ─── Provider ───────────────────────────────────────────────────────────────

export function ContextMenuProvider({ children }: { children: React.ReactNode }) {
  const [menu, setMenu] = useState<{
    x: number
    y: number
    items: MenuEntry[]
  } | null>(null)

  const show = useCallback((x: number, y: number, items: MenuEntry[]) => {
    setMenu({ x, y, items })
  }, [])

  const hide = useCallback(() => setMenu(null), [])

  return (
    <ContextMenuContext.Provider value={{ show, hide }}>
      {children}
      {menu && (
        <ContextMenuPopup
          x={menu.x}
          y={menu.y}
          items={menu.items}
          onClose={hide}
        />
      )}
    </ContextMenuContext.Provider>
  )
}

// ─── Popup ──────────────────────────────────────────────────────────────────

function ContextMenuPopup({
  x,
  y,
  items,
  onClose
}: {
  x: number
  y: number
  items: MenuEntry[]
  onClose: () => void
}) {
  const ref = useRef<HTMLDivElement>(null)
  const [pos, setPos] = useState({ x, y })

  // Adjust position so the menu doesn't overflow the viewport
  useEffect(() => {
    if (!ref.current) return
    const rect = ref.current.getBoundingClientRect()
    let adjX = x
    let adjY = y
    if (x + rect.width > window.innerWidth - 8) adjX = window.innerWidth - rect.width - 8
    if (y + rect.height > window.innerHeight - 8) adjY = window.innerHeight - rect.height - 8
    if (adjX < 4) adjX = 4
    if (adjY < 4) adjY = 4
    setPos({ x: adjX, y: adjY })
  }, [x, y])

  // Close on outside click or Escape
  useEffect(() => {
    const handleClick = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        onClose()
      }
    }
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    // Delay so the opening right-click doesn't immediately close the menu
    const timer = setTimeout(() => {
      window.addEventListener('mousedown', handleClick)
      window.addEventListener('keydown', handleKey)
    }, 0)
    return () => {
      clearTimeout(timer)
      window.removeEventListener('mousedown', handleClick)
      window.removeEventListener('keydown', handleKey)
    }
  }, [onClose])

  return (
    <div
      ref={ref}
      className="fixed z-[9999] min-w-[180px] bg-fn-dark/98 backdrop-blur-xl border border-fn-border/80 rounded-lg shadow-2xl shadow-black/60 py-1 animate-in fade-in duration-100"
      style={{ left: pos.x, top: pos.y }}
    >
      {items.map((entry, i) => {
        if (isDivider(entry)) {
          return <div key={`div-${i}`} className="h-px bg-fn-border/50 my-1 mx-2" />
        }

        return (
          <button
            key={`${entry.label}-${i}`}
            className={`w-full flex items-center gap-2.5 px-3 py-1.5 text-left transition-colors ${
              entry.disabled
                ? 'text-gray-600 cursor-not-allowed'
                : entry.danger
                  ? 'text-gray-300 hover:bg-red-500/15 hover:text-red-400'
                  : 'text-gray-300 hover:bg-white/8 hover:text-white'
            }`}
            onClick={() => {
              if (entry.disabled) return
              entry.onClick()
              onClose()
            }}
            disabled={entry.disabled}
          >
            {entry.icon && <span className="w-3.5 h-3.5 shrink-0 flex items-center justify-center">{entry.icon}</span>}
            <span className="text-[11px] flex-1">{entry.label}</span>
            {entry.shortcut && (
              <span className="text-[9px] text-gray-600 font-mono ml-4">{entry.shortcut}</span>
            )}
          </button>
        )
      })}
    </div>
  )
}
