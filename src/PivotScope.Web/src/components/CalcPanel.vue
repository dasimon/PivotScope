<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import MdxEditor from './MdxEditor.vue'
import type {
  CalculationKind, CubeMeta, ExistingCalculation, PivotContext, StoredCalculation,
} from '../types'

const props = defineProps<{
  context: PivotContext | null
  meta: CubeMeta | null
  calculations: ExistingCalculation[]
  library: StoredCalculation[]
  busy: boolean
}>()

const emit = defineEmits<{
  load: []
  apply: [payload: Definition & { addToPivot: boolean }]
  remove: [uniqueName: string]
  save: [payload: Definition]
  loadLibrary: []
  removeFromLibrary: [id: number]
}>()

const { t } = useI18n()

type Definition = {
  name: string
  expression: string
  kind: CalculationKind
  displayFolder: string
  numberFormat: string
  parentHierarchy: string
  solveOrder: number
}

const draft = ref<Definition>({
  name: '',
  expression: '',
  kind: 'Measure',
  displayFolder: '',
  numberFormat: '',
  parentHierarchy: '',
  solveOrder: 0,
})

const addToPivot = ref(true)

const hierarchies = computed(() =>
  (props.meta?.dimensions ?? []).flatMap(d =>
    d.hierarchies.map(h => ({ label: `${d.name} — ${h.name}`, value: h.uniqueName })),
  ),
)

const isMeasure = computed(() => draft.value.kind === 'Measure')
const isMember = computed(() => draft.value.kind === 'Member')

const canApply = computed(
  () =>
    !props.busy &&
    draft.value.name.trim() !== '' &&
    draft.value.expression.trim() !== '' &&
    (!isMember.value || draft.value.parentHierarchy !== ''),
)

function apply() {
  emit('apply', { ...draft.value, addToPivot: addToPivot.value })
}

function load(stored: StoredCalculation) {
  draft.value = {
    name: stored.definition.name,
    expression: stored.definition.expression,
    kind: stored.definition.kind,
    displayFolder: stored.definition.displayFolder ?? '',
    numberFormat: stored.definition.numberFormat ?? '',
    parentHierarchy: stored.definition.parentHierarchy ?? '',
    solveOrder: stored.definition.solveOrder,
  }
}
</script>

<template>
  <div class="stack">
    <h2>{{ t('calc.title') }}</h2>

    <p v-if="!context?.isOlap" class="notice">
      {{ context?.diagnostic ?? t('common.noPivot') }}
    </p>

    <template v-else>
      <label>
        {{ t('calc.kind') }}
        <select v-model="draft.kind">
          <option value="Measure">{{ t('calc.kindMeasure') }}</option>
          <option value="Member">{{ t('calc.kindMember') }}</option>
          <option value="Set">{{ t('calc.kindSet') }}</option>
        </select>
      </label>

      <label>
        {{ t('calc.name') }}
        <input v-model="draft.name" :placeholder="t('calc.namePlaceholder')" />
      </label>

      <label v-if="isMember">
        {{ t('calc.parentHierarchy') }}
        <select v-model="draft.parentHierarchy">
          <option value="">{{ t('common.choose') }}</option>
          <option v-for="h in hierarchies" :key="h.value" :value="h.value">
            {{ h.label }}
          </option>
        </select>
      </label>

      <label v-if="isMeasure">
        {{ t('calc.displayFolder') }}
        <input v-model="draft.displayFolder" :placeholder="t('calc.displayFolderPlaceholder')" />
      </label>

      <template v-if="isMember">
        <label>
          {{ t('calc.numberFormat') }}
          <input v-model="draft.numberFormat" :placeholder="t('calc.numberFormatPlaceholder')" />
        </label>
        <p class="muted">{{ t('calc.numberFormatHint') }}</p>
      </template>

      <!-- Surtout pas de <label> autour de l'éditeur : un label intercepte les
           clics et redirige le focus vers son premier contrôle, ce qui empêche
           Monaco de le prendre. Constaté en recette. -->
      <div class="field">
        <span class="field-label">{{ t('calc.expression') }}</span>
        <MdxEditor v-model="draft.expression" height="180px" @run="apply" />
      </div>

      <label>
        {{ t('calc.solveOrder') }}
        <input v-model.number="draft.solveOrder" type="number" />
      </label>

      <label v-if="isMeasure" class="row" style="gap: 6px">
        <input type="checkbox" v-model="addToPivot" style="width: auto" />
        {{ t('calc.addToPivot') }}
      </label>

      <div class="row">
        <button :disabled="!canApply" @click="apply">
          {{ busy ? t('common.applying') : t('calc.create') }}
        </button>
        <button class="secondary" :disabled="!canApply" @click="$emit('save', draft)">
          {{ t('calc.saveToLibrary') }}
        </button>
      </div>

      <div class="row">
        <h2 style="flex: 1; margin: 0">{{ t('calc.onThisTable') }}</h2>
        <button class="secondary" :disabled="busy" @click="$emit('load')">
          {{ calculations.length ? t('common.reload') : t('common.load') }}
        </button>
      </div>

      <p v-if="!calculations.length" class="notice">{{ t('calc.none') }}</p>
      <ul v-else class="tree" style="padding-left: 0; list-style: none">
        <li v-for="c in calculations" :key="c.name">
          <div class="row">
            <span style="flex: 1">
              {{ c.name }}
              <span class="leaf">{{ c.kind }}{{ c.isValid ? '' : ' — ' + t('calc.invalid') }}</span>
            </span>
            <button class="danger" :disabled="busy" @click="$emit('remove', c.name)">
              {{ t('common.remove') }}
            </button>
          </div>
          <div class="leaf">{{ c.formula }}</div>
        </li>
      </ul>

      <div class="row">
        <h2 style="flex: 1; margin: 0">{{ t('calc.library') }}</h2>
        <button class="secondary" :disabled="busy" @click="$emit('loadLibrary')">
          {{ library.length ? t('common.reload') : t('common.load') }}
        </button>
      </div>

      <p v-if="!library.length" class="notice">{{ t('calc.libraryEmpty') }}</p>
      <ul v-else class="tree" style="padding-left: 0; list-style: none">
        <li v-for="s in library" :key="s.id">
          <div class="row">
            <span style="flex: 1">
              {{ s.definition.name }}
              <span class="leaf">{{ s.cube ?? t('calc.allCubes') }}</span>
            </span>
            <button class="secondary" :disabled="busy" @click="load(s)">{{ t('common.load') }}</button>
            <button class="danger" :disabled="busy" @click="$emit('removeFromLibrary', s.id)">
              {{ t('common.remove') }}
            </button>
          </div>
        </li>
      </ul>
    </template>
  </div>
</template>
