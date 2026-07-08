import { defineConfig } from "vite"
import react from "@vitejs/plugin-react"
import tailwindcss from "@tailwindcss/vite"

export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    outDir: "../src/Gateway/wwwroot",
    emptyOutDir: true,
  },
  server: {
    proxy: {
      "/api": "http://localhost:5000"
    }
  }
})
