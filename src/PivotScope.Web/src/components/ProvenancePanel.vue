<script setup lang="ts">
import MdxEditor from './MdxEditor.vue'
import DependencyTree from './DependencyTree.vue'
import type { CellProvenance, PivotContext } from '../types'

defineProps<{
  context: PivotContext | null
  provenance: CellProvenance | null
  busy: boolean
}>()

defineEmits<{
  describe: []
  explain: [expression: string]
}>()
</script>

<template>
  <div class="stack">
    <div class="row">
      <h2 style="flex: 1">D'où vient ce chiffre ?</h2>
      <button :disabled="busy" @click="$emit('describe')">
        {{ busy ? 'Lecture…' : 'Analyser la cellule' }}
      </button>
    </div>

    <p v-if="!context?.isOlap" class="notice">
      {{ context?.diagnostic ?? 'Aucun tableau croisé dynamique OLAP actif.' }}
    </p>

    <p v-else-if="!provenance" class="notice">
      Placez le curseur sur une <strong>cellule de valeur</strong> du tableau,
      puis lancez l'analyse. Excel donnera ses coordonnées complètes — filtres
      de rapport compris — et PivotScope remontera jusqu'à l'expression qui la
      produit.
    </p>

    <template v-else>
      <div class="field">
        <span class="field-label">Coordonnées complètes de la cellule</span>
        <pre>{{ provenance.tuple }}</pre>
      </div>

      <div v-if="provenance.measure" class="field">
        <span class="field-label">Mesure</span>
        <div>{{ provenance.measure }}</div>
      </div>

      <div v-if="provenance.coordinates.length" class="field">
        <span class="field-label">Contexte</span>
        <ul class="tree">
          <li v-for="c in provenance.coordinates" :key="c" class="leaf">{{ c }}</li>
        </ul>
      </div>

      <!-- Une mesure physique ou un script illisible ne sont pas des erreurs :
           gris, pas rouge. -->
      <p v-if="provenance.note" class="notice">{{ provenance.note }}</p>

      <template v-if="provenance.expression">
        <div class="row">
          <span class="field-label" style="flex: 1">
            Expression
            <template v-if="provenance.startLine">
              — ligne {{ provenance.startLine }} du script du cube
            </template>
          </span>
          <button class="secondary" @click="$emit('explain', provenance.expression)">
            Expliquer avec l'IA
          </button>
        </div>
        <MdxEditor
          :model-value="provenance.expression"
          readonly
          height="160px"
          @update:model-value="() => {}"
        />
      </template>

      <template v-if="provenance.dependencies">
        <div class="field">
          <span class="field-label">Ce que ce calcul utilise</span>
          <ul class="tree">
            <DependencyTree :node="provenance.dependencies.root" />
          </ul>
        </div>

        <div v-if="provenance.dependencies.usedBy.length" class="field">
          <span class="field-label">
            Utilisé par {{ provenance.dependencies.usedBy.length }} autre(s) calcul(s)
          </span>
          <div class="chips">
            <span v-for="u in provenance.dependencies.usedBy" :key="u" class="chip">
              {{ u }}
            </span>
          </div>
        </div>
      </template>
    </template>
  </div>
</template>
