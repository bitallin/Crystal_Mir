import { createHash } from "node:crypto";
import { existsSync, readdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HASH_FILE = "client-build.json";

function listFiles(root) {
  const files = [];
  const visit = directory => {
    for (const name of readdirSync(directory).sort()) {
      const path = join(directory, name);
      const stat = statSync(path);
      if (stat.isDirectory()) visit(path);
      else if (stat.isFile()) files.push(path);
    }
  };
  visit(root);
  return files;
}

export function hashBuildDirectory(distDirectory) {
  const root = resolve(distDirectory);
  if (!existsSync(root) || !statSync(root).isDirectory()) {
    throw new Error(`Web build directory does not exist: ${root}`);
  }

  const entries = listFiles(root)
    .map(path => ({ path, name: relative(root, path).split(sep).join("/") }))
    .filter(entry => entry.name !== HASH_FILE)
    .sort((left, right) => (left.name < right.name ? -1 : left.name > right.name ? 1 : 0));

  if (entries.length === 0) throw new Error(`Web build directory is empty: ${root}`);

  const hash = createHash("sha256");
  for (const entry of entries) {
    const name = Buffer.from(entry.name, "utf8");
    const bytes = readFileSync(entry.path);
    const header = Buffer.allocUnsafe(8);
    header.writeUInt32LE(name.length, 0);
    header.writeUInt32LE(bytes.length, 4);
    hash.update(header.subarray(0, 4));
    hash.update(name);
    hash.update(header.subarray(4));
    hash.update(bytes);
  }

  return {
    clientBuildHash: `sha256:${hash.digest("hex")}`,
    fileCount: entries.length
  };
}

export function writeBuildHash(distDirectory) {
  const root = resolve(distDirectory);
  const result = hashBuildDirectory(root);
  const payload = { schemaVersion: 1, ...result };
  writeFileSync(join(root, HASH_FILE), `${JSON.stringify(payload, null, 2)}\n`);
  return { ...result, path: join(root, HASH_FILE) };
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const dist = resolve(dirname(fileURLToPath(import.meta.url)), "..", "dist");
  const result = writeBuildHash(dist);
  console.log(`clientBuildHash: ${result.clientBuildHash}`);
  console.log(`files: ${result.fileCount}`);
  console.log(`manifest: ${result.path}`);
}
