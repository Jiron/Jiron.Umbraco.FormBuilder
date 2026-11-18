// What the Export sidebar decides (ROADMAP 11.4, IA 6.4, ADR-0210): the choices it opens with, the fields each format
// offers, the request it sends, and the sentences of its entries line, its button and a background export.
import { describe, expect, it } from 'vitest';
import type {
  EntryExportFormatResponseModel,
  EntryExportViewResponseModel,
  EntryListFieldResponseModel,
  EntryListFilterRequestModel,
} from '../api';
import entriesEn from '../lang/entries.en';
import {
  choicesOf,
  chosenFields,
  defaultChoices,
  entriesLine,
  exportFilter,
  exportLabel,
  fieldsFor,
  hasSensitive,
  narrows,
  inBackground,
  requestOf,
  statusLine,
  unconfirmedLine,
} from './export-model';

const words = entriesEn.jfbEntries as Record<string, unknown>;
const term = (key: string, ...args: unknown[]): string => {
  const word = words[key];
  return typeof word === 'function'
    ? String((word as (...values: unknown[]) => unknown)(...args))
    : String(word ?? key);
};

const format = (name: string, extra: Partial<EntryExportFormatResponseModel> = {}): EntryExportFormatResponseModel => ({
  name,
  displayName: `#jfbExport_${name}`,
  writesFiles: false,
  takesDelimiter: name === 'csv',
  takesHeaders: name !== 'json' && name !== 'files',
  fileName: `contact-entries.${name}`,
  ...extra,
});

const FORMATS = [
  format('csv'),
  format('xlsx'),
  format('json'),
  format('print'),
  format('files', { writesFiles: true }),
];

const field = (key: string, extra: Partial<EntryListFieldResponseModel> = {}): EntryListFieldResponseModel => ({
  key,
  label: key,
  alias: key,
  type: 'text',
  kind: 'text',
  sensitive: false,
  removed: false,
  column: true,
  sorts: true,
  tests: [],
  options: [],
  ...extra,
});

const FIELDS = [
  field('name'),
  field('salary', { sensitive: true }),
  field('cv', { kind: 'file', type: 'file' }),
  field('guests', { kind: 'group', type: 'repeater' }),
  field('photo', { kind: 'file', type: 'file', parent: 'guests' }),
  field('note', { removed: true }),
];

const EMPTY: EntryListFilterRequestModel = { states: [], fields: [], forms: [] };

const view = (extra: Partial<EntryExportViewResponseModel> = {}): EntryExportViewResponseModel => ({
  formats: FORMATS,
  maySeeSensitive: true,
  exports: [],
  ...extra,
});

describe('the choices the sidebar opens with', () => {
  it('are the defaults until the user exported the form: the first format, every field, none sensitive', () => {
    expect(choicesOf(view())).toEqual(defaultChoices(FORMATS));
    expect(defaultChoices(FORMATS)).toEqual({
      format: 'csv',
      fields: null,
      sensitive: false,
      delimiter: 'comma',
      headers: 'labels',
      byteOrderMark: true,
    });
  });

  it("are the user's last, except a format the site no longer has and sensitive fields they may no longer see", () => {
    const last = {
      format: 'xlsx',
      fields: ['name'],
      sensitive: true,
      options: { delimiter: 'tab', headers: 'aliases', byteOrderMark: false },
    };
    expect(choicesOf(view({ choices: last }))).toEqual({
      format: 'xlsx',
      fields: ['name'],
      sensitive: true,
      delimiter: 'tab',
      headers: 'aliases',
      byteOrderMark: false,
    });
    const gone = choicesOf(view({ choices: { ...last, format: 'pdf' }, maySeeSensitive: false }));
    expect([gone.format, gone.sensitive]).toEqual(['csv', false]);
    const odd = choicesOf(
      view({ choices: { ...last, options: { delimiter: '|', headers: 'names', byteOrderMark: true } } }),
    );
    expect([odd.delimiter, odd.headers]).toEqual(['comma', 'labels']);
  });
});

describe('the fields a format offers', () => {
  it("are the form's own, those of older versions too, a group with its children, and sensitive ones only when they go along", () => {
    const csv = FORMATS[0];
    expect(fieldsFor(FIELDS, csv, false).map((one) => one.key)).toEqual(['name', 'cv', 'guests', 'note']);
    expect(fieldsFor(FIELDS, csv, true).map((one) => one.key)).toEqual(['name', 'salary', 'cv', 'guests', 'note']);
    expect(hasSensitive(FIELDS)).toBe(true);
    // A group's sensitive child is left out of its group the same way, so it offers the choice too.
    expect(hasSensitive([field('name'), field('inner', { sensitive: true, parent: 'guests' })])).toBe(true);
    expect(hasSensitive([field('name'), field('inner', { parent: 'guests' })])).toBe(false);
  });

  it('are the fields that hold files for the ZIP of the files, a group holding one among them', () => {
    expect(fieldsFor(FIELDS, FORMATS[4], false).map((one) => one.key)).toEqual(['cv', 'guests']);
  });

  it('keep the chosen ones in their order, and leave out a chosen field the format no longer offers', () => {
    const offered = fieldsFor(FIELDS, FORMATS[4], false);
    expect(chosenFields({ ...defaultChoices(FORMATS), fields: ['guests', 'name', 'cv'] }, offered)).toEqual([
      'cv',
      'guests',
    ]);
    expect(chosenFields(defaultChoices(FORMATS), offered)).toEqual(['cv', 'guests']);
  });
});

describe('the request', () => {
  it("takes the list's filter, with the Inbox's Form filter naming only this form, or the ticked entries, one too", () => {
    const filter: EntryListFilterRequestModel = { ...EMPTY, search: 'ada', forms: ['f1'] };
    const choices = { ...defaultChoices(FORMATS), fields: ['name', 'salary'], sensitive: false };
    const offered = fieldsFor(FIELDS, FORMATS[0], false);

    expect(requestOf(choices, offered, 'f1', filter, undefined)).toEqual({
      format: 'csv',
      filter: { ...filter, forms: ['f1'] },
      fields: ['name'],
      sensitive: false,
      options: { delimiter: 'comma', headers: 'labels', byteOrderMark: true },
      remember: true,
    });
    expect(requestOf(defaultChoices(FORMATS), offered, 'f1', filter, ['e1', 'e2'])).toEqual({
      format: 'csv',
      entries: ['e1', 'e2'],
      fields: null,
      sensitive: false,
      options: { delimiter: 'comma', headers: 'labels', byteOrderMark: true },
      remember: true,
    });
    const one = requestOf(defaultChoices(FORMATS), offered, 'f1', filter, ['e1']);
    expect([one.entries, one.filter], 'one ticked entry is an export of it alone').toEqual([['e1'], undefined]);
    expect(
      requestOf(defaultChoices(FORMATS), offered, 'f1', filter, []).filter,
      'none ticked takes the filter',
    ).toEqual({
      ...filter,
      forms: ['f1'],
    });
    expect(exportFilter({ ...EMPTY, forms: ['f1', 'f2'] }, 'f2').forms).toEqual(['f2']);
  });

  it('goes to the background when the server counts too many, and for every ZIP of the files', () => {
    const small = { entries: 3, more: false, background: false, unconfirmed: 0 };
    expect(inBackground(small, FORMATS[0])).toBe(false);
    expect(inBackground({ ...small, background: true }, FORMATS[0])).toBe(true);
    expect(inBackground(small, { ...FORMATS[0], writesFiles: true } as never), 'a file of files has no bound').toBe(
      true,
    );
    expect(inBackground(small, undefined)).toBe(false);
  });

  it('knows a filter that narrows the list from one that does not', () => {
    expect(narrows(undefined)).toBe(false);
    expect(narrows({ ...EMPTY, search: '  ', forms: ['f1'] })).toBe(false);
    expect(narrows({ ...EMPTY, states: ['approved'] })).toBe(true);
    expect(narrows({ ...EMPTY, received: { preset: 'today' } })).toBe(true);
  });
});

describe('what the sidebar says', () => {
  const plan = (entries: number, more = false, background = false, unconfirmed = 0) => ({
    entries,
    more,
    background,
    unconfirmed,
  });

  it('names which entries go, without the number the button says: all, filtered, searched, ticked', () => {
    expect(entriesLine(term, plan(214), false, false)).toBe('All, no filters');
    expect(entriesLine(term, plan(1), false, false)).toBe('All, no filters');
    expect(entriesLine(term, plan(124), false, true)).toBe('Those that match the filters');
    expect(entriesLine(term, plan(1000, true, true), false, true)).toBe('Those that match the search');
    expect(entriesLine(term, plan(6), true, true)).toBe('Those selected');
    for (const [entries, ticked, filtered] of [
      [214, false, false],
      [124, false, true],
      [6, true, true],
    ] as const)
      expect(entriesLine(term, plan(entries), ticked, filtered)).not.toMatch(/\d/);
    expect(entriesLine(term, plan(0), false, false)).toBe('No entries to export');
    expect(entriesLine(term, plan(0), false, true)).toBe('No entries match the filters');
  });

  it('says how many entries waiting for their email confirmation stay out, unless entries are ticked', () => {
    expect(unconfirmedLine(term, plan(12, false, false, 3), false)).toBe(
      '3 entries waiting for email confirmation stay out. To export them, filter by their state.',
    );
    expect(unconfirmedLine(term, plan(12, false, false, 1), false)).toBe(
      '1 entry waiting for email confirmation stays out. To export it, filter by its state.',
    );
    expect(unconfirmedLine(term, plan(12), false), 'none waiting').toBeUndefined();
    expect(unconfirmedLine(term, plan(2, false, false, 3), true), 'the ticked ones are what goes').toBeUndefined();
  });

  it('names its button by what happens and how many go', () => {
    expect(exportLabel(term, 'csv', undefined)).toBe('Export');
    expect(exportLabel(term, 'csv', plan(124))).toBe('Export 124 entries');
    expect(exportLabel(term, 'xlsx', plan(12400, false, true))).toBe('Export 12,400 entries');
    expect(exportLabel(term, 'print', plan(1))).toBe('Print 1 entry');
    expect(exportLabel(term, 'csv', plan(0)), 'nothing to count').toBe('Export');
    expect(exportLabel(term, 'json', plan(1000, true, true))).toBe('Export more than 1,000 entries');
  });

  it('says how far a background export is, that it is ready, or that it failed', () => {
    const status = { key: 'k', state: 'queued', total: 12400, more: false, done: 0 };
    expect(statusLine(term, status)).toBe('Preparing the export of 12,400 entries.');
    expect(statusLine(term, { ...status, state: 'running', done: 2500 })).toBe(
      'Preparing the export: 2,500 of 12,400 entries.',
    );
    expect(statusLine(term, { ...status, state: 'running', done: 2500, more: true })).toBe(
      'Preparing the export: 2,500 entries so far.',
    );
    expect(statusLine(term, { ...status, state: 'ready', done: 12400 })).toBe(
      'Your export of 12,400 entries is ready.',
    );
    expect(statusLine(term, { ...status, state: 'failed' })).toBe("The export of 12,400 entries couldn't be prepared.");
    const ready = { ...status, state: 'ready', done: 12400, expiresUtc: '2026-11-08T09:41:00Z' };
    const when = (culture: string) =>
      new Intl.DateTimeFormat(culture, { weekday: 'short', hour: 'numeric', minute: '2-digit' }).format(
        new Date(ready.expiresUtc),
      );
    expect(statusLine(term, ready, 'en-GB'), 'how long its file is kept').toBe(
      `Your export of 12,400 entries is ready. Kept until ${when('en-GB')}.`,
    );
    expect(when('en-GB')).toMatch(/^Sun \d{2}:41$/);
  });
});
