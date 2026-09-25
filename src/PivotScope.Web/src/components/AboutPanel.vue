<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { call } from '../bridge'
import type { AppInfo } from '../types'

const { t } = useI18n()

const info = ref<AppInfo | null>(null)

/**
 * The main bundled components. The full list, with the license texts, is
 * generated at packaging time (build/third-party-notices.ps1) into
 * THIRD-PARTY-NOTICES.txt, next to the .xll: this short list only says who
 * the pane owes what to.
 */
const components: { name: string; license: string }[] = [
  { name: 'Excel-DNA', license: 'Zlib' },
  { name: 'Microsoft Edge WebView2', license: 'Microsoft' },
  { name: 'ADOMD.NET, AMO', license: 'Microsoft' },
  { name: 'Microsoft.Data.Sqlite', license: 'MIT' },
  { name: 'SQLitePCLRaw', license: 'Apache-2.0' },
  { name: 'SDK Anthropic (.NET)', license: 'MIT' },
  { name: 'Monaco Editor', license: 'MIT' },
  { name: 'Vue, vue-i18n', license: 'MIT' },
  { name: 'DOMPurify', license: 'MPL-2.0 / Apache-2.0' },
  { name: 'marked', license: 'MIT' },
]

onMounted(async () => {
  try {
    info.value = await call<AppInfo>('app.info')
  } catch {
    // The rest of the section is still worth showing.
  }
})

async function openRepository() {
  try { await call('app.openRepository') } catch { /* the URL is shown anyway */ }
}
</script>

<template>
  <section class="about stack" :aria-label="t('about.title')">
    <p style="margin: 0">
      <strong>PivotScope {{ info?.version ?? '…' }}</strong>
      <span v-if="info?.commit" class="leaf"> ({{ info.commit }})</span>
    </p>
    <p class="muted" style="margin: 0">{{ t('about.tagline') }}</p>
    <p style="margin: 0">{{ t('about.license') }}</p>

    <div class="row wrap">
      <button class="secondary" @click="openRepository">{{ t('about.repository') }}</button>
      <span v-if="info" class="leaf">{{ info.repository }}</span>
    </div>

    <h2 style="margin: 4px 0 0">{{ t('about.thirdParty') }}</h2>
    <ul class="tree" style="padding-left: 0">
      <li v-for="c in components" :key="c.name" class="row">
        <span style="flex: 1">{{ c.name }}</span>
        <span class="leaf">{{ c.license }}</span>
      </li>
    </ul>
    <p class="muted" style="margin: 0">{{ t('about.notices') }}</p>
  </section>
</template>

<style scoped>
.about {
  padding: 8px 10px;
  background: var(--bg-raised);
  border: 1px solid var(--border);
  border-radius: 4px;
}
</style>
