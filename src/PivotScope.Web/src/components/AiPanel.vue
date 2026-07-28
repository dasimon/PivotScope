<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import MdxEditor from './MdxEditor.vue'
import { renderMarkdown } from '../markdown'
import type { AiAction, PivotContext } from '../types'

const props = defineProps<{
  context: PivotContext | null
  configured: boolean
  answer: string | null
  seed: string | null
  busy: boolean
}>()

const emit = defineEmits<{
  run: [payload: { action: AiAction; mdx: string }]
  cancel: []
}>()

const mdx = ref('')

// Le bouton « Expliquer avec l'IA » de l'onglet « Ce chiffre » dépose ici
// l'expression à analyser.
watch(
  () => props.seed,
  value => { if (value) mdx.value = value },
  { immediate: true },
)

const actions: { value: AiAction; label: string; hint: string }[] = [
  { value: 'Expliquer', label: 'Expliquer', hint: 'Que fait cette requête ?' },
  { value: 'Optimiser', label: 'Optimiser', hint: 'Comment la rendre plus rapide ?' },
  { value: 'AntiPatterns', label: 'Anti-patterns', hint: 'Quels pièges MDX s\'y trouvent ?' },
  { value: 'Formater', label: 'Formater', hint: 'La réécrire lisiblement.' },
]

const html = computed(() => (props.answer ? renderMarkdown(props.answer) : ''))
const canRun = computed(() => props.configured && !props.busy && mdx.value.trim() !== '')

function useTableQuery() {
  if (props.context?.mdx) mdx.value = props.context.mdx
}
</script>

<template>
  <div class="stack">
    <h2>Assistant MDX</h2>

    <p v-if="!configured" class="notice">
      L'assistant n'est pas configuré. Définissez la variable d'environnement
      <code>ANTHROPIC_API_KEY</code>, puis relancez Excel. La clé n'est jamais
      enregistrée par PivotScope.
    </p>

    <template v-else>
      <div class="row">
        <span class="field-label" style="flex: 1">
          MDX à analyser — le contexte du tableau est joint automatiquement
        </span>
        <button class="secondary" :disabled="!context?.mdx" @click="useTableQuery">
          Reprendre la requête du tableau
        </button>
      </div>

      <MdxEditor v-model="mdx" height="200px" />

      <div class="row" style="flex-wrap: wrap">
        <button
          v-for="a in actions"
          :key="a.value"
          class="secondary"
          :disabled="!canRun"
          :title="a.hint"
          @click="emit('run', { action: a.value, mdx })"
        >
          {{ a.label }}
        </button>
        <button v-if="busy" class="danger" @click="emit('cancel')">Arrêter</button>
      </div>

      <p v-if="busy" class="muted">Analyse en cours…</p>

      <!-- eslint-disable-next-line vue/no-v-html -->
      <div v-if="html" class="markdown" v-html="html" />
    </template>
  </div>
</template>

<style scoped>
.markdown :deep(pre) { overflow-x: auto; }
.markdown :deep(code) { font-family: Consolas, monospace; }
.markdown :deep(h3),
.markdown :deep(h4) { color: var(--accent); margin: 12px 0 4px; }
</style>
