<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { currentLocale, setLocale, type Locale } from '../i18n'
import type { PivotContext } from '../types'

defineProps<{ context: PivotContext | null; busy: boolean }>()
defineEmits<{ refresh: [] }>()

const { t } = useI18n()

function onLocaleChange(event: Event) {
  setLocale((event.target as HTMLSelectElement).value as Locale)
}
</script>

<template>
  <!-- En-tête permanent : visible depuis tous les onglets, pour qu'on sache
       toujours sur quoi on agit. -->
  <header class="pivot-header">
    <div class="row">
      <strong style="flex: 1">{{ t('header.title') }}</strong>

      <select
        class="locale"
        :value="currentLocale()"
        :title="t('app.language')"
        @change="onLocaleChange"
      >
        <option value="fr">FR</option>
        <option value="en">EN</option>
      </select>

      <button class="secondary" :disabled="busy" @click="$emit('refresh')">
        {{ busy ? t('common.reading') : t('common.refresh') }}
      </button>
    </div>

    <p v-if="!context" class="muted">{{ t('header.readingContext') }}</p>

    <p v-else-if="!context.hasPivot || !context.isOlap" class="muted">
      {{ context.diagnostic }}
    </p>

    <dl v-else class="kv">
      <dt>{{ t('header.server') }}</dt><dd>{{ context.server ?? '—' }}</dd>
      <dt>{{ t('header.catalog') }}</dt><dd>{{ context.catalog ?? '—' }}</dd>
      <dt>{{ t('header.cube') }}</dt><dd>{{ context.cube ?? '—' }}</dd>
      <dt>{{ t('header.fields') }}</dt><dd>{{ context.fields.length }}</dd>
    </dl>
  </header>
</template>

<style scoped>
.pivot-header {
  padding: 8px 10px;
  border-bottom: 1px solid var(--border);
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.locale { width: auto; padding: 2px 4px; }
</style>
