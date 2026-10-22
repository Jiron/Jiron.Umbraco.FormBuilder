// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/fix.mjs.
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { NPM_PACKAGES, plan } from '../fix.mjs';

describe('plan', () => {
  test('an empty repository has nothing to format', () => {
    const root = mkdtempSync(join(tmpdir(), 'jfb-fix-'));
    try {
      assert.deepEqual(plan(root), []);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });

  test('runs dotnet format on the solution first, then every npm package that has a "fix" script', () => {
    const root = mkdtempSync(join(tmpdir(), 'jfb-fix-'));
    try {
      writeFileSync(join(root, 'Jiron.Umbraco.FormBuilder.slnx'), '<Solution />');
      const [client, e2e, scripts] = NPM_PACKAGES;
      mkdirSync(join(root, client), { recursive: true });
      writeFileSync(join(root, client, 'package.json'), JSON.stringify({ scripts: { fix: 'eslint --fix . && prettier --write .' } }));
      mkdirSync(join(root, e2e), { recursive: true });
      writeFileSync(join(root, e2e, 'package.json'), JSON.stringify({ scripts: { test: 'playwright test' } }));
      mkdirSync(join(root, scripts), { recursive: true });
      writeFileSync(join(root, scripts, 'package.json'), JSON.stringify({ scripts: { fix: 'prettier --write .' } }));

      const steps = plan(root);
      assert.deepEqual(
        steps.map((step) => [step.command, step.args.join(' ')]),
        [
          ['dotnet', 'format Jiron.Umbraco.FormBuilder.slnx'],
          ['npm', 'run fix'],
          ['npm', 'run fix'],
        ],
      );
      assert.equal(steps[1].cwd, join(root, client));
      assert.equal(steps[2].cwd, join(root, scripts), 'a package without a "fix" script is skipped');
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
});
