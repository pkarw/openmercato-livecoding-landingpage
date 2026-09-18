import path from 'node:path'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Production builds land in the ASP.NET Core wwwroot, so Kestrel serves the app
// and the API from one origin on a single port.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(import.meta.dirname, './src') },
  },
  build: {
    outDir: '../server/wwwroot',
    emptyOutDir: true,
  },
  server: {
    host: '0.0.0.0',
    port: 5173,
    // Only used by `npm run dev`; the API still comes from the .NET app on 3000.
    proxy: { '/api': 'http://127.0.0.1:3000' },
  },
})
