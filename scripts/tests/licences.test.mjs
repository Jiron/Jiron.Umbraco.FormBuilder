// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/lib/licences.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
  ALLOWED,
  DEV,
  expressionIds,
  forbiddenText,
  isForbiddenId,
  isForbiddenUrl,
  parseExpression,
  PUBLIC_DOMAIN,
  satisfies,
  SHIPPED,
} from '../lib/licences.mjs';

describe('the lists', () => {
  test("are rule 4's for shipped code, and rule 4's development list with ADR-0027 for tools", () => {
    assert.deepEqual(SHIPPED, ['MIT', 'Apache-2.0', 'BSD-2-Clause', 'BSD-3-Clause', 'ISC', '0BSD']);
    assert.deepEqual(PUBLIC_DOMAIN, ['MIT-0', 'Unlicense', 'CC0-1.0']);
    for (const id of [...SHIPPED, 'MPL-2.0', 'BlueOak-1.0.0', 'CC-BY-4.0', 'Python-2.0', ...PUBLIC_DOMAIN]) assert.ok(DEV.includes(id), id);
    for (const id of PUBLIC_DOMAIN) assert.equal(ALLOWED.shipped.has(id), false, id);
  });
});

describe('parseExpression', () => {
  test('reads ids, AND before OR, parentheses and WITH', () => {
    assert.deepEqual(parseExpression('MIT'), { id: 'MIT' });
    assert.deepEqual(parseExpression('MIT OR Apache-2.0 AND ISC'), { or: [{ id: 'MIT' }, { and: [{ id: 'Apache-2.0' }, { id: 'ISC' }] }] });
    assert.deepEqual(parseExpression('(MIT OR ISC) AND 0BSD'), { and: [{ or: [{ id: 'MIT' }, { id: 'ISC' }] }, { id: '0BSD' }] });
    assert.deepEqual(parseExpression('Apache-2.0 WITH LLVM-exception'), { id: 'Apache-2.0' });
    assert.deepEqual(parseExpression('BSD 3-Clause'), { id: 'BSD-3-Clause' });
  });

  test('refuses an empty or malformed expression', () => {
    for (const bad of ['', '   ', undefined, 'MIT OR', '(MIT', 'MIT)', 'AND MIT', 'MIT WITH', 'MIT ISC']) {
      assert.throws(() => parseExpression(bad), Error, String(bad));
    }
  });

  test('lists every id an expression names', () => {
    assert.deepEqual(expressionIds('(MIT OR GPL-2.0-only) AND ISC'), ['MIT', 'GPL-2.0-only', 'ISC']);
  });
});

describe('satisfies', () => {
  test('an OR needs one allowed side and an AND every side', () => {
    assert.equal(satisfies('MIT OR GPL-3.0-only', ALLOWED.shipped), true);
    assert.equal(satisfies('MIT AND GPL-3.0-only', ALLOWED.shipped), false);
    assert.equal(satisfies('(MIT OR Apache-2.0) AND (BSD-3-Clause OR ISC)', ALLOWED.shipped), true);
  });

  test('groups correctly: one allowed side outside the group does not carry a refused group', () => {
    // A regular expression that only strips outer parentheses reads this as "A OR (B) AND (C) OR D" and accepts it.
    assert.equal(satisfies('(MIT OR GPL-3.0-only) AND (SSPL-1.0 OR AGPL-3.0-only)', ALLOWED.dev), false);
  });

  test('reads the usual spellings as their SPDX ids', () => {
    assert.equal(satisfies('BSD 3-Clause', ALLOWED.shipped), true);
    assert.equal(satisfies('Apache 2.0', ALLOWED.shipped), true);
  });

  test('refuses a missing, malformed or unknown licence', () => {
    for (const bad of ['', undefined, 'MIT OR', 'LicenseRef-Proprietary', 'UNLICENSED']) assert.equal(satisfies(bad, ALLOWED.dev), false, String(bad));
  });
});

describe('the forbidden families', () => {
  test('are recognised by id, by the text of a licence file and by a licence URL', () => {
    for (const id of ['GPL-3.0-only', 'AGPL-3.0', 'LGPL-2.1-or-later', 'SSPL-1.0', 'BUSL-1.1', 'Elastic-2.0', 'PolyForm-Noncommercial-1.0.0', 'UNLICENSED']) {
      assert.equal(isForbiddenId(id), true, id);
    }
    for (const id of ['MIT', 'Unlicense', 'MS-PL', 'Apache-2.0']) assert.equal(isForbiddenId(id), false, id);
    assert.ok(forbiddenText('GNU LESSER GENERAL PUBLIC LICENSE\nVersion 3'));
    assert.ok(forbiddenText('Server Side Public License, v 1'));
    // Apache-2.0 speaks of commercial damages; that alone names no family.
    assert.equal(forbiddenText('Apache License Version 2.0 ... any and all other commercial damages or losses'), undefined);
    assert.equal(isForbiddenUrl('https://www.gnu.org/licenses/gpl-3.0.html'), true);
    assert.equal(isForbiddenUrl('https://github.com/x/y/blob/main/LICENSE'), false);
  });
});
