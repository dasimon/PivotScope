<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
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

/** Seuls les champs posés sur le TCD peuvent être filtrés. */
const filterableFields = computed(() =>
  (props.context?.fields ?? []).filter(f => f.area !== 'data'),
)

/** Niveaux de la hiérarchie sélectionnée, d'après les métadonnées du cube. */
const levels = computed(() => {
  if (!props.meta || !cubeField.value) return []
  for (const dimension of props.meta.dimensions) {
    for (const hierarchy of dimension.hierarchies) {
      if (hierarchy.uniqueName === cubeField.value) return hierarchy.levels
    }
  }
  return []
})

const keyCount = computed(
  () =>
    keys.value
      .split(/[\r\n\t;,]+/)
      .map(k => k.trim())
      .filter(Boolean).length,
)

const canApply = computed(
  () => !props.busy && cubeField.value !== '' && level.value !== '' && keyCount.value > 0,
)

function apply() {
  result.value = null
  emit('apply', { cubeField: cubeField.value, level: level.value, keys: keys.value })
}

defineExpose({
  setResult(value: FilterListResult) {
    result.value = value
  },
})
</script>

<template>
  <div class="stack">
    <h2>{{ t('filter.title') }}</h2>

    <p v-if="!context?.isOlap" class="notice">
      {{ context?.diagnostic ?? t('common.noPivot') }}
    </p>

    <template v-else>
      <label>
        {{ t('filter.field') }}
        <select v-model="cubeField">
          <option value="">{{ t('common.choose') }}</option>
          <option v-for="f in filterableFields" :key="f.uniqueName" :value="f.uniqueName">
            {{ f.caption }} ({{ f.area }})
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

      <div v-if="result" class="stack">
        <p><strong>{{ t('filter.applied', { count: result.applied }) }}</strong></p>

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
