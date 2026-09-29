import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    // Mismo origen para el navegador: sin CORS y la cookie httpOnly de refresh viaja sola.
    proxy: { '/api': 'http://localhost:5080' },
  },
})
