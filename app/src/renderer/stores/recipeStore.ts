import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { DeviceRecipe } from '../../shared/recipes'
import { BUILTIN_RECIPES } from '../../shared/builtin-recipes'

interface RecipeState {
  /** All saved recipes */
  recipes: DeviceRecipe[]
  /** Currently viewed/editing recipe */
  activeRecipe: DeviceRecipe | null

  // Actions
  addRecipe: (recipe: DeviceRecipe) => void
  removeRecipe: (id: string) => void
  updateRecipe: (id: string, updates: Partial<DeviceRecipe>) => void
  setActiveRecipe: (recipe: DeviceRecipe | null) => void
  importRecipes: (recipes: DeviceRecipe[]) => void
}

export const useRecipeStore = create<RecipeState>()(
  persist(
    (set, get) => ({
      recipes: [],
      activeRecipe: null,

      addRecipe: (recipe) => {
        set((state) => ({
          recipes: [...state.recipes, recipe],
        }))
      },

      removeRecipe: (id) => {
        set((state) => ({
          recipes: state.recipes.filter((r) => r.id !== id),
          activeRecipe: state.activeRecipe?.id === id ? null : state.activeRecipe,
        }))
      },

      updateRecipe: (id, updates) => {
        set((state) => ({
          recipes: state.recipes.map((r) =>
            r.id === id ? { ...r, ...updates } : r
          ),
          activeRecipe:
            state.activeRecipe?.id === id
              ? { ...state.activeRecipe, ...updates }
              : state.activeRecipe,
        }))
      },

      setActiveRecipe: (recipe) => {
        set({ activeRecipe: recipe })
      },

      importRecipes: (recipes) => {
        set((state) => {
          const existingIds = new Set(state.recipes.map((r) => r.id))
          const newRecipes = recipes.filter((r) => !existingIds.has(r.id))
          return { recipes: [...state.recipes, ...newRecipes] }
        })
      },
    }),
    {
      name: 'wellversed-recipes',
      onRehydrate: (_state, options) => {
        // After rehydrating, ensure built-in recipes are present
        return (rehydrated) => {
          if (rehydrated) {
            const existingIds = new Set(rehydrated.recipes.map((r) => r.id))
            const missing = BUILTIN_RECIPES.filter((r) => !existingIds.has(r.id))
            if (missing.length > 0) {
              rehydrated.recipes = [...missing, ...rehydrated.recipes]
            }
          }
        }
      },
    }
  )
)
