<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { monaco } from '../monaco-mdx'
import { registerMdxCompletion } from '../mdx-completion'

const props = withDefaults(
  defineProps<{ modelValue: string; readonly?: boolean; height?: string }>(),
  { readonly: false, height: '220px' },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
  run: []
}>()

const host = ref<HTMLDivElement | null>(null)
let editor: monaco.editor.IStandaloneCodeEditor | null = null

onMounted(() => {
  if (!host.value) return
  registerMdxCompletion()

  editor = monaco.editor.create(host.value, {
    value: props.modelValue,
    language: 'mdx',
    theme: 'vs-dark',
    readOnly: props.readonly,
    automaticLayout: true,
    minimap: { enabled: false },
    scrollBeyondLastLine: false,
    fontSize: 12,
    fontFamily: 'Consolas, "Cascadia Mono", monospace',
    lineNumbers: 'on',
    wordWrap: 'on',
    tabSize: 2,
  })

  editor.onDidChangeModelContent(() => {
    const value = editor?.getValue() ?? ''
    if (value !== props.modelValue) emit('update:modelValue', value)
  })

  // F5 et Ctrl+Entrée : les deux raccourcis qu'un développeur SSAS a dans les
  // doigts, l'un venant de SSMS, l'autre de partout ailleurs.
  editor.addCommand(monaco.KeyCode.F5, () => emit('run'))
  editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, () => emit('run'))
})

watch(
  () => props.modelValue,
  value => {
    if (editor && editor.getValue() !== value) editor.setValue(value)
  },
)

onBeforeUnmount(() => {
  // Sans ça, chaque réouverture du volet laisse un éditeur derrière elle.
  editor?.dispose()
  editor = null
})
</script>

<template>
  <div ref="host" class="mdx-editor" :style="{ height }" />
</template>

<style scoped>
.mdx-editor {
  border: 1px solid var(--border);
  border-radius: 4px;
  overflow: hidden;
}
</style>
