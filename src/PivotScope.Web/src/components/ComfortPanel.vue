<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { FieldVisibility, LevelVisibility, PivotContext } from '../types'

const props = defineProps<{
  context: PivotContext | null
  fields: FieldVisibility[]
  levels: LevelVisibility[]
  levelField: string
  autoRefresh: boolean
  busy: boolean
}>()

const emit = defineEmits<{
  load: []
  toggleField: [payload: { cubeField: string; visible: boolean }]
  showAll: []
  setAutoRefresh: [enabled: boolean]
  refreshNow: []
  pickLevelField: [cubeField: string]
  setLevels: [payload: { cubeField: string; levels: string[] }]
}>()

const { t } = useI18n()

/** Seules les hiérarchies posées sur le tableau ont des niveaux affichables. */
const laidOutFields = computed(() =>
  props.fields.filter(f => f.area === 'row' || f.area === 'column'),
)

/**
 * Sélection en attente. On n'applique PAS à chaque case : chaque application
 * reconstruit le tableau, et c'est ce qui rendait la fonction pénible. Comme
 * la boîte de dialogue de l'add-in d'origine, on coche librement puis on
 * applique une seule fois.
 */
const draft = ref<Set<string>>(new Set())

watch(
  () => props.levels,
  levels => { draft.value = new Set(levels.filter(l => l.shown).map(l => l.name)) },
  { immediate: true, deep: true },
)

function toggleLevel(name: string, shown: boolean) {
  const next = new Set(draft.value)
  if (shown) next.add(name)
  else next.delete(name)
  draft.value = next
}

const dirty = computed(() => {
  const applied = new Set(props.levels.filter(l => l.shown).map(l => l.name))
  if (applied.size !== draft.value.size) return true
  for (const name of draft.value) if (!applied.has(name)) return true
  return false
})

function applyLevels() {
  emit('setLevels', { cubeField: props.levelField, levels: [...draft.value] })
}

const filter = ref('')

const shown = computed(() => {
  const needle = filter.value.trim().toLowerCase()
  return props.fields.filter(f => !needle || f.caption.toLowerCase().includes(needle))
})

const hiddenCount = computed(() => props.fields.filter(f => !f.shownInFieldList).length)
</script>

<template>
  <div class="stack">
    <h2>{{ t('comfort.title') }}</h2>

    <p v-if="!context?.isOlap" class="notice">
      {{ context?.diagnostic ?? t('common.noPivot') }}
    </p>

    <template v-else>
      <label class="row" style="gap: 6px">
        <input
          type="checkbox"
          style="width: auto"
          :checked="!autoRefresh"
          :disabled="busy"
          @change="$emit('setAutoRefresh', !($event.target as HTMLInputElement).checked)"
        />
        {{ t('comfort.defer') }}
      </label>
      <p class="muted">
        {{ t('comfort.deferHint') }}
      </p>

      <div class="row">
        <button :disabled="busy" @click="emit('refreshNow')">
          {{ busy ? t('comfort.refreshing') : t('comfort.refreshNow') }}
        </button>
      </div>

      <h2>{{ t('comfort.levels') }}</h2>
      <p class="muted">
        {{ t('comfort.levelsHint') }}
      </p>

      <label>
        {{ t('comfort.hierarchy') }}
        <select
          :value="levelField"
          :disabled="busy || !laidOutFields.length"
          @change="emit('pickLevelField', ($event.target as HTMLSelectElement).value)"
        >
          <option value="">{{ t('common.choose') }}</option>
          <option v-for="f in laidOutFields" :key="f.name" :value="f.name">
            {{ f.caption }} ({{ f.area }})
          </option>
        </select>
      </label>

      <p v-if="!laidOutFields.length" class="notice">
        {{ t('comfort.noHierarchy') }}
      </p>

      <template v-else-if="levels.length">
        <ul class="tree" style="padding-left: 0; list-style: none">
          <li v-for="l in levels" :key="l.name">
            <label class="row" style="gap: 6px">
              <input
                type="checkbox"
                style="width: auto"
                :checked="draft.has(l.name)"
                :disabled="busy"
                @change="toggleLevel(l.name, ($event.target as HTMLInputElement).checked)"
              />
              <span :class="{ muted: !draft.has(l.name) }">{{ l.caption }}</span>
            </label>
          </li>
        </ul>

        <div class="row">
          <button :disabled="busy || !dirty || draft.size === 0" @click="applyLevels">
            {{ busy ? t('common.applying') : t('comfort.applyLevels') }}
          </button>
          <span v-if="draft.size === 0" class="muted">
            {{ t('comfort.keepOneLevel') }}
          </span>
          <span v-else-if="dirty" class="muted">{{ t('comfort.pendingChanges') }}</span>
        </div>
      </template>

      <div class="row">
        <h2 style="flex: 1; margin: 0">{{ t('comfort.fieldList') }}</h2>
        <button class="secondary" :disabled="busy" @click="$emit('load')">
          {{ busy ? t('common.loading') : fields.length ? t('common.reload') : t('common.load') }}
        </button>
      </div>

      <p v-if="!fields.length" class="notice">
        {{ t('comfort.fieldListEmpty') }}
      </p>

      <template v-else>
        <input v-model="filter" :placeholder="t('comfort.filterFields')" />

        <div class="row">
          <span class="muted" style="flex: 1">
            {{ t('comfort.hiddenCount', { hidden: hiddenCount, total: fields.length }) }}
          </span>
          <button
            class="secondary"
            :disabled="busy || hiddenCount === 0"
            @click="$emit('showAll')"
          >
            {{ t('comfort.showAll') }}
          </button>
        </div>

        <ul class="tree" style="padding-left: 0; list-style: none">
          <li v-for="f in shown" :key="f.name">
            <label class="row" style="gap: 6px">
              <input
                type="checkbox"
                style="width: auto"
                :checked="f.shownInFieldList"
                :disabled="busy"
                @change="$emit('toggleField', {
                  cubeField: f.name,
                  visible: ($event.target as HTMLInputElement).checked,
                })"
              />
              <span :class="{ muted: !f.shownInFieldList }">{{ f.caption }}</span>
              <span v-if="f.area" class="leaf">{{ f.area }}</span>
            </label>
          </li>
        </ul>

        <p class="muted">
          {{ t('comfort.laidOutHint') }}
        </p>
      </template>
    </template>
  </div>
</template>
