#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Runs the browser projects for the gate (ROADMAP 3.5, "Flaky means broken"; ADR-0011): `npx playwright test` in
 * tests/e2e, and once more only when that run failed and a host left the hang marker during it. Umbraco 17 on
 * SQLite can stall in its start-up minute, and the harness's set-up or teardown then writes the marker into the
 * host's folder under .local/e2e/. Markers from an earlier run are removed first, so only this run's count. Any other
 * failure fails at once.
 *
 *   node scripts/e2e-gate.mjs [playwright test arguments]   (scripts/ci-local.ps1 runs it)
 *
 * Exit code: the last run's, or 1 when it could not start.
 */
import { spawnSync } from 'node:child_process';
import { existsSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(fileURLToPath(new URL('..', import.meta.url)));
export const E2E = join(root, 'tests', 'e2e');
export const E2E_STATE = join(root, '.local', 'e2e');

/** The hang marker's file name, read from the harness's own constant ("One source for every value"). */
export function hangMarkerName(healthSource = readFileSync(join(E2E, 'support', 'host-health.ts'), 'utf8')) {
  const name = healthSource.match(/export const HANG_MARKER = '([^']+)'/)?.[1];
  if (!name) throw new Error('tests/e2e/support/host-health.ts no longer exports HANG_MARKER');
  return name;
}

/** The hang markers in the hosts' folders below a state folder. */
export function hangMarkers(stateDir, name) {
  if (!existsSync(stateDir)) return [];
  return readdirSync(stateDir, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => join(stateDir, entry.name, name))
    .filter((file) => existsSync(file));
}

/**
 * Runs `run()` (which returns an exit code) once, and once more only when it failed and `markers()` finds a hang
 * marker. `clear(files)` removes markers: the stale ones before the first run, this run's before the second.
 */
export function runWithRerun({ run, markers, clear, read, log }) {
  clear(markers());
  const first = run();
  if (first === 0) return 0;
  const found = markers();
  if (found.length === 0) return first;
  for (const file of found) {
    log(`e2e-gate: a host's database hung (${read(file).trim()}; ${file}). Running the browser projects once more on fresh hosts (ADR-0011).`);
  }
  clear(found);
  const second = run();
  log(second === 0 ? 'e2e-gate: the rerun passed; note the hang marker in docs/WORKLOG.md.' : 'e2e-gate: the rerun failed too.');
  return second;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = ['playwright', 'test', ...process.argv.slice(2)];
  const name = hangMarkerName();
  // npx is a .cmd script on Windows, which only a shell starts; the shell gets one command line, built here.
  const quote = (arg) => (/[ "&|<>^]/.test(arg) ? `"${arg.replaceAll('"', '""')}"` : arg);
  const run = () =>
    (process.platform === 'win32'
      ? spawnSync(['npx', ...args].map(quote).join(' '), { cwd: E2E, stdio: 'inherit', shell: true })
      : spawnSync('npx', args, { cwd: E2E, stdio: 'inherit' })
    ).status ?? 1;
  process.exit(
    runWithRerun({
      run,
      markers: () => hangMarkers(E2E_STATE, name),
      clear: (files) => files.forEach((file) => rmSync(file, { force: true })),
      read: (file) => readFileSync(file, 'utf8'),
      log: (line) => console.error(line),
    }),
  );
}
