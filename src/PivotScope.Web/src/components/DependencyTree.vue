<script setup lang="ts">
// Composant récursif : un nœud de dépendance et ses enfants.
// CubeScope coupe déjà les cycles à profondeur 8 côté serveur, on ne re-garde
// donc pas ici — mais on affiche la nature de chaque nœud, qui dit tout de
// suite si la piste continue (membre calculé) ou s'arrête (mesure physique).
import type { DependencyNode } from '../types'

defineProps<{ node: DependencyNode }>()

const kindLabel: Record<string, string> = {
  CalculatedMember: 'membre calculé',
  NamedSet: 'ensemble nommé',
  Measure: 'mesure',
  Hierarchy: 'hiérarchie',
  Inconnu: '?',
}
</script>

<template>
  <li>
    <details :open="node.dependencies.length > 0 && node.dependencies.length <= 6">
      <summary v-if="node.dependencies.length">
        {{ node.name }}
        <span class="leaf">{{ kindLabel[node.kind] ?? node.kind }}</span>
      </summary>
      <span v-else>
        {{ node.name }}
        <span class="leaf">{{ kindLabel[node.kind] ?? node.kind }}</span>
      </span>

      <ul v-if="node.dependencies.length" class="tree">
        <DependencyTree v-for="d in node.dependencies" :key="d.name" :node="d" />
      </ul>
    </details>
  </li>
</template>
