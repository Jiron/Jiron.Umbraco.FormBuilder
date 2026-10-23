// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Writes a Brotli (.br) and a gzip (.gz) copy next to every website asset (ROADMAP 3.2, ADR-0026). The Web assembly
// embeds them with their files, and 6.2's asset endpoint sends the copy that Accept-Encoding asks for, because
// Umbraco serves static files uncompressed (ROADMAP section 3.4). Copies whose file is gone are removed. `npm run build` runs it
// after the package builds:
//   node scripts/compress.mjs
import { brotliCompressSync, constants, gzipSync } from 'node:zlib';
import { existsSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

export const COPIES = ['.br', '.gz'];

/** The website assets' folder in the Web project. */
export const assetsFolder = join(fileURLToPath(new URL('.', import.meta.url)), '..', '..', 'WebsiteAssets');

/** @param {Buffer} bytes */
export function brotli(bytes) {
  return brotliCompressSync(bytes, {
    params: {
      [constants.BROTLI_PARAM_QUALITY]: constants.BROTLI_MAX_QUALITY,
      [constants.BROTLI_PARAM_SIZE_HINT]: bytes.length,
    },
  });
}

/** @param {Buffer} bytes */
export function gzip(bytes) {
  return gzipSync(bytes, { level: 9 });
}

/**
 * Every file under a folder, in name order.
 * @param {string} dir
 * @returns {Generator<string>}
 */
export function* walk(dir) {
  if (!existsSync(dir)) return;
  for (const name of readdirSync(dir).sort()) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) yield* walk(path);
    else yield path;
  }
}

/** @param {string} path */
const isCopy = (path) => COPIES.some((ext) => path.endsWith(ext));

/**
 * Compresses every file in a folder and removes copies without their file. Returns the relative paths.
 * @param {string} dir
 */
export function compressFolder(dir) {
  const written = [];
  const removed = [];
  for (const path of walk(dir)) {
    if (isCopy(path)) {
      const source = path.slice(0, path.lastIndexOf('.'));
      if (!existsSync(source)) {
        rmSync(path);
        removed.push(path);
      }
      continue;
    }
    const bytes = readFileSync(path);
    writeFileSync(`${path}.br`, brotli(bytes));
    writeFileSync(`${path}.gz`, gzip(bytes));
    written.push(path);
  }
  /** @param {string} path */
  const rel = (path) => relative(dir, path).split(sep).join('/');
  return { written: written.map(rel), removed: removed.map(rel) };
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const { written, removed } = compressFolder(assetsFolder);
  console.log(`compress: ${written.length} file(s) with .br and .gz copies, ${removed.length} stale copy(ies) removed`);
}
