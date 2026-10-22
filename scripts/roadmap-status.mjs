#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Prints the one-line roadmap progress that the /goal evaluator reads (ROADMAP 0.2):
 *
 *   ROADMAP done=N/T open=N blocked=N blocked_missing_in_owner_todo=N next=<id> <title> head=<sha> origin_main=<sha> clean=yes|no
 *
 * Steps are the lines that start with "- [ ] **X.Y Title**" ([x] done, [~] blocked); a split step carries a
 * letter, "X.Yb". Every blocked step must be named in docs/OWNER-TODO.md as "step X.Y". A done step ends with
 * " (YYYY-MM-DD, <short hash>)" (ROADMAP "How to use").
 *
 * No silent misses: any line that starts with a checkbox ("- [?]") but is not a well-formed step makes the
 * script fail (exit 1) and name the line, so a step can never drop out of the count.
 *
 * The script runs `git fetch origin` quietly first; when that fails, " fetch=failed" is appended and
 * origin_main is the last known value.
 */
import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const STEP_LINE = /^- \[([ x~])\] \*\*(\d+\.\d+[a-z]?) ([^*]+?)\*\*/;
const CHECKBOX_LINE = /^- \[.\]/;
const DONE_SUFFIX = /\(\d{4}-\d{2}-\d{2}, [0-9a-f]{7,40}\)\s*$/;

/**
 * Parses the steps: `{ steps: [{ id, title, state, line }], problems: [{ line, reason }] }`, with state 'open',
 * 'done' or 'blocked' and 1-based line numbers.
 */
export function parseRoadmap(markdown) {
  const states = { ' ': 'open', x: 'done', '~': 'blocked' };
  const steps = [];
  const problems = [];
  const seen = new Map();
  markdown.split(/\r?\n/).forEach((text, index) => {
    const line = index + 1;
    if (!CHECKBOX_LINE.test(text)) return;
    const match = STEP_LINE.exec(text);
    if (!match) {
      problems.push({ line, reason: 'a checkbox line that is not a step ("- [ ] **X.Y Title**")' });
      return;
    }
    const [, mark, id, title] = match;
    const state = states[mark];
    if (seen.has(id)) problems.push({ line, reason: `step ${id} also appears on line ${seen.get(id)}` });
    seen.set(id, line);
    if (state === 'done' && !DONE_SUFFIX.test(text)) {
      problems.push({ line, reason: `done step ${id} does not end with " (YYYY-MM-DD, <short hash>)"` });
    }
    steps.push({ id, title: title.trim(), state, line });
  });
  return { steps, problems };
}

/** Parses the steps only (see parseRoadmap). */
export function parseSteps(markdown) {
  return parseRoadmap(markdown).steps;
}

/** The blocked step ids that docs/OWNER-TODO.md does not name as "step X.Y". */
export function blockedMissingInOwnerTodo(steps, ownerTodo) {
  return steps
    .filter((step) => step.state === 'blocked')
    .map((step) => step.id)
    .filter((id) => !new RegExp(`\\bstep\\s+${id.replace('.', '\\.')}(?![\\d.]*\\d|[a-z])`, 'i').test(ownerTodo));
}

/** Builds the status line from the parsed steps and the git facts. */
export function statusLine(steps, ownerTodo, { head, originMain, clean, fetchFailed }) {
  const count = (state) => steps.filter((step) => step.state === state).length;
  const next = steps.find((step) => step.state === 'open');
  const parts = [
    `ROADMAP done=${count('done')}/${steps.length}`,
    `open=${count('open')}`,
    `blocked=${count('blocked')}`,
    `blocked_missing_in_owner_todo=${blockedMissingInOwnerTodo(steps, ownerTodo).length}`,
    `next=${next ? `${next.id} ${next.title}` : 'none'}`,
    `head=${head}`,
    `origin_main=${originMain}`,
    `clean=${clean ? 'yes' : 'no'}`,
  ];
  return parts.join(' ') + (fetchFailed ? ' fetch=failed' : '');
}

function git(root, args) {
  return execFileSync('git', args, { cwd: root, stdio: ['ignore', 'pipe', 'pipe'] }).toString('utf8').trim();
}

function tryGit(root, args, fallback) {
  try {
    return git(root, args);
  } catch {
    return fallback;
  }
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const root = tryGit(process.cwd(), ['rev-parse', '--show-toplevel'], process.cwd());
  let fetchFailed = false;
  try {
    git(root, ['fetch', 'origin', '--quiet']);
  } catch {
    fetchFailed = true;
  }
  const ownerTodoFile = join(root, 'docs', 'OWNER-TODO.md');
  const { steps, problems } = parseRoadmap(readFileSync(join(root, 'ROADMAP.md'), 'utf8'));
  const ownerTodo = existsSync(ownerTodoFile) ? readFileSync(ownerTodoFile, 'utf8') : '';
  if (problems.length > 0) {
    for (const problem of problems) console.error(`roadmap-status: ROADMAP.md:${problem.line}: ${problem.reason}`);
    process.exit(1);
  }
  console.log(
    statusLine(steps, ownerTodo, {
      head: tryGit(root, ['rev-parse', '--short', 'HEAD'], 'none'),
      originMain: tryGit(root, ['rev-parse', '--short', 'origin/main'], 'none'),
      clean: tryGit(root, ['status', '--porcelain'], 'dirty') === '',
      fetchFailed,
    }),
  );
}
