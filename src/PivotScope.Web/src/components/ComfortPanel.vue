<script setup lang="ts">
import { computed, ref } from 'vue'
import type { FieldVisibility, PivotContext } from '../types'

const props = defineProps<{
  context: PivotContext | null
  fields: FieldVisibility[]
  autoRefresh: boolean
  busy: boolean
}>()

defineEmits<{
  load: []
  toggleField: [payload: { cubeField: string; visible: boolean }]
  showAll: []
  setAutoRefresh: [enabled: boolean]
}>()

const filter = ref('')

const shown = computed(() => {
  const needle = filter.value.trim().toLowerCase()
  return props.fields.filter(f => !needle || f.caption.toLowerCase().includes(needle))
})

const hiddenCount = computed(() => props.fields.filter(f => !f.shownInFieldList).length)
</script>

<template>
  <div class="stack">
    <h2>Construction du tableau</h2>

    <p v-if="!context?.isOlap" class="notice">
      {{ context?.diagnostic ?? 'Aucun tableau croisé dynamique OLAP actif.' }}
    </p>

    <template v-else>
      <label class="row" style="gap: 6px">
        <input
          type="checkbox"
          style="width: auto"
          :checked="autoRefresh"
          :disabled="busy"
          @change="$emit('setAutoRefresh', ($event.target as HTMLInputElement).checked)"
        />
        Rafraîchissement automatique
      </label>
      <p class="muted">
        Coupez-le pour déposer plusieurs champs sans attendre le serveur à chaque
        geste. L'état reste visible dans le ruban : on ne peut pas l'oublier et
        croire ensuite que le tableau est faux.
      </p>

      <div class="row">
        <h2 style="flex: 1; margin: 0">Champs de la liste</h2>
        <button class="secondary" :disabled="busy" @click="$emit('load')">
          {{ busy ? '…' : fields.length ? 'Recharger' : 'Charger' }}
        </button>
      </div>

      <p v-if="!fields.length" class="notice">
        Chargez les champs pour choisir ceux qui restent visibles dans la liste
        de champs du tableau croisé dynamique.
      </p>

      <template v-else>
        <input v-model="filter" placeholder="Filtrer les champs…" />

        <div class="row">
          <span class="muted" style="flex: 1">
            {{ hiddenCount }} masqué(s) sur {{ fields.length }}
          </span>
          <button
            class="secondary"
            :disabled="busy || hiddenCount === 0"
            @click="$emit('showAll')"
          >
            Tout réafficher
          </button>
        </div>

        <ul class="tree" style="padding-left: 0; list-style: none">
          <li v-for="f in shown" :key="f.name">
            <label class="row" style="gap: 6px">
              <input
                type="checkbox"
                style="width: auto"
                :checked="f.shownInFieldList"
                :disabled="busy"
                @change="$emit('toggleField', {
                  cubeField: f.name,
                  visible: ($event.target as HTMLInputElement).checked,
                })"
              />
              <span :class="{ muted: !f.shownInFieldList }">{{ f.caption }}</span>
              <span v-if="f.area" class="leaf">{{ f.area }}</span>
            </label>
          </li>
        </ul>

        <p class="muted">
          Un champ posé sur le tableau ne peut pas être masqué de la liste :
          retirez-le d'abord de la disposition.
        </p>
      </template>
    </template>
  </div>
</template>
