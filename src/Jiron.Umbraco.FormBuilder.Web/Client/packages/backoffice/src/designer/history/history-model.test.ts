// What the History list says of each version (ROADMAP 10.3a, ADR-0196): its name in a row and in a sentence, a publish
// a version first even where it is the draft too, and when it was made: today and yesterday by name, another day by
// its date, with the year where it is another.
import { beforeAll, describe, expect, it } from 'vitest';
import type { FormVersionResponseModel } from '../../api';
import startEn from '../../lang/en';
import historyEn from '../../lang/history.en';
import { flatten } from '../actions/action-words';
import { headingCase, madeAt, momentText, rowName, versionName } from './history-model';

let term: (key: string, ...args: unknown[]) => string;

beforeAll(() => {
  const words = { ...flatten('en', startEn), ...flatten('en', historyEn) } as Record<string, unknown>;
  term = (key, ...args) => {
    const word = words[key];
    return typeof word === 'function' ? String((word as (...values: unknown[]) => unknown)(...args)) : String(word);
  };
});

const now = new Date(2026, 10, 6, 14, 30);
const version = (more: Partial<FormVersionResponseModel>): FormVersionResponseModel => ({
  number: 14,
  kind: 'published',
  isPinned: false,
  createdUtc: new Date(2026, 10, 6, 9, 12).toISOString(),
  isDraft: false,
  isLive: false,
  ...more,
});

describe("a version's names", () => {
  it('name a publish by its number, even where it is the draft too; a draft save by when; the draft as such', () => {
    expect(rowName(version({}), term)).toBe('Version 14');
    expect(rowName(version({ isDraft: true, isLive: true }), term)).toBe('Version 14');
    expect(rowName(version({ kind: 'draft' }), term)).toBe('Draft save');
    expect(rowName(version({ kind: 'draft', isDraft: true }), term)).toBe('Current draft');
    expect(versionName(version({ isDraft: true }), now, 'en-GB', term)).toBe('version 14');
    // Within a sentence "today" is small, and the time follows it without a comma that would split the sentence.
    expect(versionName(version({ kind: 'draft' }), now, 'en-GB', term)).toBe('the draft save of today at 09:12');
    expect(versionName(version({ kind: 'draft', isDraft: true }), now, 'en-GB', term)).toBe('the current draft');
    expect(headingCase('the current draft')).toBe('The current draft');
  });

  it('say when a version was made: a publish when it was published, by day and time', () => {
    const at = (day: number, month = 10, year = 2026) => new Date(year, month, day, 16, 40);
    expect(momentText(at(6), now, 'en-GB', term)).toBe('Today, 16:40');
    expect(momentText(at(5), now, 'en-GB', term)).toBe('Yesterday, 16:40');
    expect(momentText(at(5), now, 'en-GB', term, true)).toBe('yesterday at 16:40');
    expect(momentText(at(3), now, 'en-GB', term)).toBe('3 Nov, 16:40');
    expect(momentText(at(3, 10, 2025), now, 'en-GB', term)).toBe('3 Nov 2025, 16:40');
    const published = version({ createdUtc: at(1).toISOString(), publishedUtc: at(2).toISOString() });
    expect(madeAt(published)).toEqual(at(2));
    expect(madeAt({ ...published, kind: 'draft' })).toEqual(at(1));
  });
});
