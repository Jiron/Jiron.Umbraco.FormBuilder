#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Writes the shared vector files (ROADMAP section 4.3.3, ADR-0005): each module in suites/ becomes <suite>.json in
 * this folder, in the jfb-vectors/1 format. The files are plain ASCII: JSON.stringify escapes control characters,
 * and this script escapes everything above "~" as a JSON escape of its UTF-16 code unit (an emoji becomes its
 * surrogate pair), so no editor or tool can turn an escape into a raw character. The NUnit and Vitest runners read
 * the files in place.
 *
 *   node tests/vectors/build.mjs           write every file
 *   node tests/vectors/build.mjs --check   exit 1 when a file is missing or differs from its suite
 */
import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

/** The format every vector file declares. */
export const FORMAT = 'jfb-vectors/1';

const BACKSLASH = String.fromCharCode(92);
const NEWLINE = String.fromCharCode(10);
const SUFFIX = '.mjs';

/** JSON text with every code unit above "~" written as a JSON escape. */
export function ascii(json) {
  let out = '';
  for (let i = 0; i < json.length; i++) {
    const code = json.charCodeAt(i);
    out += code > 126 ? BACKSLASH + 'u' + code.toString(16).padStart(4, '0') : json[i];
  }
  return out;
}

/** The vector files the suites in `folder`/suites make: `[{ name, text, cases }]`, by suite name. */
export async function build(folder) {
  const suites = join(folder, 'suites');
  const files = [];
  for (const module of readdirSync(suites).filter((name) => name.endsWith(SUFFIX)).sort()) {
    const { default: suite } = await import(pathToFileURL(join(suites, module)).href);
    const name = module.slice(0, -SUFFIX.length);
    if (suite.suite !== name) throw new Error(`${module}: the suite is called "${suite.suite}", not "${name}"`);
    const ids = suite.cases.map((vector) => vector.id);
    const repeated = ids.filter((id, index) => ids.indexOf(id) !== index);
    if (repeated.length > 0) throw new Error(`${module}: repeated ids ${repeated.join(', ')}`);
    const file = { format: FORMAT, suite: suite.suite, defaults: suite.defaults, fields: suite.fields, cases: suite.cases };
    files.push({ name: `${name}.json`, text: ascii(JSON.stringify(file, null, 2)) + NEWLINE, cases: suite.cases.length });
  }
  return files;
}

/** The names of the files in `folder` that are missing or differ from what their suites make. */
export async function stale(folder) {
  const files = await build(folder);
  return files
    .filter((file) => {
      const path = join(folder, file.name);
      return !existsSync(path) || readFileSync(path, 'utf8') !== file.text;
    })
    .map((file) => file.name);
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const folder = fileURLToPath(new URL('.', import.meta.url));
  if (process.argv.includes('--check')) {
    const names = await stale(folder);
    if (names.length > 0) {
      console.error(`vectors: ${names.join(', ')} differ from their suites; run node tests/vectors/build.mjs`);
      process.exit(1);
    }
    console.log('vectors: every file matches its suite');
  } else {
    for (const file of await build(folder)) {
      writeFileSync(join(folder, file.name), file.text);
      console.log(`vectors: ${file.name}, ${file.cases} cases`);
    }
  }
}
