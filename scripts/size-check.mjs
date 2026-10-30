#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * The client's size budgets (ROADMAP 3.2 and section 4.12), and the lint of the shipped stylesheets (ROADMAP 6.3b,
 * ADR-0106). It reads the chunk reports that the client build writes
 * (src/Jiron.Umbraco.FormBuilder.Web/Client/.build/<package>.chunks.json) and measures each file as it is served:
 * gzip at level 9, licence banner included, one file at a time.
 *
 *   - the runtime's core, its entry and every chunk the entry imports statically: at most 16 KB;
 *   - the runtime with every feature: every file but the languages' words, and the largest language's, as a page
 *     loads its culture's words alone: at most 40 KB (ADR-0116); the sum with every language is reported beside it;
 *   - the runtime's chunk names: every file but the entry carries a content hash, since the entry's `?v=` does not
 *     reach the chunks it imports, so a changed chunk needs a name of its own (ADR-0004);
 *   - each stylesheet the themes package builds, the base rules and each theme's: at most 10 KB (ADR-0105), and
 *     linted as it is served (scripts/lib/stylesheets.mjs): the base rules under .jfb-form, a theme's under
 *     .jfb-form:where(.jfb-theme-<alias>), so it styles only its own forms, and nothing physical anywhere;
 *   - the element and the backoffice bundle: reported, without a budget (section 4.12 sets none for the element,
 *     and 9.6 sets the backoffice's).
 *
 *   node scripts/size-check.mjs     run after the client build (npm run build in the Client folder)
 *
 * Exit codes: 0 within budget and lint, 1 over a budget, a stylesheet the lint refuses, or a report or file missing.
 */
import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'node:zlib';
import { lintStylesheet, themeScope } from './lib/stylesheets.mjs';

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

/** Whether a file holds a language's words and nothing else (ADR-0112): a page loads its culture's alone. */
export function isWords(file) {
  const modules = file.modules ?? [];
  return modules.length > 0 && modules.every((module) => /(^|\/)packages\/logic\/src\/lang\//.test(module));
}

/** Whether a file's name carries the bundler's content hash: `<name>-<eight letters, digits, _ or ->.<ext>`. */
export function isHashed(file) {
  return /-[A-Za-z0-9_-]{8}[.][a-z]+$/.test(file);
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

/** The scope of a stylesheet the themes package builds: the base rules' is the form, a theme's its own forms. */
export function scopeOf(file) {
  const name = file.replace(/[.]css$/, '');
  return name === 'base' ? '.jfb-form' : themeScope(name);
}

/** One result per budget, stylesheet lint or report: `{ line, ok }`. */
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
    const features = runtime.files.filter((f) => !isWords(f)).map((f) => f.file);
    const words = runtime.files.filter(isWords).map((f) => f.file);
    const largest = Math.max(0, ...words.map((file) => measure(runtime, [file], webProject)));
    const languages = words.length === 0 ? '' : `, and the largest of ${words.length} languages' words`;
    budget(`runtime with every feature (${files(features.length)}${languages})`, measure(runtime, features, webProject) + largest, BUDGETS.runtimeAll);
    if (words.length > 1) {
      results.push({ ok: true, line: `runtime with every feature and every language (${files(all.length)}): ${kb(measure(runtime, all, webProject))} gzip, more than a page loads` });
    }
    const unhashed = runtime.files.filter((f) => !f.entry && !isHashed(f.file)).map((f) => f.file);
    results.push({
      ok: unhashed.length === 0,
      line: `runtime chunk names: ${unhashed.length === 0 ? 'every file but the entry carries a content hash, ok' : `WITHOUT A CONTENT HASH: ${unhashed.join(', ')}`}`,
    });
  }

  const themes = readReport('themes', reports);
  if (!themes) missing('themes');
  else {
    for (const file of themes.files.filter((f) => f.file.endsWith('.css'))) {
      budget(`stylesheet ${file.file}`, measure(themes, [file.file], webProject), BUDGETS.themeCss);
      const css = readFileSync(join(webProject, ...themes.folder.split('/'), file.file), 'utf8');
      const { unscoped, physical } = lintStylesheet(css, scopeOf(file.file));
      const found = [...unscoped.map((s) => `outside ${scopeOf(file.file)}: ${s}`), ...physical.map((p) => `physical: ${p}`)];
      results.push({ ok: found.length === 0, line: `stylesheet ${file.file}: ${found.length === 0 ? `every rule under ${scopeOf(file.file)}, nothing physical, ok` : `REFUSED, ${found.join('; ')}`}` });
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
