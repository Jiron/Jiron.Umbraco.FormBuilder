#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * The client's size budgets (ROADMAP 3.2 and section 4.12). It reads the chunk reports that the client build writes
 * (src/Jiron.Umbraco.FormBuilder.Web/Client/.build/<package>.chunks.json) and measures each file as it is served:
 * gzip at level 9, licence banner included, one file at a time.
 *
 *   - the runtime's core, its entry and every chunk the entry imports statically: at most 16 KB;
 *   - the runtime with every feature, all its files: at most 40 KB;
 *   - each stylesheet the themes package builds, the base rules and each theme's: at most 10 KB (ADR-0105);
 *   - the element and the backoffice bundle: reported, without a budget (section 4.12 sets none for the element,
 *     and 9.6 sets the backoffice's).
 *
 *   node scripts/size-check.mjs     run after the client build (npm run build in the Client folder)
 *
 * Exit codes: 0 within budget, 1 over a budget or a report or file missing.
 */
import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'node:zlib';

const root = resolve(fileURLToPath(new URL('..', import.meta.url)));
export const WEB_PROJECT = join(root, 'src', 'Jiron.Umbraco.FormBuilder.Web');
export const REPORTS = join(WEB_PROJECT, 'Client', '.build');

const KB = 1024;
export const BUDGETS = { runtimeCore: 16 * KB, runtimeAll: 40 * KB, themeCss: 10 * KB };

/** The gzip size of some bytes at the strongest level, which servers and CDNs come close to. */
export function gzipSize(bytes) {
  return gzipSync(bytes, { level: 9 }).length;
}

/** The files a report's entries load at once: the entries and, transitively, their static imports. */
export function staticClosure(report) {
  const byFile = new Map(report.files.map((file) => [file.file, file]));
  const seen = new Set();
  const visit = (name) => {
    if (seen.has(name)) return;
    seen.add(name);
    for (const imported of byFile.get(name)?.imports ?? []) visit(imported);
  };
  for (const file of report.files.filter((f) => f.entry)) visit(file.file);
  return [...seen].sort();
}

/** Reads a package's chunk report, or null when the build wrote none. */
export function readReport(name, reports = REPORTS) {
  const path = join(reports, `${name}.chunks.json`);
  return existsSync(path) ? JSON.parse(readFileSync(path, 'utf8')) : null;
}

/** The gzip size of some of a report's files, summed; a missing file is an error. */
export function measure(report, files, webProject = WEB_PROJECT) {
  let total = 0;
  for (const file of files) {
    const path = join(webProject, ...report.folder.split('/'), ...file.split('/'));
    if (!existsSync(path)) throw new Error(`${report.folder}/${file} is missing: build the client first`);
    total += gzipSize(readFileSync(path));
  }
  return total;
}

const kb = (bytes) => `${(bytes / KB).toFixed(2)} KB`;
const files = (count) => `${count} file${count === 1 ? '' : 's'}`;

/** One result per budget or report: `{ line, ok }`. */
export function check({ reports = REPORTS, webProject = WEB_PROJECT } = {}) {
  const results = [];
  const budget = (label, bytes, max) => {
    const ok = bytes <= max;
    results.push({ ok, line: `${label}: ${kb(bytes)} gzip of ${kb(max)}, ${ok ? 'ok' : 'OVER BUDGET'}` });
  };
  const missing = (name) => results.push({ ok: false, line: `${name}: no chunk report, build the client first` });

  const runtime = readReport('runtime', reports);
  if (!runtime) missing('runtime');
  else {
    const core = staticClosure(runtime);
    budget(`runtime core (${files(core.length)}: the entry and its static imports)`, measure(runtime, core, webProject), BUDGETS.runtimeCore);
    const all = runtime.files.map((f) => f.file);
    budget(`runtime with every feature (${files(all.length)})`, measure(runtime, all, webProject), BUDGETS.runtimeAll);
  }

  const themes = readReport('themes', reports);
  if (!themes) missing('themes');
  else {
    for (const file of themes.files.filter((f) => f.file.endsWith('.css'))) {
      budget(`stylesheet ${file.file}`, measure(themes, [file.file], webProject), BUDGETS.themeCss);
    }
  }

  for (const [name, label, note] of [
    ['element', 'element', 'no budget (section 4.12 sets none)'],
    ['backoffice', 'backoffice bundle', 'no budget yet (9.6 sets it)'],
  ]) {
    const report = readReport(name, reports);
    if (!report) missing(name);
    else {
      const all = report.files.map((f) => f.file);
      results.push({ ok: true, line: `${label} (${files(all.length)}): ${kb(measure(report, all, webProject))} gzip, ${note}` });
    }
  }
  return results;
}

if (resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) {
  let results;
  try {
    results = check();
  } catch (error) {
    console.error(`size: ${error instanceof Error ? error.message : String(error)}`);
    process.exit(1);
  }
  for (const result of results) console.log(`size: ${result.line}`);
  if (results.some((result) => !result.ok)) process.exit(1);
}
