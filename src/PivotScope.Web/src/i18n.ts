import { createI18n } from 'vue-i18n'
import fr from './locales/fr'
import en from './locales/en'

const STORAGE_KEY = 'pivotscope.locale'

export type Locale = 'fr' | 'en'

/**
 * French by default: it is the author's language and the one used day to day.
 * The choice is kept from one session to the next: nobody wants to make it
 * again every time Excel opens.
 */
function initialLocale(): Locale {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored === 'fr' || stored === 'en') return stored
  } catch {
    // Pane with no storage available: fall back to the default.
  }
  return navigator.language?.toLowerCase().startsWith('en') ? 'en' : 'fr'
}

export const i18n = createI18n({
  legacy: false,
  locale: initialLocale(),
  fallbackLocale: 'fr',
  messages: { fr, en },
})

export function setLocale(locale: Locale): void {
  i18n.global.locale.value = locale
  try { localStorage.setItem(STORAGE_KEY, locale) } catch { /* no storage */ }
}

export function currentLocale(): Locale {
  return i18n.global.locale.value as Locale
}
