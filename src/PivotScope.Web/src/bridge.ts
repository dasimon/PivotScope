/**
 * Client du pont vers l'add-in. Symétrique de BridgeRouter côté .NET :
 * on envoie {id, method, params}, on reçoit {id, ok, result | error}.
 *
 * Le routeur .NET répond TOUJOURS, y compris en erreur — c'est ce qui garantit
 * qu'aucune promesse ne reste pendante ici.
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

const webview = window.chrome?.webview

webview?.addEventListener('message', event => {
  let response: BridgeResponse
  try {
    response = JSON.parse(event.data) as BridgeResponse
  } catch {
    return
  }

  const entry = pending.get(response.id)
  if (!entry) return
  pending.delete(response.id)

  if (response.ok) entry.resolve(response.result)
  else entry.reject(new Error(response.error ?? 'Erreur inconnue.'))
})

/** Indique si la page tourne bien dans le volet (et non dans un navigateur nu). */
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
