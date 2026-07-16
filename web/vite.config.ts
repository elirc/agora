import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    // WHY: the storefront talks to the API through a same-origin /api path so there is no CORS
    // in dev and the client code is deploy-agnostic. Vite proxies it to the API's launch port.
    proxy: {
      '/api': 'http://localhost:5158',
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
  },
});
