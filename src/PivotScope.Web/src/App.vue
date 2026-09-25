<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, isHosted, onEvent } from './bridge'
import { currentLocale } from './i18n'
import { setCubeMeta } from './mdx-completion'
import type {
  AiAction, AiRunResult, CalculationDefinition, CellProvenance, ConfirmWriteResult, CubeMeta,
  ExistingCalculation, FieldVisibility, FilterListResult, LevelVisibility,
  PivotContext, QueryRunResult, StoredCalculation, WriteMode,
} from './types'
import PivotHeader from './components/PivotHeader.vue'
import MdxView from './components/MdxView.vue'
import MetadataTree from './components/MetadataTree.vue'
import FilterList from './components/FilterList.vue'
import QueryPanel from './components/QueryPanel.vue'
import ComfortPanel from './components/ComfortPanel.vue'
import CalcPanel from './components/CalcPanel.vue'
import ProvenancePanel from './components/ProvenancePanel.vue'
import AiPanel from './components/AiPanel.vue'

/**
 * Five tabs, grouped by intent rather than by function. The previous eight
 * overflowed a 480 px pane: the last one could only be reached by guessing
 * that it existed.
 */
const TABS = ['tableau', 'requete', 'calculs', 'provenance', 'ia'] as const
type Tab = (typeof TABS)[number]

const { t } = useI18n()

const tabItems = computed<{ id: Tab; label: string }[]>(() => [
  { id: 'tableau', label: t('tabs.table') },
  { id: 'requete', label: t('tabs.query') },
  { id: 'calculs', label: t('tabs.calc') },
  { id: 'provenance', label: t('tabs.provenance') },
  { id: 'ia', label: t('tabs.ai') },
])

const tab = ref<Tab>('tableau')
const context = ref<PivotContext | null>(null)
/**
 * Last OLAP PivotTable seen. The free-form query and the completion keep
 * working from it after the cursor leaves the table — which is exactly what
 * choosing a destination cell requires.
 */
const lastOlap = ref<PivotContext | null>(null)
const meta = ref<CubeMeta | null>(null)
/** Cube the loaded metadata belongs to: a late answer for another cube is dropped. */
const metaCube = ref<string | null>(null)
const error = ref<string | null>(null)
const busyContext = ref(false)
const busyMeta = ref(false)
/** Avoids looping back on an automatic load that has just failed. */
const metaAttempted = ref(false)
const busyFilter = ref(false)
const busyQuery = ref(false)
const filterPanel = ref<InstanceType<typeof FilterList> | null>(null)
const queryPanel = ref<InstanceType<typeof QueryPanel> | null>(null)
const fields = ref<FieldVisibility[]>([])
const autoRefresh = ref(true)
const busyComfort = ref(false)
const levels = ref<LevelVisibility[]>([])
const levelField = ref('')

async function pickLevelField(cubeField: string) {
  levelField.value = cubeField
  levels.value = []
  if (!cubeField) return
  const result = await guard(busyComfort, () =>
    call<LevelVisibility[]>('comfort.levels', { cubeField }),
  )
  if (result) levels.value = result
}

async function setLevels(payload: { cubeField: string; levels: string[] }) {
  const result = await guard(busyComfort, () =>
    call<LevelVisibility[]>('comfort.setLevels', payload),
  )
  if (result) levels.value = result
}
const calculations = ref<ExistingCalculation[]>([])
const library = ref<StoredCalculation[]>([])
const busyCalc = ref(false)
const provenance = ref<CellProvenance | null>(null)
const busyProvenance = ref(false)

/**
 * The cell follows the cursor: several requests can be in flight at once,
 * and only the answer to the LAST one describes the cell under the cursor.
 */
let provenanceSequence = 0

async function describeCell() {
  const mine = ++provenanceSequence
  busyProvenance.value = true
  error.value = null
  try {
    const result = await call<CellProvenance>('cell.provenance')
    if (mine === provenanceSequence) provenance.value = result
  } catch (e) {
    if (mine === provenanceSequence) error.value = e instanceof Error ? e.message : String(e)
  } finally {
    if (mine === provenanceSequence) busyProvenance.value = false
  }
}

const aiConfigured = ref(false)
const aiAnswer = ref<string | null>(null)
/** A counter goes with the text: sending the same expression twice must still reach the panel. */
const aiSeed = ref<{ text: string; n: number } | null>(null)
const busyAi = ref(false)

/** From "Ce chiffre": switches to the AI tab with the expression pre-filled. */
function explainExpression(expression: string) {
  aiSeed.value = { text: expression, n: (aiSeed.value?.n ?? 0) + 1 }
  aiAnswer.value = null
  tab.value = 'ia'
}

async function runAi(payload: { action: AiAction; mdx: string }) {
  aiAnswer.value = null
  // The UI language is the one expected in the answer: otherwise you
  // read an English explanation in a French pane, or the other way round.
  const result = await guard(busyAi, () =>
    call<AiRunResult>('ai.run', { ...payload, lang: currentLocale() }),
  )
  if (result && !result.cancelled) aiAnswer.value = result.markdown
}

// MDX autocompletion follows the metadata of the current cube.
watch(meta, next => setCubeMeta(next))

/**
 * Each tab loads what it needs when it opens. Previously six "Charger" buttons
 * spread over four panels forced the user to guess that each section had to be
 * primed — an empty panel does not say it is waiting for a click.
 * The buttons remain, to reload and to see the error when something fails.
 */
watch(tab, current => loadTab(current))

async function loadTab(current: Tab) {
  if (!context.value?.isOlap) return

  if (current === 'tableau') {
    if (!fields.value.length) await loadFields()
  } else if (current === 'calculs') {
    if (!calculations.value.length) await loadCalculations()
    if (!library.value.length) await loadLibrary()
  } else if (current === 'provenance') {
    if (!provenance.value) await describeCell()
  }
}

/**
 * Another PivotTable: everything read from the previous one is dropped.
 * Kept, the fields, levels and calculations of table A would be shown — and
 * acted upon, by name — while the cursor is in table B.
 */
function resetPivotState() {
  fields.value = []
  levels.value = []
  levelField.value = ''
  calculations.value = []
  provenance.value = null
  filterPanel.value?.reset()
}

/** Every error surfaces in a banner. Never a dialog box. */
async function guard<T>(busy: { value: boolean }, work: () => Promise<T>): Promise<T | null> {
  busy.value = true
  error.value = null
  try {
    return await work()
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
    return null
  } finally {
    busy.value = false
  }
}

function currentCube(): string | null {
  return context.value?.cube ?? lastOlap.value?.cube ?? null
}

async function loadContext() {
  const next = await guard(busyContext, () => call<PivotContext>('pivot.context'))
  if (!next) return

  const previousKey = context.value?.pivotKey ?? null
  context.value = next
  if (next.isOlap && next.server) lastOlap.value = next

  // Leaving the table keeps what was read (coming back must not reload
  // everything); arriving in ANOTHER table drops it.
  const switched = next.pivotKey !== null && previousKey !== null && next.pivotKey !== previousKey
  if (switched) resetPivotState()

  // Another cube: the cached metadata is no longer worth anything. Outside
  // any table the cube is unknown, and the metadata stays: completion must
  // keep working while the user picks a destination cell.
  if (next.cube && next.cube !== metaCube.value) {
    meta.value = null
    metaCube.value = null
    metaAttempted.value = false
  }

  void ensureMeta()
  if (switched) void loadTab(tab.value)
}

async function loadMeta() {
  metaAttempted.value = true
  const cube = currentCube()
  const next = await guard(busyMeta, () => call<CubeMeta>('cube.meta'))
  if (next && currentCube() === cube) {
    meta.value = next
    metaCube.value = cube
  }
}

/**
 * Loads the metadata as soon as a cube is known, without the user having to
 * ask: without it MDX autocompletion answers "No suggestions", and
 * nobody will guess that they must first go through the Metadata tab.
 * Silent on failure — it is a convenience, not a requested action.
 */
async function ensureMeta() {
  if (meta.value || metaAttempted.value || busyMeta.value) return
  if (!context.value?.isOlap || !context.value.cube) return

  const cube = context.value.cube
  metaAttempted.value = true
  busyMeta.value = true
  try {
    const next = await call<CubeMeta>('cube.meta')
    // The cursor may have moved to another cube meanwhile: an answer for
    // the previous one would feed completion with the wrong cube.
    if (currentCube() === cube) {
      meta.value = next
      metaCube.value = cube
    }
  } catch {
    // The user still has the "Charger" button to retry and see the error.
  } finally {
    busyMeta.value = false
  }
}

async function runQuery(payload: {
  mdx: string
  newSheet: boolean
  includeHeaders: boolean
}) {
  const result = await guard(busyQuery, () =>
    call<QueryRunResult>('query.run', payload),
  )
  if (result) queryPanel.value?.setResult(result)
}

/** The user's answer when the destination was not empty. */
async function confirmWrite(mode: WriteMode) {
  const result = await guard(busyQuery, () =>
    call<ConfirmWriteResult>('query.confirmWrite', { mode }),
  )
  if (result) queryPanel.value?.setWritten(result)
}

type CalcDraft = {
  name: string
  expression: string
  kind: CalculationDefinition['kind']
  displayFolder: string
  numberFormat: string
  parentHierarchy: string
  solveOrder: number
}

async function loadCalculations() {
  const next = await guard(busyCalc, () => call<ExistingCalculation[]>('calc.list'))
  if (next) calculations.value = next
}

async function applyCalculation(payload: CalcDraft & { addToPivot: boolean }) {
  const next = await guard(busyCalc, () =>
    call<{ uniqueName: string; calculations: ExistingCalculation[] }>('calc.apply', payload),
  )
  if (next) calculations.value = next.calculations
}

async function removeCalculation(uniqueName: string) {
  const next = await guard(busyCalc, () =>
    call<ExistingCalculation[]>('calc.delete', { uniqueName }),
  )
  if (next) calculations.value = next
}

async function loadLibrary() {
  const next = await guard(busyCalc, () => call<StoredCalculation[]>('library.list'))
  if (next) library.value = next
}

async function saveToLibrary(payload: CalcDraft) {
  const next = await guard(busyCalc, () =>
    call<StoredCalculation[]>('library.save', payload),
  )
  if (next) library.value = next
}

async function removeFromLibrary(id: number) {
  const next = await guard(busyCalc, () =>
    call<StoredCalculation[]>('library.delete', { id }),
  )
  if (next) library.value = next
}

async function loadFields() {
  const next = await guard(busyComfort, () => call<FieldVisibility[]>('comfort.fields'))
  // On failure, stop here: the next call would clear the error banner
  // before anyone could read it.
  if (!next) return
  fields.value = next
  await refreshAutoRefresh()
}

async function toggleField(payload: { cubeField: string; visible: boolean }) {
  const next = await guard(busyComfort, () =>
    call<FieldVisibility[]>('comfort.setFieldVisibility', payload),
  )
  if (next) fields.value = next
}

async function showAllFields() {
  const next = await guard(busyComfort, () =>
    call<{ restored: number; fields: FieldVisibility[] }>('comfort.showAllFields'),
  )
  if (next) fields.value = next.fields
}

async function refreshAutoRefresh() {
  const next = await guard(busyComfort, () =>
    call<{ enabled: boolean }>('comfort.autoRefresh'),
  )
  if (next) autoRefresh.value = next.enabled
}

/**
 * `autoRefresh = false` means "deferred layout update". We go through
 * ManualUpdate and not EnableRefresh: the latter forbids refreshing,
 * Excel's button included, and leaves the user with no way to see their table.
 */
async function setAutoRefresh(enabled: boolean) {
  const next = await guard(busyComfort, () =>
    call<{ deferred: boolean }>('comfort.deferLayout', { deferred: !enabled }),
  )
  if (next) autoRefresh.value = !next.deferred
  // Failure: re-read the real state rather than trust the clicked checkbox.
  else await refreshAutoRefreshQuietly()
}

async function refreshAutoRefreshQuietly() {
  try {
    const next = await call<{ enabled: boolean }>('comfort.autoRefresh')
    autoRefresh.value = next.enabled
  } catch {
    // Keep the banner of the failed action.
  }
}

async function refreshNow() {
  const done = await guard(busyComfort, () => call<{ refreshed: boolean }>('comfort.refreshNow'))
  // Only a refresh that happened ends the deferred mode; on failure the
  // banner must stay readable, so nothing else is chained.
  if (!done) return
  autoRefresh.value = true
  if (levelField.value) await pickLevelField(levelField.value)
}

/**
 * Deliberately outside `guard`: cancelling must neither set the busy flag
 * nor clear the error banner of the running operation. One method per kind:
 * stopping the AI must not stop a query, and vice versa.
 */
async function cancel(method: 'query.cancel' | 'ai.cancel') {
  try {
    await call<{ cancelled: boolean }>(method)
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  }
}

async function applyFilter(payload: { cubeField: string; level: string; keys: string }) {
  const result = await guard(busyFilter, () =>
    call<FilterListResult>('pivot.filterList', payload),
  )
  if (!result) return
  filterPanel.value?.setResult(result)
  await loadContext()
}

/**
 * The pane follows the PivotTable instead of waiting for a click on "Actualiser".
 * Notifications arrive at the pace of cursor moves: we batch them,
 * otherwise simply moving across the table would trigger as many
 * round trips as cells crossed.
 */
let followTimer: number | undefined
/**
 * A table change seen during the batching window must survive the cell
 * moves that follow it: click into table B then press an arrow key within
 * 250 ms, and the last event alone says "same table".
 */
let pendingFull = false

function onPivotChanged(payload: Record<string, unknown>) {
  pendingFull ||= payload.pivotChanged === true
  window.clearTimeout(followTimer)
  followTimer = window.setTimeout(() => {
    const full = pendingFull
    pendingFull = false
    if (full) void loadContext()
    // Provenance follows the cell: reloading it only makes sense if the tab
    // is visible, otherwise we would query the server for nothing.
    else if (tab.value === 'provenance') void describeCell()
  }, 250)
}

function showTab(target: unknown) {
  if (typeof target === 'string' && TABS.includes(target as Tab)) tab.value = target as Tab
}

let unsubscribe: (() => void) | undefined
let unsubscribeTab: (() => void) | undefined

onMounted(() => {
  if (!isHosted) {
    error.value = t('app.notHosted')
    return
  }

  void loadContext()
  unsubscribe = onEvent('pivotChanged', onPivotChanged)

  // Excel's context menu asks for a specific tab: without this subscription,
  // "D'où vient ce chiffre ?" opened the pane without going to that tab.
  unsubscribeTab = onEvent('showTab', payload => showTab(payload.tab))

  // The same request may have been made before this page was ready to
  // listen (first opening of the pane): the host parked it.
  void call<{ tab: string | null }>('pane.takeTab')
    .then(r => showTab(r.tab))
    .catch(() => { /* not critical: the pane opens on its default tab */ })

  // The AI depends only on the environment: we query its status once,
  // so the panel degrades cleanly up front rather than on use.
  void call<{ configured: boolean }>('ai.status')
    .then(s => { aiConfigured.value = s.configured })
    .catch(() => { aiConfigured.value = false })
})

onBeforeUnmount(() => {
  window.clearTimeout(followTimer)
  unsubscribe?.()
  unsubscribeTab?.()
})
</script>

<template>
  <div v-if="error" class="banner" role="alert">
    <span style="flex: 1">{{ error }}</span>
    <button :title="t('app.hide')" :aria-label="t('app.hide')" @click="error = null">×</button>
  </div>

  <!-- Permanent header: you always know what you are acting on, whichever
       tab is open. It replaces the former "Aperçu" tab, which took up
       a whole tab for three lines you want to see all the time. -->
  <PivotHeader :context="context" :busy="busyContext" @refresh="loadContext" />

  <nav class="tabs" role="tablist">
    <button
      v-for="item in tabItems"
      :key="item.id"
      role="tab"
      :aria-selected="tab === item.id"
      :class="{ active: tab === item.id }"
      @click="tab = item.id"
    >
      {{ item.label }}
    </button>
  </nav>

  <main class="body">
    <!-- Everything that acts on the table itself, in the order you use it:
         see the query, filter, choose the levels, tune how it is built. -->
    <div v-show="tab === 'tableau'" class="stack">
      <MdxView :mdx="context?.mdx ?? null" />

      <FilterList
        ref="filterPanel"
        :context="context"
        :meta="meta"
        :busy="busyFilter"
        @apply="applyFilter"
        @load-meta="loadMeta"
      />

      <ComfortPanel
        :context="context"
        :fields="fields"
        :levels="levels"
        :level-field="levelField"
        :auto-refresh="autoRefresh"
        :busy="busyComfort"
        @pick-level-field="pickLevelField"
        @set-levels="setLevels"
        @refresh-now="refreshNow"
        @load="loadFields"
        @toggle-field="toggleField"
        @show-all="showAllFields"
        @set-auto-refresh="setAutoRefresh"
      />
    </div>

    <!-- The metadata explorer lives here, collapsed: you need it while writing
         MDX, not two tabs further along. -->
    <div v-show="tab === 'requete'" class="stack">
      <QueryPanel
        ref="queryPanel"
        :context="context"
        :connection="lastOlap"
        :busy="busyQuery"
        @run="runQuery"
        @confirm-write="confirmWrite"
        @cancel="cancel('query.cancel')"
      />

      <details>
        <summary>{{ t('metadata.title') }}</summary>
        <MetadataTree :meta="meta" :busy="busyMeta" @load="loadMeta" />
      </details>
    </div>

    <div v-show="tab === 'calculs'">
      <CalcPanel
        :context="context"
        :meta="meta"
        :calculations="calculations"
        :library="library"
        :busy="busyCalc"
        @load="loadCalculations"
        @apply="applyCalculation"
        @remove="removeCalculation"
        @save="saveToLibrary"
        @load-library="loadLibrary"
        @remove-from-library="removeFromLibrary"
      />
    </div>

    <div v-show="tab === 'provenance'">
      <ProvenancePanel
        :context="context"
        :provenance="provenance"
        :busy="busyProvenance"
        @describe="describeCell"
        @explain="explainExpression"
      />
    </div>

    <div v-show="tab === 'ia'">
      <AiPanel
        :context="context"
        :configured="aiConfigured"
        :answer="aiAnswer"
        :seed="aiSeed"
        :busy="busyAi"
        @run="runAi"
        @cancel="cancel('ai.cancel')"
      />
    </div>

  </main>
</template>
