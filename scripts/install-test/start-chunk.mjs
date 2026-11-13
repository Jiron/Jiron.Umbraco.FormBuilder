#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * The name of the backoffice's start chunk in a packed Web package (ROADMAP 18.3, ADR-0248), for the install test's
 * upgrade check on the supported floor: the chunk's name carries a hash of its content, and every build stamps its
 * version into its files, so two builds of different versions name their start chunks differently. The browser then
 * shows which build it runs by the start chunk it loaded.
 *
 *   node scripts/install-test/start-chunk.mjs <feed folder> <version>
 *
 * Prints the file name, for example start-C9Ke8sis.js. Exit codes: 0 found, 1 not found, 2 usage.
 */
import { readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { zipEntries } from '../check-packages.mjs';

const CHUNKS = 'staticwebassets/App_Plugins/JironFormBuilder/chunks/';

/** The start chunk's file name among a Web package's entries; there must be exactly one. */
export function startChunkOf(entries) {
  const found = entries.filter((name) => name.startsWith(CHUNKS) && /^start-[A-Za-z0-9_-]+\.js$/.test(name.slice(CHUNKS.length)));
  if (found.length !== 1) throw new Error(`the package holds ${found.length} start chunks, not one`);
  return found[0].slice(CHUNKS.length);
}

function main() {
  const [feed, version] = process.argv.slice(2);
  if (!feed || !version) {
    console.error('usage: node scripts/install-test/start-chunk.mjs <feed folder> <version>');
    process.exit(2);
  }
  try {
    console.log(startChunkOf(zipEntries(readFileSync(join(resolve(feed), `Jiron.Umbraco.FormBuilder.Web.${version}.nupkg`)))));
  } catch (error) {
    console.error(`start-chunk: ${error.message}`);
    process.exit(1);
  }
}

if (resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) main();
