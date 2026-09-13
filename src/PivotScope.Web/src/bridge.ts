/**
 * Client for the bridge to the add-in. Mirror of BridgeRouter on the .NET side:
 * we send {id, method, params}, we receive {id, ok, result | error}.
 *
 * The .NET router ALWAYS replies, including on error — that is what guarantees
 * that no promise is left pending here.
 */

type Pending = {
  resolve: (value: unknown) => void
  reject: (error: Error) => void
}

type BridgeResponse = {
  id: string
  ok: boolean
  result?: unknown
  error?: string
}

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage(message: string): void
        addEventListener(
          type: 'message',
          handler: (event: { data: string }) => void,
        ): void
      }
    }
  }
}

const pending = new Map<string, Pending>()
let sequence = 0

/** Notifications pushed by the add-in, with no associated request. */
type BridgeEvent = { event: string; [key: string]: unknown }

const listeners = new Map<string, Set<(payload: BridgeEvent) => void>>()

/** Subscribes to a pushed event. Returns the unsubscribe function. */
export function onEvent(
  name: string,
  handler: (payload: BridgeEvent) => void,
): () => void {
  const set = listeners.get(name) ?? new Set()
  set.add(handler)
  listeners.set(name, set)
  return () => set.delete(handler)
}

const webview = window.chrome?.webview

webview?.addEventListener('message', event => {
  let message: BridgeResponse | BridgeEvent
  try {
    message = JSON.parse(event.data) as BridgeResponse | BridgeEvent
  } catch {
    return
  }

  // A pushed notification has no id: it carries "event".
  if ('event' in message && typeof message.event === 'string') {
    for (const handler of listeners.get(message.event) ?? []) {
      try { handler(message as BridgeEvent) } catch { /* one subscriber does not block the others */ }
    }
    return
  }

  const response = message as BridgeResponse
  const entry = pending.get(response.id)
  if (!entry) return
  pending.delete(response.id)

  if (response.ok) entry.resolve(response.result)
  else entry.reject(new Error(response.error ?? 'Erreur inconnue.'))
})

/** Tells whether the page is really running in the task pane (and not in a bare browser). */
export const isHosted = webview !== undefined

export function call<T>(method: string, params?: unknown): Promise<T> {
  if (!webview) {
    return Promise.reject(
      new Error("Le pont n'est pas disponible : ouvrez le volet depuis Excel."),
    )
  }

  const id = String(++sequence)
  return new Promise<T>((resolve, reject) => {
    pending.set(id, { resolve: resolve as (v: unknown) => void, reject })
    webview.postMessage(JSON.stringify({ id, method, params }))
  })
}
