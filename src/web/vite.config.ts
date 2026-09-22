/// <reference types="vitest/config" />
import path from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.resolve(import.meta.dirname, "./src"),
    },
  },
  test: {
    // jsdom rather than node: the auth store persists through localStorage, which node has not.
    environment: "jsdom",
    include: ["src/**/*.spec.ts"],
  },
  server: {
    port: 5173,
    // Proxying keeps the browser on one origin in development, so cookies and CORS
    // behave the same locally as they do behind a reverse proxy in production.
    proxy: {
      "/api": {
        target: "http://localhost:8080",
        changeOrigin: true,
      },
    },
  },

  // The built app, served the way `npm run preview` and the end-to-end tests use it. Without
  // this it has no API at all: only the dev server was ever proxied, so previewing a build
  // meant looking at a shell that could not sign anybody in.
  preview: {
    port: 4173,
    proxy: {
      "/api": {
        target: process.env.VITE_API_TARGET ?? "http://localhost:8080",
        changeOrigin: true,
      },
    },
  },
});
