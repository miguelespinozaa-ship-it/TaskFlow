import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: Number(process.env.PORT ?? 5173),
    strictPort: true,
    // Mismo origen para el navegador: sin CORS y la cookie httpOnly de refresh viaja sola.
    // API_URL permite apuntar a otra instancia (p. ej. `API_URL=http://localhost:5081 npm run dev`).
    proxy: { '/api': process.env.API_URL ?? 'http://localhost:5080' },
  },
})
