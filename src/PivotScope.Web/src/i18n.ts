import { createI18n } from 'vue-i18n'
import fr from './locales/fr'
import en from './locales/en'

const STORAGE_KEY = 'pivotscope.locale'

export type Locale = 'fr' | 'en'

/**
 * Français par défaut — c'est la langue de l'auteur et de son usage quotidien.
 * Le choix est conservé d'une session à l'autre : personne n'a envie de le
 * refaire à chaque ouverture d'Excel.
 */
function initialLocale(): Locale {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored === 'fr' || stored === 'en') return stored
  } catch {
    // Volet sans stockage disponible : on retombe sur le défaut.
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
  try { localStorage.setItem(STORAGE_KEY, locale) } catch { /* sans stockage */ }
}

export function currentLocale(): Locale {
  return i18n.global.locale.value as Locale
}
