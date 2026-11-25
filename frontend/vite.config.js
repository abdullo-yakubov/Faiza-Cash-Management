import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// В режиме разработки запросы /api и /health уходят на API (http://localhost:5080).
// Так браузер видит один адрес, и cookie входа работает без настроек CORS.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
      '/health': 'http://localhost:5080',
    },
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.js'],
  },
})
