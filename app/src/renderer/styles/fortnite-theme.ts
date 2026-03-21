import type { RarityTier } from '../../shared/types'

export const RARITY_COLORS: Record<RarityTier, string> = {
  common: '#bfbfbf',
  uncommon: '#60aa3a',
  rare: '#3d85e0',
  epic: '#a34ee1',
  legendary: '#c76b29',
  mythic: '#c4a23c',
  exotic: '#76d6e3'
}

export const RARITY_LABELS: Record<RarityTier, string> = {
  common: 'Common',
  uncommon: 'Uncommon',
  rare: 'Rare',
  epic: 'Epic',
  legendary: 'Legendary',
  mythic: 'Mythic',
  exotic: 'Exotic'
}

// Map rarity tier to expected asset filename patterns
export const RARITY_BG_PATTERNS: Record<RarityTier, string> = {
  common: 'Common Weapon Rarity Background',
  uncommon: 'Uncommon Weapon Rarity Background',
  rare: 'Rare Weapon Rarity Background',
  epic: 'Epic Weapon Rarity Background',
  legendary: 'Legendary Weapon Rarity Background',
  mythic: 'Mythic Weapon Rarity Background',
  exotic: 'Exotic Spiked Background'
}

export const RARITY_SPIKED_PATTERNS: Record<RarityTier, string> = {
  common: 'Common Spiked Background',
  uncommon: 'Uncommon Spiked Background',
  rare: 'Rare Spiked Background',
  epic: 'Epic Spiked Background',
  legendary: 'Legendary Spiked Background',
  mythic: 'Mythic Spiked Background',
  exotic: 'Exotic Spiked Background'
}

// Font family names derived from filenames (minus extension)
export const FORTNITE_FONTS = {
  black: 'burbankbigcondensed_black',
  bold: 'burbankbigcondensed_bold'
} as const
