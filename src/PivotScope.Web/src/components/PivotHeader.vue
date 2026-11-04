<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import AboutPanel from './AboutPanel.vue'
import ScopeMark from './ScopeMark.vue'
import { currentLocale, setLocale, type Locale } from '../i18n'
import { describeDiagnostic } from '../diagnostic'
import type { PivotContext } from '../types'

const props = defineProps<{ context: PivotContext | null; busy: boolean }>()
defineEmits<{ refresh: [] }>()

const { t } = useI18n()

/** Folded by default: it matters when you look for it, not while you work. */
const showAbout = ref(false)

/** A cube in reach: an OLAP PivotTable whose connection could be read. */
const connected = computed(
  () => !!props.context && props.context.hasPivot && props.context.isOlap && !props.context.diagnosticCode,
)

function onLocaleChange(event: Event) {
  setLocale((event.target as HTMLSelectElement).value as Locale)
}
</script>

<template>
  <!-- Permanent header: visible from every tab, so you always know
       what you are acting on. -->
  <header class="pivot-header">
    <div class="row">
      <span class="wordmark"><ScopeMark :state="connected ? 'cube' : 'off'" /><b>PivotScope</b></span>
      <span style="flex: 1" />

      <select
        class="locale"
        :value="currentLocale()"
        :title="t('app.language')"
        @change="onLocaleChange"
      >
        <option value="fr">FR</option>
        <option value="en">EN</option>
      </select>

      <button class="ghost" :disabled="busy" :title="t('common.refresh')" @click="$emit('refresh')">
        {{ busy ? t('common.reading') : t('common.refresh') }}
      </button>

      <button
        class="ghost"
        :title="t('about.title')"
        :aria-label="t('about.title')"
        :aria-expanded="showAbout"
        @click="showAbout = !showAbout"
      >
        ?
      </button>
    </div>

    <AboutPanel v-if="showAbout" />

    <p v-if="!context" class="muted">{{ t('header.readingContext') }}</p>

    <p v-else-if="!context.hasPivot || !context.isOlap" class="muted">
      {{ describeDiagnostic(context, t) }}
    </p>

    <!-- OLAP table whose connection could not be read: say it, rather than
         show dashes that look like an empty but valid state. -->
    <p v-else-if="context.diagnosticCode" class="notice warn">
      {{ describeDiagnostic(context, t) }}
    </p>

    <!-- The target: server › catalog on one line, the cube on the next. -->
    <div v-else class="target" :title="t('header.title')">
      <span class="target-line">
        <span class="mono">{{ context.server ?? '—' }}</span>
        <span class="target-sep">›</span>
        <span>{{ context.catalog ?? '—' }}</span>
      </span>
      <span class="target-line target-cube">
        <b>{{ context.cube ?? '—' }}</b>
        <span class="target-meta">{{ t('header.fieldsCount', { n: context.fields.length }) }}</span>
      </span>
    </div>
  </header>
</template>

<style scoped>
.pivot-header {
  padding: 8px 12px 10px;
  border-bottom: 1px solid var(--scope-line);
  background: var(--scope-bg-1);
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.wordmark {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 14.5px;
  letter-spacing: -0.01em;
  font-variation-settings:
    'CASL' 0.35,
    'wght' 560;
}
.wordmark b {
  font-weight: inherit;
}
.locale {
  width: auto;
  min-height: 26px;
  padding: 2px 4px;
  font-size: 12px;
}
.target {
  display: flex;
  flex-direction: column;
  border: 1px solid var(--scope-line-2);
  border-radius: 6px;
  background: var(--scope-bg);
  overflow: hidden;
  font-size: 12.5px;
}
.target-line {
  display: flex;
  align-items: center;
  gap: 6px;
  min-height: 26px;
  padding: 0 10px;
  overflow-wrap: anywhere;
}
.target-line + .target-line {
  border-top: 1px solid var(--scope-line);
}
.target-sep {
  color: var(--scope-text-3);
}
.target-cube b {
  font-weight: 560;
}
.target-meta {
  color: var(--scope-text-3);
  font-size: 11.5px;
}
</style>
