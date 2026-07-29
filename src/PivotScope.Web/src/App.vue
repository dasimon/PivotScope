<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { call, isHosted, onEvent } from './bridge'
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
 * Cinq onglets, groupés par intention plutôt que par fonction. Les huit
 * précédents débordaient d'un volet de 480 px : le dernier n'était atteignable
 * qu'en devinant qu'il existait.
 */
type Tab = 'tableau' | 'requete' | 'calculs' | 'provenance' | 'ia'

const tab = ref<Tab>('tableau')
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

/**
 * Chaque onglet charge ce dont il a besoin en s'ouvrant. Auparavant six boutons
 * « Charger » répartis sur quatre panneaux obligeaient à deviner qu'il fallait
 * amorcer chaque section — un panneau vide ne dit pas qu'il attend un clic.
 * Les boutons subsistent pour recharger et pour voir l'erreur en cas d'échec.
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

/**
 * `autoRefresh = false` signifie « mise en page différée ». On passe par
 * ManualUpdate et non par EnableRefresh : ce dernier interdit l'actualisation,
 * bouton d'Excel compris, et laisse l'utilisateur sans moyen de voir son tableau.
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

/**
 * Le volet suit le TCD au lieu d'attendre un clic sur « Actualiser ».
 * Les notifications arrivent au rythme des déplacements de curseur : on les
 * regroupe, sinon un simple parcours du tableau déclencherait autant
 * d'allers-retours que de cellules traversées.
 */
let followTimer: number | undefined

function onPivotChanged(payload: Record<string, unknown>) {
  const full = payload.pivotChanged === true
  window.clearTimeout(followTimer)
  followTimer = window.setTimeout(() => {
    if (full) void loadContext()
    // La provenance suit la cellule : la recharger n'a de sens que si l'onglet
    // est visible, sinon on interrogerait le serveur pour rien.
    else if (tab.value === 'provenance') void describeCell()
  }, 250)
}

let unsubscribe: (() => void) | undefined

onMounted(() => {
  if (!isHosted) {
    error.value = "Cette page doit être ouverte depuis le volet PivotScope d'Excel."
    return
  }

  void loadContext()
  unsubscribe = onEvent('pivotChanged', onPivotChanged)

  // L'IA ne dépend que de l'environnement : on interroge son état une fois,
  // pour que le panneau se dégrade proprement plutôt qu'à l'usage.
  void call<{ configured: boolean }>('ai.status')
    .then(s => { aiConfigured.value = s.configured })
    .catch(() => { aiConfigured.value = false })
})

onBeforeUnmount(() => {
  window.clearTimeout(followTimer)
  unsubscribe?.()
})
</script>

<template>
  <div v-if="error" class="banner">
    <span style="flex: 1">{{ error }}</span>
    <button @click="error = null" title="Masquer">×</button>
  </div>

  <!-- En-tête permanent : on sait toujours sur quoi on agit, quel que soit
       l'onglet ouvert. Il remplace l'ancien onglet « Aperçu », qui occupait
       une place entière pour trois lignes qu'on veut voir tout le temps. -->
  <PivotHeader :context="context" :busy="busyContext" @refresh="loadContext" />

  <nav class="tabs">
    <button :class="{ active: tab === 'tableau' }" @click="tab = 'tableau'">Tableau</button>
    <button :class="{ active: tab === 'requete' }" @click="tab = 'requete'">Requête</button>
    <button :class="{ active: tab === 'calculs' }" @click="tab = 'calculs'">Calculs</button>
    <button :class="{ active: tab === 'provenance' }" @click="tab = 'provenance'">
      Ce chiffre
    </button>
    <button :class="{ active: tab === 'ia' }" @click="tab = 'ia'">IA</button>
  </nav>

  <main class="body">
    <!-- Tout ce qui agit sur le tableau lui-même, dans l'ordre où on s'en sert :
         voir la requête, filtrer, choisir les niveaux, régler la construction. -->
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

    <!-- L'explorateur de métadonnées vit ici, replié : c'est en écrivant du MDX
         qu'on en a besoin, pas deux onglets plus loin. -->
    <div v-show="tab === 'requete'" class="stack">
      <QueryPanel
        ref="queryPanel"
        :context="context"
        :busy="busyQuery"
        @run="runQuery"
        @cancel="cancelQuery"
      />

      <details>
        <summary>Métadonnées du cube</summary>
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
