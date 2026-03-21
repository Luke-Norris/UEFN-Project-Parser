import { create } from 'zustand'
import type { ComponentTemplate, RarityTier, TemplateVariable } from '../../shared/types'
import { templateRegistry } from '../templates/registry'

interface TemplateState {
  activeTemplate: ComponentTemplate | null
  activeRarity: RarityTier
  templateWidth: number
  templateHeight: number
  customTemplates: ComponentTemplate[]
  variableValues: Record<string, string> // variableId -> value
  // Actions
  setActiveTemplate: (template: ComponentTemplate | null) => void
  setActiveRarity: (rarity: RarityTier) => void
  setTemplateDimensions: (width: number, height: number) => void
  setVariableValue: (variableId: string, value: string) => void
  addVariable: (variable: TemplateVariable) => void
  removeVariable: (variableId: string) => void
  saveCurrentAsTemplate: (name: string, description: string) => void
  deleteCustomTemplate: (id: string) => void
  getAllTemplates: () => ComponentTemplate[]
}

export const useTemplateStore = create<TemplateState>((set, get) => ({
  activeTemplate: null,
  activeRarity: 'legendary',
  templateWidth: 512,
  templateHeight: 512,
  customTemplates: [],
  variableValues: {},

  setActiveTemplate: (template) => {
    const variableValues: Record<string, string> = {}
    if (template?.variables) {
      for (const v of template.variables) {
        variableValues[v.id] = v.defaultValue
      }
    }
    set({
      activeTemplate: template,
      templateWidth: template?.width ?? 512,
      templateHeight: template?.height ?? 512,
      variableValues
    })
  },

  setActiveRarity: (rarity) => set({ activeRarity: rarity }),

  setTemplateDimensions: (width, height) => set({ templateWidth: width, templateHeight: height }),

  setVariableValue: (variableId, value) => {
    set((state) => ({
      variableValues: { ...state.variableValues, [variableId]: value }
    }))
  },

  addVariable: (variable) => {
    set((state) => {
      const template = state.activeTemplate
      if (!template) return state
      const vars = [...(template.variables || []), variable]
      return {
        activeTemplate: { ...template, variables: vars },
        variableValues: { ...state.variableValues, [variable.id]: variable.defaultValue }
      }
    })
  },

  removeVariable: (variableId) => {
    set((state) => {
      const template = state.activeTemplate
      if (!template) return state
      const vars = (template.variables || []).filter((v) => v.id !== variableId)
      const newValues = { ...state.variableValues }
      delete newValues[variableId]
      return {
        activeTemplate: { ...template, variables: vars },
        variableValues: newValues
      }
    })
  },

  saveCurrentAsTemplate: (name, description) => {
    const { activeTemplate, templateWidth, templateHeight } = get()
    const newTemplate: ComponentTemplate = {
      id: `custom-${Date.now()}`,
      name,
      description,
      category: 'custom',
      width: templateWidth,
      height: templateHeight,
      layers: activeTemplate?.layers || [],
      variables: activeTemplate?.variables || [],
      isCustom: true
    }
    set((state) => ({
      customTemplates: [...state.customTemplates, newTemplate]
    }))
  },

  deleteCustomTemplate: (id) => {
    set((state) => ({
      customTemplates: state.customTemplates.filter((t) => t.id !== id),
      activeTemplate: state.activeTemplate?.id === id ? null : state.activeTemplate
    }))
  },

  getAllTemplates: () => {
    const { customTemplates } = get()
    return [...templateRegistry, ...customTemplates]
  }
}))
