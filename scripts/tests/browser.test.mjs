// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/lib/browser.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { launchOptions } from '../lib/browser.mjs';

test("launches Edge on the development machine and Playwright's Chromium in CI (D19)", () => {
  assert.deepEqual(launchOptions({}), { channel: 'msedge' });
  assert.deepEqual(launchOptions({ CI: 'true' }), {});
});
