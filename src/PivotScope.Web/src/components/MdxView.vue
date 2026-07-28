<script setup lang="ts">
import { ref } from 'vue'

const props = defineProps<{ mdx: string | null }>()
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
      <h2 style="flex: 1">MDX généré</h2>
      <button class="secondary" :disabled="!mdx" @click="copy">
        {{ copied ? 'Copié' : 'Copier' }}
      </button>
    </div>

    <!-- PivotTable.MDX lève quand le TCD n'a aucun élément de données :
         l'add-in renvoie alors null plutôt qu'une erreur. -->
    <p v-if="!mdx" class="notice">
      Aucune requête à afficher : déposez au moins une mesure dans le tableau
      croisé dynamique.
    </p>

    <pre v-else>{{ mdx }}</pre>
  </div>
</template>
