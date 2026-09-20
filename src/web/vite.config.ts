/// <reference types="vitest/config" />
import path from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// Nothing in this repository serves dist/, so the page is the only place a policy can live;
// a <meta> is what the CSP spec provides for that. It is added at build rather than written in
// index.html because @vitejs/plugin-react injects an inline preamble script in development,
// which `script-src 'self'` would refuse. No frame-ancestors: browsers ignore it in a <meta>,
// so clickjacking protection belongs to a header from whatever ends up serving dist/.
const contentSecurityPolicy = [
  "default-src 'self'",
  "script-src 'self'",
  // CodeMirror (style-mod) and Sonner inject <style> elements at runtime and Base UI positions
  // through inline `style` attributes; without a nonce there is no narrower allowance.
  "style-src 'self' 'unsafe-inline'",
  // Vite inlines assets under build.assetsInlineLimit as data: URLs. Never for scripts.
  "img-src 'self' data:",
  "font-src 'self' data:",
  "connect-src 'self'",
  "worker-src 'self'",
  "base-uri 'self'",
  "form-action 'self'",
  "object-src 'none'",
].join("; ");

export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    {
      name: "csp",
      apply: "build",
      // The default, made explicit: the policy has to precede every script and style tag it
      // governs, and the charset declaration it pushes down stays well inside the first
      // 1024 bytes the HTML spec allows it.
      transformIndexHtml: () => [
        {
          tag: "meta",
          attrs: { "http-equiv": "Content-Security-Policy", content: contentSecurityPolicy },
          injectTo: "head-prepend",
        },
      ],
    },
  ],
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
