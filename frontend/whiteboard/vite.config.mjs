import { defineConfig } from 'vite';
import { fileURLToPath } from 'node:url';

export default defineConfig({
  base: '/vendor/whiteboard/',
  define: { 'process.env.IS_PREACT': 'false' },
  build: {
    cssCodeSplit: false,
    rolldownOptions: {
      preserveEntrySignatures: 'strict',
      input: {
        preview: fileURLToPath(new URL('./index.html', import.meta.url)),
        classroom: fileURLToPath(new URL('./src/board-adapter.tsx', import.meta.url)),
      },
      output: { entryFileNames: '[name].js', assetFileNames: asset => asset.names.some(name => name.endsWith('.css')) ? 'whiteboard.css' : 'assets/[name]-[hash][extname]' },
    },
    outDir: fileURLToPath(new URL('../../src/Lms.Poc/wwwroot/vendor/whiteboard/', import.meta.url)),
    emptyOutDir: true,
  },
});
