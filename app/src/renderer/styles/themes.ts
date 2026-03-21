export interface ThemeDef {
  name: string
  description: string
  defaultAccent: string // recommended accent color for this theme
  colors: Record<string, string>
}

export const themes: Record<string, ThemeDef> = {
  midnight: {
    name: 'Midnight',
    description: 'Deep blue darkness',
    defaultAccent: 'blue',
    colors: {
      '--fn-darker': '#0a0a1a',
      '--fn-dark': '#0f0f23',
      '--fn-panel': '#16162e',
      '--fn-border': '#1e1e3a',
      '--text-primary': '#e2e8f0',
      '--text-secondary': '#94a3b8',
    },
  },
  obsidian: {
    name: 'Obsidian',
    description: 'Pure dark carbon',
    defaultAccent: 'cyan',
    colors: {
      '--fn-darker': '#0a0a0a',
      '--fn-dark': '#111111',
      '--fn-panel': '#1a1a1a',
      '--fn-border': '#2a2a2a',
      '--text-primary': '#e5e5e5',
      '--text-secondary': '#999999',
    },
  },
  arctic: {
    name: 'Arctic',
    description: 'Light, clean workspace',
    defaultAccent: 'blue',
    colors: {
      '--fn-darker': '#f0f2f5',
      '--fn-dark': '#ffffff',
      '--fn-panel': '#f8f9fa',
      '--fn-border': '#e1e4e8',
      '--text-primary': '#1a1a2e',
      '--text-secondary': '#586069',
    },
  },
  cyberpunk: {
    name: 'Cyberpunk',
    description: 'Neon-accented dark',
    defaultAccent: 'purple',
    colors: {
      '--fn-darker': '#0d0d1a',
      '--fn-dark': '#13132b',
      '--fn-panel': '#1a1a3e',
      '--fn-border': '#2d1b69',
      '--text-primary': '#e0e0ff',
      '--text-secondary': '#8888cc',
    },
  },
  forest: {
    name: 'Forest',
    description: 'Deep green tones',
    defaultAccent: 'green',
    colors: {
      '--fn-darker': '#0a120a',
      '--fn-dark': '#0f1a0f',
      '--fn-panel': '#162016',
      '--fn-border': '#1e3a1e',
      '--text-primary': '#d4e8d4',
      '--text-secondary': '#7aaa7a',
    },
  },
  sunset: {
    name: 'Sunset',
    description: 'Warm dark amber',
    defaultAccent: 'orange',
    colors: {
      '--fn-darker': '#120d0a',
      '--fn-dark': '#1a140f',
      '--fn-panel': '#201a14',
      '--fn-border': '#3a2e1e',
      '--text-primary': '#e8ddd4',
      '--text-secondary': '#aa957a',
    },
  },
}

export const accentColors: Record<string, { primary: string; hover: string }> = {
  blue: { primary: '#3d85e0', hover: '#5a9de6' },
  purple: { primary: '#a34ee1', hover: '#b76ae8' },
  green: { primary: '#60aa3a', hover: '#7bc255' },
  orange: { primary: '#e89020', hover: '#f0a840' },
  red: { primary: '#e04040', hover: '#e86060' },
  pink: { primary: '#e040a0', hover: '#e860b8' },
  cyan: { primary: '#20c0d0', hover: '#40d0e0' },
}
