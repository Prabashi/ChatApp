import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/chatHub': {
        target: 'http://localhost:5065',
        ws: true,
        changeOrigin: true,
      },
    },
  },
})
