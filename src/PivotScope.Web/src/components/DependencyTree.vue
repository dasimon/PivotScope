<script setup lang="ts">
// Composant récursif : un nœud de dépendance et ses enfants.
// CubeScope coupe déjà les cycles à profondeur 8 côté serveur, on ne re-garde
// donc pas ici — mais on affiche la nature de chaque nœud, qui dit tout de
// suite si la piste continue (membre calculé) ou s'arrête (mesure physique).
import { useI18n } from 'vue-i18n'
import type { DependencyNode } from '../types'

defineProps<{ node: DependencyNode }>()

const { t } = useI18n()

const kindKeys: Record<string, string> = {
  CalculatedMember: 'provenance.kindCalculatedMember',
  NamedSet: 'provenance.kindNamedSet',
  Measure: 'provenance.kindMeasure',
  Hierarchy: 'provenance.kindHierarchy',
}

const kindLabel = (kind: string) => (kindKeys[kind] ? t(kindKeys[kind]) : kind)
</script>

<template>
  <li>
    <details :open="node.dependencies.length > 0 && node.dependencies.length <= 6">
      <summary v-if="node.dependencies.length">
        {{ node.name }}
        <span class="leaf">{{ kindLabel(node.kind) }}</span>
      </summary>
      <span v-else>
        {{ node.name }}
        <span class="leaf">{{ kindLabel(node.kind) }}</span>
      </span>

      <ul v-if="node.dependencies.length" class="tree">
        <DependencyTree v-for="d in node.dependencies" :key="d.name" :node="d" />
      </ul>
    </details>
  </li>
</template>
