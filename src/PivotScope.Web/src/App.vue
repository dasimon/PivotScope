<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { call, isHosted } from './bridge'
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

type Tab =
  | 'apercu' | 'requete' | 'calculs' | 'provenance' | 'ia'
  | 'metadonnees' | 'filtre' | 'construction'

const tab = ref<Tab>('apercu')
const context = ref<PivotContext | null>(null)
const meta = ref<CubeMeta | null>(null)
const error = ref<string | null>(null)
const busyContext = ref(false)
const busyMeta = ref(false)
/** Évite de reboucler sur un chargement automatique qui vient d'échouer. */
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

/** Depuis « Ce chiffre » : bascule sur l'IA avec l'expression pré-remplie. */
function explainExpression(expression: string) {
  aiSeed.value = expression
  aiAnswer.value = null
  tab.value = 'ia'
}

async function runAi(payload: { action: AiAction; mdx: string }) {
  aiAnswer.value = null
  const result = await guard(busyAi, () => call<AiRunResult>('ai.run', payload))
  if (result && !result.cancelled) aiAnswer.value = result.markdown
}

// L'autocomplétion MDX suit les métadonnées du cube courant.
watch(meta, next => setCubeMeta(next))

/** Toute erreur remonte dans un bandeau. Jamais de boîte de dialogue. */
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
  // Changement de cube : les métadonnées en cache ne valent plus rien.
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
 * Charge les métadonnées dès qu'un cube est connu, sans que l'utilisateur ait à
 * le demander : sans elles l'autocomplétion MDX répond « No suggestions », et
 * personne ne devinera qu'il faut d'abord passer par l'onglet Métadonnées.
 * Silencieux en cas d'échec — c'est un confort, pas une action demandée.
 */
async function ensureMeta() {
  if (meta.value || metaAttempted.value || busyMeta.value) return
  if (!context.value?.isOlap || !context.value.cube) return

  metaAttempted.value = true
  try {
    meta.value = await call<CubeMeta>('cube.meta')
  } catch {
    // L'utilisateur garde le bouton « Charger » pour réessayer et voir l'erreur.
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

async function setAutoRefresh(enabled: boolean) {
  const next = await guard(busyComfort, () =>
    call<{ enabled: boolean }>('comfort.autoRefresh', { enabled }),
  )
  if (next) autoRefresh.value = next.enabled
}

async function cancelQuery() {
  // Volontairement hors de `guard` : l'annulation ne doit ni poser le drapeau
  // occupé ni effacer le bandeau d'erreur de la requête en cours.
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

onMounted(() => {
  if (isHosted) {
    void loadContext()
    // L'IA ne dépend que de l'environnement : on interroge son état une fois,
    // pour que le panneau se dégrade proprement plutôt qu'à l'usage.
    void call<{ configured: boolean }>('ai.status')
      .then(s => { aiConfigured.value = s.configured })
      .catch(() => { aiConfigured.value = false })
  }
  else error.value = "Cette page doit être ouverte depuis le volet PivotScope d'Excel."
})
</script>

<template>
  <div v-if="error" class="banner">
    <span style="flex: 1">{{ error }}</span>
    <button @click="error = null" title="Masquer">×</button>
  </div>

  <nav class="tabs">
    <button :class="{ active: tab === 'apercu' }" @click="tab = 'apercu'">Aperçu</button>
    <button :class="{ active: tab === 'requete' }" @click="tab = 'requete'">Requête</button>
    <button :class="{ active: tab === 'calculs' }" @click="tab = 'calculs'">Calculs</button>
    <button :class="{ active: tab === 'provenance' }" @click="tab = 'provenance'">
      Ce chiffre
    </button>
    <button :class="{ active: tab === 'ia' }" @click="tab = 'ia'">IA</button>
    <button :class="{ active: tab === 'metadonnees' }" @click="tab = 'metadonnees'">
      Métadonnées
    </button>
    <button :class="{ active: tab === 'filtre' }" @click="tab = 'filtre'">Filtre</button>
    <button :class="{ active: tab === 'construction' }" @click="tab = 'construction'">
      Construction
    </button>
  </nav>

  <main class="body">
    <div v-show="tab === 'apercu'" class="stack">
      <PivotHeader :context="context" :busy="busyContext" @refresh="loadContext" />
      <MdxView :mdx="context?.mdx ?? null" />
    </div>

    <div v-show="tab === 'requete'">
      <QueryPanel
        ref="queryPanel"
        :context="context"
        :busy="busyQuery"
        @run="runQuery"
        @cancel="cancelQuery"
      />
    </div>

    <div v-show="tab === 'metadonnees'">
      <MetadataTree :meta="meta" :busy="busyMeta" @load="loadMeta" />
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

    <div v-show="tab === 'construction'">
      <ComfortPanel
        :context="context"
        :fields="fields"
        :levels="levels"
        :level-field="levelField"
        :auto-refresh="autoRefresh"
        :busy="busyComfort"
        @pick-level-field="pickLevelField"
        @set-levels="setLevels"
        @load="loadFields"
        @toggle-field="toggleField"
        @show-all="showAllFields"
        @set-auto-refresh="setAutoRefresh"
      />
    </div>

    <div v-show="tab === 'filtre'">
      <FilterList
        ref="filterPanel"
        :context="context"
        :meta="meta"
        :busy="busyFilter"
        @apply="applyFilter"
        @load-meta="loadMeta"
      />
    </div>
  </main>
</template>
