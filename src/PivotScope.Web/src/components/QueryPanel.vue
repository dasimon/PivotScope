<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import MdxEditor from './MdxEditor.vue'
import type { ConfirmWriteResult, PivotContext, QueryRunResult, WriteMode } from '../types'

const props = defineProps<{
  context: PivotContext | null
  /**
   * Last OLAP PivotTable seen. The panel works from it, not from the table
   * under the cursor: choosing a destination cell means leaving the table,
   * and the editor must not vanish at that very moment.
   */
  connection: PivotContext | null
  busy: boolean
}>()

const emit = defineEmits<{
  run: [payload: { mdx: string; newSheet: boolean; includeHeaders: boolean }]
  confirmWrite: [mode: WriteMode]
  cancel: []
}>()

const { t } = useI18n()

const mdx = ref('')
const newSheet = ref(true)
const includeHeaders = ref(true)
const result = ref<QueryRunResult | null>(null)
const discarded = ref(false)

/** Written or waiting for the user: what the last run produced. */
const pending = computed(() => result.value?.pendingOverwrite === true)

function run() {
  if (props.busy || pending.value || mdx.value.trim() === '') return
  result.value = null
  discarded.value = false
  emit('run', {
    mdx: mdx.value,
    newSheet: newSheet.value,
    includeHeaders: includeHeaders.value,
  })
}

function template() {
  const cube = props.connection?.cube ?? 'Cube'
  mdx.value =
    `SELECT\n` +
    `  {[Measures].DefaultMember} ON COLUMNS\n` +
    `FROM [${cube.replace(/]/g, ']]')}]`
}

defineExpose({
  setResult(value: QueryRunResult) {
    result.value = value
  },
  setWritten(value: ConfirmWriteResult) {
    if (!result.value) return
    discarded.value = !value.written
    result.value = { ...result.value, pendingOverwrite: false, address: value.address }
  },
})
</script>

<template>
  <div class="stack">
    <h2>{{ t('query.title') }}</h2>

    <p v-if="!connection" class="notice">
      {{ t('query.noConnection') }}
    </p>

    <template v-else>
      <!-- The server is shown on purpose: dev and prod can carry the same
           catalog name, and a query must never leave for the wrong one unseen. -->
      <p class="muted leaf">
        {{ t('query.target', { server: connection.server, catalog: connection.catalog }) }}
      </p>

      <MdxEditor v-model="mdx" height="240px" @run="run" />

      <div class="row wrap">
        <label class="row" style="gap: 4px">
          <input type="checkbox" v-model="newSheet" style="width: auto" />
          {{ t('query.newSheet') }}
        </label>
        <label class="row" style="gap: 4px">
          <input type="checkbox" v-model="includeHeaders" style="width: auto" />
          {{ t('query.headers') }}
        </label>
      </div>

      <div class="row">
        <button :disabled="busy || pending || !mdx.trim()" @click="run">
          {{ busy ? t('query.running') : t('query.run') }}
        </button>
        <!-- Stop calls AdomdCommand.Cancel(): the server really stops
             computing, we do not just give up waiting. -->
        <button v-if="busy" class="danger" @click="$emit('cancel')">{{ t('common.stop') }}</button>
        <button v-else class="secondary" @click="template">{{ t('query.template') }}</button>
      </div>

      <p v-if="!newSheet" class="muted">{{ t('query.activeCellHint') }}</p>

      <div v-if="result" class="stack" aria-live="polite">
        <p v-if="result.cancelled" class="muted">{{ t('query.cancelled') }}</p>

        <!-- The destination holds data: the result waits, nothing is written
             until the user chooses. A COM write cannot be undone. -->
        <div v-else-if="pending" class="notice stack">
          <p style="margin: 0">{{ t('query.overwrite', { address: result.address }) }}</p>
          <div class="row wrap">
            <button class="danger" :disabled="busy" @click="emit('confirmWrite', 'overwrite')">
              {{ t('query.overwriteConfirm') }}
            </button>
            <button :disabled="busy" @click="emit('confirmWrite', 'newSheet')">
              {{ t('query.writeNewSheet') }}
            </button>
            <button class="secondary" :disabled="busy" @click="emit('confirmWrite', 'discard')">
              {{ t('query.discard') }}
            </button>
          </div>
        </div>

        <p v-else-if="discarded" class="muted">{{ t('query.discarded') }}</p>

        <template v-else>
          <p>
            {{ t('query.written', {
              rows: result.rows,
              columns: result.columns,
              ms: result.durationMs,
            }) }}
          </p>
          <p class="leaf">{{ result.address }}</p>
        </template>
      </div>
    </template>
  </div>
</template>
