import react from '@vitejs/plugin-react'
import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { defineConfig } from 'vite'

const certificate = resolve('../../../artifacts/certificates/https/tls.pem')
const privateKey = resolve('../../../artifacts/certificates/https/tls.key')

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    https: existsSync(certificate) && existsSync(privateKey)
      ? { cert: readFileSync(certificate), key: readFileSync(privateKey) }
      : undefined,
    headers: {
      'X-Content-Type-Options': 'nosniff',
      'Referrer-Policy': 'no-referrer',
      'Content-Security-Policy': "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; connect-src 'self' wss://localhost:5173 ws://localhost:5173; img-src 'self' data:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'",
    },
    proxy: {
      '/api': {
        target: process.env.API_PROXY_TARGET ?? 'https://localhost:7210',
        changeOrigin: false,
        secure: false, // The local .NET development certificate is self-signed.
        rewrite: (path) => path.replace(/^\/api(?=\/)/, ''),
      },
    },
  },
})
