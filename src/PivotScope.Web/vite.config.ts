import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  // Relative paths: the SPA is served from a virtual origin
  // (https://pivotscope.local/) backed by the embedded resources.
  base: './',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    // A single JS and a single CSS file: fewer resources to embed and serve.
    rollupOptions: {
      output: {
        entryFileNames: 'app.js',
        chunkFileNames: 'app-[hash].js',
        assetFileNames: 'app.[ext]',
      },
    },
  },
})
