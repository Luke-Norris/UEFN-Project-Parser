import { useEffect, useState, useCallback, useRef } from 'react'
import { useAssetStore } from '../../stores/assetStore'
import { useCanvasStore } from '../../stores/canvasStore'
import { AssetThumbnail } from './AssetThumbnail'
import { useAssetDrag } from '../../hooks/useAssetDrag'
import { useContextMenu } from '../ContextMenu/ContextMenu'
import type { MenuEntry } from '../ContextMenu/ContextMenu'
import type { AssetCategory } from '../../../shared/types'

export function AssetBrowser() {
  const {
    index,
    loading,
    searchQuery,
    selectedCategory,
    loadAssets,
    setSearchQuery,
    setSelectedCategory,
    getFilteredAssets,
    setIndex
  } = useAssetStore()
  const { selectedObjectId } = useCanvasStore()
  const { handleAssetDrop } = useAssetDrag()
  const contextMenu = useContextMenu()
  const [displayLimit, setDisplayLimit] = useState(100)
  const [expandedFolders, setExpandedFolders] = useState<Set<string>>(new Set())
  const [creatingFolder, setCreatingFolder] = useState(false)
  const [newFolderName, setNewFolderName] = useState('')
  const [importStatus, setImportStatus] = useState<string | null>(null)
  const newFolderInputRef = useRef<HTMLInputElement>(null)

  // Right-click on an asset thumbnail
  const showAssetContextMenu = useCallback(
    (e: React.MouseEvent, assetPath: string, _assetName: string) => {
      e.preventDefault()
      e.stopPropagation()

      const items: MenuEntry[] = [
        {
          label: 'Add to Canvas',
          onClick: () => handleAssetDrop(assetPath, undefined),
          icon: (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M12 5v14M5 12h14" />
            </svg>
          )
        },
        ...(selectedObjectId
          ? [
              {
                label: 'Swap with Selected',
                onClick: () => handleAssetDrop(assetPath, selectedObjectId),
                icon: (
                  <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path d="M7 16V4m0 0L3 8m4-4l4 4M17 8v12m0 0l4-4m-4 4l-4-4" />
                  </svg>
                )
              } as MenuEntry
            ]
          : []),
        { type: 'divider' as const },
        {
          label: 'Delete Asset',
          danger: true,
          onClick: () => handleDeleteAsset(assetPath),
          icon: (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2" />
            </svg>
          )
        }
      ]

      contextMenu.show(e.clientX, e.clientY, items)
    },
    [handleAssetDrop, selectedObjectId, contextMenu]
  )

  // Right-click on a folder
  const showFolderContextMenu = useCallback(
    (e: React.MouseEvent, category: AssetCategory) => {
      e.preventDefault()
      e.stopPropagation()

      const folderRelPath = getCategoryRelativePath(index, category)

      const items: MenuEntry[] = [
        {
          label: 'Import to Folder',
          onClick: () => handleImportToFolder(folderRelPath),
          icon: (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
            </svg>
          )
        },
        {
          label: 'New Subfolder',
          onClick: () => {
            setSelectedCategory(category.name)
            startCreatingFolder()
          },
          icon: (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M9 13h6m-3-3v6m5 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
            </svg>
          )
        },
        { type: 'divider' as const },
        {
          label: 'Open Folder',
          onClick: () => setSelectedCategory(category.name),
          icon: (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M5 19a2 2 0 01-2-2V7a2 2 0 012-2h4l2 2h4a2 2 0 012 2v1M5 19h14a2 2 0 002-2v-5a2 2 0 00-2-2H9a2 2 0 00-2 2v5a2 2 0 01-2 2z" />
            </svg>
          )
        }
      ]

      contextMenu.show(e.clientX, e.clientY, items)
    },
    [index, contextMenu, setSelectedCategory]
  )

  useEffect(() => {
    loadAssets()
  }, [loadAssets])

  // Reset display limit when search or category changes
  useEffect(() => {
    setDisplayLimit(100)
  }, [searchQuery, selectedCategory])

  // Focus the folder name input when creating
  useEffect(() => {
    if (creatingFolder && newFolderInputRef.current) {
      newFolderInputRef.current.focus()
    }
  }, [creatingFolder])

  const assets = getFilteredAssets()

  const toggleFolder = useCallback((name: string) => {
    setExpandedFolders((prev) => {
      const next = new Set(prev)
      if (next.has(name)) {
        next.delete(name)
      } else {
        next.add(name)
      }
      return next
    })
  }, [])

  // Get the relative path for the currently selected category (or root)
  const getCurrentFolderPath = useCallback((): string => {
    if (!selectedCategory || !index) return ''
    const cat = findCategory(index.categories, selectedCategory)
    if (!cat) return ''
    return getCategoryRelativePath(index, cat)
  }, [selectedCategory, index])

  // Import files to the currently viewed folder (or root)
  const handleImportToFolder = useCallback(async (targetPath?: string) => {
    const folder = targetPath ?? getCurrentFolderPath()
    setImportStatus('Importing...')
    try {
      const result = await window.electronAPI.importToAssets(folder)
      if (result.success && result.index) {
        setIndex(result.index)
        setImportStatus(`Imported ${result.imported.length} file${result.imported.length !== 1 ? 's' : ''}`)
      } else if (!result.success && result.imported.length === 0) {
        // User cancelled the dialog
        setImportStatus(null)
        return
      } else {
        setImportStatus(result.error || 'Import failed')
      }
    } catch (err) {
      setImportStatus('Import failed')
      console.error('Import error:', err)
    }

    // Clear status after a delay
    setTimeout(() => setImportStatus(null), 2500)
  }, [getCurrentFolderPath, setIndex])

  // Create a new folder
  const startCreatingFolder = useCallback(() => {
    setCreatingFolder(true)
    setNewFolderName('')
  }, [])

  const confirmCreateFolder = useCallback(async () => {
    const name = newFolderName.trim()
    if (!name) {
      setCreatingFolder(false)
      return
    }

    const parentPath = getCurrentFolderPath()
    try {
      const result = await window.electronAPI.createAssetFolder(name, parentPath)
      if (result.success && result.index) {
        setIndex(result.index)
        setImportStatus(`Created folder "${name}"`)
      } else {
        setImportStatus(result.error || 'Failed to create folder')
      }
    } catch (err) {
      setImportStatus('Failed to create folder')
      console.error('Create folder error:', err)
    }

    setCreatingFolder(false)
    setNewFolderName('')
    setTimeout(() => setImportStatus(null), 2500)
  }, [newFolderName, getCurrentFolderPath, setIndex])

  // Delete an asset
  const handleDeleteAsset = useCallback(async (filePath: string) => {
    try {
      const result = await window.electronAPI.deleteAsset(filePath)
      if (result.success && result.index) {
        setIndex(result.index)
        setImportStatus('Asset deleted')
      } else {
        setImportStatus(result.error || 'Delete failed')
      }
    } catch (err) {
      setImportStatus('Delete failed')
      console.error('Delete error:', err)
    }
    setTimeout(() => setImportStatus(null), 2500)
  }, [setIndex])

  const isSearching = searchQuery.trim().length > 0

  return (
    <div className="flex flex-col h-full">
      {/* Header */}
      <div className="p-3 border-b border-fn-border space-y-2 shrink-0">
        <div className="flex items-center justify-between">
          <h2 className="text-xs font-semibold text-gray-300">Assets</h2>
          <div className="flex items-center gap-1">
            <button
              className="text-[10px] px-2 py-0.5 rounded bg-white/5 text-gray-400 hover:bg-white/10 hover:text-white transition-colors"
              onClick={startCreatingFolder}
              title="New Folder"
            >
              <svg className="w-3 h-3 inline-block mr-0.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path d="M9 13h6m-3-3v6m5 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
              </svg>
              Folder
            </button>
            <button
              className="text-[10px] px-2 py-0.5 rounded bg-fn-rare/10 text-fn-rare hover:bg-fn-rare/20 transition-colors"
              onClick={() => handleImportToFolder()}
              title={selectedCategory ? `Import to ${selectedCategory}` : 'Import to root folder'}
            >
              + Import
            </button>
          </div>
        </div>
        <div className="relative">
          <svg className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3 h-3 text-gray-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <circle cx="11" cy="11" r="8" />
            <path d="m21 21-4.3-4.3" />
          </svg>
          <input
            type="text"
            placeholder="Search assets..."
            className="w-full bg-fn-darker border border-fn-border rounded-md pl-8 pr-8 py-1.5 text-xs text-white placeholder-gray-600 outline-none focus:border-fn-rare/40 transition-colors"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
          />
          {searchQuery && (
            <button
              className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-500 hover:text-white transition-colors"
              onClick={() => setSearchQuery('')}
            >
              <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          )}
        </div>
        {index && (
          <p className="text-[10px] text-gray-600">
            {searchQuery
              ? `${assets.length} of ${index.totalAssets.toLocaleString()} assets`
              : `${index.totalAssets.toLocaleString()} assets loaded`}
          </p>
        )}
        {/* Import status message */}
        {importStatus && (
          <p className="text-[10px] text-fn-rare animate-pulse">{importStatus}</p>
        )}
      </div>

      {/* Content area */}
      <div className="flex-1 overflow-y-auto min-h-0">
        {loading && (
          <div className="flex flex-col items-center justify-center py-12">
            <div className="w-5 h-5 border-2 border-fn-rare/30 border-t-fn-rare rounded-full animate-spin mb-2" />
            <p className="text-gray-500 text-xs">Scanning assets...</p>
          </div>
        )}

        {/* When searching, show flat asset grid */}
        {!loading && isSearching && (
          <div className="p-2">
            {assets.length === 0 ? (
              <div className="text-center py-8">
                <p className="text-gray-500 text-xs">No assets match your search</p>
                <button
                  className="text-fn-rare text-[10px] mt-1 hover:underline"
                  onClick={() => setSearchQuery('')}
                >
                  Clear search
                </button>
              </div>
            ) : (
              <>
                <div className="asset-grid">
                  {assets.slice(0, displayLimit).map((asset) => (
                    <AssetThumbnail
                      key={asset.path}
                      asset={asset}
                      onClick={() => handleAssetDrop(asset.path, selectedObjectId || undefined)}
                      onContextMenu={(e) => showAssetContextMenu(e, asset.path, asset.name)}
                    />
                  ))}
                </div>
                {assets.length > displayLimit && (
                  <button
                    className="w-full mt-2 py-1.5 text-[10px] text-gray-400 hover:text-white bg-fn-darker border border-fn-border rounded hover:border-gray-500 transition-colors"
                    onClick={() => setDisplayLimit((l) => l + 100)}
                  >
                    Load more ({assets.length - displayLimit} remaining)
                  </button>
                )}
              </>
            )}
          </div>
        )}

        {/* When not searching, show folder tree with category filter */}
        {!loading && !isSearching && index && (
          <div className="py-1">
            {/* Breadcrumb / back button when viewing a category */}
            {selectedCategory && (
              <div className="flex items-center justify-between px-3 py-1.5">
                <button
                  className="flex items-center gap-1.5 text-[11px] text-fn-rare hover:underline transition-colors"
                  onClick={() => setSelectedCategory(null)}
                >
                  <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path d="M15 19l-7-7 7-7" />
                  </svg>
                  All Folders
                </button>
                <span className="text-[10px] text-gray-600 font-mono truncate ml-2">{selectedCategory}</span>
              </div>
            )}

            {/* New folder creation inline */}
            {creatingFolder && (
              <div className="px-3 py-1.5">
                <div className="flex items-center gap-1.5">
                  <svg className="w-3.5 h-3.5 text-yellow-500/70 shrink-0" fill="currentColor" viewBox="0 0 24 24">
                    <path d="M10 4H4a2 2 0 00-2 2v12a2 2 0 002 2h16a2 2 0 002-2V8a2 2 0 00-2-2h-8l-2-2z" />
                  </svg>
                  <input
                    ref={newFolderInputRef}
                    type="text"
                    className="flex-1 bg-fn-darker border border-fn-rare/40 rounded px-2 py-0.5 text-[11px] text-white outline-none"
                    placeholder="Folder name..."
                    value={newFolderName}
                    onChange={(e) => setNewFolderName(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') confirmCreateFolder()
                      if (e.key === 'Escape') setCreatingFolder(false)
                    }}
                    onBlur={confirmCreateFolder}
                  />
                </div>
              </div>
            )}

            {/* Category folder tree */}
            {!selectedCategory && (
              <div>
                {index.categories.map((cat) => (
                  <FolderNode
                    key={cat.name}
                    category={cat}
                    depth={0}
                    expanded={expandedFolders}
                    onToggle={toggleFolder}
                    onSelect={(name) => setSelectedCategory(name)}
                    onContextMenu={showFolderContextMenu}
                  />
                ))}
              </div>
            )}

            {/* Assets in selected category */}
            {selectedCategory && (
              <div className="p-2">
                {assets.length === 0 ? (
                  <div className="text-center py-4">
                    <p className="text-gray-500 text-xs">No assets in this folder</p>
                    <button
                      className="text-fn-rare text-[10px] mt-2 hover:underline"
                      onClick={() => handleImportToFolder()}
                    >
                      Import files here
                    </button>
                  </div>
                ) : (
                  <>
                    <div className="asset-grid">
                      {assets.slice(0, displayLimit).map((asset) => (
                        <AssetThumbnail
                          key={asset.path}
                          asset={asset}
                          onClick={() => handleAssetDrop(asset.path, selectedObjectId || undefined)}
                          onContextMenu={(e) => showAssetContextMenu(e, asset.path, asset.name)}
                        />
                      ))}
                    </div>
                    {assets.length > displayLimit && (
                      <button
                        className="w-full mt-2 py-1.5 text-[10px] text-gray-400 hover:text-white bg-fn-darker border border-fn-border rounded hover:border-gray-500 transition-colors"
                        onClick={() => setDisplayLimit((l) => l + 100)}
                      >
                        Load more ({assets.length - displayLimit} remaining)
                      </button>
                    )}
                  </>
                )}
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  )
}

/** Recursive folder node in the category tree */
function FolderNode({
  category,
  depth,
  expanded,
  onToggle,
  onSelect,
  onContextMenu
}: {
  category: AssetCategory
  depth: number
  expanded: Set<string>
  onToggle: (name: string) => void
  onSelect: (name: string) => void
  onContextMenu: (e: React.MouseEvent, cat: AssetCategory) => void
}) {
  const isOpen = expanded.has(category.name)
  const hasChildren = category.subcategories.length > 0
  const assetCount = countCategoryAssets(category)

  return (
    <div>
      <button
        className="w-full flex items-center gap-1.5 px-3 py-1 text-left hover:bg-white/5 transition-colors group"
        style={{ paddingLeft: `${12 + depth * 12}px` }}
        onClick={() => {
          if (hasChildren) {
            onToggle(category.name)
          } else {
            onSelect(category.name)
          }
        }}
        onDoubleClick={() => onSelect(category.name)}
        onContextMenu={(e) => onContextMenu(e, category)}
      >
        {/* Expand/collapse chevron */}
        {hasChildren ? (
          <svg
            className={`w-2.5 h-2.5 text-gray-500 shrink-0 transition-transform ${isOpen ? 'rotate-90' : ''}`}
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            strokeWidth={2.5}
          >
            <path d="M9 5l7 7-7 7" />
          </svg>
        ) : (
          <span className="w-2.5 shrink-0" />
        )}

        {/* Folder icon */}
        <svg className="w-3.5 h-3.5 text-yellow-500/70 shrink-0" fill="currentColor" viewBox="0 0 24 24">
          <path d="M10 4H4a2 2 0 00-2 2v12a2 2 0 002 2h16a2 2 0 002-2V8a2 2 0 00-2-2h-8l-2-2z" />
        </svg>

        {/* Name */}
        <span className="text-[11px] text-gray-300 group-hover:text-white truncate flex-1">
          {category.name}
        </span>

        {/* Asset count */}
        <span className="text-[9px] text-gray-600 font-mono shrink-0">{assetCount}</span>
      </button>

      {/* Children */}
      {isOpen && hasChildren && (
        <div>
          {category.subcategories.map((sub) => (
            <FolderNode
              key={sub.name}
              category={sub}
              depth={depth + 1}
              expanded={expanded}
              onToggle={onToggle}
              onSelect={onSelect}
              onContextMenu={onContextMenu}
            />
          ))}
        </div>
      )}
    </div>
  )
}

function countCategoryAssets(cat: AssetCategory): number {
  let count = cat.assets.length
  for (const sub of cat.subcategories) {
    count += countCategoryAssets(sub)
  }
  return count
}

/** Find a category by name in the tree */
function findCategory(categories: AssetCategory[], name: string): AssetCategory | null {
  for (const cat of categories) {
    if (cat.name === name) return cat
    const found = findCategory(cat.subcategories, name)
    if (found) return found
  }
  return null
}

/** Get the relative folder path for a category (relative to the assets root) */
function getCategoryRelativePath(index: ReturnType<typeof useAssetStore.getState>['index'], category: AssetCategory): string {
  if (!index) return ''

  // The category.path is the absolute path, we need relative
  // We can derive it from the first asset's relativePath, or from the category's path
  // Since the asset scanner stores absolute paths in category.path, and the root
  // category is "Fortnite Assets" which maps to the fortnite_assets/ dir,
  // we need to build the relative path from category names in the tree

  function buildPath(cats: AssetCategory[], target: string, prefix: string): string | null {
    for (const cat of cats) {
      const currentPath = prefix ? `${prefix}/${cat.name}` : cat.name
      if (cat.name === target) return currentPath
      const found = buildPath(cat.subcategories, target, currentPath)
      if (found) return found
    }
    return null
  }

  const path = buildPath(index.categories, category.name, '')
  return path || ''
}
