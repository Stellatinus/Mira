import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// In sviluppo: `npm run dev` (porta 5173) inoltra /api al backend C# sulla porta 5080.
// In produzione: `npm run build` scrive il frontend in wwwroot, servito dal backend stesso.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://127.0.0.1:5080', changeOrigin: true },
    },
  },
  build: {
    outDir: '../src/Mira.Server/wwwroot',
    emptyOutDir: true,
  },
});
