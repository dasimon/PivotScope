// Update, pane side. The add-in does the network work (AddInUpdates, CubeScope.Core): the pane
// only reads its state and relays the user's choices. The automatic check is the pane's call —
// once it is open, so that loading the add-in stays free of network — and the add-in skips it
// when this Excel process already checked recently.
import { reactive } from 'vue'
import { call, isHosted } from './bridge'
import type { UpdateDownload, UpdateStatus } from './types'

const AUTO_KEY = 'pivotscope.update.auto'
const FIRST_CHECK_MS = 10_000
const RECHECK_MS = 12 * 60 * 60_000
const BUSY_POLL_MS = 500

function readAuto(): boolean {
  try {
    return localStorage.getItem(AUTO_KEY) !== '0'
  } catch {
    return true
  }
}

export const update = reactive({
  status: null as UpdateStatus | null,
  autoCheck: readAuto(),
  download: null as UpdateDownload | null,
  error: '',
})

let poll: number | undefined

async function readStatus(): Promise<void> {
  try {
    update.status = await call<UpdateStatus>('update.status')
  } catch {
    // Keep the last state.
  }
}

async function check(automatic: boolean): Promise<void> {
  update.error = ''
  try {
    update.status = await call<UpdateStatus>('update.check', { automatic })
  } catch (e) {
    update.error = e instanceof Error ? e.message : String(e)
  }
}

export function startUpdateChecks(): void {
  if (!isHosted) return
  void readStatus()
  window.setTimeout(() => {
    if (update.autoCheck) void check(true)
  }, FIRST_CHECK_MS)
  window.setInterval(() => {
    if (update.autoCheck) void check(true)
  }, RECHECK_MS)
}

export const updateActions = {
  check: () => check(false),
  async download() {
    update.error = ''
    update.download = null
    // The answer comes once the zip is verified and saved: show the progress meanwhile.
    window.clearInterval(poll)
    poll = window.setInterval(() => void readStatus(), BUSY_POLL_MS)
    try {
      update.download = await call<UpdateDownload>('update.download')
    } catch (e) {
      update.error = e instanceof Error ? e.message : String(e)
    } finally {
      window.clearInterval(poll)
      await readStatus()
    }
  },
  async openNotes() {
    try { await call('update.openNotes') } catch { /* the version is shown anyway */ }
  },
  setAuto(enabled: boolean) {
    update.autoCheck = enabled
    try {
      localStorage.setItem(AUTO_KEY, enabled ? '1' : '0')
    } catch {
      // No storage: the choice holds for this session only.
    }
  },
}
