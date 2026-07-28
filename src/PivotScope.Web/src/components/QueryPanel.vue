<script setup lang="ts">
import { ref } from 'vue'
import MdxEditor from './MdxEditor.vue'
import type { PivotContext, QueryRunResult } from '../types'

const props = defineProps<{ context: PivotContext | null; busy: boolean }>()

const emit = defineEmits<{
  run: [payload: { mdx: string; newSheet: boolean; includeHeaders: boolean }]
  cancel: []
}>()

const mdx = ref('')
const newSheet = ref(true)
const includeHeaders = ref(true)
const result = ref<QueryRunResult | null>(null)

function run() {
  if (props.busy || mdx.value.trim() === '') return
  result.value = null
  emit('run', {
    mdx: mdx.value,
    newSheet: newSheet.value,
    includeHeaders: includeHeaders.value,
  })
}

function template() {
  const cube = props.context?.cube ?? 'Cube'
  mdx.value =
    `SELECT\n` +
    `  {[Measures].DefaultMember} ON COLUMNS\n` +
    `FROM [${cube}]`
}

defineExpose({
  setResult(value: QueryRunResult) {
    result.value = value
  },
})
</script>

<template>
  <div class="stack">
    <h2>Requête MDX</h2>

    <p v-if="!context?.isOlap" class="notice">
      {{ context?.diagnostic ?? 'Aucun tableau croisé dynamique OLAP actif.' }}
    </p>

    <template v-else>
      <MdxEditor v-model="mdx" height="240px" @run="run" />

      <div class="row">
        <label class="row" style="gap: 4px">
          <input type="checkbox" v-model="newSheet" style="width: auto" />
          Nouvelle feuille
        </label>
        <label class="row" style="gap: 4px">
          <input type="checkbox" v-model="includeHeaders" style="width: auto" />
          En-têtes
        </label>
      </div>

      <div class="row">
        <button :disabled="busy || !mdx.trim()" @click="run">
          {{ busy ? 'Exécution…' : 'Exécuter (F5)' }}
        </button>
        <!-- Arrêter appelle AdomdCommand.Cancel() : le serveur cesse
             réellement de calculer, on n'abandonne pas juste l'attente. -->
        <button v-if="busy" class="danger" @click="$emit('cancel')">Arrêter</button>
        <button v-else class="secondary" @click="template">Modèle</button>
      </div>

      <p v-if="!newSheet" class="muted">
        Le résultat sera écrit à partir de la cellule active. Une cellule
        appartenant à un tableau croisé dynamique est refusée.
      </p>

      <div v-if="result" class="stack">
        <p v-if="result.cancelled" class="muted">Requête arrêtée.</p>
        <template v-else>
          <p>
            <strong>{{ result.rows }}</strong> ligne(s) ×
            <strong>{{ result.columns }}</strong> colonne(s) écrites en
            {{ result.durationMs }} ms.
          </p>
          <p class="leaf">{{ result.address }}</p>
        </template>
      </div>
    </template>
  </div>
</template>
