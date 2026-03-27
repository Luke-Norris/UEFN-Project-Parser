import { create } from 'zustand'

export type MessageRole = 'user' | 'assistant' | 'system'

export type MessageStatus = 'sending' | 'generating' | 'complete' | 'error'

export interface GenerationAction {
  label: string
  description: string
  page?: string // navigate to this page to see result
  data?: Record<string, unknown>
}

export interface ExecutionStep {
  id: string
  label: string
  status: 'pending' | 'active' | 'done' | 'error'
  detail?: string
  timestamp?: number
}

export interface GenerationPlanDevice {
  role: string
  type: string
  class: string
  properties?: Record<string, unknown>
}

export interface GenerationPlanWiring {
  from: string
  event: string
  to: string
  action: string
}

export interface GenerationPlan {
  name: string
  description: string
  devices: GenerationPlanDevice[]
  wiring: GenerationPlanWiring[]
  verseCode?: string
  verseFileName?: string
  widgetSpec?: Record<string, unknown>
}

export interface ChatMessage {
  id: string
  role: MessageRole
  content: string
  timestamp: number
  status: MessageStatus
  // Rich content for assistant messages
  verseCode?: string
  deviceList?: Array<{ role: string; type: string; class: string }>
  wiringList?: Array<{ from: string; event: string; to: string; action: string }>
  actions?: GenerationAction[]
  // Generation context
  systemCategory?: string
  generationSteps?: Array<{ step: string; status: 'pending' | 'active' | 'done' | 'error' }>
  // Plan data
  plan?: GenerationPlan
}

export interface QuickAction {
  id: string
  label: string
  description: string
  prompt: string
  category: 'generate' | 'analyze' | 'create' | 'modify'
  icon: string // svg path data
}

interface ChatState {
  messages: ChatMessage[]
  isGenerating: boolean
  inputValue: string
  // Context
  contextExpanded: boolean
  // Bridge integration
  bridgeConnected: boolean
  // Generation plan
  generationPlan: GenerationPlan | null
  // Execution progress
  executionProgress: ExecutionStep[]

  // Actions
  setInputValue: (value: string) => void
  addMessage: (msg: Omit<ChatMessage, 'id' | 'timestamp'>) => string
  updateMessage: (id: string, update: Partial<ChatMessage>) => void
  clearMessages: () => void
  setIsGenerating: (value: boolean) => void
  setContextExpanded: (value: boolean) => void
  setBridgeConnected: (value: boolean) => void
  setGenerationPlan: (plan: GenerationPlan | null) => void
  setExecutionProgress: (steps: ExecutionStep[]) => void
  updateExecutionStep: (id: string, update: Partial<ExecutionStep>) => void
}

let _nextId = 0
function genId() {
  return `msg_${Date.now()}_${++_nextId}`
}

export const useChatStore = create<ChatState>((set, _get) => ({
  messages: [],
  isGenerating: false,
  inputValue: '',
  contextExpanded: true,
  bridgeConnected: false,
  generationPlan: null,
  executionProgress: [],

  setInputValue: (value) => set({ inputValue: value }),

  addMessage: (msg) => {
    const id = genId()
    const message: ChatMessage = { ...msg, id, timestamp: Date.now() }
    set((s) => ({ messages: [...s.messages, message] }))
    return id
  },

  updateMessage: (id, update) => {
    set((s) => ({
      messages: s.messages.map((m) => (m.id === id ? { ...m, ...update } : m)),
    }))
  },

  clearMessages: () => set({ messages: [], generationPlan: null, executionProgress: [] }),
  setIsGenerating: (value) => set({ isGenerating: value }),
  setContextExpanded: (value) => set({ contextExpanded: value }),
  setBridgeConnected: (value) => set({ bridgeConnected: value }),
  setGenerationPlan: (plan) => set({ generationPlan: plan }),
  setExecutionProgress: (steps) => set({ executionProgress: steps }),
  updateExecutionStep: (id, update) => {
    set((s) => ({
      executionProgress: s.executionProgress.map((step) =>
        step.id === id ? { ...step, ...update } : step
      ),
    }))
  },
}))

// ─── Quick actions for the chat ─────────────────────────────────────────────
// These call REAL sidecar methods — not local pattern matching.

export const QUICK_ACTIONS: QuickAction[] = [
  {
    id: 'capture-point',
    label: 'Capture Point System',
    description: 'Trigger zone + timer + score tracking + HUD feedback',
    prompt: 'Generate a capture point system with a trigger zone that starts a timer when a player enters, awards points on completion, and shows progress on the HUD.',
    category: 'generate',
    icon: 'M17.657 16.657L13.414 20.9a1.998 1.998 0 01-2.827 0l-4.244-4.243a8 8 0 1111.314 0z M15 11a3 3 0 11-6 0 3 3 0 016 0z',
  },
  {
    id: 'elimination-game',
    label: 'Elimination Game Mode',
    description: 'Spawn system + kill tracking + round management + scoreboard',
    prompt: 'Generate a full elimination game mode with team spawns (2 teams), elimination tracking with kill streaks, round-based management (best of 5), and a scoreboard widget.',
    category: 'generate',
    icon: 'M13 10V3L4 14h7v7l9-11h-7z',
  },
  {
    id: 'item-shop',
    label: 'Item Shop System',
    description: 'Vending machines + currency + item granting + purchase UI',
    prompt: 'Generate an item shop system with 3 vending machines selling different weapons, gold currency tracking, purchase confirmation UI, and visual effects on purchase.',
    category: 'generate',
    icon: 'M3 3h2l.4 2M7 13h10l4-8H5.4M7 13L5.4 5M7 13l-2.293 2.293c-.63.63-.184 1.707.707 1.707H17m0 0a2 2 0 100 4 2 2 0 000-4zm-8 2a2 2 0 100 4 2 2 0 000-4z',
  },
  {
    id: 'tycoon-system',
    label: 'Tycoon Economy',
    description: 'Currency generation + upgrades + persistent progression',
    prompt: 'Generate a tycoon economy system with passive currency generation, tiered upgrade stations (3 tiers), purchase gates that unlock new areas, and persistent data saving for player progress.',
    category: 'generate',
    icon: 'M12 8c-1.657 0-3 .895-3 2s1.343 2 3 2 3 .895 3 2-1.343 2-3 2m0-8c1.11 0 2.08.402 2.599 1M12 8V7m0 1v8m0 0v1m0-1c-1.11 0-2.08-.402-2.599-1M21 12a9 9 0 11-18 0 9 9 0 0118 0z',
  },
  {
    id: 'analyze-systems',
    label: 'Analyze Current Project',
    description: 'Extract device systems, wiring patterns, and recipes',
    prompt: 'Analyze the current project and extract all multi-device systems. Show me what device patterns are being used, how they are wired together, and suggest improvements.',
    category: 'analyze',
    icon: 'M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z',
  },
  {
    id: 'hud-widget',
    label: 'Custom HUD Widget',
    description: 'Health + shield + ammo + score display with animations',
    prompt: 'Create a HUD widget blueprint with health bar, shield bar, ammo counter, and score display. Use the WellVersed widget editor format with proper UEFN containment rules.',
    category: 'create',
    icon: 'M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z',
  },
  {
    id: 'verse-device',
    label: 'Custom Verse Device',
    description: 'Describe behavior, get compilable Verse code',
    prompt: 'Create a custom Verse device that ',
    category: 'create',
    icon: 'M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4',
  },
]
