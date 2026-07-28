<script setup lang="ts">
import type { PivotContext } from '../types'

defineProps<{ context: PivotContext | null; busy: boolean }>()
defineEmits<{ refresh: [] }>()
</script>

<template>
  <div class="stack">
    <div class="row">
      <h2 style="flex: 1">Tableau croisé dynamique</h2>
      <button class="secondary" :disabled="busy" @click="$emit('refresh')">
        {{ busy ? 'Lecture…' : 'Actualiser' }}
      </button>
    </div>

    <p v-if="!context" class="notice">Lecture du contexte…</p>

    <p v-else-if="!context.hasPivot || !context.isOlap" class="notice">
      {{ context.diagnostic }}
    </p>

    <dl v-else class="kv">
      <dt>Serveur</dt><dd>{{ context.server ?? '—' }}</dd>
      <dt>Catalogue</dt><dd>{{ context.catalog ?? '—' }}</dd>
      <dt>Cube</dt><dd>{{ context.cube ?? '—' }}</dd>
      <dt>Champs</dt><dd>{{ context.fields.length }}</dd>
    </dl>
  </div>
</template>
