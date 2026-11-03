// What the library says of its rows (ADR-0166): days, changes and entries, an import's notes, and the pages a form is on.
import { describe, expect, it } from 'vitest';
import type { FormDocumentReferenceResponseModel, ImportNoteResponseModel, LibraryRowResponseModel } from '../api';
import {
  changedText,
  dayText,
  entriesText,
  isGoodNews,
  NAMED_PAGES,
  noteTexts,
  showsEntries,
  usedOn,
  type NoteWords,
} from './rows';

const words = { today: 'Today', yesterday: 'Yesterday', by: (day: string, name: string) => `${day}, by ${name}` };
const now = new Date(2026, 10, 3, 9, 30);

const row = (change: Partial<LibraryRowResponseModel>): LibraryRowResponseModel => ({
  item: {
    key: 'k',
    kind: 'form',
    name: 'Contact us',
    isRestricted: false,
    isPublished: true,
    isTrashed: false,
    hasChildren: false,
    permissions: [],
    hasUnseenEntries: false,
  },
  updatedUtc: new Date(2026, 10, 3, 8).toISOString(),
  ...change,
});

describe('a day', () => {
  it('is today, yesterday, a day this year, or a day with its year', () => {
    expect(dayText(new Date(2026, 10, 3, 0, 5), now, 'en', words)).toBe('Today');
    expect(dayText(new Date(2026, 10, 2, 23, 59), now, 'en', words)).toBe('Yesterday');
    expect(dayText(new Date(2026, 9, 20), now, 'en-GB', words)).toBe('20 Oct');
    expect(dayText(new Date(2026, 9, 20), now, 'en-US', words)).toBe('Oct 20');
    expect(dayText(new Date(2026, 9, 20), now, 'de', words)).toBe('20. Okt.');
    expect(dayText(new Date(2025, 9, 20), now, 'en-GB', words)).toBe('20 Oct 2025');
  });

  it('is today for a moment a clock ahead put in the future', () => {
    expect(dayText(new Date(2026, 10, 4, 1), now, 'en', words)).toBe('Today');
  });
});

describe("a row's change", () => {
  it('names who changed it when that is known', () => {
    expect(changedText(row({ updatedBy: 'Ana Novak' }), now, 'en', words)).toBe('Today, by Ana Novak');
    expect(changedText(row({}), now, 'en', words)).toBe('Today');
  });

  it('is when it went to the Recycle Bin, for a trashed row', () => {
    const trashed = row({ trashedUtc: new Date(2026, 10, 2, 12).toISOString(), updatedBy: 'Ana Novak' });
    expect(changedText(trashed, now, 'en', words)).toBe('Yesterday, by Ana Novak');
  });
});

describe("a row's entries", () => {
  it('is a number, none, or nothing for a user who may view none', () => {
    expect(entriesText(1204, 'en', 'none')).toBe('1,204');
    expect(entriesText(1204, 'de', 'keine')).toBe('1.204');
    expect(entriesText(0, 'en', 'none')).toBe('none');
    expect(entriesText(null, 'en', 'none')).toBe('');
    expect(entriesText(undefined, 'en', 'none')).toBe('');
  });

  it('shows its column when any row has a count', () => {
    expect(showsEntries([row({}), row({ recentEntries: 0 })])).toBe(true);
    expect(showsEntries([row({}), row({ recentEntries: null })])).toBe(false);
    expect(showsEntries([])).toBe(false);
  });
});

const noteWords: NoteWords = {
  themeMissing: (theme) => `theme ${theme}`,
  optionListReused: (list) => `reused ${list}`,
  optionListCreated: (list) => `created ${list}`,
  optionListMissing: (list) => `missing ${list}`,
  optionListUnnamed: 'unnamed list',
  actionConfigurationMissing: (key) => `setting ${key}`,
  actionTypeMissing: (type) => `type ${type}`,
  pageMissing: 'page missing',
  other: (code) => `other ${code}`,
};

const note = (code: string, alias?: string): ImportNoteResponseModel => ({
  code,
  path: '$',
  ...(alias ? { alias } : {}),
});

describe("an import's notes", () => {
  it('say each note in file order, each sentence once', () => {
    const notes = [
      note('option_list_missing', 'countries'),
      note('theme_missing', 'dark'),
      note('option_list_missing', 'countries'),
      note('option_list_missing'),
      note('option_list_reused', 'sizes'),
      note('option_list_created', 'colours'),
      note('action_configuration_missing', 'webhookUrl'),
      note('action_type_missing', 'crm.push'),
      note('page_missing'),
      note('page_missing'),
      note('something_new'),
    ];
    expect(noteTexts(notes, noteWords)).toEqual([
      'missing countries',
      'theme dark',
      'unnamed list',
      'reused sizes',
      'created colours',
      'setting webhookUrl',
      'type crm.push',
      'page missing',
      'other something_new',
    ]);
  });

  it('tell what came along apart from what is missing', () => {
    expect(isGoodNews(note('option_list_reused', 'a'))).toBe(true);
    expect(isGoodNews(note('option_list_created', 'a'))).toBe(true);
    expect(isGoodNews(note('option_list_missing', 'a'))).toBe(false);
    expect(isGoodNews(note('page_missing'))).toBe(false);
  });
});

describe('the pages a form is on', () => {
  const page = (name: string | null, variants: string[] = []): FormDocumentReferenceResponseModel => ({
    $type: 'DocumentReferenceResponseModel',
    id: name ?? 'unnamed',
    name,
    documentType: { id: 't' },
    variants: variants.map((variant) => ({ name: variant, state: 'Published' })),
  });

  it('count the pages the user may not open, and name those they may', () => {
    expect(usedOn({ total: 2, notShown: 1, items: [page('Home'), page(null, ['', 'Kontakt'])] })).toEqual({
      count: 3,
      names: ['Home', 'Kontakt'],
      more: 1,
    });
  });

  it(`name at most ${NAMED_PAGES} and count the rest`, () => {
    const items = Array.from({ length: 7 }, (_, i) => page(`Page ${i + 1}`));
    expect(usedOn({ total: 7, notShown: 0, items })).toEqual({
      count: 7,
      names: ['Page 1', 'Page 2', 'Page 3', 'Page 4', 'Page 5'],
      more: 2,
    });
  });

  it('are none without an answer', () => {
    expect(usedOn(undefined)).toEqual({ count: 0, names: [], more: 0 });
  });
});
