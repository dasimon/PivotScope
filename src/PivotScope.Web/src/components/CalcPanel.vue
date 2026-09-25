<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import MdxEditor from './MdxEditor.vue'
import { describeDiagnostic } from '../diagnostic'
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

/** Unique name the draft will get, as the host computes it (CalculationValidator). */
const draftUniqueName = computed(() => {
  const name = draft.value.name.trim()
  if (draft.value.kind === 'Measure') return `[Measures].[${name}]`
  if (draft.value.kind === 'Set') return `[${name}]`
  return `${draft.value.parentHierarchy}.[${name}]`
})

/** Creating over an existing calculation replaces it: that deserves a confirmation. */
const replaces = computed(() =>
  props.calculations.some(c => c.name.toLowerCase() === draftUniqueName.value.toLowerCase()),
)

/**
 * Two-click confirmation for what cannot be undone: the first click arms the
 * button (its label asks "Confirm?"), the second acts. A dialog box would be
 * the other option, and the pane has none by design.
 */
const armed = ref<string | null>(null)
let disarmTimer: number | undefined

function confirmThen(key: string, action: () => void) {
  window.clearTimeout(disarmTimer)
  if (armed.value === key) {
    armed.value = null
    action()
    return
  }
  armed.value = key
  disarmTimer = window.setTimeout(() => { armed.value = null }, 4000)
}

onBeforeUnmount(() => window.clearTimeout(disarmTimer))

function apply() {
  // F5 in the editor lands here too: it must obey the same rules as the button.
  if (!canApply.value) return
  const send = () => emit('apply', { ...draft.value, addToPivot: addToPivot.value })
  if (replaces.value) confirmThen('apply', send)
  else send()
}

function load(stored: StoredCalculation) {
  draft.value = {
    name: stored.definition.name,
    expression: stored.definition.expression,
    kind: stored.definition.kind,
    displayFolder: stored.definition.displayFolder ?? '',
    numberFormat: normalizeFormat(stored.definition.numberFormat),
    parentHierarchy: stored.definition.parentHierarchy ?? '',
    solveOrder: stored.definition.solveOrder,
  }
}

/**
 * Earlier releases stored free text ("0.00%", "#,##0.00"). Excel only knows
 * three formats for a calculated member: read the old text by intent, as the
 * host does (CalculationNumberFormat).
 */
function normalizeFormat(format: string | null): string {
  if (!format || format === 'default') return ''
  if (format === 'number' || format === 'percent') return format
  return format.includes('%') ? 'percent' : 'number'
}

function kindLabel(kind: string): string {
  if (kind === 'Measure') return t('calc.kindMeasureShort')
  if (kind === 'Set') return t('calc.kindSetShort')
  return t('calc.kindMemberShort')
}
</script>

<template>
  <div class="stack">
    <h2>{{ t('calc.title') }}</h2>

    <p v-if="!context?.isOlap" class="notice">
      {{ describeDiagnostic(context, t) }}
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
        <!-- Excel's NumberFormat for a calculated member is an enumeration
             (default / number / percent), not a format string. -->
        <label>
          {{ t('calc.numberFormat') }}
          <select v-model="draft.numberFormat">
            <option value="">{{ t('calc.numberFormatDefault') }}</option>
            <option value="number">{{ t('calc.numberFormatNumber') }}</option>
            <option value="percent">{{ t('calc.numberFormatPercent') }}</option>
          </select>
        </label>
        <p class="muted">{{ t('calc.numberFormatHint') }}</p>
      </template>

      <!-- Above all, no <label> around the editor: a label intercepts
           clicks and redirects focus to its first control, which prevents
           Monaco from taking it. Observed during acceptance testing. -->
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

      <div class="row wrap">
        <button :class="{ danger: armed === 'apply' }" :disabled="!canApply" @click="apply">
          {{
            busy ? t('common.applying')
            : armed === 'apply' ? t('calc.replaceConfirm', { name: draft.name.trim() })
            : replaces ? t('calc.replace')
            : t('calc.create')
          }}
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
            <span style="flex: 1; min-width: 0" class="wrap-text">
              {{ c.name }}
              <span class="leaf">{{ kindLabel(c.kind) }}{{ c.isValid ? '' : ' — ' + t('calc.invalid') }}</span>
            </span>
            <button
              class="danger"
              :disabled="busy"
              @click="confirmThen(`calc:${c.name}`, () => emit('remove', c.name))"
            >
              {{ armed === `calc:${c.name}` ? t('calc.removeConfirm') : t('common.remove') }}
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
            <span style="flex: 1; min-width: 0" class="wrap-text">
              {{ s.definition.name }}
              <span class="leaf">{{ s.cube ?? t('calc.allCubes') }}</span>
            </span>
            <button class="secondary" :disabled="busy" @click="load(s)">{{ t('common.load') }}</button>
            <button
              class="danger"
              :disabled="busy"
              @click="confirmThen(`lib:${s.id}`, () => emit('removeFromLibrary', s.id))"
            >
              {{ armed === `lib:${s.id}` ? t('calc.removeConfirm') : t('common.remove') }}
            </button>
          </div>
        </li>
      </ul>
    </template>
  </div>
</template>
