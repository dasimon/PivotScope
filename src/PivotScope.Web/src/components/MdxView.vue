<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ mdx: string | null }>()
const { t } = useI18n()
const copied = ref(false)

async function copy() {
  if (!props.mdx) return
  await navigator.clipboard.writeText(props.mdx)
  copied.value = true
  setTimeout(() => (copied.value = false), 1500)
}
</script>

<template>
  <div class="stack">
    <div class="row">
      <h2 style="flex: 1">{{ t('mdx.title') }}</h2>
      <button class="secondary" :disabled="!mdx" @click="copy">
        {{ copied ? t('common.copied') : t('common.copy') }}
      </button>
    </div>

    <!-- PivotTable.MDX lève quand le TCD n'a aucun élément de données :
         l'add-in renvoie alors null plutôt qu'une erreur. -->
    <p v-if="!mdx" class="notice">{{ t('mdx.empty') }}</p>

    <pre v-else>{{ mdx }}</pre>
  </div>
</template>
