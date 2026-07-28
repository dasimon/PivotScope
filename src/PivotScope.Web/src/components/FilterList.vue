<script setup lang="ts">
import { computed, ref } from 'vue'
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
    <h2>Filtrer par une liste</h2>

    <p v-if="!context?.isOlap" class="notice">
      {{ context?.diagnostic ?? 'Aucun tableau croisé dynamique OLAP actif.' }}
    </p>

    <template v-else>
      <label>
        Champ du tableau croisé dynamique
        <select v-model="cubeField">
          <option value="">— choisir —</option>
          <option v-for="f in filterableFields" :key="f.uniqueName" :value="f.uniqueName">
            {{ f.caption }} ({{ f.area }})
          </option>
        </select>
      </label>

      <label>
        Niveau contenant les clés
        <select v-model="level" :disabled="levels.length === 0">
          <option value="">— choisir —</option>
          <option v-for="l in levels" :key="l.uniqueName" :value="l.uniqueName">
            {{ l.name }}
          </option>
        </select>
      </label>

      <p v-if="cubeField && levels.length === 0" class="notice">
        Chargez les métadonnées du cube pour lister les niveaux de ce champ.
        <button class="secondary" @click="$emit('loadMeta')">Charger</button>
      </p>

      <label>
        Valeurs à conserver ({{ keyCount }})
        <textarea
          v-model="keys"
          rows="8"
          placeholder="Une valeur par ligne — clé (PRD014) ou libellé (Aurore)"
        />
      </label>
      <p class="muted">
        Clés et libellés sont acceptés. La clé est essayée d'abord ; à défaut,
        le libellé est recherché parmi les membres du niveau choisi.
      </p>

      <div class="row">
        <button :disabled="!canApply" @click="apply">
          {{ busy ? 'Application…' : 'Appliquer le filtre' }}
        </button>
      </div>

      <div v-if="result" class="stack">
        <p>
          <strong>{{ result.applied }}</strong> membre(s) appliqué(s).
        </p>
        <template v-if="result.unresolved.length">
          <p class="muted">
            {{ result.unresolved.length }} valeur(s) introuvable(s) à ce niveau,
            ni comme clé ni comme libellé :
          </p>
          <div class="chips">
            <span v-for="k in result.unresolved" :key="k" class="chip">{{ k }}</span>
          </div>
        </template>

        <template v-if="result.ambiguous.length">
          <p class="muted">
            {{ result.ambiguous.length }} libellé(s) porté(s) par plusieurs
            membres — non appliqué(s), collez la clé pour lever le doute :
          </p>
          <div class="chips">
            <span v-for="k in result.ambiguous" :key="k" class="chip warn">{{ k }}</span>
          </div>
        </template>
      </div>
    </template>
  </div>
</template>
