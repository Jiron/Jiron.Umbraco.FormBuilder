#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * The pre-commit checks (ROADMAP 0.2), run by .githooks/pre-commit:
 *   1. the author and the committer need a name and one of GitHub's private commit addresses, or the clone says
 *      that its own address may be used (scripts/lib/identity.mjs, ADR-0265);
 *   2. the secret scan, the term guard and the text check pass on the staged files.
 * Every check runs, so one commit attempt shows every problem. Exit code 1 when any check fails.
 */
import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import { run as checkText } from './check-text.mjs';
import { identityProblems } from './lib/identity.mjs';
import { run as secretScan } from './secret-scan.mjs';
import { run as termGuard } from './term-guard.mjs';

function git(args, cwd) {
  try {
    return execFileSync('git', args, { cwd, stdio: ['ignore', 'pipe', 'pipe'] }).toString('utf8').trim();
  } catch {
    return '';
  }
}

/** The idents git would write into a commit made in `cwd` now, and whether that clone allows its own address. */
function commitIdentity(cwd) {
  return {
    author: git(['var', 'GIT_AUTHOR_IDENT'], cwd),
    committer: git(['var', 'GIT_COMMITTER_IDENT'], cwd),
    ownEmail: git(['config', '--type=bool', '--get', 'jfb.ownEmail'], cwd) === 'true',
  };
}

/** Runs every check and returns `{ exitCode, output }`. */
export async function run({ cwd = process.cwd(), idents } = {}) {
  const output = [];
  let failed = false;

  const problems = identityProblems(idents ?? commitIdentity(cwd));
  if (problems.length > 0) {
    failed = true;
    output.push(`pre-commit: refused, ${problems.join(' and ')}.`);
    output.push('  The history is public for good. Commit with the address GitHub gives your account (Settings > Emails, "Keep my');
    output.push('  email addresses private"): git config user.email "<id>+<login>@users.noreply.github.com"');
    output.push('  Or allow your own address in this clone: git config jfb.ownEmail true');
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
