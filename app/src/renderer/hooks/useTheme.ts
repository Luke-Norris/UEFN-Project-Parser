import { useEffect } from 'react'
import { useSettingsStore } from '../stores/settingsStore'
import { themes, accentColors } from '../styles/themes'

/**
 * Applies the active theme + accent color + font size + reduced motion
 * as CSS custom properties on document.documentElement.
 * Call once in App.tsx.
 */
export function useTheme(): void {
  const theme = useSettingsStore((s) => s.theme)
  const accentColor = useSettingsStore((s) => s.accentColor)
  const fontSize = useSettingsStore((s) => s.fontSize)
  const reducedMotion = useSettingsStore((s) => s.reducedMotion)
  const highContrast = useSettingsStore((s) => s.highContrast)
  const animationsEnabled = useSettingsStore((s) => s.animationsEnabled)

  // Apply theme — override Tailwind's hardcoded colors via dynamic <style>
  useEffect(() => {
    const themeDef = themes[theme]
    if (!themeDef) return
    const c = themeDef.colors

    const root = document.documentElement
    for (const [prop, value] of Object.entries(c)) {
      root.style.setProperty(prop, value)
    }

    // Also set the theme's recommended accent
    const accent = accentColors[themeDef.defaultAccent] || accentColors.blue
    root.style.setProperty('--fn-accent', accent.primary)
    root.style.setProperty('--fn-accent-hover', accent.hover)

    const darker = c['--fn-darker'] || '#0f0f1a'
    const dark = c['--fn-dark'] || '#1a1a2e'
    const panel = c['--fn-panel'] || '#16213e'
    const border = c['--fn-border'] || '#2a2a4a'
    const textPri = c['--text-primary'] || '#e2e8f0'
    const textSec = c['--text-secondary'] || '#94a3b8'

    let styleEl = document.getElementById('wellversed-theme') as HTMLStyleElement | null
    if (!styleEl) {
      styleEl = document.createElement('style')
      styleEl.id = 'wellversed-theme'
      document.head.appendChild(styleEl)
    }

    // Override accent color in Tailwind classes too
    const accentPri = accent.primary

    styleEl.textContent = `
      body { background: ${darker} !important; color: ${textPri} !important; }
      .bg-fn-darker, .bg-fn-darker\\/50 { background-color: ${darker} !important; }
      .bg-fn-dark, .bg-fn-dark\\/50 { background-color: ${dark} !important; }
      .bg-fn-panel { background-color: ${panel} !important; }
      .border-fn-border { border-color: ${border} !important; }
      .border-fn-border\\/20, .border-fn-border\\/30, .border-fn-border\\/40, .border-fn-border\\/50 { border-color: ${border} !important; }
      .text-gray-400 { color: ${textSec} !important; }
      .text-gray-500 { color: ${textSec} !important; }
      .text-gray-600 { color: color-mix(in srgb, ${textSec} 60%, transparent) !important; }
      .text-fn-rare { color: ${accentPri} !important; }
      .bg-fn-rare { background-color: ${accentPri} !important; }
      .bg-fn-rare\\/10 { background-color: color-mix(in srgb, ${accentPri} 10%, transparent) !important; }
      .bg-fn-rare\\/20 { background-color: color-mix(in srgb, ${accentPri} 20%, transparent) !important; }
      .border-fn-rare { border-color: ${accentPri} !important; }
      ::-webkit-scrollbar-thumb { background: ${border} !important; }
      .input-field { background-color: ${darker} !important; border-color: ${border} !important; }
      /* Canvas/widget editor areas */
      .canvas-container { background-color: ${darker} !important; }
    `
  }, [theme])

  // Apply accent color
  useEffect(() => {
    const root = document.documentElement
    const accent = accentColors[accentColor]
    if (!accent) return

    root.style.setProperty('--fn-accent', accent.primary)
    root.style.setProperty('--fn-accent-hover', accent.hover)
  }, [accentColor])

  // Apply font size class
  useEffect(() => {
    const root = document.documentElement
    root.classList.remove('text-size-small', 'text-size-medium', 'text-size-large')
    root.classList.add(`text-size-${fontSize}`)
  }, [fontSize])

  // Apply reduced motion
  useEffect(() => {
    const root = document.documentElement
    if (reducedMotion || !animationsEnabled) {
      root.classList.add('reduce-motion')
    } else {
      root.classList.remove('reduce-motion')
    }
  }, [reducedMotion, animationsEnabled])

  // Apply high contrast
  useEffect(() => {
    const root = document.documentElement
    if (highContrast) {
      root.classList.add('high-contrast')
    } else {
      root.classList.remove('high-contrast')
    }
  }, [highContrast])
}
