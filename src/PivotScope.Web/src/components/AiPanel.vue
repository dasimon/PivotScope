<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import MdxEditor from './MdxEditor.vue'
import { codeBlocks, renderMarkdown } from '../markdown'
import type { AiAction, PivotContext } from '../types'

const props = defineProps<{
  context: PivotContext | null
  configured: boolean
  answer: string | null
  /** The counter makes the same expression sent twice still a new request. */
  seed: { text: string; n: number } | null
  busy: boolean
}>()

const emit = defineEmits<{
  run: [payload: { action: AiAction; mdx: string }]
  cancel: []
}>()

const { t } = useI18n()

const mdx = ref('')
/** What the editor held before an automatic replacement: nothing typed is lost. */
const previous = ref<string | null>(null)

/** Replaces the editor's text, keeping what it held if it was something else. */
function replaceWith(text: string) {
  const current = mdx.value.trim()
  previous.value = current !== '' && current !== text.trim() ? mdx.value : null
  mdx.value = text
}

// The "Expliquer avec l'IA" button of the "Ce chiffre" tab drops the
// expression to analyse here.
watch(
  () => props.seed,
  value => { if (value) replaceWith(value.text) },
  { immediate: true },
)

function restorePrevious() {
  if (previous.value === null) return
  const text = previous.value
  previous.value = null
  mdx.value = text
}

const actions = computed<{ value: AiAction; label: string; hint: string }[]>(() => [
  { value: 'Expliquer', label: t('ai.explain'), hint: t('ai.explainHint') },
  { value: 'Optimiser', label: t('ai.optimise'), hint: t('ai.optimiseHint') },
  { value: 'AntiPatterns', label: t('ai.antiPatterns'), hint: t('ai.antiPatternsHint') },
  { value: 'Formater', label: t('ai.format'), hint: t('ai.formatHint') },
])

const html = computed(() => (props.answer ? renderMarkdown(props.answer, t('ai.copyCode')) : ''))
const blocks = computed(() => (props.answer ? codeBlocks(props.answer) : []))
const canRun = computed(() => props.configured && !props.busy && mdx.value.trim() !== '')

function useTableQuery() {
  if (props.context?.mdx) replaceWith(props.context.mdx)
}

/**
 * The copy buttons live in the rendered HTML: one delegated listener reads
 * the block index and copies the RAW text of that block, never the escaped HTML.
 */
async function onAnswerClick(event: MouseEvent) {
  const button = (event.target as HTMLElement).closest<HTMLButtonElement>('button[data-code]')
  if (!button) return
  const text = blocks.value[Number(button.dataset.code)]
  if (text === undefined) return
  try {
    await navigator.clipboard.writeText(text)
    button.textContent = t('ai.copied')
    window.setTimeout(() => { button.textContent = t('ai.copyCode') }, 1500)
  } catch {
    // Clipboard refused: the text is still selectable by hand.
  }
}
</script>

<template>
  <div class="stack">
    <h2>{{ t('ai.title') }}</h2>

    <p v-if="!configured" class="notice">
      {{ t('ai.notConfigured') }}
    </p>

    <template v-else>
      <div class="row wrap">
        <span class="field-label" style="flex: 1">
          {{ t('ai.source') }}
        </span>
        <button class="secondary" :disabled="!context?.mdx" @click="useTableQuery">
          {{ t('ai.useTableQuery') }}
        </button>
      </div>

      <MdxEditor v-model="mdx" height="200px" />

      <div v-if="previous !== null" class="row">
        <button class="secondary" @click="restorePrevious">{{ t('ai.restorePrevious') }}</button>
      </div>

      <div class="row wrap">
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
        <button v-if="busy" class="danger" @click="emit('cancel')">{{ t('common.stop') }}</button>
      </div>

      <p v-if="busy" class="muted" aria-live="polite">{{ t('ai.running') }}</p>

      <!-- eslint-disable-next-line vue/no-v-html -->
      <div v-if="html" class="markdown" aria-live="polite" @click="onAnswerClick" v-html="html" />
    </template>
  </div>
</template>

<style scoped>
.markdown :deep(pre) { overflow-x: auto; }
.markdown :deep(code) { font-family: Consolas, monospace; }
.markdown :deep(h3),
.markdown :deep(h4) { color: var(--accent); margin: 12px 0 4px; }
.markdown :deep(.code) { position: relative; }
.markdown :deep(.copy) { position: absolute; top: 4px; right: 4px; padding: 1px 8px; }
.markdown :deep(.table) { overflow-x: auto; }
.markdown :deep(table) { border-collapse: collapse; }
.markdown :deep(th),
.markdown :deep(td) { border: 1px solid var(--border); padding: 3px 6px; text-align: left; }
</style>
