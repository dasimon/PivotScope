<script setup lang="ts">
import { computed, ref } from 'vue'
import type { CubeMeta } from '../types'

const props = defineProps<{ meta: CubeMeta | null; busy: boolean }>()
defineEmits<{ load: [] }>()

const filter = ref('')

const matches = (text: string) =>
  text.toLowerCase().includes(filter.value.trim().toLowerCase())

const dimensions = computed(() =>
  (props.meta?.dimensions ?? [])
    .map(d => ({
      ...d,
      hierarchies: d.hierarchies.filter(
        h => !filter.value || matches(h.name) || matches(d.name),
      ),
    }))
    .filter(d => d.hierarchies.length > 0),
)

const folders = computed(() =>
  (props.meta?.measureFolders ?? [])
    .map(f => ({
      ...f,
      measures: f.measures.filter(m => !filter.value || matches(m.name)),
    }))
    .filter(f => f.measures.length > 0),
)
</script>

<template>
  <div class="stack">
    <div class="row">
      <h2 style="flex: 1">Métadonnées du cube</h2>
      <button class="secondary" :disabled="busy" @click="$emit('load')">
        {{ busy ? 'Chargement…' : meta ? 'Recharger' : 'Charger' }}
      </button>
    </div>

    <p v-if="!meta" class="notice">
      Les métadonnées ne sont pas chargées. Elles nécessitent une connexion au
      cube, qui n'est ouverte qu'à la demande.
    </p>

    <template v-else>
      <input v-model="filter" placeholder="Filtrer dimensions et mesures…" />

      <details open>
        <summary>Mesures ({{ folders.length }} dossiers)</summary>
        <ul class="tree">
          <li v-for="f in folders" :key="f.folder">
            <details>
              <summary>{{ f.folder || '(racine)' }} — {{ f.measures.length }}</summary>
              <ul class="tree">
                <li v-for="m in f.measures" :key="m.uniqueName">
                  {{ m.name }}
                  <div class="leaf">{{ m.uniqueName }}</div>
                </li>
              </ul>
            </details>
          </li>
        </ul>
      </details>

      <details open>
        <summary>Dimensions ({{ dimensions.length }})</summary>
        <ul class="tree">
          <li v-for="d in dimensions" :key="d.uniqueName">
            <details>
              <summary>{{ d.name }}</summary>
              <ul class="tree">
                <li v-for="h in d.hierarchies" :key="h.uniqueName">
                  <details>
                    <summary>{{ h.name }}</summary>
                    <ul class="tree">
                      <li v-for="l in h.levels" :key="l.uniqueName">
                        {{ l.name }}
                        <div class="leaf">{{ l.uniqueName }}</div>
                      </li>
                    </ul>
                  </details>
                </li>
              </ul>
            </details>
          </li>
        </ul>
      </details>
    </template>
  </div>
</template>
