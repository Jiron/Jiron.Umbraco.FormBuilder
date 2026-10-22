#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * The pre-commit checks (ROADMAP 0.2), run by .githooks/pre-commit:
 *   1. the author and the committer must be the repository's identity (CLAUDE.md rule 9);
 *   2. the secret scan, the term guard and the text check pass on the staged files.
 * Every check runs, so one commit attempt shows every problem. Exit code 1 when any check fails.
 */
import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import { run as checkText } from './check-text.mjs';
import { identityProblems } from './lib/identity.mjs';
import { run as secretScan } from './secret-scan.mjs';
import { run as termGuard } from './term-guard.mjs';

function gitVar(name) {
  try {
    return execFileSync('git', ['var', name], { stdio: ['ignore', 'pipe', 'pipe'] }).toString('utf8');
  } catch {
    return '';
  }
}

/** Runs every check and returns `{ exitCode, output }`. */
export async function run({ cwd = process.cwd(), idents } = {}) {
  const output = [];
  let failed = false;

  const problems = identityProblems(idents ?? { author: gitVar('GIT_AUTHOR_IDENT'), committer: gitVar('GIT_COMMITTER_IDENT') });
  if (problems.length > 0) {
    failed = true;
    output.push(`pre-commit: refused, ${problems.join(' and ')}.`);
    output.push('  Set it in this repository: git config user.name "Jiron" and git config user.email "59139789+Jiron@users.noreply.github.com".');
  }

  for (const check of [() => secretScan({ mode: 'staged', cwd }), () => termGuard({ mode: 'staged', cwd }), () => checkText({ mode: 'staged', cwd })]) {
    const result = await check();
    output.push(...result.output);
    if (result.exitCode !== 0) failed = true;
  }
  return { exitCode: failed ? 1 : 0, output };
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const { exitCode, output } = await run();
  (exitCode === 0 ? console.log : console.error)(output.join('\n'));
  process.exit(exitCode);
}
