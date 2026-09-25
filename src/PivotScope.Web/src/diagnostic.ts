import type { PivotContext } from './types'

type Translate = (key: string) => string

/**
 * The host's diagnostic, in the pane's language. The host sends a stable code
 * next to its French text: the text is only the fallback for a code this
 * version of the pane does not know.
 */
export function describeDiagnostic(context: PivotContext | null, t: Translate): string {
  if (!context) return t('diagnostic.noPivot')
  if (context.diagnosticCode) return t(`diagnostic.${context.diagnosticCode}`)
  return context.diagnostic ?? t('common.noPivot')
}
