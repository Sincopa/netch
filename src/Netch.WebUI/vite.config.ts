import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';

export default defineConfig({
  plugins: [svelte()],
  base: '/',
  build: {
    target: 'es2022',
    sourcemap: false,
    assetsInlineLimit: 4096
  },
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true
  }
});
