import { useCallback, useEffect, useRef, useState } from 'react'
import { useForgeStore } from '../stores/forgeStore'
import { useBridgeStore } from '../stores/bridgeStore'
import {
  useChatStore,
  QUICK_ACTIONS,
  type ChatMessage,
  type QuickAction,
  type GenerationAction,
  type GenerationPlan,
} from '../stores/chatStore'
import type { PageId } from '../components/Sidebar/Sidebar'
import {
  forgeDesignGame,
  forgeBridgeCommand,
  forgeAnalyzeLevelSystems,
} from '../lib/api'

interface ChatPageProps {
  selectedLevel: string | null
  onNavigate: (page: PageId) => void
}

export function ChatPage({ selectedLevel, onNavigate }: ChatPageProps) {
  const status = useForgeStore((s) => s.status)
  const bridgeConnected = useBridgeStore((s) => s.connected)
  const bridgeDeviceCount = useBridgeStore((s) => s.deviceCatalogCount)
  const bridgeActorCount = useBridgeStore((s) => s.levelActorCount)

  const messages = useChatStore((s) => s.messages)
  const isGenerating = useChatStore((s) => s.isGenerating)
  const inputValue = useChatStore((s) => s.inputValue)
  const setInputValue = useChatStore((s) => s.setInputValue)
  const addMessage = useChatStore((s) => s.addMessage)
  const updateMessage = useChatStore((s) => s.updateMessage)
  const setIsGenerating = useChatStore((s) => s.setIsGenerating)
  const contextExpanded = useChatStore((s) => s.contextExpanded)
  const setContextExpanded = useChatStore((s) => s.setContextExpanded)
  const generationPlan = useChatStore((s) => s.generationPlan)
  const setGenerationPlan = useChatStore((s) => s.setGenerationPlan)
  const executionProgress = useChatStore((s) => s.executionProgress)
  const setExecutionProgress = useChatStore((s) => s.setExecutionProgress)
  const updateExecutionStep = useChatStore((s) => s.updateExecutionStep)

  const messagesEndRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLTextAreaElement>(null)
  const [showQuickActions, setShowQuickActions] = useState(true)

  // Auto-scroll to bottom on new messages
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  // Focus input on mount
  useEffect(() => {
    inputRef.current?.focus()
  }, [])

  // Hide quick actions once there are messages
  useEffect(() => {
    if (messages.length > 0) setShowQuickActions(false)
  }, [messages.length])

  // ─── Send handler: calls real sidecar ─────────────────────────────────────

  const handleSend = useCallback(async () => {
    const text = inputValue.trim()
    if (!text || isGenerating) return

    setInputValue('')
    setShowQuickActions(false)

    // Add user message
    addMessage({ role: 'user', content: text, status: 'complete' })

    // Add assistant thinking message
    const assistantId = addMessage({
      role: 'assistant',
      content: '',
      status: 'generating',
      generationSteps: [
        { step: 'Understanding request', status: 'active' },
        { step: 'Analyzing project context', status: 'pending' },
        { step: 'Designing system', status: 'pending' },
        { step: 'Generating plan', status: 'pending' },
      ],
    })

    setIsGenerating(true)

    try {
      // Step 1: Understanding
      await delay(200)
      updateMessage(assistantId, {
        generationSteps: [
          { step: 'Understanding request', status: 'done' },
          { step: 'Analyzing project context', status: 'active' },
          { step: 'Designing system', status: 'pending' },
          { step: 'Generating plan', status: 'pending' },
        ],
      })

      // Step 2: Analyze existing project systems if available
      let systemsContext = ''
      if (selectedLevel && status?.isConfigured) {
        try {
          const analysis = await forgeAnalyzeLevelSystems(selectedLevel)
          if (analysis && analysis.systemsFound > 0) {
            systemsContext = `Existing: ${analysis.systemsFound} systems, ${analysis.totalDevices} devices`
          }
        } catch {
          // Non-critical
        }
      }

      updateMessage(assistantId, {
        generationSteps: [
          { step: 'Understanding request', status: 'done' },
          { step: 'Analyzing project context', status: 'done' },
          { step: 'Designing system', status: 'active' },
          { step: 'Generating plan', status: 'pending' },
        ],
      })

      // Step 3: Call the real sidecar game designer
      let design: Awaited<ReturnType<typeof forgeDesignGame>> | null = null
      try {
        design = await forgeDesignGame(text)
      } catch (err) {
        // Sidecar command may not exist yet — fall back to structured prompt
        const fallbackContent = buildFallbackContent(text, systemsContext)
        updateMessage(assistantId, {
          content: fallbackContent,
          status: 'complete',
          systemCategory: 'custom',
          actions: buildFallbackActions(),
          generationSteps: [
            { step: 'Understanding request', status: 'done' },
            { step: 'Analyzing project context', status: 'done' },
            { step: 'Designing system', status: 'done' },
            { step: 'Generating plan', status: 'done' },
          ],
        })
        setIsGenerating(false)
        return
      }

      updateMessage(assistantId, {
        generationSteps: [
          { step: 'Understanding request', status: 'done' },
          { step: 'Analyzing project context', status: 'done' },
          { step: 'Designing system', status: 'done' },
          { step: 'Generating plan', status: 'active' },
        ],
      })

      // Step 4: Build the plan from the design result
      const dName = (design.name as string) ?? 'Generated System'
      const dDesc = (design.description as string) ?? text
      const dDevices = (design.devices as Array<Record<string, string>>) ?? []
      const dWiring = (design.wiring as Array<Record<string, string>>) ?? []
      const plan: GenerationPlan = {
        name: dName,
        description: dDesc,
        devices: dDevices.map((d) => ({
          role: d.role ?? 'device',
          type: d.type ?? d.deviceType ?? 'Unknown',
          class: d.class ?? d.deviceClass ?? 'Unknown',
        })),
        wiring: dWiring.map((w) => ({
          from: w.from ?? '',
          event: w.event ?? '',
          to: w.to ?? '',
          action: w.action ?? '',
        })),
        verseCode: (design.verseCode as string | undefined),
        verseFileName: (design.verseFileName as string | undefined),
      }

      setGenerationPlan(plan)

      const actions: GenerationAction[] = []
      if (bridgeConnected) {
        actions.push({
          label: 'Execute in UEFN',
          description: 'Place devices, wire connections, deploy Verse code',
          data: { action: 'execute', plan },
        })
      }
      actions.push({
        label: 'Save as Plan',
        description: 'Export plan JSON for later execution',
        data: { action: 'save', plan },
      })
      actions.push({ label: 'View Wiring Graph', description: 'See connections visually', page: 'device-wiring' })
      actions.push({ label: 'Open Scene Preview', description: 'See device layout', page: 'scene-preview' })

      updateMessage(assistantId, {
        content: plan.description,
        status: 'complete',
        deviceList: plan.devices.map((d) => ({ role: d.role, type: d.type, class: d.class })),
        wiringList: plan.wiring,
        verseCode: plan.verseCode,
        actions,
        systemCategory: (design.category as string) ?? 'custom',
        plan,
        generationSteps: [
          { step: 'Understanding request', status: 'done' },
          { step: 'Analyzing project context', status: 'done' },
          { step: 'Designing system', status: 'done' },
          { step: 'Generating plan', status: 'done' },
        ],
      })
    } catch (err) {
      updateMessage(assistantId, {
        content: `Error: ${err instanceof Error ? err.message : 'Unknown error'}`,
        status: 'error',
        generationSteps: undefined,
      })
    } finally {
      setIsGenerating(false)
    }
  }, [
    inputValue, isGenerating, status, selectedLevel, bridgeConnected,
    addMessage, updateMessage, setInputValue, setIsGenerating, setGenerationPlan,
  ])

  // ─── Execute plan via bridge ──────────────────────────────────────────────

  const handleExecutePlan = useCallback(async (plan: GenerationPlan) => {
    if (!bridgeConnected) return

    const steps = [
      { id: 'place', label: `Place ${plan.devices.length} devices`, status: 'pending' as const },
      ...(plan.wiring.length > 0 ? [{ id: 'wire', label: `Wire ${plan.wiring.length} connections`, status: 'pending' as const }] : []),
      ...(plan.verseCode ? [{ id: 'verse', label: 'Deploy Verse code', status: 'pending' as const }] : []),
      { id: 'validate', label: 'Validate placement', status: 'pending' as const },
    ]
    setExecutionProgress(steps)

    const execMsgId = addMessage({
      role: 'assistant',
      content: 'Executing plan in UEFN...',
      status: 'generating',
    })

    try {
      // Place devices
      updateExecutionStep('place', { status: 'active' })
      await forgeBridgeCommand('place_devices', { devices: plan.devices })
      updateExecutionStep('place', { status: 'done', detail: `${plan.devices.length} devices placed` })

      // Wire connections
      if (plan.wiring.length > 0) {
        updateExecutionStep('wire', { status: 'active' })
        await forgeBridgeCommand('wire_connections', { wiring: plan.wiring })
        updateExecutionStep('wire', { status: 'done', detail: `${plan.wiring.length} connections wired` })
      }

      // Deploy Verse
      if (plan.verseCode) {
        updateExecutionStep('verse', { status: 'active' })
        await forgeBridgeCommand('deploy_verse', {
          code: plan.verseCode,
          fileName: plan.verseFileName ?? 'generated_device.verse',
        })
        updateExecutionStep('verse', { status: 'done' })
      }

      // Validate
      updateExecutionStep('validate', { status: 'active' })
      await forgeBridgeCommand('validate_placement', {})
      updateExecutionStep('validate', { status: 'done' })

      updateMessage(execMsgId, {
        content: `Plan executed successfully. ${plan.devices.length} devices placed, ${plan.wiring.length} connections wired.`,
        status: 'complete',
        actions: [
          { label: 'View in Scene Preview', description: 'See the result', page: 'scene-preview' },
          { label: 'Open Wiring Graph', description: 'See connections', page: 'device-wiring' },
        ],
      })
    } catch (err) {
      const errMsg = err instanceof Error ? err.message : 'Execution failed'
      updateMessage(execMsgId, {
        content: `Execution failed: ${errMsg}`,
        status: 'error',
      })
      // Mark remaining steps as error
      for (const step of useChatStore.getState().executionProgress) {
        if (step.status === 'pending' || step.status === 'active') {
          updateExecutionStep(step.id, { status: 'error' })
        }
      }
    }
  }, [bridgeConnected, addMessage, updateMessage, setExecutionProgress, updateExecutionStep])

  // ─── Save plan as JSON ────────────────────────────────────────────────────

  const handleSavePlan = useCallback(async (plan: GenerationPlan) => {
    try {
      const { save } = await import('@tauri-apps/plugin-dialog')
      const filePath = await save({
        defaultPath: `${plan.name.replace(/\s+/g, '_').toLowerCase()}_plan.json`,
        filters: [{ name: 'JSON', extensions: ['json'] }],
      })
      if (!filePath) return

      const { invoke } = await import('@tauri-apps/api/core')
      const json = JSON.stringify(plan, null, 2)
      const encoder = new TextEncoder()
      const bytes = encoder.encode(json)
      const binary = Array.from(bytes).map((b) => String.fromCharCode(b)).join('')
      const b64 = btoa(binary)
      await invoke('export_png', {
        dataUrl: `data:application/json;base64,${b64}`,
        filePath,
      })

      addMessage({
        role: 'system',
        content: `Plan saved to ${filePath}`,
        status: 'complete',
      })
    } catch (err) {
      addMessage({
        role: 'system',
        content: `Failed to save plan: ${err instanceof Error ? err.message : 'Unknown error'}`,
        status: 'error',
      })
    }
  }, [addMessage])

  // ─── Handlers ─────────────────────────────────────────────────────────────

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault()
        handleSend()
      }
    },
    [handleSend]
  )

  const handleQuickAction = useCallback(
    (action: QuickAction) => {
      if (action.prompt.endsWith(' ')) {
        // Partial prompt -- put in input for user to complete
        setInputValue(action.prompt)
        inputRef.current?.focus()
        return
      }
      setInputValue(action.prompt)
      setTimeout(() => {
        useChatStore.getState().setInputValue(action.prompt)
        handleSend()
      }, 100)
    },
    [setInputValue, handleSend]
  )

  const handleActionClick = useCallback(
    (action: GenerationAction) => {
      if (action.page) {
        onNavigate(action.page as PageId)
        return
      }
      if (action.data?.action === 'execute' && action.data.plan) {
        handleExecutePlan(action.data.plan as GenerationPlan)
        return
      }
      if (action.data?.action === 'save' && action.data.plan) {
        handleSavePlan(action.data.plan as GenerationPlan)
        return
      }
    },
    [onNavigate, handleExecutePlan, handleSavePlan]
  )

  const handleCopyPrompt = useCallback((msg: ChatMessage) => {
    const ctx = buildContext(useForgeStore.getState().status, null)
    const userMsg = messages.find((m) => m.role === 'user' && m.timestamp < msg.timestamp)?.content ?? ''
    const structured = buildStructuredPrompt(userMsg, ctx, '')
    navigator.clipboard.writeText(structured)
  }, [messages])

  return (
    <div className="flex-1 flex flex-col bg-fn-darker min-h-0">
      {/* Context Bar */}
      <div className="border-b border-fn-border bg-fn-dark/50">
        <button
          onClick={() => setContextExpanded(!contextExpanded)}
          className="w-full px-4 py-2 flex items-center justify-between hover:bg-white/[0.02] transition-colors"
        >
          <div className="flex items-center gap-3">
            <svg className="w-4 h-4 text-fn-rare" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path d="M8.625 12a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0H8.25m4.125 0a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0H12m4.125 0a.375.375 0 11-.75 0 .375.375 0 01.75 0zm0 0h-.375M21 12c0 4.556-4.03 8.25-9 8.25a9.764 9.764 0 01-2.555-.337A5.972 5.972 0 015.41 20.97a5.969 5.969 0 01-.474-.065 4.48 4.48 0 00.978-2.025c.09-.457-.133-.901-.467-1.226C3.93 16.178 3 14.189 3 12c0-4.556 4.03-8.25 9-8.25s9 3.694 9 8.25z" />
            </svg>
            <span className="text-[11px] font-semibold text-white">WellVersed Studio</span>
            {status?.isConfigured && (
              <span className="text-[10px] text-gray-500">
                {status.projectName} {selectedLevel ? `/ ${selectedLevel.split('/').pop()?.replace('.umap', '')}` : ''}
              </span>
            )}
          </div>
          <div className="flex items-center gap-2">
            {/* Bridge connection badge */}
            <BridgeStatusBadge connected={bridgeConnected} deviceCount={bridgeDeviceCount} actorCount={bridgeActorCount} />
            <svg
              className={`w-3 h-3 text-gray-500 transition-transform ${contextExpanded ? 'rotate-180' : ''}`}
              fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}
            >
              <path d="M19 9l-7 7-7-7" />
            </svg>
          </div>
        </button>

        {contextExpanded && (
          <div className="px-4 pb-3 space-y-2">
            <div className="grid grid-cols-4 gap-2">
              <ContextChip
                label="Project"
                value={status?.isConfigured ? status.projectName : 'None'}
                active={!!status?.isConfigured}
              />
              <ContextChip
                label="Mode"
                value={status?.mode ?? 'None'}
                active={status?.mode === 'Staged' || status?.mode === 'Direct'}
                warn={status?.mode === 'ReadOnly'}
              />
              <ContextChip
                label="UEFN"
                value={status?.isUefnRunning ? 'Running' : 'Stopped'}
                active={!status?.isUefnRunning}
                warn={status?.isUefnRunning}
              />
              <ContextChip
                label="Level"
                value={selectedLevel ? selectedLevel.split('/').pop()?.replace('.umap', '') ?? 'Selected' : 'None'}
                active={!!selectedLevel}
              />
            </div>
            {status?.isConfigured && (
              <div className="flex items-center gap-4 text-[9px] text-gray-600 pt-1">
                <span>{status.assetCount ?? 0} assets</span>
                <span>{status.verseCount ?? 0} verse files</span>
                <span>{status.levelCount ?? 0} levels</span>
                {bridgeConnected && (
                  <>
                    <span className="text-emerald-500">{bridgeDeviceCount} catalog devices</span>
                    <span className="text-emerald-500">{bridgeActorCount} level actors</span>
                  </>
                )}
              </div>
            )}
          </div>
        )}
      </div>

      {/* Execution Progress Bar */}
      {executionProgress.length > 0 && (
        <ExecutionProgressBar steps={executionProgress} />
      )}

      {/* Messages Area */}
      <div className="flex-1 overflow-y-auto min-h-0">
        {messages.length === 0 && showQuickActions ? (
          <EmptyState onQuickAction={handleQuickAction} bridgeConnected={bridgeConnected} />
        ) : (
          <div className="max-w-3xl mx-auto px-4 py-4 space-y-4">
            {messages.map((msg) => (
              <MessageBubble
                key={msg.id}
                message={msg}
                onActionClick={handleActionClick}
                onCopyPrompt={() => handleCopyPrompt(msg)}
                bridgeConnected={bridgeConnected}
              />
            ))}
            <div ref={messagesEndRef} />
          </div>
        )}
      </div>

      {/* Input Area */}
      <div className="border-t border-fn-border bg-fn-dark/50 p-4">
        <div className="max-w-3xl mx-auto">
          {/* Quick action chips when input is empty */}
          {!isGenerating && inputValue === '' && messages.length > 0 && (
            <div className="flex flex-wrap gap-1.5 mb-3">
              {QUICK_ACTIONS.slice(0, 4).map((action) => (
                <button
                  key={action.id}
                  onClick={() => handleQuickAction(action)}
                  className="text-[9px] px-2 py-1 rounded-full border border-fn-border text-gray-500 hover:text-fn-rare hover:border-fn-rare/30 transition-colors"
                >
                  {action.label}
                </button>
              ))}
            </div>
          )}

          <div className="relative">
            <textarea
              ref={inputRef}
              value={inputValue}
              onChange={(e) => setInputValue(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder={isGenerating ? 'Generating...' : 'Describe what you want to build...'}
              disabled={isGenerating}
              rows={1}
              className="w-full bg-fn-panel border border-fn-border rounded-xl px-4 py-3 pr-12 text-[12px] text-white placeholder-gray-600 resize-none focus:outline-none focus:border-fn-rare/40 focus:ring-1 focus:ring-fn-rare/20 transition-all disabled:opacity-50"
              style={{
                minHeight: '44px',
                maxHeight: '160px',
                height: 'auto',
              }}
              onInput={(e) => {
                const target = e.target as HTMLTextAreaElement
                target.style.height = 'auto'
                target.style.height = Math.min(target.scrollHeight, 160) + 'px'
              }}
            />
            <button
              onClick={handleSend}
              disabled={isGenerating || !inputValue.trim()}
              className="absolute right-2 bottom-2 p-1.5 rounded-lg bg-fn-rare/10 text-fn-rare hover:bg-fn-rare/20 disabled:opacity-30 disabled:cursor-not-allowed transition-all"
            >
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path d="M12 19V5m-7 7l7-7 7 7" />
              </svg>
            </button>
          </div>

          <div className="flex items-center justify-between mt-2">
            <p className="text-[9px] text-gray-700">
              {bridgeConnected
                ? 'Bridge connected -- generated plans can execute directly in UEFN.'
                : 'Bridge disconnected -- plans are generated offline for later execution.'}
            </p>
            {messages.length > 0 && (
              <button
                onClick={() => {
                  useChatStore.getState().clearMessages()
                  setShowQuickActions(true)
                }}
                className="text-[9px] text-gray-600 hover:text-gray-400 transition-colors"
              >
                Clear chat
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}

// ─── Bridge Status Badge ────────────────────────────────────────────────────

function BridgeStatusBadge({
  connected,
  deviceCount,
  actorCount,
}: {
  connected: boolean
  deviceCount: number
  actorCount: number
}) {
  return (
    <div
      className={`flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[9px] font-medium border ${
        connected
          ? 'text-emerald-400 bg-emerald-400/10 border-emerald-400/20'
          : 'text-gray-500 bg-gray-500/5 border-fn-border'
      }`}
      title={connected ? `Bridge: ${deviceCount} devices, ${actorCount} actors` : 'Bridge disconnected'}
    >
      <span className={`w-1.5 h-1.5 rounded-full ${connected ? 'bg-emerald-400' : 'bg-gray-600'}`} />
      {connected ? 'Bridge' : 'Offline'}
    </div>
  )
}

// ─── Execution Progress Bar ────────────────────────────────────────────────

function ExecutionProgressBar({ steps }: { steps: Array<{ id: string; label: string; status: string; detail?: string }> }) {
  const doneCount = steps.filter((s) => s.status === 'done').length
  const total = steps.length
  const progress = total > 0 ? (doneCount / total) * 100 : 0

  return (
    <div className="border-b border-fn-border bg-fn-dark/30 px-4 py-2">
      <div className="max-w-3xl mx-auto">
        <div className="flex items-center justify-between mb-1">
          <span className="text-[10px] font-medium text-gray-400">Executing plan</span>
          <span className="text-[9px] text-gray-600">{doneCount}/{total} steps</span>
        </div>
        <div className="h-1 bg-fn-panel rounded-full overflow-hidden mb-2">
          <div
            className="h-full bg-fn-rare rounded-full transition-all duration-500"
            style={{ width: `${progress}%` }}
          />
        </div>
        <div className="flex flex-wrap gap-3">
          {steps.map((step) => (
            <div key={step.id} className="flex items-center gap-1.5">
              {step.status === 'active' ? (
                <div className="w-2.5 h-2.5 rounded-full border-2 border-fn-rare border-t-transparent animate-spin" />
              ) : step.status === 'done' ? (
                <svg className="w-2.5 h-2.5 text-emerald-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={3}>
                  <path d="M5 13l4 4L19 7" />
                </svg>
              ) : step.status === 'error' ? (
                <svg className="w-2.5 h-2.5 text-red-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={3}>
                  <path d="M6 18L18 6M6 6l12 12" />
                </svg>
              ) : (
                <div className="w-2.5 h-2.5 rounded-full border border-gray-600" />
              )}
              <span className={`text-[9px] ${
                step.status === 'active' ? 'text-fn-rare' :
                step.status === 'done' ? 'text-gray-400' :
                step.status === 'error' ? 'text-red-400' :
                'text-gray-600'
              }`}>
                {step.label}
              </span>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}

// ─── Empty State ────────────────────────────────────────────────────────────

function EmptyState({ onQuickAction, bridgeConnected }: { onQuickAction: (a: QuickAction) => void; bridgeConnected: boolean }) {
  return (
    <div className="flex flex-col items-center justify-center h-full px-4">
      <div className="max-w-2xl w-full space-y-8">
        {/* Hero */}
        <div className="text-center space-y-3">
          <div className="inline-flex items-center justify-center w-14 h-14 rounded-2xl bg-fn-rare/10 border border-fn-rare/20">
            <svg className="w-7 h-7 text-fn-rare" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09zM18.259 8.715L18 9.75l-.259-1.035a3.375 3.375 0 00-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 002.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 002.455 2.456L21.75 6l-1.036.259a3.375 3.375 0 00-2.455 2.456zM16.894 20.567L16.5 21.75l-.394-1.183a2.25 2.25 0 00-1.423-1.423L13.5 18.75l1.183-.394a2.25 2.25 0 001.423-1.423l.394-1.183.394 1.183a2.25 2.25 0 001.423 1.423l1.183.394-1.183.394a2.25 2.25 0 00-1.423 1.423z" />
            </svg>
          </div>
          <h2 className="text-lg font-semibold text-white">What do you want to build?</h2>
          <p className="text-[11px] text-gray-500 max-w-md mx-auto leading-relaxed">
            Describe a game system, mechanic, or UI and WellVersed will generate the devices,
            Verse code, widgets, and wiring to make it real in UEFN.
          </p>
          {!bridgeConnected && (
            <p className="text-[10px] text-amber-400/70">
              Bridge not connected -- plans will be generated offline.
            </p>
          )}
        </div>

        {/* Quick Actions Grid */}
        <div>
          <h3 className="text-[10px] font-semibold text-gray-600 uppercase tracking-wider mb-3 text-center">
            Start with a template
          </h3>
          <div className="grid grid-cols-2 gap-2">
            {QUICK_ACTIONS.map((action) => (
              <button
                key={action.id}
                onClick={() => onQuickAction(action)}
                className="group text-left bg-fn-panel border border-fn-border rounded-lg p-3 hover:bg-white/[0.03] hover:border-fn-rare/20 transition-all"
              >
                <div className="flex items-center gap-2 mb-1">
                  <svg
                    className="w-3.5 h-3.5 text-gray-600 group-hover:text-fn-rare transition-colors"
                    fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}
                  >
                    <path d={action.icon} />
                  </svg>
                  <span className="text-[11px] font-medium text-gray-300 group-hover:text-white transition-colors">
                    {action.label}
                  </span>
                  <span className={`ml-auto text-[8px] px-1.5 py-0.5 rounded-full border ${
                    action.category === 'generate'
                      ? 'text-fn-rare border-fn-rare/20 bg-fn-rare/5'
                      : action.category === 'analyze'
                      ? 'text-blue-400 border-blue-400/20 bg-blue-400/5'
                      : 'text-amber-400 border-amber-400/20 bg-amber-400/5'
                  }`}>
                    {action.category}
                  </span>
                </div>
                <p className="text-[9px] text-gray-600 leading-relaxed">{action.description}</p>
              </button>
            ))}
          </div>
        </div>
      </div>
    </div>
  )
}

// ─── Message Bubble ─────────────────────────────────────────────────────────

function MessageBubble({
  message,
  onActionClick,
  onCopyPrompt,
  bridgeConnected,
}: {
  message: ChatMessage
  onActionClick: (action: GenerationAction) => void
  onCopyPrompt: () => void
  bridgeConnected: boolean
}) {
  const isUser = message.role === 'user'
  const isSystem = message.role === 'system'
  const isGenerating = message.status === 'generating'

  if (isSystem) {
    return (
      <div className="flex justify-center">
        <div className="text-[10px] text-gray-500 bg-fn-panel border border-fn-border rounded-lg px-3 py-1.5">
          {message.content}
        </div>
      </div>
    )
  }

  return (
    <div className={`flex gap-3 ${isUser ? 'justify-end' : ''}`}>
      {!isUser && (
        <div className="shrink-0 w-7 h-7 rounded-lg bg-fn-rare/10 border border-fn-rare/20 flex items-center justify-center mt-0.5">
          <svg className="w-3.5 h-3.5 text-fn-rare" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09z" />
          </svg>
        </div>
      )}

      <div className={`max-w-[85%] ${isUser ? 'order-first' : ''}`}>
        {isUser ? (
          <div className="bg-fn-rare/10 border border-fn-rare/20 rounded-xl rounded-tr-sm px-4 py-2.5">
            <p className="text-[12px] text-white whitespace-pre-wrap">{message.content}</p>
          </div>
        ) : (
          <div className="space-y-2">
            {/* Generation Steps */}
            {isGenerating && message.generationSteps && (
              <div className="bg-fn-panel border border-fn-border rounded-xl px-4 py-3 space-y-1.5">
                {message.generationSteps.map((step, i) => (
                  <div key={i} className="flex items-center gap-2">
                    {step.status === 'active' ? (
                      <div className="w-3 h-3 rounded-full border-2 border-fn-rare border-t-transparent animate-spin" />
                    ) : step.status === 'done' ? (
                      <svg className="w-3 h-3 text-fn-rare" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={3}>
                        <path d="M5 13l4 4L19 7" />
                      </svg>
                    ) : (
                      <div className="w-3 h-3 rounded-full border border-gray-700" />
                    )}
                    <span className={`text-[10px] ${
                      step.status === 'active' ? 'text-fn-rare' :
                      step.status === 'done' ? 'text-gray-400' : 'text-gray-600'
                    }`}>
                      {step.step}
                    </span>
                  </div>
                ))}
              </div>
            )}

            {/* Content */}
            {message.content && (
              <div className="bg-fn-panel border border-fn-border rounded-xl rounded-tl-sm px-4 py-3">
                <p className="text-[12px] text-gray-300 whitespace-pre-wrap leading-relaxed">
                  {message.content}
                </p>
              </div>
            )}

            {/* Device List */}
            {message.deviceList && message.deviceList.length > 0 && (
              <div className="bg-fn-panel border border-fn-border rounded-xl px-4 py-3">
                <h4 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
                  Devices ({message.deviceList.length})
                </h4>
                <div className="space-y-1">
                  {message.deviceList.map((d, i) => (
                    <div key={i} className="flex items-center gap-2 text-[11px]">
                      <span className="w-1.5 h-1.5 rounded-full bg-fn-rare" />
                      <span className="text-gray-400 font-medium w-24">{d.role}</span>
                      <span className="text-gray-500">{d.type}</span>
                      <span className="ml-auto text-gray-700 text-[9px] font-mono">{d.class}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* Wiring */}
            {message.wiringList && message.wiringList.length > 0 && (
              <div className="bg-fn-panel border border-fn-border rounded-xl px-4 py-3">
                <h4 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
                  Wiring ({message.wiringList.length} connections)
                </h4>
                <div className="space-y-1">
                  {message.wiringList.map((w, i) => (
                    <div key={i} className="flex items-center gap-1.5 text-[10px]">
                      <span className="text-gray-400">{w.from}</span>
                      <span className="text-gray-600">.{w.event}</span>
                      <svg className="w-3 h-3 text-fn-rare shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                        <path d="M13 7l5 5m0 0l-5 5m5-5H6" />
                      </svg>
                      <span className="text-gray-400">{w.to}</span>
                      <span className="text-gray-600">.{w.action}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* Verse Code */}
            {message.verseCode && (
              <div className="bg-[#1a1a2e] border border-fn-border rounded-xl overflow-hidden">
                <div className="flex items-center justify-between px-3 py-1.5 bg-black/20 border-b border-fn-border">
                  <span className="text-[9px] text-gray-500 font-mono">verse</span>
                  <button
                    onClick={() => navigator.clipboard.writeText(message.verseCode!)}
                    className="text-[9px] text-gray-600 hover:text-gray-400 transition-colors"
                  >
                    Copy
                  </button>
                </div>
                <pre className="px-3 py-2 text-[10px] font-mono text-gray-300 overflow-x-auto leading-relaxed">
                  {message.verseCode}
                </pre>
              </div>
            )}

            {/* Actions */}
            {message.actions && message.actions.length > 0 && (
              <div className="flex flex-wrap gap-1.5">
                {message.actions.map((action, i) => {
                  const isExecute = action.data?.action === 'execute'
                  const needsBridge = isExecute && !bridgeConnected
                  return (
                    <button
                      key={i}
                      onClick={() => !needsBridge && onActionClick(action)}
                      disabled={needsBridge}
                      className={`inline-flex items-center gap-1.5 text-[10px] px-3 py-1.5 rounded-lg border transition-colors ${
                        isExecute
                          ? bridgeConnected
                            ? 'border-emerald-400/30 bg-emerald-400/10 text-emerald-400 hover:bg-emerald-400/20'
                            : 'border-gray-600 bg-gray-600/10 text-gray-500 cursor-not-allowed'
                          : 'border-fn-rare/20 bg-fn-rare/5 text-fn-rare hover:bg-fn-rare/10'
                      }`}
                      title={needsBridge ? 'Connect to UEFN bridge first' : action.description}
                    >
                      {isExecute ? (
                        <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                          <path d="M14.752 11.168l-3.197-2.132A1 1 0 0010 9.87v4.263a1 1 0 001.555.832l3.197-2.132a1 1 0 000-1.664z" />
                          <path d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                        </svg>
                      ) : (
                        <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                          <path d="M13 7l5 5m0 0l-5 5m5-5H6" />
                        </svg>
                      )}
                      {action.label}
                    </button>
                  )
                })}
                <button
                  onClick={onCopyPrompt}
                  className="inline-flex items-center gap-1.5 text-[10px] px-3 py-1.5 rounded-lg border border-fn-border text-gray-500 hover:text-gray-300 hover:border-gray-600 transition-colors"
                >
                  <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z" />
                  </svg>
                  Copy prompt for Claude Code
                </button>
              </div>
            )}
          </div>
        )}

        <div className={`text-[8px] text-gray-700 mt-1 ${isUser ? 'text-right' : ''}`}>
          {new Date(message.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
        </div>
      </div>
    </div>
  )
}

// ─── Context Chip ───────────────────────────────────────────────────────────

function ContextChip({
  label,
  value,
  active,
  warn,
}: {
  label: string
  value: string
  active?: boolean
  warn?: boolean
}) {
  return (
    <div className="bg-fn-panel border border-fn-border rounded-lg px-2.5 py-1.5">
      <div className="text-[8px] text-gray-600 uppercase tracking-wider">{label}</div>
      <div className={`text-[10px] font-medium ${
        warn ? 'text-amber-400' :
        active ? 'text-fn-rare' : 'text-gray-500'
      }`}>
        {value}
      </div>
    </div>
  )
}

// ─── Prompt Building ────────────────────────────────────────────────────────

function buildContext(
  status: ReturnType<typeof useForgeStore.getState>['status'],
  selectedLevel: string | null
): string {
  const parts: string[] = []

  if (status?.isConfigured) {
    parts.push(`Project: ${status.projectName}`)
    parts.push(`Mode: ${status.mode}`)
    parts.push(`Assets: ${status.assetCount ?? 0}, Verse: ${status.verseCount ?? 0}`)
    if (status.isUefnRunning) parts.push('UEFN is currently running -- writes will be staged')
  }

  if (selectedLevel) {
    parts.push(`Active level: ${selectedLevel}`)
  }

  return parts.join('\n')
}

function buildStructuredPrompt(
  userRequest: string,
  context: string,
  systemsContext: string
): string {
  return `# WellVersed Generation Request

## User Request
${userRequest}

## Project Context
${context || 'No project configured -- generating standalone system definition.'}
${systemsContext}

## Instructions
Using the WellVersed MCP tools, generate the requested system:
1. Use \`analyze_level_systems\` to understand existing devices
2. Use \`generate_verse_file\` for Verse code generation
3. Use \`clone_actor\` for device placement
4. Use \`preview_set_property\` + \`apply_modification\` for device configuration
5. Use \`forge_build_uasset\` for widget blueprint creation

Generate all devices, Verse code, widgets, and wiring needed.
Validate all written files with the post-write validation system.`
}

// ─── Fallback content when sidecar command is not available ─────────────────

function buildFallbackContent(userRequest: string, systemsContext: string): string {
  const parts = [
    `I understand you want to build: "${userRequest}"`,
    '',
    'The game designer sidecar command is not yet available. To generate this system:',
    '1. Copy the structured prompt below and run it in Claude Code with the WellVersed MCP server active.',
    '2. Or select a quick action template to get started with a common pattern.',
  ]
  if (systemsContext) {
    parts.push('', `Current project context: ${systemsContext}`)
  }
  return parts.join('\n')
}

function buildFallbackActions(): GenerationAction[] {
  return [
    { label: 'Browse Device Recipes', description: 'See pre-built patterns', page: 'recipes' },
    { label: 'Browse Library', description: 'Reference real project implementations', page: 'library-browse' },
    { label: 'View Encyclopedia', description: 'Look up device reference', page: 'encyclopedia' },
  ]
}

function delay(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms))
}
