import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Lets the dev server use the same relative "/api" calls as
    // production (where the API and built app share one origin/container)
    // instead of a hardcoded absolute URL + CORS.
    proxy: {
      '/api': 'http://localhost:5284',
    },
  },
})
