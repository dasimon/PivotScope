<script setup lang="ts">
import { computed, ref } from 'vue'
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
    <h2>Calculs du tableau</h2>

    <p v-if="!context?.isOlap" class="notice">
      {{ context?.diagnostic ?? 'Aucun tableau croisé dynamique OLAP actif.' }}
    </p>

    <template v-else>
      <label>
        Nature
        <select v-model="draft.kind">
          <option value="Measure">Mesure calculée</option>
          <option value="Member">Membre calculé</option>
          <option value="Set">Ensemble nommé</option>
        </select>
      </label>

      <label>
        Nom
        <input v-model="draft.name" placeholder="Marge nette" />
      </label>

      <label v-if="isMember">
        Hiérarchie parente
        <select v-model="draft.parentHierarchy">
          <option value="">— choisir —</option>
          <option v-for="h in hierarchies" :key="h.value" :value="h.value">
            {{ h.label }}
          </option>
        </select>
      </label>

      <label v-if="isMeasure">
        Dossier d'affichage
        <input v-model="draft.displayFolder" placeholder="Rentabilité (facultatif)" />
      </label>

      <template v-if="isMember">
        <label>
          Format de nombre
          <input v-model="draft.numberFormat" placeholder="#,##0.00 (facultatif)" />
        </label>
        <p class="muted">
          Excel ne propose aucune interface pour formater un membre calculé —
          seule une macro peut le faire. PivotScope le fait ici.
        </p>
      </template>

      <!-- Surtout pas de <label> autour de l'éditeur : un label intercepte les
           clics et redirige le focus vers son premier contrôle, ce qui empêche
           Monaco de le prendre. Constaté en recette. -->
      <div class="field">
        <span class="field-label">Expression MDX</span>
        <MdxEditor v-model="draft.expression" height="180px" @run="apply" />
      </div>

      <label>
        Ordre de résolution
        <input v-model.number="draft.solveOrder" type="number" />
      </label>

      <label v-if="isMeasure" class="row" style="gap: 6px">
        <input type="checkbox" v-model="addToPivot" style="width: auto" />
        Ajouter au tableau après création
      </label>

      <div class="row">
        <button :disabled="!canApply" @click="apply">
          {{ busy ? 'Application…' : 'Créer / remplacer' }}
        </button>
        <button class="secondary" :disabled="!canApply" @click="$emit('save', draft)">
          Enregistrer dans la bibliothèque
        </button>
      </div>

      <div class="row">
        <h2 style="flex: 1; margin: 0">Sur ce tableau</h2>
        <button class="secondary" :disabled="busy" @click="$emit('load')">
          {{ calculations.length ? 'Recharger' : 'Charger' }}
        </button>
      </div>

      <p v-if="!calculations.length" class="notice">Aucun calcul sur ce tableau.</p>
      <ul v-else class="tree" style="padding-left: 0; list-style: none">
        <li v-for="c in calculations" :key="c.name">
          <div class="row">
            <span style="flex: 1">
              {{ c.name }}
              <span class="leaf">{{ c.kind }}{{ c.isValid ? '' : ' — invalide' }}</span>
            </span>
            <button class="danger" :disabled="busy" @click="$emit('remove', c.name)">
              Supprimer
            </button>
          </div>
          <div class="leaf">{{ c.formula }}</div>
        </li>
      </ul>

      <div class="row">
        <h2 style="flex: 1; margin: 0">Bibliothèque</h2>
        <button class="secondary" :disabled="busy" @click="$emit('loadLibrary')">
          {{ library.length ? 'Recharger' : 'Charger' }}
        </button>
      </div>

      <p v-if="!library.length" class="notice">
        La bibliothèque est vide. Enregistrez un calcul pour le retrouver dans un
        autre classeur.
      </p>
      <ul v-else class="tree" style="padding-left: 0; list-style: none">
        <li v-for="s in library" :key="s.id">
          <div class="row">
            <span style="flex: 1">
              {{ s.definition.name }}
              <span class="leaf">{{ s.cube ?? 'tous cubes' }}</span>
            </span>
            <button class="secondary" :disabled="busy" @click="load(s)">Charger</button>
            <button class="danger" :disabled="busy" @click="$emit('removeFromLibrary', s.id)">
              Supprimer
            </button>
          </div>
        </li>
      </ul>
    </template>
  </div>
</template>
