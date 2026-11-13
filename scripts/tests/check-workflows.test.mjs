// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/check-workflows.mjs. Needs `npm ci --prefix scripts`. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { after, describe, test } from 'node:test';
import { dependabotErrors, hasTopLevelOr, parserErrors, run, workflowRuleErrors } from '../check-workflows.mjs';

const GOOD = `name: CI
on:
  push:
    branches: [main, v18/main]
  workflow_dispatch:
permissions:
  contents: read
concurrency:
  group: ci-\${{ github.ref }}
  cancel-in-progress: true
jobs:
  build:
    if: github.event_name != 'push' || vars.JFB_CI == 'true'
    runs-on: ubuntu-latest
    timeout-minutes: 10
    steps:
      - uses: actions/checkout@v7
      - run: echo \${{ github.sha }}
`;

const GATED = "if: github.event_name != 'push' || vars.JFB_CI == 'true'";

const DEPENDABOT = {
  version: 2,
  updates: [
    { 'package-ecosystem': 'nuget', directory: '/', schedule: { interval: 'weekly' }, 'open-pull-requests-limit': 0 },
    { 'package-ecosystem': 'npm', directories: ['/scripts', '/tests/e2e'], schedule: { interval: 'weekly' }, 'open-pull-requests-limit': 0 },
    { 'package-ecosystem': 'github-actions', directory: '/', schedule: { interval: 'weekly' }, 'open-pull-requests-limit': 0 },
  ],
};
const LOCKS = ['scripts/package-lock.json', 'tests/e2e/package-lock.json'];

const parse = async (text) => (await import('yaml')).parse(text);

describe('parserErrors (GitHub workflow parser)', () => {
  test('accepts a valid workflow', async () => {
    assert.deepEqual(await parserErrors('ci.yml', GOOD), []);
  });

  test('reports an unknown context, an unknown job key and a job that needs a missing job', async () => {
    const context = await parserErrors('ci.yml', GOOD.replace('github.sha', 'nope.sha'));
    assert.ok(context.some((e) => /nope/i.test(e)), context.join('\n'));
    const key = await parserErrors('ci.yml', GOOD.replace('    timeout-minutes: 10', '    timeout-minutes: 10\n    run-on: oops'));
    assert.ok(key.some((e) => /run-on/.test(e)), key.join('\n'));
    const needs = await parserErrors('ci.yml', `${GOOD}  later:\n    needs: [missing]\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo later\n`);
    assert.ok(needs.some((e) => /missing/.test(e)), needs.join('\n'));
  });
});

describe('workflowRuleErrors', () => {
  test('accepts a gated, cancelling, read-only workflow with pinned actions on both lines', async () => {
    assert.deepEqual(workflowRuleErrors(await parse(GOOD)), []);
  });

  test('requires the JFB_CI gate on every job and refuses always()', async () => {
    const ungated = workflowRuleErrors(await parse(GOOD.replace(" || vars.JFB_CI == 'true'", '')));
    assert.ok(ungated.some((e) => e.includes('JFB_CI')));
    const always = workflowRuleErrors(await parse(GOOD.replace("if: github.event_name != 'push'", "if: always() || github.event_name != 'push'")));
    assert.ok(always.some((e) => e.includes('always()')));
  });

  test('accepts a job only started by hand or only for a version tag, and nothing that merely mentions one', async () => {
    const withIf = async (condition) => workflowRuleErrors(await parse(GOOD.replace(GATED, `if: ${condition}`)));
    assert.deepEqual(await withIf("github.event_name == 'workflow_dispatch' && inputs.load"), []);
    assert.deepEqual(await withIf("github.event_name == 'workflow_dispatch'"), []);
    assert.deepEqual(await withIf("startsWith(github.ref, 'refs/tags/v') && vars.JFB_NUGET_PUBLISH == 'true'"), []);
    assert.deepEqual(await withIf("startsWith(github.ref, 'refs/tags/v') && (inputs.x || inputs.y)"), []);
    for (const loose of [
      "inputs.load || github.event_name == 'workflow_dispatch'",
      "vars.JFB_NUGET_PUBLISH == 'true' || startsWith(github.ref, 'refs/tags/v')",
      "startsWith(github.ref, 'refs/tags/v') && inputs.x || true",
      "github.event_name == 'workflow_dispatch' && inputs.load || github.event_name == 'push'",
    ]) {
      assert.ok((await withIf(loose)).some((e) => e.includes('JFB_CI')), loose);
    }
    assert.equal(hasTopLevelOr("a && contains(b, '||')"), false);
    assert.equal(hasTopLevelOr('a && b || c'), true);
  });

  test('requires both release lines in a push trigger on branches', async () => {
    const errors = workflowRuleErrors(await parse(GOOD.replace('branches: [main, v18/main]', 'branches: [main]')));
    assert.ok(errors.some((e) => e.includes('v18/main')), errors.join('\n'));
    assert.deepEqual(workflowRuleErrors(await parse(GOOD.replace("    branches: [main, v18/main]\n", "    tags: ['v*']\n"))), []);
  });

  test('a job that calls another workflow needs no timeout of its own', async () => {
    const doc = await parse(GOOD);
    doc.jobs.ci = { if: "github.event_name != 'push' || vars.JFB_CI == 'true'", uses: './.github/workflows/ci.yml' };
    assert.deepEqual(workflowRuleErrors(doc), []);
  });

  test('requires read-only permissions, cancelling concurrency, workflow_dispatch and job timeouts', async () => {
    const doc = await parse(GOOD);
    doc.permissions = { contents: 'write' };
    delete doc.concurrency;
    delete doc.on.workflow_dispatch;
    delete doc.jobs.build['timeout-minutes'];
    const errors = workflowRuleErrors(doc);
    for (const fragment of ['permissions', 'cancel', 'workflow_dispatch', 'timeout-minutes']) {
      assert.ok(errors.some((e) => e.includes(fragment)), `${fragment}: ${errors.join('\n')}`);
    }
  });

  test('requires actions pinned to a major version or a commit, and no env context in services', async () => {
    const errors = workflowRuleErrors(await parse(GOOD.replace('actions/checkout@v7', 'actions/checkout@main')));
    assert.ok(errors.some((e) => e.includes('actions/checkout@main')));
    assert.deepEqual(workflowRuleErrors(await parse(GOOD.replace('@v7', `@${'a'.repeat(40)}`))), []);
    const doc = await parse(GOOD);
    doc.jobs.build.services = { db: { image: 'x', env: { PASSWORD: '${{ env.X }}' } } };
    assert.ok(workflowRuleErrors(doc).some((e) => e.includes('env context')));
  });

  test("refuses a tag's name, an input or an event's text written into a script, and accepts it through env (ADR-0247)", async () => {
    for (const inline of [
      '${{ github.ref_name }}',
      '${{ github.ref }}',
      '${{ inputs.version }}',
      '${{ github.event.head_commit.message }}',
      '${{ github.head_ref }}',
      "${{ format('{0}', github.head_ref) }}",
      '${{ steps.version.outputs.version }}',
      '${{ needs.prepare.outputs.version }}',
      '${{ env.VERSION }}',
    ]) {
      const doc = await parse(GOOD);
      doc.jobs.build.steps.push({ name: 'Use it', run: `node scripts/release.mjs version "${inline}"` });
      assert.deepEqual(workflowRuleErrors(doc), [
        'job build: step "Use it" writes a tag, an input or an event\'s text into its script; pass it through `env`',
      ], inline);
    }
    const doc = await parse(GOOD);
    doc.jobs.build.steps.push({ name: 'Use it', env: { REQUESTED: '${{ inputs.version || github.ref_name }}' }, run: 'node scripts/release.mjs version "$REQUESTED"' });
    // GitHub's own words and values inline are fine: the kind of ref, the run's id, a service's container.
    doc.jobs.build.steps.push({ name: 'Safe', run: 'echo "${{ github.ref_type }} ${{ github.run_id }} ${{ job.services.mssql.id }}"' });
    assert.deepEqual(workflowRuleErrors(doc), []);
  });
});

describe('dependabotErrors', () => {
  const root = mkdtempSync(join(tmpdir(), 'jfb-dependabot-'));
  mkdirSync(join(root, 'scripts'));
  mkdirSync(join(root, 'tests', 'e2e'), { recursive: true });
  after(() => rmSync(root, { recursive: true, force: true }));

  test('accepts the three ecosystems with a limit of 0 over every npm project', () => {
    assert.deepEqual(dependabotErrors(DEPENDABOT, root, LOCKS), []);
  });

  test('refuses a raised limit, a missing ecosystem, a missing directory and an npm project left out', () => {
    const doc = structuredClone(DEPENDABOT);
    doc.updates[0]['open-pull-requests-limit'] = 5;
    doc.updates[1].directories = ['/scripts', '/nope'];
    doc.updates.pop();
    const errors = dependabotErrors(doc, root, LOCKS);
    for (const fragment of ['must be 0', 'github-actions', '/nope does not exist', 'npm project /tests/e2e']) {
      assert.ok(errors.some((e) => e.includes(fragment)), `${fragment}: ${errors.join('\n')}`);
    }
  });
});

describe('run', () => {
  test('fails without workflow files or dependabot.yml, and passes with good ones', async () => {
    const root = mkdtempSync(join(tmpdir(), 'jfb-workflows-'));
    try {
      assert.equal((await run(root, LOCKS)).ok, false);
      mkdirSync(join(root, '.github', 'workflows'), { recursive: true });
      mkdirSync(join(root, 'scripts'));
      mkdirSync(join(root, 'tests', 'e2e'), { recursive: true });
      writeFileSync(join(root, '.github', 'workflows', 'ci.yml'), GOOD);
      writeFileSync(join(root, '.github', 'dependabot.yml'), JSON.stringify(DEPENDABOT));
      const result = await run(root, LOCKS);
      assert.equal(result.ok, true, result.lines.join('\n'));
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });

  test("the repository's own workflows and dependabot.yml pass", async () => {
    const result = await run(resolve(fileURLToPath(new URL('../..', import.meta.url))));
    assert.equal(result.ok, true, result.lines.join('\n'));
  });
});
