import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  // Chemins relatifs : la SPA est servie depuis une origine virtuelle
  // (https://pivotscope.local/) alimentée par les ressources embarquées.
  base: './',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    // Un seul JS et un seul CSS : moins de ressources à embarquer et à servir.
    rollupOptions: {
      output: {
        entryFileNames: 'app.js',
        chunkFileNames: 'app-[hash].js',
        assetFileNames: 'app.[ext]',
      },
    },
  },
})
