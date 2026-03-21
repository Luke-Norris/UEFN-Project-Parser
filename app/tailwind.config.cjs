/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ['./src/renderer/**/*.{js,ts,jsx,tsx}'],
  theme: {
    extend: {
      colors: {
        fn: {
          common: '#bfbfbf',
          uncommon: '#60aa3a',
          rare: '#3d85e0',
          epic: '#a34ee1',
          legendary: '#c76b29',
          mythic: '#c4a23c',
          exotic: '#76d6e3',
          // Theme colors use CSS variables — opacity variants work via raw hex fallback
          // For bg-fn-dark/50 etc to work, we keep these as plain hex defaults
          // The useTheme hook overrides the actual CSS properties at runtime
          dark: '#1a1a2e',
          darker: '#0f0f1a',
          panel: '#16213e',
          border: '#2a2a4a'
        }
      },
      fontFamily: {
        burbank: ['burbankbigcondensed_black', 'sans-serif'],
        'burbank-bold': ['burbankbigcondensed_bold', 'sans-serif']
      }
    }
  },
  plugins: []
}
