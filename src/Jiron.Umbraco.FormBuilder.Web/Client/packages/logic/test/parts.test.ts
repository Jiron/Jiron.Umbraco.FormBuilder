// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The reader of a value with parts (ADR-0083) on what a client may store but Core never writes: a text that holds a
// lone surrogate itself, outside any escape, has no parts, as Core's FieldPartsJson gives none. A shared vector cannot
// hold such a text, since a JSON file holds none, so each runtime tests it alone.
import { describe, expect, it } from 'vitest';
import { valueParts } from '../src/display.ts';

const high = String.fromCharCode(0xd83d);
const low = String.fromCharCode(0xde00);
const backslash = String.fromCharCode(92);

describe('valueParts', () => {
  it.each([
    ['a lone high surrogate in a text', `{"first":"A${high}"}`],
    ['a lone low surrogate in a name', `{"f${low}":"Ada"}`],
    [
      'an escaped high surrogate before a lone low one, which JSON.parse would join',
      `{"first":"${backslash}ud83d${low}"}`,
    ],
  ])('gives no parts for %s', (_, text) => {
    expect(valueParts(text)).toBeNull();
  });

  it('reads a well-formed pair', () => {
    expect(valueParts(`{"first":"A${high}${low}"}`)).toEqual([['first', `A${high}${low}`]]);
  });
});
