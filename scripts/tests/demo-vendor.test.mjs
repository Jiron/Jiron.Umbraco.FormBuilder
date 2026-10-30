// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The third-party files the demo carries, byte for byte as published (ROADMAP 6.3c, ADR-0107, THIRD-PARTY-NOTICES).
// Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { DEMO } from '../demo-check.mjs';

const ROOT = fileURLToPath(new URL('../../', import.meta.url));
const BOOTSTRAP = join(ROOT, DEMO, 'wwwroot/demo/vendor/bootstrap-5.3.8');

// The SHA-256 of each file in the npm package bootstrap@5.3.8 (integrity sha512-HP1SZDqaLDPwsNiqRqi5NcP0SSXciX2s9E+Ry
// qJIIqGo+vJeN5AJVM98CXmW/Wux0nQ5L7jeWUdplCEf0Ee+tg==): dist/css/bootstrap.min.css, dist/css/bootstrap.rtl.min.css and
// LICENSE.
const PUBLISHED = {
  'bootstrap.min.css': 'd85327d99c7a3ee1f9b5d0500d1370acea3ad2db39c163c2f51f232baedbdede',
  'bootstrap.rtl.min.css': 'b9048c83571ab73da54395b6b307219f9eb9c5447f4f8edb17a069eb0f19465a',
  LICENSE: '4620c84ad5ce8602ff65640ed6b7c8b78ebb9e036584f0ebc1ccc88206a4bb51',
};

const sha256 = (path) => createHash('sha256').update(readFileSync(path)).digest('hex');

describe("the demo's copy of Bootstrap", () => {
  test('holds the two stylesheets and the licence, and nothing else', () => {
    assert.deepEqual(readdirSync(BOOTSTRAP).sort(), Object.keys(PUBLISHED).sort());
  });

  for (const [name, hash] of Object.entries(PUBLISHED)) {
    test(`${name} is the published file, unchanged`, () => {
      assert.equal(sha256(join(BOOTSTRAP, name)), hash);
    });
  }

  test('the licence is MIT, as THIRD-PARTY-NOTICES says', () => {
    assert.match(readFileSync(join(BOOTSTRAP, 'LICENSE'), 'utf8'), /^The MIT License \(MIT\)/);
  });
});
