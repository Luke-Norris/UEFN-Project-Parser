import { create } from 'zustand'
import type { AssetIndex, AssetCategory, AssetEntry } from '../../shared/types'

interface AssetState {
  index: AssetIndex | null
  loading: boolean
  searchQuery: string
  selectedCategory: string | null
  // Actions
  loadAssets: () => Promise<void>
  setIndex: (index: AssetIndex) => void
  setSearchQuery: (query: string) => void
  setSelectedCategory: (category: string | null) => void
  getFilteredAssets: () => AssetEntry[]
  findAssetsByPattern: (category: string, pattern: string) => AssetEntry[]
}

function flattenAssets(categories: AssetCategory[]): AssetEntry[] {
  const result: AssetEntry[] = []
  for (const cat of categories) {
    result.push(...cat.assets)
    if (cat.subcategories.length > 0) {
      result.push(...flattenAssets(cat.subcategories))
    }
  }
  return result
}

function findCategory(categories: AssetCategory[], name: string): AssetCategory | null {
  for (const cat of categories) {
    if (cat.name === name) return cat
    const found = findCategory(cat.subcategories, name)
    if (found) return found
  }
  return null
}

export const useAssetStore = create<AssetState>((set, get) => ({
  index: null,
  loading: false,
  searchQuery: '',
  selectedCategory: null,

  loadAssets: async () => {
    set({ loading: true })
    try {
      const index = await window.electronAPI.scanAssets()
      set({ index, loading: false })
    } catch (err) {
      console.error('Failed to scan assets:', err)
      set({ loading: false })
    }
  },

  setIndex: (index) => set({ index }),
  setSearchQuery: (query) => set({ searchQuery: query }),
  setSelectedCategory: (category) => set({ selectedCategory: category }),

  getFilteredAssets: () => {
    const { index, searchQuery, selectedCategory } = get()
    if (!index) return []

    let assets: AssetEntry[]

    // When searching, always search ALL assets for better results
    if (searchQuery.trim()) {
      assets = flattenAssets(index.categories)
      const q = searchQuery.toLowerCase()
      assets = assets.filter(
        (a) =>
          a.name.toLowerCase().includes(q) ||
          a.category.toLowerCase().includes(q) ||
          a.filename.toLowerCase().includes(q)
      )
    } else if (selectedCategory) {
      const cat = findCategory(index.categories, selectedCategory)
      // Include subcategory assets too
      assets = cat ? flattenAssets([cat]) : []
    } else {
      assets = flattenAssets(index.categories)
    }

    return assets
  },

  findAssetsByPattern: (categoryName, pattern) => {
    const { index } = get()
    if (!index) return []

    const cat = findCategory(index.categories, categoryName)
    if (!cat) return []

    const p = pattern.toLowerCase()
    return flattenAssets([cat]).filter((a) => a.name.toLowerCase().includes(p))
  }
}))
