import { beforeEach, afterEach, describe, expect, it, vi } from "vitest";
import { AssetStore, type WebAssetManifest } from "./assetStore";

const BASE = "http://localhost";
const MANIFEST_URL = "/assets/dev/manifest.json";

function writeUint32(target: Uint8Array, offset: number, value: number): void {
  target[offset] = value & 0xff;
  target[offset + 1] = (value >>> 8) & 0xff;
  target[offset + 2] = (value >>> 16) & 0xff;
  target[offset + 3] = (value >>> 24) & 0xff;
}

async function hashFiles(files: Array<{ name: string; bytes: Uint8Array }>): Promise<string> {
  const chunks: Uint8Array[] = [];
  const encoder = new TextEncoder();
  for (const file of [...files].sort((left, right) => (left.name < right.name ? -1 : left.name > right.name ? 1 : 0))) {
    const name = encoder.encode(file.name);
    const header = new Uint8Array(8);
    writeUint32(header, 0, name.byteLength);
    writeUint32(header, 4, file.bytes.byteLength);
    chunks.push(header.subarray(0, 4), name, header.subarray(4), file.bytes);
  }
  const merged = new Uint8Array(chunks.reduce((sum, chunk) => sum + chunk.byteLength, 0));
  let offset = 0;
  for (const chunk of chunks) {
    merged.set(chunk, offset);
    offset += chunk.byteLength;
  }
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", merged));
  return `sha256:${Array.from(digest, value => value.toString(16).padStart(2, "0")).join("")}`;
}

function response(bytes: Uint8Array): Response {
  const copy = new Uint8Array(bytes.byteLength);
  copy.set(bytes);
  return new Response(copy.buffer);
}

function installFetch(files: Record<string, Uint8Array>): void {
  vi.stubGlobal("fetch", vi.fn(async (input: string | URL | Request) => {
    const path = new URL(String(input)).pathname.replace(/^\/assets\/dev\//, "");
    const bytes = files[path];
    return bytes ? response(bytes) : new Response(null, { status: 404 });
  }));
}

describe("AssetStore integrity", () => {
  beforeEach(() => {
    vi.stubGlobal("window", { location: { origin: BASE } });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("verifies library frames and page hashes before exposing blob URLs", async () => {
    const framesBytes = new TextEncoder().encode(JSON.stringify({
      version: 3,
      pages: ["atlas-0.png"],
      maskPages: [],
      images: [{ index: 0, page: 0, x: 0, y: 0, width: 1, height: 1 }],
      frames: {},
    }));
    const pageBytes = new Uint8Array([137, 80, 78, 71]);
    const hash = await hashFiles([
      { name: "lib/Hero/frames.json", bytes: framesBytes },
      { name: "lib/Hero/atlas-0.png", bytes: pageBytes },
    ]);
    const manifest: WebAssetManifest = {
      schemaVersion: 1,
      assetVersion: "dev",
      clientBuildHash: "sha256:build",
      maps: {},
      mapLibraries: {},
      libraries: {
        Hero: {
          sourceHash: "sha256:source",
          framesUrl: "lib/Hero/frames.json",
          pages: ["lib/Hero/atlas-0.png"],
          maskPages: [],
          hash,
        },
      },
    };
    installFetch({
      "manifest.json": new TextEncoder().encode(JSON.stringify(manifest)),
      "lib/Hero/frames.json": framesBytes,
      "lib/Hero/atlas-0.png": pageBytes,
    });

    const store = new AssetStore();
    await store.load(MANIFEST_URL);
    const pack = await store.loadLibrary("Hero");
    expect(pack.byIndex.get(0)?.width).toBe(1);
    expect(pack.pageUrls[0]).toMatch(/^blob:/);
    store.clearRuntimeCaches();
  });

  it("rejects a tampered library page", async () => {
    const framesBytes = new TextEncoder().encode(JSON.stringify({ version: 2, pages: [], maskPages: [], images: [], frames: {} }));
    const manifest: WebAssetManifest = {
      schemaVersion: 1,
      assetVersion: "dev",
      clientBuildHash: "sha256:build",
      maps: {},
      mapLibraries: {},
      libraries: {
        Hero: {
          sourceHash: "sha256:source",
          framesUrl: "lib/Hero/frames.json",
          pages: ["lib/Hero/atlas-0.png"],
          maskPages: [],
          hash: "sha256:0000000000000000000000000000000000000000000000000000000000000000",
        },
      },
    };
    installFetch({
      "manifest.json": new TextEncoder().encode(JSON.stringify(manifest)),
      "lib/Hero/frames.json": framesBytes,
      "lib/Hero/atlas-0.png": new Uint8Array([1, 2, 3]),
    });

    const store = new AssetStore();
    await store.load(MANIFEST_URL);
    await expect(store.loadLibrary("Hero")).rejects.toThrow(/哈希不匹配/);
  });
});
