<script setup lang="ts">
import { computed, ref } from 'vue'
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

/** Seules les hiérarchies posées sur le tableau ont des niveaux affichables. */
const laidOutFields = computed(() =>
  props.fields.filter(f => f.area === 'row' || f.area === 'column'),
)

function toggleLevel(name: string, shown: boolean) {
  const next = props.levels
    .filter(l => (l.name === name ? shown : l.shown))
    .map(l => l.name)
  emit('setLevels', { cubeField: props.levelField, levels: next })
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
    <h2>Construction du tableau</h2>

    <p v-if="!context?.isOlap" class="notice">
      {{ context?.diagnostic ?? 'Aucun tableau croisé dynamique OLAP actif.' }}
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
        Différer la mise en page
      </label>
      <p class="muted">
        Activez-le pour déposer plusieurs champs sans attendre le serveur à
        chaque geste, puis appliquez tout d'un coup. L'état reste visible dans le
        ruban : on ne peut pas l'oublier et croire ensuite que le tableau est faux.
      </p>

      <div class="row">
        <button :disabled="busy" @click="emit('refreshNow')">
          {{ busy ? 'Actualisation…' : 'Appliquer et actualiser' }}
        </button>
      </div>

      <h2>Niveaux affichés</h2>
      <p class="muted">
        Une hiérarchie à quatre ou cinq niveaux les impose tous. Cochez ceux que
        vous voulez voir : Excel n'offre nulle part ce choix.
      </p>

      <label>
        Hiérarchie posée sur le tableau
        <select
          :value="levelField"
          :disabled="busy || !laidOutFields.length"
          @change="emit('pickLevelField', ($event.target as HTMLSelectElement).value)"
        >
          <option value="">— choisir —</option>
          <option v-for="f in laidOutFields" :key="f.name" :value="f.name">
            {{ f.caption }} ({{ f.area }})
          </option>
        </select>
      </label>

      <p v-if="!laidOutFields.length" class="notice">
        Aucune hiérarchie en ligne ou en colonne. Chargez les champs, ou posez-en
        une sur le tableau.
      </p>

      <ul v-else-if="levels.length" class="tree" style="padding-left: 0; list-style: none">
        <li v-for="l in levels" :key="l.name">
          <label class="row" style="gap: 6px">
            <input
              type="checkbox"
              style="width: auto"
              :checked="l.shown"
              :disabled="busy"
              @change="toggleLevel(l.name, ($event.target as HTMLInputElement).checked)"
            />
            <span :class="{ muted: !l.shown }">{{ l.caption }}</span>
          </label>
        </li>
      </ul>

      <div class="row">
        <h2 style="flex: 1; margin: 0">Champs de la liste</h2>
        <button class="secondary" :disabled="busy" @click="$emit('load')">
          {{ busy ? '…' : fields.length ? 'Recharger' : 'Charger' }}
        </button>
      </div>

      <p v-if="!fields.length" class="notice">
        Chargez les champs pour choisir ceux qui restent visibles dans la liste
        de champs du tableau croisé dynamique.
      </p>

      <template v-else>
        <input v-model="filter" placeholder="Filtrer les champs…" />

        <div class="row">
          <span class="muted" style="flex: 1">
            {{ hiddenCount }} masqué(s) sur {{ fields.length }}
          </span>
          <button
            class="secondary"
            :disabled="busy || hiddenCount === 0"
            @click="$emit('showAll')"
          >
            Tout réafficher
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
          Un champ posé sur le tableau ne peut pas être masqué de la liste :
          retirez-le d'abord de la disposition.
        </p>
      </template>
    </template>
  </div>
</template>
