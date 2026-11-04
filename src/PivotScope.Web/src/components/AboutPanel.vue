<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { call } from '../bridge'
import type { AppInfo } from '../types'
import { update, updateActions } from '../update'

const { t, locale } = useI18n()

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

const busy = computed(() => !!update.status && update.status.phase !== 'Idle')
const percent = computed(() => Math.round((update.status?.progress ?? 0) * 100))
const lastCheck = computed(() =>
  update.status?.lastCheckUtc ? new Date(update.status.lastCheckUtc).toLocaleString(locale.value) : '',
)

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

    <h2 style="margin: 4px 0 0">{{ t('update.title') }}</h2>
    <template v-if="update.status && !update.status.canUpdate">
      <p class="muted">{{ t('update.devBuild') }}</p>
    </template>
    <template v-else-if="update.status">
      <p v-if="update.status.available" style="margin: 0">
        <strong>{{ t('update.available', { version: update.status.available.version }) }}</strong>
        <button class="ghost" @click="updateActions.openNotes()">{{ t('update.notes') }}</button>
      </p>
      <p v-else-if="update.status.lastCheckUtc && !update.status.lastError" class="muted">{{ t('update.upToDate') }}</p>
      <p v-if="update.status.lastCheckUtc" class="muted">{{ t('update.lastCheck', { when: lastCheck }) }}</p>

      <p v-if="update.status.phase === 'Downloading'" class="muted mono">
        {{ t('update.downloading', { percent }) }}
      </p>

      <p v-if="update.download" class="notice">
        {{ t('update.saved') }}
        <span class="leaf" style="display: block; margin-top: 4px">{{ update.download.path }}</span>
        <template v-if="update.download.addInFolder">
          {{ t('update.replaceIn') }}
          <span class="leaf" style="display: block; margin-top: 4px">{{ update.download.addInFolder }}</span>
        </template>
      </p>
      <p v-else-if="update.status.lastError" class="notice warn">
        {{ t('update.failed', { reason: update.status.lastError }) }}
      </p>
      <p v-if="update.error" class="notice warn">{{ update.error }}</p>

      <div class="row wrap">
        <button class="secondary" :disabled="busy" @click="updateActions.check()">
          {{ update.status.phase === 'Checking' ? t('update.checking') : t('update.checkNow') }}
        </button>
        <button v-if="update.status.available" :disabled="busy" @click="updateActions.download()">
          {{ t('update.download') }}
        </button>
      </div>
      <p v-if="update.status.available && !update.download" class="muted">{{ t('update.downloadHint') }}</p>

      <label class="row" style="gap: 6px">
        <input
          type="checkbox"
          :checked="update.autoCheck"
          @change="updateActions.setAuto(($event.target as HTMLInputElement).checked)"
        />
        {{ t('update.auto') }}
      </label>
    </template>

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
