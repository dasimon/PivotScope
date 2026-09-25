<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { describeDiagnostic } from '../diagnostic'
import type { CubeMeta, FilterListResult, PivotContext } from '../types'

const props = defineProps<{
  context: PivotContext | null
  meta: CubeMeta | null
  busy: boolean
}>()

const emit = defineEmits<{
  apply: [payload: { cubeField: string; level: string; keys: string }]
  loadMeta: []
}>()

const { t } = useI18n()

const cubeField = ref('')
const level = ref('')
const keys = ref('')
const result = ref<FilterListResult | null>(null)

// A level belongs to ONE field: kept across a field change, it would still
// enable "Apply" and send the members of the previous field to the new one.
watch(cubeField, () => {
  level.value = ''
  result.value = null
})

/** Only fields laid out on the PivotTable can be filtered. */
const filterableFields = computed(() =>
  (props.context?.fields ?? []).filter(f => f.area !== 'data'),
)

/** Levels of the selected hierarchy, according to the cube metadata. */
const levels = computed(() => {
  if (!props.meta || !cubeField.value) return []
  for (const dimension of props.meta.dimensions) {
    for (const hierarchy of dimension.hierarchies) {
      if (hierarchy.uniqueName === cubeField.value) return hierarchy.levels
    }
  }
  return []
})

/**
 * Same rule as MemberResolver.ParseKeys on the host: a multi-line paste is
 * split on lines and tabs only — "Actions, Europe" stays one caption —
 * while a single typed line is split on commas and semicolons.
 */
const keyCount = computed(() => {
  const separators = /[\r\n\t]/.test(keys.value) ? /[\r\n\t]+/ : /[;,]+/
  return keys.value.split(separators).map(k => k.trim()).filter(Boolean).length
})

const canApply = computed(
  () => !props.busy && cubeField.value !== '' && level.value !== '' && keyCount.value > 0,
)

function apply() {
  if (!canApply.value) return
  result.value = null
  emit('apply', { cubeField: cubeField.value, level: level.value, keys: keys.value })
}

defineExpose({
  setResult(value: FilterListResult) {
    result.value = value
  },
  /** Another PivotTable: its fields are not this one's. The pasted list stays. */
  reset() {
    cubeField.value = ''
    level.value = ''
    result.value = null
  },
})
</script>

<template>
  <div class="stack">
    <h2>{{ t('filter.title') }}</h2>

    <p v-if="!context?.isOlap" class="notice">
      {{ describeDiagnostic(context, t) }}
    </p>

    <template v-else>
      <label>
        {{ t('filter.field') }}
        <select v-model="cubeField">
          <option value="">{{ t('common.choose') }}</option>
          <option v-for="f in filterableFields" :key="f.uniqueName" :value="f.uniqueName">
            {{ f.caption }} ({{ t(`areas.${f.area}`) }})
          </option>
        </select>
      </label>

      <label>
        {{ t('filter.level') }}
        <select v-model="level" :disabled="levels.length === 0">
          <option value="">{{ t('common.choose') }}</option>
          <option v-for="l in levels" :key="l.uniqueName" :value="l.uniqueName">
            {{ l.name }}
          </option>
        </select>
      </label>

      <p v-if="cubeField && levels.length === 0" class="notice">
        {{ t('filter.loadMetaFirst') }}
        <button class="secondary" @click="$emit('loadMeta')">{{ t('common.load') }}</button>
      </p>

      <label>
        {{ t('filter.values', { count: keyCount }) }}
        <textarea v-model="keys" rows="8" :placeholder="t('filter.placeholder')" />
      </label>
      <p class="muted">{{ t('filter.hint') }}</p>

      <div class="row">
        <button :disabled="!canApply" @click="apply">
          {{ busy ? t('common.applying') : t('filter.action') }}
        </button>
      </div>

      <div v-if="result" class="stack" aria-live="polite">
        <p><strong>{{ t('filter.applied', { count: result.applied }) }}</strong></p>

        <p v-if="result.truncated" class="notice">{{ t('filter.truncated') }}</p>

        <template v-if="result.unresolved.length">
          <p class="muted">{{ t('filter.unresolved', { count: result.unresolved.length }) }}</p>
          <div class="chips">
            <span v-for="k in result.unresolved" :key="k" class="chip">{{ k }}</span>
          </div>
        </template>

        <template v-if="result.ambiguous.length">
          <p class="muted">{{ t('filter.ambiguous', { count: result.ambiguous.length }) }}</p>
          <div class="chips">
            <span v-for="k in result.ambiguous" :key="k" class="chip warn">{{ k }}</span>
          </div>
        </template>
      </div>
    </template>
  </div>
</template>
