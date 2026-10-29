// What the vectors of tests/vectors/messages.json do not reach (ROADMAP 6.2g, ADR-0099), the same as Core's
// VisitorMessagesTests: a template's name without a value and a brace left open stay as written, a value is never read
// as a template, and a culture takes its own words, its more general culture's or English's.
import { describe, expect, it } from 'vitest';
import { fill, messageWordsOf } from '../src/index.ts';
import { de } from '../src/lang/messages.de.ts';
import { en } from '../src/lang/messages.en.ts';

describe('fill', () => {
  it('keeps a name it has no value for, as written', () => {
    expect(fill('Enter {min} to {max}', { min: '1' })).toBe('Enter 1 to {max}');
  });

  it('keeps a brace left open, and what follows it', () => {
    expect(fill('Enter {min} or {more', { min: '1' })).toBe('Enter 1 or {more');
    expect(fill('Enter } {min}', { min: '1' })).toBe('Enter } 1');
  });

  it('never reads a value as a template', () => {
    expect(fill('{a} and {b}', { a: '{b}', b: 'x' })).toBe('{b} and x');
  });
});

describe('messageWordsOf', () => {
  it("takes a culture's own words, then its more general culture's, in any letter case", () => {
    expect(messageWordsOf('DE-ch', { en, de })).toBe(de);
    expect(messageWordsOf('de', { en, de })).toBe(de);
  });

  it('falls back to English', () => {
    expect(messageWordsOf('fr-FR', { en, de })).toBe(en);
    expect(messageWordsOf('', { en, de })).toBe(en);
  });

  it('refuses tables without English', () => {
    expect(() => messageWordsOf('fr', { de })).toThrow('no English');
  });
});
