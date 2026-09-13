<script setup lang="ts">
// Recursive component: a dependency node and its children.
// CubeScope already cuts cycles at depth 8 on the server side, so we do not
// guard again here — but we show the kind of each node, which tells right
// away whether the trail goes on (calculated member) or stops (physical measure).
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
