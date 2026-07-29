<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import MdxEditor from './MdxEditor.vue'
import DependencyTree from './DependencyTree.vue'
import type { CellProvenance, PivotContext } from '../types'

defineProps<{
  context: PivotContext | null
  provenance: CellProvenance | null
  busy: boolean
}>()

const { t } = useI18n()

defineEmits<{
  describe: []
  explain: [expression: string]
}>()
</script>

<template>
  <div class="stack">
    <div class="row">
      <h2 style="flex: 1">{{ t('provenance.title') }}</h2>
      <button :disabled="busy" @click="$emit('describe')">
        {{ busy ? t('common.reading') : t('provenance.analyse') }}
      </button>
    </div>

    <p v-if="!context?.isOlap" class="notice">
      {{ context?.diagnostic ?? t('common.noPivot') }}
    </p>

    <p v-else-if="!provenance" class="notice">
      {{ t('provenance.intro') }}
    </p>

    <template v-else>
      <div class="field">
        <span class="field-label">{{ t('provenance.coordinates') }}</span>
        <pre>{{ provenance.tuple }}</pre>
      </div>

      <div v-if="provenance.measure" class="field">
        <span class="field-label">{{ t('provenance.measure') }}</span>
        <div>{{ provenance.measure }}</div>
      </div>

      <div v-if="provenance.coordinates.length" class="field">
        <span class="field-label">{{ t('provenance.context') }}</span>
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
            {{ t('provenance.expression') }}
            <template v-if="provenance.startLine">
              {{ t('provenance.atLine', { line: provenance.startLine }) }}
            </template>
          </span>
          <button class="secondary" @click="$emit('explain', provenance.expression)">
            {{ t('provenance.explainWithAi') }}
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
          <span class="field-label">{{ t('provenance.uses') }}</span>
          <ul class="tree">
            <DependencyTree :node="provenance.dependencies.root" />
          </ul>
        </div>

        <div v-if="provenance.dependencies.usedBy.length" class="field">
          <span class="field-label">
            {{ t('provenance.usedBy', { count: provenance.dependencies.usedBy.length }) }}
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
