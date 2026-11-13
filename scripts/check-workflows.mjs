#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Checks the GitHub workflow files and dependabot.yml locally (ROADMAP 3.5), since CI results cannot be read from
 * the development machine. GitHub's own parser (@actions/workflow-parser, MIT) validates each workflow: syntax,
 * keys, expressions and contexts. Then Jiron Form Builder's rules apply (ROADMAP 3.5, D17):
 *
 *   - every job is gated: a push runs nothing until the owner sets the repository variable JFB_CI to 'true'. A job
 *     may instead run only when someone starts the workflow by hand, or only for a version tag (a release, 18.2);
 *   - push triggers on branches include `main` and `v18/main` (the Umbraco 18 line, Phase 19);
 *   - every job has a timeout, except a job that calls another workflow, whose own jobs have theirs;
 *   - a newer push to the same ref cancels the older run, the token is read-only, and the workflow can be started
 *     by hand;
 *   - actions are pinned to a major version or a commit, and services do not use the env context (GitHub does not
 *     offer it there);
 *   - Dependabot covers nuget, npm (every lock file of check-npm-licences.mjs) and github-actions, and opens no
 *     pull request (limit 0) until the owner raises it.
 *
 *   npm ci --prefix scripts, then node scripts/check-workflows.mjs
 *
 * Exit codes: 0 fine, 1 problems.
 */
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { LOCK_FILES } from './check-npm-licences.mjs';

const repo = resolve(fileURLToPath(new URL('..', import.meta.url)));

/** The gate every pushed job's `if` must contain (D17). */
export const GATE = "vars.JFB_CI == 'true'";

/** A job whose `if` starts with this runs only when someone starts the workflow by hand, never on a push. */
export const MANUAL = "github.event_name == 'workflow_dispatch'";

/** A job whose `if` starts with this runs only for a version tag, which only a release pushes (18.2). */
export const TAGS = "startsWith(github.ref, 'refs/tags/v')";

/** Expressions whose text someone else chooses (a tag, a branch, an input, an event), which a script must not inline. */
export const UNTRUSTED_INLINE = /\$\{\{[^}]*\b(inputs\.|github\.(ref_name|ref|head_ref|event\.))/;

/** The branches a push trigger must name: the Umbraco 17 line and the Umbraco 18 line (D16). */
export const PUSH_BRANCHES = Object.freeze(['main', 'v18/main']);

/** The ecosystems Dependabot must watch. */
export const ECOSYSTEMS = Object.freeze(['nuget', 'npm', 'github-actions']);

// The parser imports its JSON schema without an import attribute, which bundlers accept and Node does not.
registerHooks({
  load(url, context, nextLoad) {
    if (url.endsWith('.json') && url.includes('/node_modules/@actions/')) {
      return nextLoad(url, { ...context, importAttributes: { ...context.importAttributes, type: 'json' } });
    }
    return nextLoad(url, context);
  },
});

let modules;
async function load() {
  modules ??= {
    parser: await import('@actions/workflow-parser'),
    yaml: await import('yaml'),
  };
  return modules;
}

/** Errors from GitHub's parser: syntax, unknown keys, bad expressions and contexts. */
export async function parserErrors(name, content) {
  const { parser } = await load();
  const result = parser.parseWorkflow({ name, content }, new parser.NoOperationTraceWriter());
  if (result.value) await parser.convertWorkflowTemplate(result.context, result.value);
  return result.context.errors.getErrors().map((error) => error.message);
}

const ifText = (value) => (typeof value === 'string' ? value : value === undefined ? '' : String(value));

/** Whether an expression has an `||` outside parentheses and string literals. */
export function hasTopLevelOr(text) {
  let depth = 0;
  let quote = null;
  for (let at = 0; at < text.length; at++) {
    const char = text[at];
    if (quote) {
      if (char === quote) quote = null;
    } else if (char === "'" || char === '"') {
      quote = char;
    } else if (char === '(') {
      depth++;
    } else if (char === ')') {
      depth--;
    } else if (depth === 0 && char === '|' && text[at + 1] === '|') {
      return true;
    }
  }
  return false;
}

/** The rules for one parsed workflow document. */
export function workflowRuleErrors(doc) {
  const errors = [];
  if (!doc || typeof doc !== 'object') return ['the file is empty'];
  if (doc.permissions?.contents !== 'read' || Object.keys(doc.permissions ?? {}).length !== 1) {
    errors.push('the workflow must set `permissions: contents: read` and nothing more at the top level');
  }
  if (doc.concurrency?.['cancel-in-progress'] !== true || !String(doc.concurrency?.group ?? '').includes('github.ref')) {
    errors.push('the workflow must cancel older runs of the same ref (`concurrency` with `github.ref` and `cancel-in-progress: true`)');
  }
  const triggers = doc.on ?? {};
  if (typeof triggers !== 'object' || !('workflow_dispatch' in triggers)) errors.push('the workflow must run on `workflow_dispatch`');
  const branches = triggers.push?.branches;
  if (Array.isArray(branches)) {
    for (const branch of PUSH_BRANCHES) {
      if (!branches.includes(branch)) errors.push(`the push trigger must name the branch ${branch}`);
    }
  }

  for (const [id, job] of Object.entries(doc.jobs ?? {})) {
    const condition = ifText(job.if).trim();
    // "start && rest" only while rest has no top-level ||: `start && x || true` would always run.
    const only = (start) => condition === start || (condition.startsWith(`${start} && `) && !hasTopLevelOr(condition.slice(start.length)));
    if (!condition.includes(GATE) && !only(MANUAL) && !only(TAGS)) {
      errors.push(`job ${id}: its \`if\` must contain ${GATE}, or start with ${MANUAL} && or ${TAGS} &&`);
    }
    if (/always\(\)/.test(condition)) errors.push(`job ${id}: \`always()\` would bypass the gate`);
    // GitHub refuses timeout-minutes on a job that calls another workflow; that workflow's jobs carry their own.
    if (!job['timeout-minutes'] && !job.uses) errors.push(`job ${id}: set \`timeout-minutes\``);
    for (const [name, service] of Object.entries(job.services ?? {})) {
      if (/\$\{\{[^}]*\benv\./.test(JSON.stringify(service))) {
        errors.push(`job ${id}: service ${name} uses the env context, which services cannot read`);
      }
    }
    for (const step of job.steps ?? []) {
      // A tag's name, a manual run's input or an event's text reach a script through `env`, never inline: GitHub
      // pastes an inline expression into the script before the shell reads it (18.2, ADR-0247).
      if (typeof step.run === 'string' && UNTRUSTED_INLINE.test(step.run)) {
        errors.push(`job ${id}: step "${step.name ?? step.run.split('\n')[0]}" writes a tag, an input or an event's text into its script; pass it through \`env\``);
      }
      const uses = step.uses;
      if (uses && !uses.startsWith('./') && !/@(v\d+|[0-9a-f]{40})$/.test(uses)) {
        errors.push(`job ${id}: \`${uses}\` must be pinned to a major version (@vN) or a commit`);
      }
    }
  }
  return errors;
}

/** The rules for dependabot.yml. `lockFiles` are the npm projects it must cover. */
export function dependabotErrors(doc, root = repo, lockFiles = LOCK_FILES) {
  const errors = [];
  if (doc?.version !== 2) errors.push('dependabot.yml must have `version: 2`');
  const updates = Array.isArray(doc?.updates) ? doc.updates : [];
  for (const ecosystem of ECOSYSTEMS) {
    if (!updates.some((update) => update['package-ecosystem'] === ecosystem)) errors.push(`dependabot.yml must cover ${ecosystem}`);
  }
  const folders = (update) => update.directories ?? [update.directory];
  for (const update of updates) {
    const label = `${update['package-ecosystem']} ${folders(update).join(', ')}`;
    if (update['open-pull-requests-limit'] !== 0) {
      errors.push(`dependabot ${label}: \`open-pull-requests-limit\` must be 0 until the owner raises it`);
    }
    if (!update.schedule?.interval) errors.push(`dependabot ${label}: needs \`schedule.interval\``);
    for (const dir of folders(update)) {
      if (typeof dir !== 'string' || !existsSync(join(root, dir))) errors.push(`dependabot ${label}: the directory ${dir} does not exist`);
    }
  }
  const npmFolders = new Set(
    updates
      .filter((update) => update['package-ecosystem'] === 'npm')
      .flatMap(folders)
      .map((dir) => String(dir).replace(/^\/+|\/+$/g, '')),
  );
  for (const lockFile of lockFiles) {
    const folder = dirname(lockFile).replaceAll('\\', '/');
    if (!npmFolders.has(folder)) errors.push(`dependabot.yml must cover the npm project /${folder}`);
  }
  return errors;
}

/** Checks every workflow and dependabot.yml under the given root. */
export async function run(root = repo, lockFiles = LOCK_FILES) {
  const { yaml } = await load();
  const lines = [];
  let ok = true;
  const dir = join(root, '.github', 'workflows');
  const files = existsSync(dir) ? readdirSync(dir).filter((file) => /\.ya?ml$/.test(file)) : [];
  if (files.length === 0) {
    lines.push('check-workflows: no workflow files found');
    ok = false;
  }
  for (const file of files) {
    const path = join(dir, file);
    const name = relative(root, path).replaceAll('\\', '/');
    const content = readFileSync(path, 'utf8');
    const doc = yaml.parse(content);
    const errors = [...(await parserErrors(name, content)), ...workflowRuleErrors(doc)];
    if (errors.length > 0) ok = false;
    for (const error of errors) lines.push(`check-workflows: ${name}: ${error}`);
    if (errors.length === 0) lines.push(`check-workflows: ${name}: ok (${Object.keys(doc.jobs ?? {}).length} jobs)`);
  }
  const dependabot = join(root, '.github', 'dependabot.yml');
  if (existsSync(dependabot)) {
    const errors = dependabotErrors(yaml.parse(readFileSync(dependabot, 'utf8')), root, lockFiles);
    if (errors.length > 0) ok = false;
    for (const error of errors) lines.push(`check-workflows: ${error}`);
    if (errors.length === 0) lines.push('check-workflows: .github/dependabot.yml: ok');
  } else {
    lines.push('check-workflows: .github/dependabot.yml is missing');
    ok = false;
  }
  return { ok, lines };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const { ok, lines } = await run();
  for (const line of lines) (ok ? console.log : console.error)(line);
  process.exit(ok ? 0 : 1);
}
