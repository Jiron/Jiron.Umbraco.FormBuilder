// What the Entries view says of its entries and filters (ROADMAP 11.1, IA 6.1, ADR-0207): names, action status, the chips
// of each filter, the columns a filter fixes, which bulk actions apply, the request a list sends, and the sort's turn.
import { beforeAll, describe, expect, it } from 'vitest';
import type { EntryListFieldResponseModel } from '../api';
import { flatten } from '../designer/actions/action-words';
import entriesEn from '../lang/entries.en';
import {
  bulkActions,
  chipsOf,
  cleanFilter,
  conditionText,
  emptyFilter,
  fixedColumns,
  isFiltered,
  isTyping,
  leftOut,
  nextSort,
  receivedText,
  requestOf,
  rowLabel,
  rowName,
  runsText,
} from './entry-list-model';

let term: (key: string, ...args: unknown[]) => string;

beforeAll(() => {
  const words = flatten('en', entriesEn) as Record<string, unknown>;
  term = (key, ...args) => {
    const word = words[key];
    return typeof word === 'function' ? String((word as (...values: unknown[]) => unknown)(...args)) : String(word);
  };
});

const field = (
  key: string,
  label: string,
  more: Partial<EntryListFieldResponseModel> = {},
): EntryListFieldResponseModel => ({
  key,
  label,
  alias: key,
  type: 'text',
  kind: 'text',
  sensitive: false,
  removed: false,
  column: true,
  sorts: true,
  tests: ['oneOf', 'noneOf', 'startsWith', 'answered', 'notAnswered'],
  options: [],
  ...more,
});

const runs = (
  more: Partial<{ delivered: number; failed: number; uncertain: number; waiting: number; skipped: number }>,
) => ({
  delivered: 0,
  failed: 0,
  uncertain: 0,
  waiting: 0,
  skipped: 0,
  ...more,
});

describe('a row', () => {
  it('is named by its title, or by its number when the form has none', () => {
    expect(rowName({ title: 'Ada Lovelace', number: 7 }, term)).toBe('Ada Lovelace');
    expect(rowName({ title: null, number: 1042 }, term)).toBe('#1042');
    expect(rowLabel({ title: null, number: 1042 }, term)).toBe('entry 1042');
  });

  it('says its runs, the worst first, and nothing without runs', () => {
    expect(runsText(runs({}), term)).toBeNull();
    expect(runsText(runs({ delivered: 3 }), term)).toEqual({ text: '3/3 delivered', tone: 'default' });
    expect(runsText(runs({ delivered: 2, skipped: 1 }), term)?.text).toBe('2/2 delivered');
    expect(runsText(runs({ delivered: 1, failed: 1, uncertain: 1 }), term)).toEqual({
      text: '1 failed',
      tone: 'danger',
    });
    expect(runsText(runs({ delivered: 1, uncertain: 2 }), term)).toEqual({ text: '2 uncertain', tone: 'warning' });
    expect(runsText(runs({ delivered: 1, waiting: 2 }), term)?.text).toBe('2 waiting');
    expect(runsText(runs({ skipped: 2 }), term)).toEqual({ text: 'Not run', tone: 'muted' });
  });

  it('says when it was received: today with the time, another day by its date', () => {
    const now = new Date(2026, 10, 7, 12, 0);
    expect(receivedText(new Date(2026, 10, 7, 7, 48).toISOString(), now, 'en-GB', term)).toBe('Today, 07:48');
    expect(receivedText(new Date(2026, 10, 6, 18, 2).toISOString(), now, 'en-GB', term)).toBe('Yesterday, 18:02');
    expect(receivedText(new Date(2026, 9, 20, 9, 30).toISOString(), now, 'en-GB', term)).toBe('20 Oct, 09:30');
  });
});

describe('the chips', () => {
  const fields = new Map([
    ['position', field('position', 'Position')],
    ['amount', field('amount', 'Amount', { kind: 'number', tests: ['between', 'answered', 'notAnswered'] })],
    ['day', field('day', 'Day', { kind: 'date', tests: ['between', 'answered', 'notAnswered'] })],
  ]);

  it('say each part of the filter once, in the order of the menu', () => {
    const chips = chipsOf(
      {
        states: ['rejected', 'submitted'],
        received: { preset: 'last7Days' },
        actions: 'failed',
        fields: [
          { field: 'position', test: 'oneOf', values: ['Frontend developer', 'Accountant', 'Designer'] },
          { field: 'amount', test: 'between', values: [], min: 5, max: 1500 },
        ],
      },
      fields,
      'en-GB',
      term,
    );

    expect(chips.map((chip) => chip.text)).toEqual([
      'State: Waiting for approval or Rejected',
      'Received: Last 7 days',
      'Actions: Failed',
      'Position: Frontend developer, Accountant or Designer',
      'Amount: 5 to 1,500',
    ]);
    expect(chips.map((chip) => chip.index)).toEqual([undefined, undefined, undefined, 0, 1]);
  });

  it('say a range open at either end, each test in words, and a ticked option by its label', () => {
    expect(conditionText({ field: 'amount', test: 'between', values: [], min: 5 }, 'en-GB', term)).toBe('from 5');
    expect(conditionText({ field: 'day', test: 'between', values: [], to: '2026-03-01' }, 'en-GB', term)).toBe(
      'until 1 Mar 2026',
    );
    expect(conditionText({ field: 'position', test: 'noneOf', values: ['A'] }, 'en-GB', term)).toBe('not A');
    expect(conditionText({ field: 'position', test: 'startsWith', values: ['Fr'] }, 'en-GB', term)).toBe(
      'starts with Fr',
    );
    expect(conditionText({ field: 'position', test: 'notAnswered', values: [] }, 'en-GB', term)).toBe('not answered');
    const options = [
      { value: 'p1', label: 'Frontend developer' },
      { value: 'p3', label: 'Accountant' },
    ];
    expect(conditionText({ field: 'position', test: 'oneOf', values: ['p3', 'p9'] }, 'en-GB', term, options)).toBe(
      'Accountant or p9',
    );
    expect(conditionText({ field: 'position', test: 'noneOf', values: ['p1'] }, 'en-GB', term, options)).toBe(
      'not Frontend developer',
    );
    expect(
      chipsOf({ states: [], fields: [], received: { from: '2026-10-01', to: '2026-10-15' } }, fields, 'en-GB', term)[0]
        ?.text,
    ).toBe('Received: 1 Oct 2026 to 15 Oct 2026');
  });
});

describe('the columns a filter fixes', () => {
  it('hide the state for one state and a field fixed to one value; a range keeps its column', () => {
    const fixed = fixedColumns({
      states: ['approved'],
      fields: [
        { field: 'position', test: 'oneOf', values: ['Accountant'] },
        { field: 'team', test: 'oneOf', values: ['A', 'B'] },
        { field: 'agreed', test: 'checked', values: [] },
        { field: 'amount', test: 'between', values: [], min: 1 },
      ],
    });

    expect(fixed.state).toBe(true);
    expect([...fixed.fields]).toEqual(['position', 'agreed']);
    expect(fixedColumns({ states: ['approved', 'rejected'], fields: [] }).state).toBe(false);
  });
});

describe('the bulk bar', () => {
  const everything = ['editEntries', 'deleteEntries', 'manageActions'];

  it('offers what applies to one ticked entry at least, in its order, as the user may', () => {
    expect(bulkActions(['submitted'], everything)).toEqual(['approve', 'reject', 'markSpam', 'delete', 'rerun']);
    expect(bulkActions(['approved', 'spam'], everything)).toEqual(['reject', 'markSpam', 'notSpam', 'delete', 'rerun']);
    expect(bulkActions(['unverified'], everything)).toEqual(['markSpam', 'delete']);
    expect(bulkActions(['submitted'], ['deleteEntries'])).toEqual(['delete']);
    expect(bulkActions(['submitted'], [])).toEqual([]);
  });

  it('counts the entries an action leaves out', () => {
    expect(leftOut('approve', ['submitted', 'approved', 'rejected', 'spam'])).toBe(2);
    expect(leftOut('delete', ['spam'])).toBe(0);
  });
});

describe('the request', () => {
  it('sends only what the filter sets, counts on the first page, and goes on after a cursor', () => {
    const filter = { ...emptyFilter(), search: '  ada ', sort: { field: null, descending: false } };

    expect(requestOf(filter, ['position'], null)).toEqual({
      filter: { states: [], fields: [], search: 'ada', sort: { field: null, descending: false } },
      columns: ['position'],
      count: true,
    });
    expect(requestOf(emptyFilter(), [], 'cursor')).toEqual({
      filter: { states: [], fields: [] },
      columns: [],
      after: 'cursor',
      count: false,
    });
    expect(cleanFilter({ ...emptyFilter(), search: '   ' })).toEqual({ states: [], fields: [] });
    expect(isFiltered(emptyFilter())).toBe(false);
    expect(isFiltered({ ...emptyFilter(), search: 'ada' })).toBe(false);
    expect(isFiltered({ ...emptyFilter(), actions: 'failed' })).toBe(true);
  });

  it('turns a sorted column, and starts another in its natural order', () => {
    expect(nextSort(undefined, null)).toEqual({ field: null, descending: false });
    expect(nextSort({ field: null, descending: false }, null)).toEqual({ field: null, descending: true });
    expect(nextSort({ field: null, descending: true }, 'amount')).toEqual({ field: 'amount', descending: false });
    expect(nextSort({ field: 'amount', descending: false }, 'amount')).toEqual({ field: 'amount', descending: true });
    expect(nextSort({ field: 'amount', descending: true }, null)).toEqual({ field: null, descending: true });
  });
});

describe('the keyboard', () => {
  it('leaves keys typed in a box to the box', () => {
    expect(isTyping(document.createElement('input'))).toBe(true);
    expect(isTyping(document.createElement('textarea'))).toBe(true);
    expect(isTyping(document.createElement('tr'))).toBe(false);
    expect(isTyping(null)).toBe(false);
  });
});
