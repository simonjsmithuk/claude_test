/**
 * vite.config.ts
 * ==============
 * Vite configuration for the DataViewer React application.
 *
 * Features:
 *  - React plugin for Fast Refresh
 *  - Proxy to backend API server (http://localhost:8080)
 *  - Development server on port 3000
 */

import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],

  server: {
    port: 3000,
    proxy: {
      // Proxy all /api requests to the backend API server
      '/api': {
        target: 'http://localhost:8080',
        changeOrigin: true,
        secure: false,
      },
    },
  },

  build: {
    outDir: 'build',
    sourcemap: true,
  },

  // Resolve paths
  resolve: {
    alias: {
      '@': '/src',
    },
  },
});
