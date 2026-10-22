#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Runs every formatter that exists in the repository (ROADMAP "How to use", "Formatters before the gate"):
 *   - `dotnet format` on the solution, once there is one (step 3.1);
 *   - `npm run fix` in every npm package that defines a "fix" script (ESLint --fix and Prettier; the client
 *     workspace from 3.2, the browser tests from 3.4).
 * Run it before every gate run: the build does not catch IDE1006, and a gate that fails on formatting alone
 * wastes a long run. Exit code 1 when a formatter fails.
 *
 *   node scripts/fix.mjs           run every formatter
 *   node scripts/fix.mjs --list    only print what would run
 */
import { spawnSync } from 'node:child_process';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

/** The npm packages that may define a "fix" script, relative to the repository root. */
export const NPM_PACKAGES = ['src/Jiron.Umbraco.FormBuilder.Web/Client', 'tests/e2e', 'scripts'];

/** The formatters to run in `root`: `[{ name, command, args, cwd }]`, in order. */
export function plan(root) {
  const steps = [];
  const solutions = existsSync(root) ? readdirSync(root).filter((name) => name.endsWith('.slnx')) : [];
  if (solutions.length > 0) {
    steps.push({ name: `dotnet format ${solutions[0]}`, command: 'dotnet', args: ['format', solutions[0]], cwd: root });
  }
  for (const folder of NPM_PACKAGES) {
    const manifest = join(root, folder, 'package.json');
    if (!existsSync(manifest)) continue;
    const scripts = JSON.parse(readFileSync(manifest, 'utf8')).scripts ?? {};
    if (typeof scripts.fix === 'string') steps.push({ name: `npm run fix (${folder})`, command: 'npm', args: ['run', 'fix'], cwd: join(root, folder) });
  }
  return steps;
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const root = join(fileURLToPath(new URL('.', import.meta.url)), '..');
  const steps = plan(root);
  if (steps.length === 0) {
    console.log('fix: no formatter exists yet (no solution, no npm package with a "fix" script)');
    process.exit(0);
  }
  if (process.argv.includes('--list')) {
    for (const step of steps) console.log(`fix: would run ${step.name} in ${relative(root, step.cwd) || '.'}`);
    process.exit(0);
  }
  let failed = false;
  for (const step of steps) {
    const started = Date.now();
    // npm is a .cmd file on Windows, which Node only starts through a shell; the arguments are fixed strings.
    const result = spawnSync(step.command, step.args, { cwd: step.cwd, stdio: 'inherit', shell: process.platform === 'win32' });
    const seconds = ((Date.now() - started) / 1000).toFixed(1);
    if (result.status === 0) console.log(`fix: ${step.name} done (${seconds} s)`);
    else {
      failed = true;
      console.error(`fix: ${step.name} failed with exit code ${result.status ?? result.error?.message} (${seconds} s)`);
    }
  }
  process.exit(failed ? 1 : 0);
}
