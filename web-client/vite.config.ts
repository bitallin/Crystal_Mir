import { createReadStream, existsSync, statSync } from "node:fs";
import { extname, resolve, sep } from "node:path";
import { defineConfig, type Plugin } from "vite";

const gateway = process.env.CRYSTAL_GATEWAY_URL ?? "http://127.0.0.1:5080";
const assetRoot = resolve(process.env.CRYSTAL_WEB_ASSET_ROOT ?? "../artifacts/web-assets");

const CONTENT_TYPES: Readonly<Record<string, string>> = {
  ".bin": "application/octet-stream",
  ".json": "application/json; charset=utf-8",
  ".mp3": "audio/mpeg",
  ".ogg": "audio/ogg",
  ".png": "image/png",
  ".svg": "image/svg+xml",
  ".wav": "audio/wav",
  ".webp": "image/webp"
};

function localWebAssets(): Plugin {
  return {
    name: "crystal-local-web-assets",
    apply: "serve",
    configureServer(server) {
      server.middlewares.use((request, response, next) => {
        const pathname = (request.url ?? "").split("?", 1)[0] ?? "";
        if (!pathname.startsWith("/assets/")) {
          next();
          return;
        }

        let relative: string;
        try {
          relative = decodeURIComponent(pathname.slice("/assets/".length));
        } catch {
          response.statusCode = 400;
          response.end("Invalid asset path.");
          return;
        }

        const candidate = resolve(assetRoot, relative);
        if (!candidate.startsWith(`${assetRoot}${sep}`) || !existsSync(candidate) || !statSync(candidate).isFile()) {
          response.statusCode = 404;
          response.setHeader("Content-Type", "text/plain; charset=utf-8");
          response.end(`Web asset not found: ${pathname}. Compile assets into ${assetRoot}.`);
          return;
        }

        response.setHeader("Content-Type", CONTENT_TYPES[extname(candidate).toLowerCase()] ?? "application/octet-stream");
        response.setHeader("Cache-Control", "no-cache");
        createReadStream(candidate).pipe(response);
      });
    }
  };
}

export default defineConfig({
  base: "./",
  plugins: [localWebAssets()],
  server: {
    host: "0.0.0.0",
    port: 5173,
    proxy: {
      "/api/web": { target: gateway, changeOrigin: true },
      "/ws": { target: gateway, ws: true, changeOrigin: true },
      "/health": { target: gateway, changeOrigin: true }
    }
  },
  build: {
    target: "es2022",
    sourcemap: true,
    assetsInlineLimit: 4096,
    chunkSizeWarningLimit: 1400,
    rollupOptions: {
      output: {
        manualChunks(id: string) {
          if (id.includes("pixi.js") || id.includes("@pixi")) return "pixi";
          if (id.includes("preact")) return "ui";
          return undefined;
        }
      }
    }
  }
});
