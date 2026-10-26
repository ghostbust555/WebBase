import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// The development web server. It serves the website with hot reload (save a
// file and the browser updates) and forwards every /api request to the C#
// API, so the browser only ever talks to one address - exactly as in prod,
// where the API serves the built website itself.
//
// API_URL is set by compose.dev.yaml (http://api:8080 inside Docker). Running
// `npm run dev` straight on your machine, it falls back to localhost:5000.
const apiUrl = process.env.API_URL || 'http://localhost:5000'

export default defineConfig({
  plugins: [vue()],
  server: {
    host: true,
    port: 5173,
    strictPort: true,
    // Any hostname may reach the dev server: localhost, your machine's name
    // on the LAN, a phone on the same Wi-Fi.
    allowedHosts: true,
    proxy: {
      '/api': { target: apiUrl, changeOrigin: true },
      // The CMS's pictures and files (content/...). A pattern, so a page
      // called /contents is not caught by it.
      '^/content/': { target: apiUrl, changeOrigin: true },
      '/scalar': { target: apiUrl, changeOrigin: true },
      '/openapi': { target: apiUrl, changeOrigin: true }
    },
    // File changes inside Docker on macOS/Windows aren't always noticed
    // without polling. compose.dev.yaml turns it on.
    watch: process.env.VITE_POLL ? { usePolling: true, interval: 300 } : undefined,
    // The guides in ../docs (and ../README.md) are built into the site.
    fs: { allow: ['..'] }
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true
  }
})
