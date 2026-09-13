<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, isHosted, onEvent } from './bridge'
import { currentLocale } from './i18n'
import { setCubeMeta } from './mdx-completion'
import type {
  AiAction, AiRunResult, CalculationDefinition, CellProvenance, CubeMeta,
  ExistingCalculation, FieldVisibility, FilterListResult, LevelVisibility,
  PivotContext, QueryRunResult, StoredCalculation,
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

const tab = ref<Tab>('tableau')
const context = ref<PivotContext | null>(null)
const meta = ref<CubeMeta | null>(null)
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

async function describeCell() {
  const result = await guard(busyProvenance, () =>
    call<CellProvenance>('cell.provenance'),
  )
  if (result) provenance.value = result
}

const aiConfigured = ref(false)
const aiAnswer = ref<string | null>(null)
const aiSeed = ref<string | null>(null)
const busyAi = ref(false)

/** From "Ce chiffre": switches to the AI tab with the expression pre-filled. */
function explainExpression(expression: string) {
  aiSeed.value = expression
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
watch(tab, async current => {
  if (!context.value?.isOlap) return

  if (current === 'tableau') {
    if (!fields.value.length) await loadFields()
  } else if (current === 'calculs') {
    if (!calculations.value.length) await loadCalculations()
    if (!library.value.length) await loadLibrary()
  } else if (current === 'provenance') {
    if (!provenance.value) await describeCell()
  }
})

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

async function loadContext() {
  const next = await guard(busyContext, () => call<PivotContext>('pivot.context'))
  if (!next) return
  // Cube change: the cached metadata is no longer worth anything.
  if (context.value?.cube !== next.cube) {
    meta.value = null
    metaAttempted.value = false
  }
  context.value = next
  void ensureMeta()
}

async function loadMeta() {
  metaAttempted.value = true
  const next = await guard(busyMeta, () => call<CubeMeta>('cube.meta'))
  if (next) meta.value = next
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

  metaAttempted.value = true
  try {
    meta.value = await call<CubeMeta>('cube.meta')
  } catch {
    // The user still has the "Charger" button to retry and see the error.
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
  if (next) fields.value = next
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
}

async function refreshNow() {
  await guard(busyComfort, () => call<{ refreshed: boolean }>('comfort.refreshNow'))
  autoRefresh.value = true
  if (levelField.value) await pickLevelField(levelField.value)
}

async function cancelQuery() {
  // Deliberately outside `guard`: cancelling must neither set the busy flag
  // nor clear the error banner of the running query.
  try {
    await call<{ cancelled: boolean }>('query.cancel')
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

function onPivotChanged(payload: Record<string, unknown>) {
  const full = payload.pivotChanged === true
  window.clearTimeout(followTimer)
  followTimer = window.setTimeout(() => {
    if (full) void loadContext()
    // Provenance follows the cell: reloading it only makes sense if the tab
    // is visible, otherwise we would query the server for nothing.
    else if (tab.value === 'provenance') void describeCell()
  }, 250)
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
  unsubscribeTab = onEvent('showTab', payload => {
    const target = payload.tab
    if (typeof target === 'string' && TABS.includes(target as Tab)) {
      tab.value = target as Tab
    }
  })

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
  <div v-if="error" class="banner">
    <span style="flex: 1">{{ error }}</span>
    <button :title="t('app.hide')" @click="error = null">×</button>
  </div>

  <!-- Permanent header: you always know what you are acting on, whichever
       tab is open. It replaces the former "Aperçu" tab, which took up
       a whole tab for three lines you want to see all the time. -->
  <PivotHeader :context="context" :busy="busyContext" @refresh="loadContext" />

  <nav class="tabs">
    <button :class="{ active: tab === 'tableau' }" @click="tab = 'tableau'">{{ t('tabs.table') }}</button>
    <button :class="{ active: tab === 'requete' }" @click="tab = 'requete'">{{ t('tabs.query') }}</button>
    <button :class="{ active: tab === 'calculs' }" @click="tab = 'calculs'">{{ t('tabs.calc') }}</button>
    <button :class="{ active: tab === 'provenance' }" @click="tab = 'provenance'">
      {{ t('tabs.provenance') }}
    </button>
    <button :class="{ active: tab === 'ia' }" @click="tab = 'ia'">{{ t('tabs.ai') }}</button>
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
        :busy="busyQuery"
        @run="runQuery"
        @cancel="cancelQuery"
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
        @cancel="cancelQuery"
      />
    </div>

  </main>
</template>
