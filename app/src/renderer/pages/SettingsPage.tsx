import { useSettingsStore, settingsDefaults } from '../stores/settingsStore'
import type { SettingsState } from '../stores/settingsStore'
import { themes, accentColors } from '../styles/themes'

// ─── Toggle switch ─────────────────────────────────────────────────────────

function Toggle({
  value,
  onChange,
}: {
  value: boolean
  onChange: (v: boolean) => void
}) {
  return (
    <button
      type="button"
      className={`relative inline-flex h-5 w-9 shrink-0 rounded-full transition-colors duration-150 ${
        value ? 'bg-emerald-500' : 'bg-gray-600'
      }`}
      onClick={() => onChange(!value)}
    >
      <span
        className={`pointer-events-none inline-block h-4 w-4 rounded-full bg-white shadow transform transition-transform duration-150 mt-0.5 ${
          value ? 'translate-x-[18px]' : 'translate-x-0.5'
        }`}
      />
    </button>
  )
}

// ─── Section card wrapper ──────────────────────────────────────────────────

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="bg-fn-dark border border-fn-border rounded-lg p-5">
      <h3 className="text-[13px] font-semibold text-white mb-4">{title}</h3>
      <div className="space-y-4">{children}</div>
    </div>
  )
}

// ─── Setting row ───────────────────────────────────────────────────────────

function SettingRow({
  label,
  description,
  children,
}: {
  label: string
  description?: string
  children: React.ReactNode
}) {
  return (
    <div className="flex items-center justify-between gap-4">
      <div className="flex-1 min-w-0">
        <div className="text-[11px] text-gray-400">{label}</div>
        {description && <div className="text-[10px] text-gray-600 mt-0.5">{description}</div>}
      </div>
      <div className="shrink-0">{children}</div>
    </div>
  )
}

// ─── Number input ──────────────────────────────────────────────────────────

function NumberInput({
  value,
  onChange,
  min,
  max,
  step,
  width,
}: {
  value: number
  onChange: (v: number) => void
  min?: number
  max?: number
  step?: number
  width?: string
}) {
  return (
    <input
      type="number"
      className={`bg-fn-darker border border-fn-border rounded px-2 py-1 text-[11px] text-white outline-none focus:border-fn-rare/50 transition-colors ${width ?? 'w-20'}`}
      value={value}
      min={min}
      max={max}
      step={step}
      onChange={(e) => {
        const v = Number(e.target.value)
        if (!isNaN(v)) onChange(v)
      }}
    />
  )
}

// ─── Segmented toggle buttons ──────────────────────────────────────────────

function SegmentedToggle<T extends string>({
  options,
  value,
  onChange,
}: {
  options: { label: string; value: T }[]
  value: T
  onChange: (v: T) => void
}) {
  return (
    <div className="flex rounded-md overflow-hidden border border-fn-border">
      {options.map((opt) => (
        <button
          key={opt.value}
          className={`px-3 py-1 text-[10px] font-medium transition-colors ${
            value === opt.value
              ? 'bg-fn-rare text-white'
              : 'bg-fn-darker text-gray-400 hover:text-gray-200 hover:bg-white/[0.03]'
          }`}
          onClick={() => onChange(opt.value)}
        >
          {opt.label}
        </button>
      ))}
    </div>
  )
}

// ─── Theme type helper ─────────────────────────────────────────────────────

type ThemeId = SettingsState['theme']
type AccentId = SettingsState['accentColor']

// ─── Main settings page ────────────────────────────────────────────────────

export function SettingsPage() {
  const store = useSettingsStore()
  const {
    theme,
    accentColor,
    fontSize,
    uiZoom,
    fortnitePath,
    animationsEnabled,
    highContrast,
    reducedMotion,
    largeClickTargets,
    autoRefreshOnFocus,
    showHiddenFiles,
    copyOnReadWarnings,
    maxPropertiesPerExport,
    defaultScanPath,
    verseEditorFontSize,
    defaultCanvasWidth,
    defaultCanvasHeight,
    snapToGrid,
    gridSize,
    setSetting,
    resetToDefaults,
  } = store

  return (
    <div className="flex-1 bg-fn-darker overflow-y-auto min-h-0">
      <div className="max-w-2xl mx-auto p-6 space-y-5">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-[16px] font-semibold text-white">Settings</h1>
            <p className="text-[11px] text-gray-500 mt-0.5">
              Customize appearance, accessibility, and developer options
            </p>
          </div>
          <button
            className="text-[10px] text-gray-500 hover:text-red-400 transition-colors px-3 py-1.5 rounded border border-fn-border hover:border-red-400/30"
            onClick={resetToDefaults}
          >
            Reset All to Defaults
          </button>
        </div>

        {/* ─── Appearance ─── */}
        <Section title="Appearance">
          {/* Theme picker */}
          <div>
            <div className="text-[11px] text-gray-400 mb-2">Theme</div>
            <div className="grid grid-cols-3 gap-2">
              {(Object.entries(themes) as [ThemeId, (typeof themes)[ThemeId]][]).map(
                ([id, def]) => (
                  <button
                    key={id}
                    className={`text-left rounded-lg border p-3 transition-colors ${
                      theme === id
                        ? 'border-fn-rare bg-fn-rare/10'
                        : 'border-fn-border hover:border-gray-500'
                    }`}
                    onClick={() => setSetting('theme', id)}
                  >
                    <div className="text-[11px] text-white font-medium">{def.name}</div>
                    <div className="text-[10px] text-gray-500 mt-0.5">{def.description}</div>
                    {/* Color preview swatches */}
                    <div className="flex gap-1 mt-2">
                      {Object.values(def.colors).map((color, i) => (
                        <div
                          key={i}
                          className="w-4 h-4 rounded-sm border border-white/10"
                          style={{ background: color }}
                        />
                      ))}
                    </div>
                  </button>
                )
              )}
            </div>
          </div>

          {/* Accent color */}
          <div>
            <div className="text-[11px] text-gray-400 mb-2">Accent Color</div>
            <div className="flex gap-2">
              {(Object.entries(accentColors) as [AccentId, (typeof accentColors)[AccentId]][]).map(
                ([id, def]) => (
                  <button
                    key={id}
                    className={`w-7 h-7 rounded-full transition-all ${
                      accentColor === id
                        ? 'ring-2 ring-white ring-offset-2 ring-offset-fn-dark scale-110'
                        : 'hover:scale-110'
                    }`}
                    style={{ background: def.primary }}
                    onClick={() => setSetting('accentColor', id)}
                    title={id.charAt(0).toUpperCase() + id.slice(1)}
                  />
                )
              )}
            </div>
          </div>

          {/* Font size */}
          <SettingRow label="Font Size">
            <SegmentedToggle
              options={[
                { label: 'Small', value: 'small' as const },
                { label: 'Medium', value: 'medium' as const },
                { label: 'Large', value: 'large' as const },
              ]}
              value={fontSize}
              onChange={(v) => setSetting('fontSize', v)}
            />
          </SettingRow>

          {/* UI Scale */}
          <SettingRow
            label="UI Scale"
            description="Scale the entire interface (50% – 300%)"
          >
            <div className="flex items-center gap-3 w-full max-w-xs">
              <input
                type="range"
                min={50}
                max={300}
                step={10}
                value={Math.round(uiZoom * 100)}
                onChange={(e) => {
                  const val = Number(e.target.value) / 100
                  setSetting('uiZoom', val)
                  document.body.style.zoom = String(val)
                }}
                className="flex-1 accent-blue-500"
              />
              <button
                className="text-sm font-mono min-w-[48px] text-center px-2 py-1 rounded bg-fn-panel border border-fn-border hover:bg-white/5 transition-colors"
                onClick={() => {
                  setSetting('uiZoom', 1)
                  document.body.style.zoom = '1'
                }}
                title="Reset to 100%"
              >
                {Math.round(uiZoom * 100)}%
              </button>
            </div>
          </SettingRow>

          {/* Animations */}
          <SettingRow
            label="Animations"
            description="Enable transition and animation effects throughout the UI"
          >
            <Toggle
              value={animationsEnabled}
              onChange={(v) => setSetting('animationsEnabled', v)}
            />
          </SettingRow>
        </Section>

        {/* ─── Accessibility ─── */}
        <Section title="Accessibility">
          <SettingRow
            label="High Contrast"
            description="Increases border and text contrast for better readability"
          >
            <Toggle
              value={highContrast}
              onChange={(v) => setSetting('highContrast', v)}
            />
          </SettingRow>

          <SettingRow
            label="Reduced Motion"
            description="Disables all animations and transitions for motion sensitivity"
          >
            <Toggle
              value={reducedMotion}
              onChange={(v) => setSetting('reducedMotion', v)}
            />
          </SettingRow>

          <SettingRow
            label="Larger Click Targets"
            description="Increases the size of buttons and interactive elements"
          >
            <Toggle
              value={largeClickTargets}
              onChange={(v) => setSetting('largeClickTargets', v)}
            />
          </SettingRow>
        </Section>

        {/* ─── UEFN Developer ─── */}
        <Section title="UEFN Developer">
          <SettingRow
            label="Auto-Refresh on Focus"
            description="Automatically refresh project data when the app gains focus"
          >
            <Toggle
              value={autoRefreshOnFocus}
              onChange={(v) => setSetting('autoRefreshOnFocus', v)}
            />
          </SettingRow>

          <SettingRow
            label="Show Hidden Files"
            description="Show system directories like __External* without collapsing"
          >
            <Toggle
              value={showHiddenFiles}
              onChange={(v) => setSetting('showHiddenFiles', v)}
            />
          </SettingRow>

          <SettingRow
            label="Copy-on-Read Warnings"
            description="Show safety indicators when inspecting .uasset files"
          >
            <Toggle
              value={copyOnReadWarnings}
              onChange={(v) => setSetting('copyOnReadWarnings', v)}
            />
          </SettingRow>

          <SettingRow
            label="Max Properties per Export"
            description="Cap for inspect results (10-200)"
          >
            <NumberInput
              value={maxPropertiesPerExport}
              onChange={(v) =>
                setSetting('maxPropertiesPerExport', Math.min(200, Math.max(10, v)))
              }
              min={10}
              max={200}
            />
          </SettingRow>

          <SettingRow
            label="Default Scan Path"
            description="Default directory when scanning for UEFN projects"
          >
            <input
              type="text"
              className="bg-fn-darker border border-fn-border rounded px-2 py-1 text-[11px] text-white outline-none focus:border-fn-rare/50 transition-colors w-48"
              value={defaultScanPath}
              onChange={(e) => setSetting('defaultScanPath', e.target.value)}
              placeholder="C:\Users\..."
            />
          </SettingRow>

          <SettingRow
            label="Verse Viewer Font Size"
            description="Font size for verse code display (10-24px)"
          >
            <NumberInput
              value={verseEditorFontSize}
              onChange={(v) =>
                setSetting('verseEditorFontSize', Math.min(24, Math.max(10, v)))
              }
              min={10}
              max={24}
            />
          </SettingRow>
        </Section>

        {/* ─── Asset Preview (CUE4Parse) ─── */}
        <Section title="Asset Preview">
          <SettingRow
            label="Fortnite Install Path"
            description="Point to your Fortnite installation to enable real asset previews (textures, meshes). Read-only access — no game files are modified."
          >
            <div className="flex items-center gap-2 w-full">
              <input
                type="text"
                value={fortnitePath}
                onChange={(e) => setSetting('fortnitePath', e.target.value)}
                placeholder="C:\Program Files\Epic Games\Fortnite"
                className="flex-1 bg-fn-darker border border-fn-border rounded px-2.5 py-1.5 text-[10px] text-gray-300 placeholder-gray-600 focus:outline-none focus:border-blue-500/50 font-mono"
              />
              <button
                onClick={async () => {
                  const dir = await window.electronAPI.selectDirectory()
                  if (dir) setSetting('fortnitePath', dir)
                }}
                className="shrink-0 px-2 py-1.5 text-[10px] text-gray-400 bg-fn-darker border border-fn-border rounded hover:bg-white/[0.05] transition-colors"
              >
                Browse
              </button>
            </div>
          </SettingRow>
        </Section>

        {/* ─── Widget Editor Defaults ─── */}
        <Section title="Widget Editor Defaults">
          <div>
            <div className="text-[11px] text-gray-400 mb-2">Default Canvas Size</div>
            <div className="flex items-center gap-2 mb-2">
              <NumberInput
                value={defaultCanvasWidth}
                onChange={(v) => setSetting('defaultCanvasWidth', Math.max(64, v))}
                min={64}
                max={4096}
              />
              <span className="text-[10px] text-gray-600">x</span>
              <NumberInput
                value={defaultCanvasHeight}
                onChange={(v) => setSetting('defaultCanvasHeight', Math.max(64, v))}
                min={64}
                max={4096}
              />
            </div>
            {/* Presets */}
            <div className="flex gap-1.5">
              {(
                [
                  { label: '512', w: 512, h: 512 },
                  { label: '1024', w: 1024, h: 1024 },
                  { label: '1080p', w: 1920, h: 1080 },
                ] as const
              ).map((preset) => (
                <button
                  key={preset.label}
                  className={`text-[9px] px-2 py-0.5 rounded border transition-colors ${
                    defaultCanvasWidth === preset.w && defaultCanvasHeight === preset.h
                      ? 'border-fn-rare text-fn-rare bg-fn-rare/10'
                      : 'border-fn-border text-gray-500 hover:text-gray-300 hover:border-gray-500'
                  }`}
                  onClick={() => {
                    setSetting('defaultCanvasWidth', preset.w)
                    setSetting('defaultCanvasHeight', preset.h)
                  }}
                >
                  {preset.label}
                </button>
              ))}
            </div>
          </div>

          <SettingRow
            label="Snap to Grid"
            description="Snap widget elements to grid when placing"
          >
            <Toggle value={snapToGrid} onChange={(v) => setSetting('snapToGrid', v)} />
          </SettingRow>

          <SettingRow
            label="Grid Size"
            description="Pixel spacing for the snap grid"
          >
            <NumberInput
              value={gridSize}
              onChange={(v) => setSetting('gridSize', Math.min(64, Math.max(1, v)))}
              min={1}
              max={64}
            />
          </SettingRow>
        </Section>

        {/* ─── Data & Cache ─── */}
        <Section title="Data & Cache">
          <div className="flex items-center gap-3">
            <button
              className="text-[10px] font-medium text-gray-300 bg-fn-darker border border-fn-border rounded px-3 py-1.5 hover:border-red-400/40 hover:text-red-400 transition-colors"
              onClick={() => {
                localStorage.removeItem('wellversed-settings')
                window.location.reload()
              }}
            >
              Clear All Cache
            </button>
            <button
              className="text-[10px] font-medium text-gray-300 bg-fn-darker border border-fn-border rounded px-3 py-1.5 hover:border-fn-rare/40 hover:text-fn-rare transition-colors"
              onClick={() => window.location.reload()}
            >
              Refresh All Data
            </button>
          </div>
          <div className="text-[10px] text-gray-600 mt-1">
            Settings are stored in localStorage. Clearing cache will reset all preferences to defaults.
          </div>
        </Section>

        {/* ─── About ─── */}
        <Section title="About">
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-[11px] text-gray-400">Application</span>
              <span className="text-[11px] text-white">WellVersed</span>
            </div>
            <div className="flex items-center justify-between">
              <span className="text-[11px] text-gray-400">Version</span>
              <span className="text-[11px] text-white">1.0.0</span>
            </div>
            <div className="flex items-center justify-between">
              <span className="text-[11px] text-gray-400">Backend</span>
              <span className="text-[11px] text-white">FortniteForge (.NET 8)</span>
            </div>
            <div className="flex items-center justify-between">
              <span className="text-[11px] text-gray-400">Asset Parser</span>
              <span className="text-[11px] text-white">UAssetAPI (patched)</span>
            </div>
            <div className="flex items-center justify-between">
              <span className="text-[11px] text-gray-400">Engine Target</span>
              <span className="text-[11px] text-white">Unreal Engine 5.4 (UEFN)</span>
            </div>
          </div>
        </Section>

        {/* Bottom spacer */}
        <div className="h-6" />
      </div>
    </div>
  )
}
