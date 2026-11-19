import { decodeMapCells, type MapCell } from "../world/map";

export interface WebAssetLibrary {
  sourceHash: string;
  framesUrl: string;
  pages: string[];
  maskPages: string[];
  hash: string;
}

export interface WebAssetMap {
  sourceHash: string;
  format: number;
  width: number;
  height: number;
  cellOrder: string;
  cellStride: number;
  metaUrl: string;
  cellsUrl: string;
  libraryIds: number[];
  hash: string;
}

export interface WebAssetManifest {
  schemaVersion: number;
  assetVersion: string;
  clientBuildHash: string;
  libraries: Record<string, WebAssetLibrary>;
  maps: Record<string, WebAssetMap>;
  mapLibraries: Record<string, string>;
}

export interface MapMeta {
  schemaVersion: number;
  file: string;
  sourceHash: string;
  format: number;
  width: number;
  height: number;
  cellOrder: string;
  cellStride: number;
  cellsUrl: string;
  libraries: number[];
}

export interface LibraryFrame {
  index: number;
  page: number;
  x: number;
  y: number;
  width: number;
  height: number;
  offsetX: number;
  offsetY: number;
  shadowX: number;
  shadowY: number;
  shadow: number;
  mask: boolean;
  maskPage: number;
  maskX: number;
  maskY: number;
  maskWidth: number;
  maskHeight: number;
  maskOffsetX: number;
  maskOffsetY: number;
}

export interface FrameSet {
  start: number;
  count: number;
  skip: number;
  interval: number;
  effectStart: number;
  effectCount: number;
  effectSkip: number;
  effectInterval: number;
  reverse: boolean;
  blend: boolean;
}

export interface LibraryFrames {
  version: number;
  pages: string[];
  maskPages: string[];
  images: LibraryFrame[];
  frames: Record<string, FrameSet>;
}

export interface LibraryPack extends LibraryFrames {
  id: string;
  pageUrls: string[];
  maskPageUrls: string[];
  byIndex: Map<number, LibraryFrame>;
}

export interface LoadedMap {
  meta: MapMeta;
  cells: MapCell[];
}

export class AssetError extends Error {
  constructor(message: string, readonly detail?: unknown) {
    super(message);
    this.name = "AssetError";
  }
}

function safeRelativeUrl(base: URL, relative: string): string {
  if (!relative || relative.startsWith("/") || relative.startsWith("\\") || /^[a-z]+:/i.test(relative)) {
    throw new AssetError(`Asset path must be relative: ${relative}`);
  }
  const resolved = new URL(relative, base);
  if (resolved.origin !== base.origin || !resolved.pathname.startsWith(base.pathname)) {
    throw new AssetError(`Asset path escapes manifest root: ${relative}`);
  }
  return resolved.toString();
}

function writeUint32(target: Uint8Array, offset: number, value: number): void {
  target[offset] = value & 0xff;
  target[offset + 1] = (value >>> 8) & 0xff;
  target[offset + 2] = (value >>> 16) & 0xff;
  target[offset + 3] = (value >>> 24) & 0xff;
}

function toArrayBuffer(bytes: Uint8Array): ArrayBuffer {
  const copy = new Uint8Array(bytes.byteLength);
  copy.set(bytes);
  return copy.buffer;
}

async function sha256Files(files: ReadonlyArray<{ name: string; bytes: Uint8Array }>): Promise<string> {
  const sorted = [...files].sort((left, right) => (left.name < right.name ? -1 : left.name > right.name ? 1 : 0));
  const encoder = new TextEncoder();
  const chunks: Uint8Array[] = [];
  for (const file of sorted) {
    const name = encoder.encode(file.name.replaceAll("\\", "/"));
    const header = new Uint8Array(8);
    writeUint32(header, 0, name.byteLength);
    writeUint32(header, 4, file.bytes.byteLength);
    chunks.push(header.subarray(0, 4), name, header.subarray(4), file.bytes);
  }
  const total = chunks.reduce((sum, chunk) => sum + chunk.byteLength, 0);
  const merged = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) {
    merged.set(chunk, offset);
    offset += chunk.byteLength;
  }
  const digest = await crypto.subtle.digest("SHA-256", merged);
  return `sha256:${Array.from(new Uint8Array(digest), value => value.toString(16).padStart(2, "0")).join("")}`;
}

async function fetchJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  let response: Response;
  try {
    const init: RequestInit = { cache: "force-cache", credentials: "same-origin" };
    if (signal) init.signal = signal;
    response = await fetch(url, init);
  } catch (exception) {
    throw new AssetError(`无法加载资源：${url}`, exception);
  }
  if (!response.ok) throw new AssetError(`资源请求失败 ${response.status}：${url}`);
  try {
    return (await response.json()) as T;
  } catch (exception) {
    throw new AssetError(`资源 JSON 无效：${url}`, exception);
  }
}

async function fetchBytes(url: string, signal?: AbortSignal): Promise<Uint8Array> {
  let response: Response;
  try {
    const init: RequestInit = { cache: "force-cache", credentials: "same-origin" };
    if (signal) init.signal = signal;
    response = await fetch(url, init);
  } catch (exception) {
    throw new AssetError(`无法加载资源：${url}`, exception);
  }
  if (!response.ok) throw new AssetError(`资源请求失败 ${response.status}：${url}`);
  return new Uint8Array(await response.arrayBuffer());
}

export class AssetStore {
  private manifestUrl = "";
  manifest: WebAssetManifest | null = null;
  private readonly libraries = new Map<string, Promise<LibraryPack>>();
  private readonly maps = new Map<string, Promise<LoadedMap>>();
  private readonly objectUrls = new Set<string>();

  get baseUrl(): URL {
    if (!this.manifest) throw new AssetError("资源 manifest 尚未加载");
    return new URL(".", this.manifestUrl);
  }

  async load(manifestUrl: string, signal?: AbortSignal): Promise<WebAssetManifest> {
    const url = new URL(manifestUrl, window.location.origin).toString();
    const manifest = await fetchJson<WebAssetManifest>(url, signal);
    if (manifest.schemaVersion !== 1) throw new AssetError(`不支持的资源版本 ${manifest.schemaVersion}`);
    if (!manifest.maps || !manifest.libraries) throw new AssetError("资源 manifest 缺少索引");
    this.manifest = manifest;
    this.manifestUrl = url;
    return manifest;
  }

  async loadMap(fileName: string, signal?: AbortSignal): Promise<LoadedMap> {
    const manifest = this.requireManifest();
    const entry = manifest.maps[fileName];
    if (!entry) throw new AssetError(`地图资源缺失：${fileName}`);
    const cached = this.maps.get(fileName);
    if (cached) return cached;

    const promise = (async () => {
      const base = this.baseUrl;
      const metaUrl = safeRelativeUrl(base, entry.metaUrl);
      const cellsUrl = safeRelativeUrl(base, entry.cellsUrl);
      const [metaBytes, cellsBytes] = await Promise.all([
        fetchBytes(metaUrl, signal),
        fetchBytes(cellsUrl, signal)
      ]);
      let meta: MapMeta;
      try {
        meta = JSON.parse(new TextDecoder().decode(metaBytes)) as MapMeta;
      } catch (exception) {
        throw new AssetError(`地图 ${fileName} meta.json 无效`, exception);
      }
      if (meta.width !== entry.width || meta.height !== entry.height || meta.cellStride !== entry.cellStride) {
        throw new AssetError(`地图 ${fileName} 元数据与 manifest 不一致`);
      }
      if (meta.cellOrder !== "x-major") throw new AssetError(`不支持地图格顺序 ${meta.cellOrder}`);
      if (cellsBytes.byteLength !== meta.width * meta.height * meta.cellStride) {
        throw new AssetError(`地图 ${fileName} cells.bin 尺寸不正确`);
      }
      if (crypto?.subtle) {
        const hash = await sha256Files([
          { name: entry.metaUrl, bytes: metaBytes },
          { name: entry.cellsUrl, bytes: cellsBytes }
        ]);
        if (hash !== entry.hash) throw new AssetError(`地图 ${fileName} 哈希不匹配`);
      }
      return { meta, cells: decodeMapCells(cellsBytes, meta.cellStride) };
    })();
    this.maps.set(fileName, promise);
    try {
      return await promise;
    } catch (error) {
      this.maps.delete(fileName);
      throw error;
    }
  }

  async loadLibrary(id: string, signal?: AbortSignal): Promise<LibraryPack> {
    const manifest = this.requireManifest();
    const entry = manifest.libraries[id];
    if (!entry) throw new AssetError(`图库资源缺失：${id}`);
    const cached = this.libraries.get(id);
    if (cached) return cached;
    const promise = (async () => {
      const base = this.baseUrl;
      const framesUrl = safeRelativeUrl(base, entry.framesUrl);
      const pageNames = [...entry.pages, ...entry.maskPages];
      const [framesBytes, ...pageBytes] = await Promise.all([
        fetchBytes(framesUrl, signal),
        ...pageNames.map(page => fetchBytes(safeRelativeUrl(base, page), signal))
      ]);
      const expectedHash = await sha256Files([
        { name: entry.framesUrl, bytes: framesBytes },
        ...entry.pages.map((page, index) => ({ name: page, bytes: pageBytes[index]! })),
        ...entry.maskPages.map((page, index) => ({ name: page, bytes: pageBytes[entry.pages.length + index]! }))
      ]);
      if (expectedHash !== entry.hash) throw new AssetError(`图库 ${id} 哈希不匹配`);

      let frames: LibraryFrames;
      try {
        frames = JSON.parse(new TextDecoder().decode(framesBytes)) as LibraryFrames;
      } catch (exception) {
        throw new AssetError(`图库 ${id} frames.json 无效`, exception);
      }

      const objectUrls = pageNames.map((name, index) => {
        const url = URL.createObjectURL(new Blob([toArrayBuffer(pageBytes[index]!)], { type: name.endsWith(".png") ? "image/png" : "application/octet-stream" }));
        this.objectUrls.add(url);
        return url;
      });
      return {
        ...frames,
        id,
        pageUrls: objectUrls.slice(0, entry.pages.length),
        maskPageUrls: objectUrls.slice(entry.pages.length),
        byIndex: new Map(frames.images.map(frame => [frame.index, frame]))
      };
    })();
    this.libraries.set(id, promise);
    try {
      return await promise;
    } catch (error) {
      this.libraries.delete(id);
      throw error;
    }
  }

  async loadMapLibraries(fileName: string, signal?: AbortSignal): Promise<Map<number, LibraryPack>> {
    const map = await this.loadMap(fileName, signal);
    const manifest = this.requireManifest();
    const packs = await Promise.all(map.meta.libraries.map(async libraryIndex => {
      const packId = manifest.mapLibraries[String(libraryIndex)];
      if (!packId) return [libraryIndex, null] as const;
      return [libraryIndex, await this.loadLibrary(packId, signal)] as const;
    }));
    const result = new Map<number, LibraryPack>();
    for (const [index, pack] of packs) if (pack) result.set(index, pack);
    return result;
  }

  clearRuntimeCaches(): void {
    this.maps.clear();
    this.libraries.clear();
    for (const url of this.objectUrls) URL.revokeObjectURL(url);
    this.objectUrls.clear();
  }

  private requireManifest(): WebAssetManifest {
    if (!this.manifest) throw new AssetError("资源 manifest 尚未加载");
    return this.manifest;
  }
}
