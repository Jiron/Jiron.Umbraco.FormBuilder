#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Points git at the committed hooks in .githooks (ROADMAP 0.2):
 *   - pre-commit runs scripts/pre-commit.mjs (the identity check, then the secret scan, the term guard and the
 *     text check on the staged files);
 *   - commit-msg runs scripts/term-guard.mjs on the message.
 * Safe to run again at any time.
 */
import { execFileSync } from 'node:child_process';
import { chmodSync, existsSync } from 'node:fs';
import { join } from 'node:path';

export const HOOKS = ['pre-commit', 'commit-msg'];

const root = execFileSync('git', ['rev-parse', '--show-toplevel']).toString('utf8').trim();
for (const name of HOOKS) {
  const hook = join(root, '.githooks', name);
  if (!existsSync(hook)) {
    console.error(`install-hooks: ${hook} is missing`);
    process.exit(1);
  }
  chmodSync(hook, 0o755);
}
execFileSync('git', ['config', 'core.hooksPath', '.githooks'], { cwd: root });
const configured = execFileSync('git', ['config', '--get', 'core.hooksPath'], { cwd: root }).toString('utf8').trim();
console.log(`install-hooks: core.hooksPath=${configured} (${HOOKS.join(', ')})`);
